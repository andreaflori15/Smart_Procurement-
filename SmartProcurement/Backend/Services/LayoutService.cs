using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SmartProcurement.Models;

namespace SmartProcurement.Services
{
    public class LayoutService
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly string _layoutPath;
        private readonly string _historialPath;
        private readonly TableroJefeService _tablero;
        private readonly ILogger<LayoutService> _logger;

        public LayoutService(
            IWebHostEnvironment env,
            TableroJefeService tablero,
            ILogger<LayoutService> logger)
        {
            var dataDir = Path.Combine(env.ContentRootPath, "Backend", "Data");
            Directory.CreateDirectory(dataDir);
            _layoutPath = Path.Combine(dataDir, "Layout.json");
            _historialPath = Path.Combine(dataDir, "LayoutHistorial.json");
            _tablero = tablero;
            _logger = logger;
        }

        public async Task<object> ListarAsync()
        {
            var store = await LeerLayoutsAsync();
            var historial = await LeerHistorialAsync();
            var fechasPorLayout = historial.Historial
                .GroupBy(h => h.LayoutId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(h => h.FechaGrabacion).Distinct().OrderBy(f => f).ToList());

            return new
            {
                valido = true,
                layouts = store.Layouts.Select(l => new
                {
                    l.Id,
                    l.NombreLayout,
                    l.Prompt,
                    l.TipoEsquema,
                    l.FechaCreacion,
                    l.FechaActualizacion,
                    fechas = fechasPorLayout.GetValueOrDefault(l.Id) ?? []
                })
            };
        }

        public async Task<object> GrabarLayoutAsync(GrabarLayoutRequest req)
        {
            var nombre = (req.NombreLayout ?? "").Trim();
            if (nombre.Length == 0 || nombre.Length > 50)
            {
                return new { valido = false, mensaje = "El nombre del layout debe tener entre 1 y 50 caracteres." };
            }

            var esquema = req.Esquema ?? await _tablero.ExtraerEsquemaAsync();
            var store = await LeerLayoutsAsync();
            var id = Slug(nombre);
            var ahora = DateTime.Now;
            var existente = store.Layouts.FirstOrDefault(l => l.Id == id);

            if (existente is null)
            {
                existente = new LayoutRegistro
                {
                    Id = id,
                    NombreLayout = nombre,
                    FechaCreacion = ahora
                };
                store.Layouts.Add(existente);
            }

            existente.NombreLayout = nombre;
            existente.Prompt = req.Prompt ?? "";
            existente.TipoEsquema = "tablero-completo";
            existente.FechaActualizacion = ahora;
            existente.Esquema = esquema;

            await GuardarLayoutsAsync(store);
            _logger.LogInformation("Layout guardado: {Id} ({Nombre})", id, nombre);

            return new { valido = true, id, mensaje = "Layout guardado." };
        }

        public async Task<object> GrabarHistorialAsync(GrabarHistorialRequest req)
        {
            var layoutId = (req.LayoutId ?? "").Trim();
            if (layoutId.Length == 0)
            {
                return new { valido = false, mensaje = "Selecciona un layout." };
            }

            var store = await LeerLayoutsAsync();
            var layout = store.Layouts.FirstOrDefault(l => l.Id == layoutId);
            if (layout is null)
            {
                return new { valido = false, mensaje = "Layout no encontrado." };
            }

            var esquema = req.Esquema ?? await _tablero.ExtraerEsquemaAsync();
            var historial = await LeerHistorialAsync();
            var fecha = DateTime.Now.ToString("yyyy-MM-dd");

            historial.Historial.RemoveAll(h => h.LayoutId == layoutId && h.FechaGrabacion == fecha);
            historial.Historial.Add(new LayoutHistorialEntrada
            {
                LayoutId = layoutId,
                FechaGrabacion = fecha,
                TipoEsquema = layout.TipoEsquema,
                Esquema = esquema
            });

            await GuardarHistorialAsync(historial);
            _logger.LogInformation("Historial guardado: {LayoutId} {Fecha}", layoutId, fecha);

            return new { valido = true, fecha, mensaje = "Historial guardado para hoy." };
        }

        public async Task<object> HistorialAsync(string? layoutId)
        {
            if (string.IsNullOrWhiteSpace(layoutId))
            {
                return new { valido = false, mensaje = "Layout requerido." };
            }

            var historial = await LeerHistorialAsync();
            var fechas = historial.Historial
                .Where(h => h.LayoutId == layoutId)
                .Select(h => h.FechaGrabacion)
                .Distinct()
                .OrderBy(f => f)
                .ToList();

            return new { valido = true, layoutId, fechas };
        }

        public async Task<object> HistorialDiaAsync(string? layoutId, string? fecha)
        {
            if (string.IsNullOrWhiteSpace(layoutId) || string.IsNullOrWhiteSpace(fecha))
            {
                return new { valido = false, mensaje = "Layout y fecha requeridos." };
            }

            var historial = await LeerHistorialAsync();
            var entrada = historial.Historial
                .Where(h => h.LayoutId == layoutId && h.FechaGrabacion == fecha)
                .OrderByDescending(h => h.FechaGrabacion)
                .FirstOrDefault();

            if (entrada?.Esquema is null)
            {
                return new { valido = false, mensaje = "No hay snapshot para esa fecha." };
            }

            return new
            {
                valido = true,
                layoutId,
                fecha,
                esquema = entrada.Esquema
            };
        }

        public async Task<object?> ObtenerLayoutAsync(string layoutId)
        {
            var store = await LeerLayoutsAsync();
            var layout = store.Layouts.FirstOrDefault(l => l.Id == layoutId);
            return layout is null ? null : new
            {
                valido = true,
                layout = new
                {
                    layout.Id,
                    layout.NombreLayout,
                    layout.Prompt,
                    layout.TipoEsquema,
                    layout.FechaCreacion,
                    layout.FechaActualizacion,
                    layout.Esquema
                }
            };
        }

        private async Task<LayoutStore> LeerLayoutsAsync()
        {
            if (!File.Exists(_layoutPath))
            {
                var vacio = new LayoutStore();
                await GuardarLayoutsAsync(vacio);
                return vacio;
            }

            await using var stream = File.OpenRead(_layoutPath);
            return await JsonSerializer.DeserializeAsync<LayoutStore>(stream, JsonOpts) ?? new LayoutStore();
        }

        private async Task<LayoutHistorialStore> LeerHistorialAsync()
        {
            if (!File.Exists(_historialPath))
            {
                var vacio = new LayoutHistorialStore();
                await GuardarHistorialAsync(vacio);
                return vacio;
            }

            await using var stream = File.OpenRead(_historialPath);
            return await JsonSerializer.DeserializeAsync<LayoutHistorialStore>(stream, JsonOpts) ?? new LayoutHistorialStore();
        }

        private async Task GuardarLayoutsAsync(LayoutStore store)
        {
            await using var stream = File.Create(_layoutPath);
            await JsonSerializer.SerializeAsync(stream, store, JsonOpts);
        }

        private async Task GuardarHistorialAsync(LayoutHistorialStore store)
        {
            await using var stream = File.Create(_historialPath);
            await JsonSerializer.SerializeAsync(stream, store, JsonOpts);
        }

        private static string Slug(string nombre)
        {
            var s = nombre.ToLowerInvariant().Trim();
            s = Regex.Replace(s, @"[^a-z0-9\s-]", "");
            s = Regex.Replace(s, @"\s+", "-");
            s = Regex.Replace(s, @"-+", "-").Trim('-');
            return string.IsNullOrEmpty(s) ? "layout-" + DateTime.Now.Ticks : s;
        }
    }
}
