using Microsoft.AspNetCore.Mvc;
using SmartProcurement.Services;

namespace SmartProcurement.Controllers
{
    public class ConsultaController : Controller
    {
        private readonly SqlServerService _sql;
        private readonly ConsultaIntentService _intent;
        private readonly DemoDataService _demo;
        private readonly ILogger<ConsultaController> _logger;

        public ConsultaController(SqlServerService sql, ConsultaIntentService intent, DemoDataService demo, ILogger<ConsultaController> logger)
        {
            _sql = sql;
            _intent = intent;
            _demo = demo;
            _logger = logger;
        }

        public IActionResult Index() => View();

        [HttpPost]
        public async Task<IActionResult> ProcesarConsulta(string prompt)
        {
            try
            {
                var filtro = await _intent.ResolverAsync(prompt);

                if (!filtro.Valido)
                {
                    return Json(new { valido = false, mensaje = filtro.Mensaje, respuestaIA = filtro.RespuestaIa });
                }

                if (_demo.Activo)
                {
                    var traza = _demo.ObtenerTrazabilidadConsulta(filtro);
                    return Json(new
                    {
                        valido = true,
                        demo = true,
                        vista = "trazabilidad",
                        tipo = filtro.Tipo,
                        origen = filtro.UsoIa ? "ia" : "directo",
                        traza
                    });
                }

                // Producción SQL: aún tabla clásica hasta conectar traza real
                var datos = await _sql.ObtenerSolpedAsync(filtro);
                return Json(new
                {
                    valido = true,
                    vista = "tabla",
                    tipo = filtro.Tipo,
                    origen = filtro.UsoIa ? "ia" : "directo",
                    datos = DataTableMapper.ToDictionaryList(datos)
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SQL no disponible; usando trazabilidad demo para Consulta");
                try
                {
                    var filtro = await _intent.ResolverAsync(prompt);
                    if (!filtro.Valido)
                    {
                        return Json(new { valido = false, mensaje = filtro.Mensaje });
                    }

                    var traza = _demo.ObtenerTrazabilidadConsulta(filtro);
                    return Json(new
                    {
                        valido = true,
                        demo = true,
                        vista = "trazabilidad",
                        tipo = filtro.Tipo,
                        origen = filtro.UsoIa ? "ia" : "directo",
                        traza
                    });
                }
                catch
                {
                    return Json(new { valido = false, mensaje = "No pude completar la consulta.", detalle = ex.Message });
                }
            }
        }
    }
}
