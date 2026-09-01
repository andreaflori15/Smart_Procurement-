using Microsoft.AspNetCore.Mvc;
using SmartProcurement.Services;

namespace SmartProcurement.Controllers
{
    public class CompradorController : Controller
    {
        private readonly SqlServerService _sql;
        private readonly DemoDataService _demo;
        private readonly ClasificacionComprasService _clasificacion;
        private readonly CotizacionFlujoService _flujo;
        private readonly SolicitudCotizacionService _solicitudes;
        private readonly ILogger<CompradorController> _logger;

        public CompradorController(
            SqlServerService sql,
            DemoDataService demo,
            ClasificacionComprasService clasificacion,
            CotizacionFlujoService flujo,
            SolicitudCotizacionService solicitudes,
            ILogger<CompradorController> logger)
        {
            _sql = sql;
            _demo = demo;
            _clasificacion = clasificacion;
            _flujo = flujo;
            _solicitudes = solicitudes;
            _logger = logger;
        }

        public IActionResult Index() => View();

        [HttpGet]
        public async Task<IActionResult> Compradores()
        {
            try
            {
                if (_demo.Activo)
                {
                    return Json(new { valido = true, demo = true, compradores = _demo.ListarCompradores() });
                }

                var lista = DataTableMapper.ToDictionaryList(await _sql.ObtenerCompradorAsync());
                var nombres = lista
                    .Select(r => r.TryGetValue("COMPRADOR_DESC", out var v) ? Convert.ToString(v)?.Trim() : null)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s)
                    .ToList();

                return Json(new { valido = true, compradores = nombres });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SQL no disponible; listando compradores de prueba");
                return Json(new { valido = true, demo = true, compradores = _demo.ListarCompradores() });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Pendientes(string comprador)
        {
            if (string.IsNullOrWhiteSpace(comprador))
            {
                return Json(new { valido = false, mensaje = "Elige un comprador." });
            }

            try
            {
                var (filas, _) = await CargarAsync(comprador);
                var pendientes = _clasificacion.UnificarPendientes(filas);
                return Json(new
                {
                    valido = true,
                    demo = _demo.Activo,
                    comprador,
                    datos = pendientes
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar pendientes");
                return Json(new { valido = false, mensaje = "No pude cargar las SOLPEDs del comprador." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Clasificar(string comprador)
        {
            if (string.IsNullOrWhiteSpace(comprador))
            {
                return Json(new { valido = false, mensaje = "Elige un comprador." });
            }

            try
            {
                var (filas, catalogo) = await CargarAsync(comprador);
                var (cubetas, items) = _clasificacion.Clasificar(filas, simularContratos: _demo.Activo, catalogo);
                foreach (var item in items)
                {
                    string lote = Convert.ToString(item["LoteId"]) ?? "";
                    item["LoteId"] = comprador.Trim() + "|" + lote;
                }

                var lotes = items
                    .Select(i => Convert.ToString(i["LoteId"]) ?? "")
                    .Where(id => id.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        id => id,
                        id =>
                        {
                            string cubeta = Convert.ToString(
                                items.First(i => string.Equals(Convert.ToString(i["LoteId"]), id, StringComparison.OrdinalIgnoreCase))["Cubeta"]) ?? "";
                            return _flujo.Ver(id, cubeta);
                        },
                        StringComparer.OrdinalIgnoreCase);

                _logger.LogInformation("Clasificación {Comprador}: {Count} SOLPEDs", comprador, items.Count);
                return Json(new
                {
                    valido = true,
                    demo = _demo.Activo,
                    comprador,
                    cubetas,
                    datos = items,
                    lotes
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al clasificar");
                return Json(new { valido = false, mensaje = "No pude clasificar las SOLPEDs." });
            }
        }

        [HttpPost]
        public IActionResult Solicitar(
            string loteId,
            string cubeta,
            string? comprador,
            string? proveedores,
            string? items)
        {
            if (string.IsNullOrWhiteSpace(loteId))
            {
                return Json(new { valido = false, mensaje = "Falta el lote." });
            }

            var lote = _flujo.Solicitar(loteId.Trim(), cubeta?.Trim() ?? "");
            string? numeroCotizacion = null;

            if (!string.IsNullOrWhiteSpace(comprador))
            {
                var solpeds = SolicitudCotizacionService.ParsearItems(items);
                numeroCotizacion = _solicitudes.RegistrarSolicitud(
                    comprador.Trim(),
                    loteId.Trim(),
                    cubeta?.Trim() ?? "",
                    proveedores?.Trim() ?? "",
                    solpeds,
                    lote.OfertasNecesarias);
            }

            return Json(new { valido = true, lote, numeroCotizacion });
        }

        [HttpPost]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> SubirCotizacion([FromForm] string loteId, IFormFile? archivo, [FromForm] string? proveedor)
        {
            if (string.IsNullOrWhiteSpace(loteId))
            {
                return Json(new { valido = false, mensaje = "Falta el lote." });
            }

            if (archivo is null)
            {
                return Json(new { valido = false, mensaje = "Elige un PDF de cotización." });
            }

            try
            {
                var lote = await _flujo.SubirPdfAsync(loteId.Trim(), archivo, proveedor);
                string? fechaRespuesta = lote.Archivos.LastOrDefault()?.Fecha;
                _solicitudes.ActualizarPorOferta(
                    loteId.Trim(),
                    lote.Ofertas,
                    lote.OfertasNecesarias,
                    fechaRespuesta);
                return Json(new { valido = true, lote });
            }
            catch (InvalidOperationException ex)
            {
                return Json(new { valido = false, mensaje = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult SolicitudesCotizacion(string comprador)
        {
            if (string.IsNullOrWhiteSpace(comprador))
            {
                return Json(new { valido = false, mensaje = "Elige un comprador." });
            }

            var lista = _solicitudes.ListarAgrupadas(comprador.Trim());
            return Json(new { valido = true, comprador, solicitudes = lista });
        }

        [HttpPost]
        public IActionResult DetalleSolicitud(string comprador, string numeroCotizacion)
        {
            if (string.IsNullOrWhiteSpace(comprador) || string.IsNullOrWhiteSpace(numeroCotizacion))
            {
                return Json(new { valido = false, mensaje = "Faltan datos de la solicitud." });
            }

            var filas = _solicitudes.Detalle(numeroCotizacion.Trim(), comprador.Trim());
            if (filas.Count == 0)
            {
                return Json(new { valido = false, mensaje = "No encontré esa solicitud." });
            }

            return Json(new { valido = true, numeroCotizacion, filas });
        }

        [HttpPost]
        public IActionResult ConfirmarUsuario(string loteId)
        {
            try
            {
                var lote = _flujo.ConfirmarUsuario(loteId?.Trim() ?? "");
                return Json(new { valido = true, lote });
            }
            catch (InvalidOperationException ex)
            {
                return Json(new { valido = false, mensaje = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult CorrerSap(string loteId)
        {
            if (string.IsNullOrWhiteSpace(loteId))
            {
                return Json(new { valido = false, mensaje = "Falta el lote." });
            }

            var lote = _flujo.CorrerSap(loteId.Trim());
            return Json(new { valido = true, lote });
        }

        [HttpPost]
        public async Task<IActionResult> ProcesarComprador()
        {
            var compradores = _demo.Activo
                ? _demo.ListarCompradores()
                : new List<string>();

            if (compradores.Count == 1)
            {
                return await Pendientes(compradores[0]);
            }

            return Json(new { valido = false, mensaje = "Elige un comprador para ver sus pendientes." });
        }

        private async Task<(List<Dictionary<string, object?>> Filas, List<Dictionary<string, object?>> Catalogo)> CargarAsync(string comprador)
        {
            var catalogo = await ObtenerCatalogoAsync();
            var filas = catalogo
                .Where(r => string.Equals(
                    r.TryGetValue("COMPRADOR_DESC", out var v) ? Convert.ToString(v)?.Trim() : "",
                    comprador.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            return (filas, catalogo);
        }

        private async Task<List<Dictionary<string, object?>>> ObtenerCatalogoAsync()
        {
            if (_demo.Activo)
            {
                return _demo.TodasLasFilas();
            }

            try
            {
                return DataTableMapper.ToDictionaryList(await _sql.ObtenerCompradorAsync());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SQL no disponible; usando datos de prueba");
                return _demo.TodasLasFilas();
            }
        }
    }
}
