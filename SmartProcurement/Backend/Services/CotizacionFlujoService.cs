using System.Collections.Concurrent;
using SmartProcurement.Models;

namespace SmartProcurement.Services
{
    public class CotizacionFlujoService
    {
        private const long MaxBytes = 10 * 1024 * 1024;
        private readonly ConcurrentDictionary<string, LoteEstado> _lotes = new(StringComparer.OrdinalIgnoreCase);
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<CotizacionFlujoService> _logger;

        public CotizacionFlujoService(IWebHostEnvironment env, ILogger<CotizacionFlujoService> logger)
        {
            _env = env;
            _logger = logger;
        }

        public LoteEstado Ver(string loteId, string cubeta)
        {
            return _lotes.GetOrAdd(loteId, _ => Nuevo(loteId, cubeta));
        }

        public Dictionary<string, LoteEstado> Todos() =>
            _lotes.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        public LoteEstado Solicitar(string loteId, string cubeta)
        {
            var lote = _lotes.AddOrUpdate(
                loteId,
                _ =>
                {
                    var n = Nuevo(loteId, cubeta);
                    MarcarSolicitud(n);
                    return n;
                },
                (_, actual) =>
                {
                    if (actual.EstadoId == "sin_solicitar")
                    {
                        MarcarSolicitud(actual);
                    }

                    return actual;
                });

            return lote;
        }

        public async Task<LoteEstado> SubirPdfAsync(string loteId, IFormFile archivo, string? proveedor)
        {
            if (!_lotes.TryGetValue(loteId, out var lote) || lote.EstadoId == "sin_solicitar")
            {
                throw new InvalidOperationException("Primero solicita la cotización.");
            }

            ValidarPdf(archivo);

            string carpeta = CarpetaLote(loteId);
            string destino = Path.Combine(_env.WebRootPath, "uploads", "cotizaciones", carpeta);
            Directory.CreateDirectory(destino);

            string seguro = Path.GetFileNameWithoutExtension(archivo.FileName);
            seguro = string.Concat(seguro.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ')).Trim();
            if (seguro.Length == 0)
            {
                seguro = "cotizacion";
            }

            string nombre = DateTime.Now.ToString("yyyyMMddHHmmss") + "_" + seguro + ".pdf";
            string ruta = Path.Combine(destino, nombre);
            await using (var stream = File.Create(ruta))
            {
                await archivo.CopyToAsync(stream);
            }

            var adjunto = new CotizacionArchivo
            {
                Nombre = archivo.FileName,
                Url = "/uploads/cotizaciones/" + carpeta + "/" + Uri.EscapeDataString(nombre),
                Proveedor = string.IsNullOrWhiteSpace(proveedor) ? null : proveedor.Trim(),
                Fecha = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
            };

            lock (lote)
            {
                lote.Archivos.Add(adjunto);
                if (lote.EstadoId is not ("listo_comparativo" or "confirmar_usuario" or "pedido_sap"))
                {
                    AvanzarPorOferta(lote);
                }
            }

            _logger.LogInformation("PDF de cotización {Archivo} en lote {Lote}", nombre, loteId);
            return lote;
        }

        public LoteEstado ConfirmarUsuario(string loteId)
        {
            if (!_lotes.TryGetValue(loteId, out var lote))
            {
                throw new InvalidOperationException("No hay un lote con esa cotización.");
            }

            lote.EstadoId = "pedido_sap";
            lote.Estado = "Pedido SAP";
            return lote;
        }

        public LoteEstado CorrerSap(string loteId)
        {
            var lote = _lotes.AddOrUpdate(
                loteId,
                _ => new LoteEstado
                {
                    LoteId = loteId,
                    EstadoId = "pedido_sap",
                    Estado = "Pedido SAP",
                    OfertasNecesarias = 0,
                    Fecha = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
                },
                (_, actual) =>
                {
                    actual.EstadoId = "pedido_sap";
                    actual.Estado = "Pedido SAP";
                    actual.Fecha = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
                    return actual;
                });

            return lote;
        }

        private static LoteEstado Nuevo(string loteId, string cubeta)
        {
            int need = cubeta == ClasificacionComprasService.Licitacion
                ? 2
                : cubeta == ClasificacionComprasService.Contrato ? 0 : 1;

            return new LoteEstado
            {
                LoteId = loteId,
                EstadoId = "sin_solicitar",
                Estado = "Sin solicitar",
                OfertasNecesarias = need
            };
        }

        private static void MarcarSolicitud(LoteEstado lote)
        {
            lote.EstadoId = "pendiente_proveedor";
            lote.Estado = "Pendiente respuesta proveedor";
            lote.Ofertas = 0;
            lote.Fecha = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        }

        private static void AvanzarPorOferta(LoteEstado lote)
        {
            lote.Ofertas = lote.Archivos.Count;
            if (lote.Ofertas >= lote.OfertasNecesarias)
            {
                if (lote.OfertasNecesarias >= 2)
                {
                    lote.EstadoId = "listo_comparativo";
                    lote.Estado = "Listo para comparativo";
                }
                else
                {
                    lote.EstadoId = "confirmar_usuario";
                    lote.Estado = "En espera de confirmación usuario";
                }
            }
            else
            {
                lote.EstadoId = "ofertas_parciales";
                lote.Estado = $"Ofertas parciales ({lote.Ofertas} de {lote.OfertasNecesarias})";
            }
        }

        private static void ValidarPdf(IFormFile archivo)
        {
            if (archivo.Length <= 0)
            {
                throw new InvalidOperationException("El archivo está vacío.");
            }

            if (archivo.Length > MaxBytes)
            {
                throw new InvalidOperationException("El PDF no puede pesar más de 10 MB.");
            }

            string ext = Path.GetExtension(archivo.FileName);
            if (!string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Solo se acepta PDF.");
            }

            Span<byte> cabeza = stackalloc byte[5];
            using var stream = archivo.OpenReadStream();
            int leidos = stream.Read(cabeza);
            stream.Position = 0;
            if (leidos < 5 || cabeza[0] != (byte)'%' || cabeza[1] != (byte)'P' || cabeza[2] != (byte)'D' || cabeza[3] != (byte)'F')
            {
                throw new InvalidOperationException("Ese archivo no parece un PDF.");
            }
        }

        private static string CarpetaLote(string loteId)
        {
            var chars = loteId.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
            string slug = new string(chars).Trim('_');
            if (slug.Length > 80)
            {
                slug = slug[..80];
            }

            return string.IsNullOrWhiteSpace(slug) ? "lote" : slug;
        }
    }
}
