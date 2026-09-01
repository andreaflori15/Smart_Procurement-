using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartProcurement.Services
{
    public class SolicitudCotizacionService
    {
        private static readonly string[] Columnas =
        [
            "NumeroCotizacion", "FechaCotizacion", "Comprador", "ProveedoresCotizados",
            "SOLPED", "Posicion", "LoteId", "Cubeta", "Estado", "FechaRespuestaProveedor", "OfertasNecesarias"
        ];

        private static readonly Regex SecuenciaCorrelativo = new(@"_(\d{3})$", RegexOptions.Compiled);

        private readonly IWebHostEnvironment _env;
        private readonly ILogger<SolicitudCotizacionService> _logger;
        private readonly object _lock = new();

        public SolicitudCotizacionService(IWebHostEnvironment env, ILogger<SolicitudCotizacionService> logger)
        {
            _env = env;
            _logger = logger;
        }

        public string RegistrarSolicitud(
            string comprador,
            string loteId,
            string cubeta,
            string proveedores,
            IEnumerable<Dictionary<string, object?>> solpeds,
            int ofertasNecesarias)
        {
            lock (_lock)
            {
                var filas = LeerFilas();
                var existente = filas.FirstOrDefault(f =>
                    string.Equals(V(f, "LoteId"), loteId, StringComparison.OrdinalIgnoreCase));

                if (existente is not null)
                {
                    return V(existente, "NumeroCotizacion") ?? "";
                }

                string numero = SiguienteCorrelativo(comprador, filas);
                string fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var listaSolpeds = solpeds.ToList();

                if (listaSolpeds.Count == 0)
                {
                    listaSolpeds.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
                }

                foreach (var solped in listaSolpeds)
                {
                    filas.Add(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["NumeroCotizacion"] = numero,
                        ["FechaCotizacion"] = fecha,
                        ["Comprador"] = comprador.Trim(),
                        ["ProveedoresCotizados"] = proveedores.Trim(),
                        ["SOLPED"] = S(solped, "SOLPED"),
                        ["Posicion"] = S(solped, "NPOs") ?? S(solped, "NPos"),
                        ["LoteId"] = loteId.Trim(),
                        ["Cubeta"] = cubeta.Trim(),
                        ["Estado"] = "no_cotizada",
                        ["FechaRespuestaProveedor"] = "",
                        ["OfertasNecesarias"] = ofertasNecesarias.ToString()
                    });
                }

                GuardarFilas(filas);
                _logger.LogInformation("Solicitud {Numero} registrada para lote {Lote}", numero, loteId);
                return numero;
            }
        }

        public void ActualizarPorOferta(string loteId, int ofertas, int ofertasNecesarias, string? fechaRespuesta)
        {
            if (ofertasNecesarias <= 0)
            {
                ofertasNecesarias = 1;
            }

            bool cotizada = ofertas >= ofertasNecesarias;
            string estado = cotizada ? "cotizada" : "no_cotizada";
            string fecha = cotizada ? (fechaRespuesta ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) : "";

            lock (_lock)
            {
                var filas = LeerFilas();
                bool cambio = false;

                foreach (var fila in filas.Where(f =>
                    string.Equals(V(f, "LoteId"), loteId, StringComparison.OrdinalIgnoreCase)))
                {
                    fila["Estado"] = estado;
                    if (cotizada)
                    {
                        fila["FechaRespuestaProveedor"] = fecha;
                    }

                    cambio = true;
                }

                if (cambio)
                {
                    GuardarFilas(filas);
                    _logger.LogInformation("Lote {Lote} actualizado a {Estado}", loteId, estado);
                }
            }
        }

        public List<Dictionary<string, object?>> ListarAgrupadas(string comprador)
        {
            lock (_lock)
            {
                return LeerFilas()
                    .Where(f => string.Equals(V(f, "Comprador"), comprador.Trim(), StringComparison.OrdinalIgnoreCase))
                    .GroupBy(f => V(f, "NumeroCotizacion") ?? "", StringComparer.OrdinalIgnoreCase)
                    .Select(g =>
                    {
                        var primera = g.First();
                        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["numeroCotizacion"] = g.Key,
                            ["fechaCotizacion"] = V(primera, "FechaCotizacion"),
                            ["proveedoresCotizados"] = V(primera, "ProveedoresCotizados"),
                            ["estado"] = g.Any(r => string.Equals(V(r, "Estado"), "cotizada", StringComparison.OrdinalIgnoreCase))
                                ? "cotizada"
                                : "no_cotizada",
                            ["totalSolpeds"] = g.Count(),
                            ["fechaRespuestaProveedor"] = g.Select(r => V(r, "FechaRespuestaProveedor"))
                                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)),
                            ["loteId"] = V(primera, "LoteId"),
                            ["cubeta"] = V(primera, "Cubeta")
                        };
                    })
                    .OrderByDescending(s => Convert.ToString(s["fechaCotizacion"]))
                    .ToList();
            }
        }

        public List<Dictionary<string, object?>> Detalle(string numeroCotizacion, string comprador)
        {
            lock (_lock)
            {
                return LeerFilas()
                    .Where(f =>
                        string.Equals(V(f, "NumeroCotizacion"), numeroCotizacion.Trim(), StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(V(f, "Comprador"), comprador.Trim(), StringComparison.OrdinalIgnoreCase))
                    .Select(f => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["numeroCotizacion"] = V(f, "NumeroCotizacion"),
                        ["fechaCotizacion"] = V(f, "FechaCotizacion"),
                        ["comprador"] = V(f, "Comprador"),
                        ["proveedoresCotizados"] = V(f, "ProveedoresCotizados"),
                        ["solped"] = V(f, "SOLPED"),
                        ["posicion"] = V(f, "Posicion"),
                        ["loteId"] = V(f, "LoteId"),
                        ["cubeta"] = V(f, "Cubeta"),
                        ["estado"] = V(f, "Estado"),
                        ["fechaRespuestaProveedor"] = V(f, "FechaRespuestaProveedor")
                    })
                    .OrderBy(r => Convert.ToString(r["solped"]))
                    .ThenBy(r => Convert.ToString(r["posicion"]))
                    .ToList();
            }
        }

        public static List<Dictionary<string, object?>> ParsearItems(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            try
            {
                var items = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json);
                if (items is null)
                {
                    return [];
                }

                return items.Select(item =>
                {
                    var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in item)
                    {
                        dict[kv.Key] = kv.Value.ValueKind switch
                        {
                            JsonValueKind.String => kv.Value.GetString(),
                            JsonValueKind.Number => kv.Value.GetRawText(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.Null => null,
                            _ => kv.Value.GetRawText()
                        };
                    }

                    return dict;
                }).ToList();
            }
            catch
            {
                return [];
            }
        }

        private string SiguienteCorrelativo(string comprador, List<Dictionary<string, string?>> filas)
        {
            string prefijo = PrefijoComprador(comprador);
            string patron = "COT_" + prefijo + "_";

            int max = filas
                .Select(f => V(f, "NumeroCotizacion"))
                .Where(n => n is not null && n.StartsWith(patron, StringComparison.OrdinalIgnoreCase))
                .Select(n =>
                {
                    var m = SecuenciaCorrelativo.Match(n!);
                    return m.Success && int.TryParse(m.Groups[1].Value, out int seq) ? seq : 0;
                })
                .DefaultIfEmpty(0)
                .Max();

            return patron + (max + 1).ToString("D3");
        }

        private static string PrefijoComprador(string comprador)
        {
            var partes = comprador.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string nombre = partes.Length > 0 ? partes[0] : comprador;
            var slug = string.Concat(nombre.Where(char.IsLetterOrDigit)).ToUpperInvariant();
            return slug.Length > 0 ? slug : "COMPRADOR";
        }

        private string RutaCsv() =>
            Path.Combine(_env.ContentRootPath, "Backend", "Data", "solicitudes_cotizacion.csv");

        private List<Dictionary<string, string?>> LeerFilas()
        {
            string ruta = RutaCsv();
            if (!File.Exists(ruta))
            {
                GuardarFilas([]);
                return [];
            }

            var lineas = File.ReadAllLines(ruta, Encoding.UTF8);
            if (lineas.Length < 2)
            {
                return [];
            }

            string[] encabezados = lineas[0].Split(';').Select(h => h.Trim().TrimStart('\uFEFF')).ToArray();
            var filas = new List<Dictionary<string, string?>>();

            foreach (string linea in lineas.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(linea))
                {
                    continue;
                }

                string[] cols = linea.Split(';');
                var fila = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < encabezados.Length; i++)
                {
                    string crudo = i < cols.Length ? cols[i].Trim() : "";
                    fila[encabezados[i]] = crudo.Length == 0 ? null : crudo;
                }

                filas.Add(fila);
            }

            return filas;
        }

        private void GuardarFilas(List<Dictionary<string, string?>> filas)
        {
            string ruta = RutaCsv();
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(";", Columnas));

            foreach (var fila in filas)
            {
                var cols = Columnas.Select(c =>
                {
                    string? val = fila.TryGetValue(c, out var v) ? v : null;
                    return Escapar(val ?? "");
                });
                sb.AppendLine(string.Join(";", cols));
            }

            File.WriteAllText(ruta, sb.ToString(), Encoding.UTF8);
        }

        private static string Escapar(string valor)
        {
            if (valor.Contains(';') || valor.Contains('"') || valor.Contains('\n'))
            {
                return "\"" + valor.Replace("\"", "\"\"") + "\"";
            }

            return valor;
        }

        private static string? V(Dictionary<string, string?> fila, string clave) =>
            fila.TryGetValue(clave, out var v) ? v : null;

        private static string? S(Dictionary<string, object?> fila, string clave) =>
            fila.TryGetValue(clave, out var v) ? Convert.ToString(v) : null;
    }
}
