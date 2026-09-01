using Microsoft.AspNetCore.Mvc;
using SmartProcurement.Models;
using SmartProcurement.Services;

namespace SmartProcurement.Controllers
{
    public class JefeCompraController : Controller
    {
        private readonly TableroJefeService _tablero;
        private readonly LayoutService _layouts;
        private readonly ILogger<JefeCompraController> _logger;

        public JefeCompraController(
            TableroJefeService tablero,
            LayoutService layouts,
            ILogger<JefeCompraController> logger)
        {
            _tablero = tablero;
            _layouts = layouts;
            _logger = logger;
        }

        public IActionResult Index() => View();

        [HttpGet]
        [HttpPost]
        public async Task<IActionResult> Tablero()
        {
            try
            {
                return Json(await _tablero.ArmarAsync());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al armar tablero de jefe");
                return Json(new { valido = false, mensaje = "No pude armar el tablero." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Preguntar(string prompt)
        {
            try
            {
                return Json(await _tablero.PreguntarAsync(prompt));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al preguntar al tablero");
                return Json(new { valido = false, mensaje = "No pude responder esa pregunta." });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Layouts() => Json(await _layouts.ListarAsync());

        [HttpPost]
        public async Task<IActionResult> GrabarLayout([FromBody] GrabarLayoutRequest req)
        {
            try
            {
                return Json(await _layouts.GrabarLayoutAsync(req));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al grabar layout");
                return Json(new { valido = false, mensaje = "No pude grabar el layout." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> GrabarHistorial([FromBody] GrabarHistorialRequest req)
        {
            try
            {
                return Json(await _layouts.GrabarHistorialAsync(req));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al grabar historial");
                return Json(new { valido = false, mensaje = "No pude grabar el historial." });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Layout(string id)
        {
            var layout = await _layouts.ObtenerLayoutAsync(id);
            if (layout is null)
            {
                return Json(new { valido = false, mensaje = "Layout no encontrado." });
            }

            return Json(layout);
        }

        [HttpGet]
        public async Task<IActionResult> Historial(string layoutId) => Json(await _layouts.HistorialAsync(layoutId));

        [HttpGet]
        public async Task<IActionResult> HistorialDia(string layoutId, string fecha) =>
            Json(await _layouts.HistorialDiaAsync(layoutId, fecha));

        [HttpPost]
        public Task<IActionResult> ProcesarJefeCompra() => Tablero();
    }
}
