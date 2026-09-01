using System.Text;
using SmartProcurement.Models;

namespace SmartProcurement.Services
{
    public class DemoDataService
    {
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DemoDataService> _logger;
        private readonly Lazy<List<Dictionary<string, string?>>> _filas;
        private readonly Lazy<List<Dictionary<string, string?>>> _traza;

        public DemoDataService(IWebHostEnvironment env, IConfiguration configuration, ILogger<DemoDataService> logger)
        {
            _env = env;
            _configuration = configuration;
            _logger = logger;
            _filas = new Lazy<List<Dictionary<string, string?>>>(() => CargarCsv("solpeds-demo.csv"));
            _traza = new Lazy<List<Dictionary<string, string?>>>(() => CargarCsv("pedido_traza.csv"));
        }

        public bool Activo => _configuration.GetValue("Demo:UsarDatosDePrueba", false);

        public int TotalFilas => _filas.Value.Count;

        public List<string> ListarCompradores()
        {
            return _filas.Value
                .Select(r => (V(r, "COMPRADOR_DESC") ?? "").Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();
        }

        public List<Dictionary<string, object?>> FilasDeComprador(string comprador)
        {
            return _filas.Value
                .Where(r => string.Equals((V(r, "COMPRADOR_DESC") ?? "").Trim(), comprador.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(ADiccionario)
                .ToList();
        }

        public List<Dictionary<string, object?>> TodasLasFilas()
        {
            return _filas.Value.Select(ADiccionario).ToList();
        }

        public List<Dictionary<string, object?>> ObtenerComprador(int max = 500)
        {
            return _filas.Value
                .OrderByDescending(r => !string.IsNullOrWhiteSpace(V(r, "RazSocial")))
                .ThenBy(r => V(r, "SOLPED"))
                .ThenBy(r => V(r, "NPOs"))
                .Take(max)
                .Select(ADiccionario)
                .ToList();
        }

        public List<Dictionary<string, object?>> ObtenerJefeCompra(int max = 400)
        {
            return _filas.Value
                .GroupBy(r => (V(r, "SOLPED"), V(r, "NPOs"), V(r, "Centro"), V(r, "Material")))
                .Select(g =>
                {
                    var r = g.First();
                    return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Accion_Usuario_Fecha"] = V(r, "Accion_Usuario_Fecha"),
                        ["SOLPED"] = V(r, "SOLPED"),
                        ["NPOs"] = V(r, "NPOs"),
                        ["Centro"] = V(r, "Centro"),
                        ["Material"] = V(r, "Material"),
                        ["Texto_Breve"] = V(r, "Texto_Breve"),
                        ["Cantidad_Solicitada"] = V(r, "Cantidad_Solicitada"),
                        ["UNIDAD_MEDIDA"] = V(r, "Unidad_Medida"),
                        ["MONEDA"] = V(r, "Moneda"),
                        ["IMPORTE"] = V(r, "PrecioNeto"),
                        ["Pro"] = V(r, "Pro"),
                        ["RazSocial"] = V(r, "RazSocial"),
                        ["COMPRADOR_DESC"] = V(r, "COMPRADOR_DESC"),
                        ["RANGO_FECHA"] = V(r, "RANGO_FECHA")
                    };
                })
                .Take(max)
                .ToList();
        }

        public List<Dictionary<string, object?>> ObtenerConsulta(ConsultaFiltro filtro, int max = 200)
        {
            IEnumerable<Dictionary<string, string?>> query = _filas.Value;

            if (filtro.Tipo.Equals("SOLPED", StringComparison.OrdinalIgnoreCase))
            {
                var set = filtro.Valores.ToHashSet(StringComparer.OrdinalIgnoreCase);
                query = query.Where(r => set.Contains(V(r, "SOLPED") ?? ""));
            }
            else if (filtro.Tipo.Equals("PEDIDO", StringComparison.OrdinalIgnoreCase))
            {
                var set = filtro.Valores.ToHashSet(StringComparer.OrdinalIgnoreCase);
                query = query.Where(r => set.Contains(V(r, "NumPed") ?? ""));
            }
            else if (filtro.Tipo.Equals("PROVEEDOR", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(r =>
                {
                    string nombre = V(r, "RazSocial") ?? "";
                    return filtro.Valores.Any(v => nombre.Contains(v, StringComparison.OrdinalIgnoreCase));
                });
            }
            else
            {
                return [];
            }

            return query
                .Take(max)
                .Select(r => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["NSOLPED"] = V(r, "SOLPED"),
                    ["NPedido"] = V(r, "NumPed"),
                    ["NPos"] = V(r, "NPOs"),
                    ["Razon_Social"] = V(r, "RazSocial"),
                    ["Fecha"] = V(r, "FecPed") ?? V(r, "Accion_Usuario_Fecha"),
                    ["Moneda"] = V(r, "Moneda"),
                    ["Estado_Ped"] = string.IsNullOrWhiteSpace(V(r, "NumPed")) ? "Sin pedido" : "Con pedido histórico",
                    ["Tipo de compra Denominacion"] = V(r, "TipoCompra_Den")
                })
                .ToList();
        }

        public object ObtenerTrazabilidadConsulta(ConsultaFiltro filtro, int maxDocs = 12)
        {
            var etapas = DefinirEtapas();
            var documentos = new List<object>();

            if (filtro.Tipo.Equals("PROVEEDOR", StringComparison.OrdinalIgnoreCase))
            {
                var hits = _traza.Value
                    .Where(r =>
                    {
                        string nombre = Tv(r, "RAZON_SOCIAL", "Razón social", "RazSocial") ?? "";
                        return filtro.Valores.Any(v => nombre.Contains(v, StringComparison.OrdinalIgnoreCase));
                    })
                    .GroupBy(r => Tv(r, "SOLPED", "NSOLPED") ?? "")
                    .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                    .Take(maxDocs)
                    .ToList();

                foreach (var g in hits)
                {
                    var fila = MejorFila(g);
                    documentos.Add(ArmarDocumento(fila, g.Key, "SOLPED", true));
                }
            }
            else
            {
                foreach (var valor in filtro.Valores.Take(maxDocs))
                {
                    List<Dictionary<string, string?>> filas;
                    if (filtro.Tipo.Equals("SOLPED", StringComparison.OrdinalIgnoreCase))
                    {
                        filas = _traza.Value
                            .Where(r => string.Equals(Tv(r, "SOLPED", "NSOLPED"), valor, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                    }
                    else if (filtro.Tipo.Equals("PEDIDO", StringComparison.OrdinalIgnoreCase))
                    {
                        filas = _traza.Value
                            .Where(r => string.Equals(Tv(r, "PEDIDO", "NumPed", "NPedido"), valor, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                    }
                    else
                    {
                        filas = [];
                    }

                    if (filas.Count == 0)
                    {
                        documentos.Add(DocumentoVacio(valor, filtro.Tipo));
                        continue;
                    }

                    documentos.Add(ArmarDocumento(MejorFila(filas), valor, filtro.Tipo, true));
                }
            }

            return new
            {
                filasEtapa = etapas,
                columnas = DefinirColumnas(),
                documentos
            };
        }

        /// <summary>
        /// Prefiere filas con aprobación final de alcance (versión más completa del flujo).
        /// </summary>
        private static Dictionary<string, string?> MejorFila(IEnumerable<Dictionary<string, string?>> filas)
        {
            return filas
                .OrderByDescending(r => ParseFecha(Tv(r, "FEC_ALCANCE_APROB_FINAL")) is not null)
                .ThenByDescending(r => ParseFecha(Tv(r, "INFORME_FECHA_APROB")) is not null)
                .ThenByDescending(r => ParseFecha(Tv(r, "FEC_PAGO_CONV")) is not null)
                .ThenBy(r => Tv(r, "SOLPED_POS") ?? "")
                .ThenByDescending(r => Tv(r, "ALCANCE_VERSION") ?? "")
                .First();
        }

        private static object[] DefinirEtapas() =>
        [
            new { id = "solped", titulo = "Número de SOLPED", icono = "/img/consulta/01_SOLPED.png", orden = 1 },
            new { id = "alcance", titulo = "Alcance Técnico", icono = "/img/consulta/01_Alcance_Tecnico.jpg", orden = 2 },
            new { id = "informe", titulo = "Informe de Trabajo", icono = "/img/consulta/01_Informe_de_Trabajo.jpg", orden = 3 },
            new { id = "pedido", titulo = "Número Pedido", icono = "/img/consulta/02_Pedido.png", orden = 4 },
            new { id = "estadoPedido", titulo = "Estado pedido", icono = "/img/consulta/03_Estado_pedido.jpg", orden = 5 },
            new { id = "ingreso", titulo = "Ingreso Mat / HEs", icono = "/img/consulta/04_HES_INGMAT.png", orden = 6 },
            new { id = "estadoIngreso", titulo = "Estado Ingreso Mat / HEs", icono = "/img/consulta/05_Estado_HES_INGMAT.png", orden = 7 },
            new { id = "facturado", titulo = "Facturado", icono = "/img/consulta/06_Factura.jpg", orden = 8 },
            new { id = "pagado", titulo = "Pagado", icono = "/img/consulta/07_Pagado.jpg", orden = 9 }
        ];

        /// <summary>
        /// Columnas de UI: etapas + días entre cada proceso + ciclos resumen.
        /// </summary>
        private static object[] DefinirColumnas() =>
        [
            new { id = "solped", tipo = "etapa", titulo = "Número de SOLPED", icono = "/img/consulta/01_SOLPED.png" },
            new { id = "dias_solped_alcance", tipo = "dias", titulo = "Días", desde = "SOLPED", hasta = "Alcance" },
            new { id = "alcance", tipo = "etapa", titulo = "Alcance Técnico", icono = "/img/consulta/01_Alcance_Tecnico.jpg" },
            new { id = "dias_alcance_informe", tipo = "dias", titulo = "Días", desde = "Alcance", hasta = "Informe" },
            new { id = "informe", tipo = "etapa", titulo = "Informe de Trabajo", icono = "/img/consulta/01_Informe_de_Trabajo.jpg" },
            new { id = "dias_informe_pedido", tipo = "dias", titulo = "Días", desde = "Informe", hasta = "Pedido" },
            new { id = "pedido", tipo = "etapa", titulo = "Número Pedido", icono = "/img/consulta/02_Pedido.png" },
            new { id = "dias_pedido_estado", tipo = "dias", titulo = "Días", desde = "Pedido", hasta = "Liberación" },
            new { id = "estadoPedido", tipo = "etapa", titulo = "Estado pedido", icono = "/img/consulta/03_Estado_pedido.jpg" },
            new { id = "dias_estado_hes", tipo = "dias", titulo = "Días", desde = "Liberación", hasta = "HES" },
            new { id = "ingreso", tipo = "etapa", titulo = "Ingreso Mat / HEs", icono = "/img/consulta/04_HES_INGMAT.png" },
            new { id = "estadoIngreso", tipo = "etapa", titulo = "Estado Ingreso Mat / HEs", icono = "/img/consulta/05_Estado_HES_INGMAT.png" },
            new { id = "dias_hes_factura", tipo = "dias", titulo = "Días", desde = "HES", hasta = "Factura" },
            new { id = "facturado", tipo = "etapa", titulo = "Facturado", icono = "/img/consulta/06_Factura.jpg" },
            new { id = "dias_factura_pago", tipo = "dias", titulo = "Días", desde = "Factura", hasta = "Pago" },
            new { id = "pagado", tipo = "etapa", titulo = "Pagado", icono = "/img/consulta/07_Pagado.jpg" },
            new { id = "dias_ciclo_total", tipo = "dias", titulo = "Ciclo total", desde = "SOLPED", hasta = "Pago" },
            new { id = "dias_ciclo_compra", tipo = "dias", titulo = "Ciclo compra", desde = "Pedido", hasta = "Pago" }
        ];

        private static object DocumentoVacio(string valor, string tipo) => new
        {
            id = valor,
            tipo,
            existe = false,
            titulo = (tipo.Equals("PEDIDO", StringComparison.OrdinalIgnoreCase) ? "Pedido " : "SOLPED ") + valor,
            celdas = CeldasVacias()
        };

        private static Dictionary<string, object?> CeldasVacias() => new()
        {
            ["solped"] = new { linea1 = "", linea2 = "", linea3 = "" },
            ["dias_solped_alcance"] = null,
            ["alcance"] = new { linea1 = "", linea2 = "", linea3 = "" },
            ["dias_alcance_informe"] = null,
            ["informe"] = new { linea1 = "", linea2 = "", linea3 = "" },
            ["dias_informe_pedido"] = null,
            ["pedido"] = new { linea1 = "", linea2 = "", linea3 = "", linea4 = "" },
            ["dias_pedido_estado"] = null,
            ["estadoPedido"] = new { linea1 = "", linea2 = "" },
            ["dias_estado_hes"] = null,
            ["ingreso"] = new { doc = "", fecha = "" },
            ["estadoIngreso"] = new { linea1 = "", linea2 = "" },
            ["dias_hes_factura"] = null,
            ["facturado"] = new { linea1 = "", linea2 = "" },
            ["dias_factura_pago"] = null,
            ["pagado"] = new { linea1 = "", linea2 = "" },
            ["dias_ciclo_total"] = null,
            ["dias_ciclo_compra"] = null
        };

        private static object ArmarDocumento(Dictionary<string, string?> fila, string valor, string tipo, bool existe)
        {
            string solped = Tv(fila, "SOLPED") ?? "";
            string posSolped = Tv(fila, "SOLPED_POS") ?? "";
            string pedido = Tv(fila, "PEDIDO") ?? "";
            string posPedido = Tv(fila, "PEDIDO_POS") ?? "";
            string razon = Tv(fila, "RAZON_SOCIAL") ?? "";
            string estadoLib = Tv(fila, "PEDIDO_ESTADO_LIBERA") ?? "";
            string hes = Tv(fila, "HES") ?? "";
            string factura = Tv(fila, "NroFacturaProveedor") ?? "";
            string docPago = Tv(fila, "DocPago") ?? "";
            string alcanceNro = Tv(fila, "ALCANCE_NRO") ?? "";
            string alcanceEstado = Tv(fila, "ALCANCE_ESTADO") ?? "";
            string informeNro = Tv(fila, "INFORME_NRO") ?? "";
            string informeEstado = Tv(fila, "INFORME_ESTADO") ?? "";

            // Fechas según fórmulas V02
            DateTime? fSolpedLib = ParseFecha(Tv(fila, "SOLPED_FEC_LIBERACION"));
            DateTime? fAlcance = ParseFecha(Tv(fila, "FEC_ALCANCE_APROB_FINAL"));
            DateTime? fInforme = ParseFecha(Tv(fila, "INFORME_FECHA_APROB"));
            DateTime? fPedido = ParseFecha(Tv(fila, "PEDIDO_FECHA_CREACION"));
            DateTime? fLiberaFin = ParseFecha(Tv(fila, "PEDIDO_FECHA_LIBERA_FIN"));
            DateTime? fHes = ParseFecha(Tv(fila, "HES_FECHA_CREACION"));
            DateTime? fFactura = ParseFecha(Tv(fila, "FEC_FACTURA_CONV"));
            DateTime? fPago = ParseFecha(Tv(fila, "FEC_PAGO_CONV"));

            string titulo = tipo.Equals("PEDIDO", StringComparison.OrdinalIgnoreCase)
                ? "Pedido " + valor
                : "SOLPED " + valor;

            return new
            {
                id = valor,
                tipo,
                existe,
                titulo,
                celdas = new Dictionary<string, object?>
                {
                    ["solped"] = new
                    {
                        linea1 = solped,
                        linea2 = string.IsNullOrWhiteSpace(posSolped) ? "" : "Pos. " + posSolped,
                        linea3 = FormatearFecha(Tv(fila, "SOLPED_FEC_LIBERACION"))
                    },
                    // FEC_ALCANCE_APROB_FINAL − SOLPED_FEC_LIBERACION
                    ["dias_solped_alcance"] = DiasEntre(fSolpedLib, fAlcance),
                    ["alcance"] = new
                    {
                        linea1 = string.IsNullOrWhiteSpace(alcanceNro) ? "" : "N° " + alcanceNro,
                        linea2 = alcanceEstado,
                        linea3 = FormatearFecha(Tv(fila, "FEC_ALCANCE_APROB_FINAL"))
                    },
                    // INFORME_FECHA_APROB − FEC_ALCANCE_APROB_FINAL
                    ["dias_alcance_informe"] = DiasEntre(fAlcance, fInforme),
                    ["informe"] = new
                    {
                        linea1 = string.IsNullOrWhiteSpace(informeNro) ? "" : "N° " + informeNro,
                        linea2 = informeEstado,
                        linea3 = FormatearFecha(Tv(fila, "INFORME_FECHA_APROB"))
                    },
                    // PEDIDO_FECHA_CREACION − INFORME_FECHA_APROB
                    ["dias_informe_pedido"] = DiasEntre(fInforme, fPedido),
                    ["pedido"] = new
                    {
                        linea1 = pedido,
                        linea2 = string.IsNullOrWhiteSpace(posPedido) ? "" : "Pos. " + posPedido,
                        linea3 = razon,
                        linea4 = FormatearFecha(Tv(fila, "PEDIDO_FECHA_CREACION"))
                    },
                    // PEDIDO_FECHA_LIBERA_FIN − PEDIDO_FECHA_CREACION
                    ["dias_pedido_estado"] = DiasEntre(fPedido, fLiberaFin),
                    ["estadoPedido"] = new
                    {
                        linea1 = EstadoPedido(estadoLib, fLiberaFin),
                        linea2 = FormatearFecha(Tv(fila, "PEDIDO_FECHA_LIBERA_FIN"))
                    },
                    // HES_FECHA_CREACION − PEDIDO_FECHA_LIBERA_FIN
                    ["dias_estado_hes"] = DiasEntre(fLiberaFin, fHes),
                    ["ingreso"] = new
                    {
                        doc = string.IsNullOrWhiteSpace(hes) ? "" : "HES " + hes,
                        fecha = FormatearFecha(Tv(fila, "HES_FECHA_CREACION"))
                    },
                    ["estadoIngreso"] = new
                    {
                        linea1 = EstadoIngreso(hes, fHes),
                        linea2 = FormatearFecha(Tv(fila, "HES_FECHA_CREACION"))
                    },
                    // FEC_FACTURA_CONV − HES_FECHA_CREACION
                    ["dias_hes_factura"] = DiasEntre(fHes, fFactura),
                    ["facturado"] = new
                    {
                        linea1 = factura,
                        linea2 = FormatearFecha(Tv(fila, "FEC_FACTURA_CONV"))
                    },
                    // FEC_PAGO_CONV − FEC_FACTURA_CONV
                    ["dias_factura_pago"] = DiasEntre(fFactura, fPago),
                    ["pagado"] = new
                    {
                        linea1 = string.IsNullOrWhiteSpace(docPago) ? "" : "Doc " + docPago,
                        linea2 = FormatearFecha(Tv(fila, "FEC_PAGO_CONV"))
                    },
                    // FEC_PAGO_CONV − SOLPED_FEC_LIBERACION
                    ["dias_ciclo_total"] = DiasEntre(fSolpedLib, fPago),
                    // FEC_PAGO_CONV − PEDIDO_FECHA_CREACION
                    ["dias_ciclo_compra"] = DiasEntre(fPedido, fPago)
                }
            };
        }

        private static int? DiasEntre(DateTime? desde, DateTime? hasta)
        {
            if (desde is null || hasta is null) return null;
            return (int)(hasta.Value.Date - desde.Value.Date).TotalDays;
        }

        private static DateTime? ParseFecha(string? f)
        {
            if (string.IsNullOrWhiteSpace(f)) return null;
            if (DateTime.TryParse(f, out var d)) return d;

            // AAAAMMDD (FechaFactura / FechaPago crudas)
            string digits = new string(f.Where(char.IsDigit).ToArray());
            if (digits.Length == 8 &&
                DateTime.TryParseExact(digits, "yyyyMMdd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var d2))
            {
                return d2;
            }

            return null;
        }

        private static string EstadoPedido(string? raw, DateTime? liberaFin)
        {
            if (liberaFin is not null ||
                string.Equals(raw?.Trim(), "Liberado", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(raw?.Trim(), "Liberación concluida", StringComparison.OrdinalIgnoreCase))
            {
                return "Liberación concluida";
            }

            return "Pendiente Liberacion";
        }

        private static string EstadoIngreso(string? hes, DateTime? fHes)
        {
            if (!string.IsNullOrWhiteSpace(hes) || fHes is not null)
            {
                return "Aprobado";
            }

            return "Pendiente";
        }

        private static string FormatearFecha(string? f)
        {
            if (string.IsNullOrWhiteSpace(f)) return "";
            var d = ParseFecha(f);
            return d is null ? f : d.Value.ToString("dd/MM/yyyy");
        }

        private List<Dictionary<string, string?>> CargarCsv(string nombreArchivo)
        {
            string ruta = Path.Combine(_env.ContentRootPath, "Backend", "Data", nombreArchivo);
            if (!File.Exists(ruta))
            {
                throw new FileNotFoundException($"No encontré Backend/Data/{nombreArchivo}", ruta);
            }

            var lineas = File.ReadAllLines(ruta, Encoding.UTF8);
            if (lineas.Length < 2)
            {
                return [];
            }

            string[] encabezados = lineas[0].Split(';').Select(h => h.Trim().TrimStart('\uFEFF')).ToArray();
            var filas = new List<Dictionary<string, string?>>(lineas.Length - 1);

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
                    fila[encabezados[i]] = EsNulo(crudo) ? null : crudo;
                }

                filas.Add(fila);
            }

            _logger.LogInformation("Datos cargados: {Count} filas desde {Ruta}", filas.Count, ruta);
            return filas;
        }

        private static string? Tv(Dictionary<string, string?> fila, params string[] claves)
        {
            foreach (var clave in claves)
            {
                if (fila.TryGetValue(clave, out string? valor) && !string.IsNullOrWhiteSpace(valor))
                {
                    return valor;
                }
            }

            // Fallback: match ignoring accents / special chars (N° vs N)
            foreach (var kv in fila)
            {
                string normKey = Normalizar(kv.Key);
                foreach (var clave in claves)
                {
                    if (normKey == Normalizar(clave) && !string.IsNullOrWhiteSpace(kv.Value))
                    {
                        return kv.Value;
                    }
                }
            }

            return null;
        }

        private static string Normalizar(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s.Normalize(System.Text.NormalizationForm.FormD))
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                    == System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }

            return sb.ToString();
        }

        private static Dictionary<string, object?> ADiccionario(Dictionary<string, string?> fila)
        {
            var destino = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in fila)
            {
                destino[kv.Key] = kv.Value;
            }

            return destino;
        }

        private static string? V(Dictionary<string, string?> fila, string clave) =>
            fila.TryGetValue(clave, out string? valor) ? valor : null;

        private static bool EsNulo(string valor) =>
            valor.Length == 0 || valor.Equals("NULL", StringComparison.OrdinalIgnoreCase);
    }
}
