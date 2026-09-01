using System.Globalization;
using System.Text.RegularExpressions;
using SmartProcurement.Models;

namespace SmartProcurement.Services
{
    public class TableroJefeService
    {
        private static readonly Regex Numeros = new(@"\d{6,12}", RegexOptions.Compiled);

        private readonly DemoDataService _demo;
        private readonly SqlServerService _sql;
        private readonly ClasificacionComprasService _clasificacion;
        private readonly ILogger<TableroJefeService> _logger;

        public TableroJefeService(
            DemoDataService demo,
            SqlServerService sql,
            ClasificacionComprasService clasificacion,
            ILogger<TableroJefeService> logger)
        {
            _demo = demo;
            _sql = sql;
            _clasificacion = clasificacion;
            _logger = logger;
        }

        public async Task<object> ArmarAsync()
        {
            var utiles = await ObtenerItemsAsync();

            var porComprador = utiles
                .GroupBy(i => S(i, "COMPRADOR_DESC") ?? "Sin comprador")
                .Select(g => new
                {
                    nombre = g.Key,
                    total = g.Count(),
                    alDia = g.Count(i => Edad(S(i, "RANGO_FECHA")) == "ok"),
                    media = g.Count(i => Edad(S(i, "RANGO_FECHA")) == "mid"),
                    critica = g.Count(i => Edad(S(i, "RANGO_FECHA")) == "bad")
                })
                .OrderByDescending(x => x.critica)
                .ThenByDescending(x => x.total)
                .ToList();

            var criticas = utiles
                .Where(i => Edad(S(i, "RANGO_FECHA")) == "bad")
                .OrderByDescending(i => ParseUsd(S(i, "ImporteUsd")))
                .Take(15)
                .Select(Corto)
                .ToList();

            var licitacion = utiles
                .Where(i => S(i, "Cubeta") == ClasificacionComprasService.Licitacion)
                .GroupBy(i => S(i, "Grupo") ?? "Sin historial")
                .Select(g => new { proveedor = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(8)
                .ToList();

            var razones = utiles
                .GroupBy(i => S(i, "RazonId") ?? "")
                .Select(g => new { id = g.Key, titulo = S(g.First(), "Razon"), count = g.Count() })
                .Where(x => !string.IsNullOrWhiteSpace(x.id))
                .OrderByDescending(x => x.count)
                .ToList();

            var embudo = new[]
            {
                Paso("clasificar", "Por clasificar", utiles),
                Paso("cotizar", "Enviar a cotizar", utiles),
                Paso("esperar", "Esperando oferta", utiles),
                Paso("comparar", "En comparación", utiles),
                Paso("confirmar", "Confirmar usuario", utiles),
                Paso("sap", "Pedido SAP", utiles)
            };

            var cubetas = ResumenCubetas(utiles);

            return new
            {
                valido = true,
                demo = _demo.Activo,
                kpis = ArmarKpis(utiles),
                compradores = porComprador,
                cubetas,
                criticas,
                licitacion,
                embudo,
                razones,
                sugerencias = ArmarSugerencias(utiles),
                items = utiles.Select(Corto).ToList()
            };
        }

        public async Task<object> PreguntarAsync(string? prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return new { valido = false, mensaje = "Escribe una pregunta." };
            }

            var utiles = await ObtenerItemsAsync();
            var q = prompt.Trim();
            var filtrados = FiltrarPregunta(utiles, q).ToList();
            string respuesta = Redactar(q, filtrados, utiles);

            return new
            {
                valido = true,
                respuesta,
                titulo = "Resultado",
                datos = filtrados.Take(20).Select(Corto).ToList()
            };
        }

        public async Task<Dictionary<string, object?>> ExtraerEsquemaAsync()
        {
            var utiles = await ObtenerItemsAsync();

            var porRango = utiles
                .GroupBy(i => S(i, "RANGO_FECHA") ?? "Sin dato")
                .Select(g => new Dictionary<string, object?>
                {
                    ["rango"] = g.Key,
                    ["count"] = g.Count(),
                    ["edad"] = Edad(g.Key)
                })
                .OrderBy(x => OrdenRango(x["rango"]?.ToString()))
                .ToList();

            var compradores = utiles
                .GroupBy(i => S(i, "COMPRADOR_DESC") ?? "Sin comprador")
                .Select(g => new Dictionary<string, object?>
                {
                    ["nombre"] = g.Key,
                    ["total"] = g.Count(),
                    ["alDia"] = g.Count(i => Edad(S(i, "RANGO_FECHA")) == "ok"),
                    ["media"] = g.Count(i => Edad(S(i, "RANGO_FECHA")) == "mid"),
                    ["critica"] = g.Count(i => Edad(S(i, "RANGO_FECHA")) == "bad")
                })
                .OrderByDescending(x => (int)(x["critica"] ?? 0))
                .ThenByDescending(x => (int)(x["total"] ?? 0))
                .ToList();

            var trazabilidad = new List<Dictionary<string, object?>>
            {
                DictPaso("cantidad", "SOLPED Cantidad", utiles),
                DictPaso("cotizados", "Cotizados", utiles.Where(i => S(i, "EtapaId") is "cotizar" or "esperar" or "comparar").ToList()),
                DictPaso("esperando", "Esperando cotización", utiles.Where(i => S(i, "EtapaId") == "esperar").ToList()),
                DictPaso("completo", "Completo", utiles.Where(i => S(i, "EtapaId") is "comparar" or "confirmar").ToList()),
                DictPaso("pedido", "Pedido generado", utiles.Where(i => S(i, "EtapaId") == "sap").ToList())
            };

            var criticas = utiles
                .Where(i => Edad(S(i, "RANGO_FECHA")) == "bad")
                .OrderByDescending(i => ParseUsd(S(i, "ImporteUsd")))
                .Take(15)
                .Select(i => new Dictionary<string, object?>
                {
                    ["solped"] = S(i, "SOLPED"),
                    ["comprador"] = S(i, "COMPRADOR_DESC"),
                    ["etapa"] = S(i, "Etapa"),
                    ["razon"] = S(i, "Razon"),
                    ["rango"] = S(i, "RANGO_FECHA"),
                    ["proveedor"] = string.IsNullOrWhiteSpace(S(i, "RazSocial")) ? "—" : S(i, "RazSocial")
                })
                .ToList();

            return new Dictionary<string, object?>
            {
                ["porRango"] = porRango,
                ["compradores"] = compradores,
                ["trazabilidad"] = trazabilidad,
                ["sugerencias"] = ArmarSugerencias(utiles),
                ["criticas"] = criticas
            };
        }

        private static int OrdenRango(string? rango)
        {
            if (string.IsNullOrWhiteSpace(rango)) return 99;
            if (rango.Contains("01", StringComparison.Ordinal)) return 1;
            if (rango.Contains("08", StringComparison.Ordinal)) return 2;
            if (rango.Contains("15", StringComparison.Ordinal)) return 3;
            if (rango.Contains("22", StringComparison.Ordinal)) return 4;
            return 50;
        }

        private static Dictionary<string, object?> DictPaso(string id, string titulo, List<Dictionary<string, object?>> items) =>
            new()
            {
                ["id"] = id,
                ["titulo"] = titulo,
                ["count"] = items.Count,
                ["filtro"] = "etapa:" + id
            };

        private async Task<List<Dictionary<string, object?>>> ObtenerItemsAsync()
        {
            var filas = await CargarFilasAsync();
            var (_, items) = _clasificacion.Clasificar(filas, simularContratos: _demo.Activo);
            return items
                .Where(i => !EsRuido(S(i, "COMPRADOR_DESC")))
                .Select(EnriquecerTrazabilidad)
                .ToList();
        }

        private static List<CubetaResumen> ResumenCubetas(List<Dictionary<string, object?>> utiles)
        {
            string[] ids =
            [
                ClasificacionComprasService.Contrato,
                ClasificacionComprasService.Exclusivo,
                ClasificacionComprasService.Menor1000,
                ClasificacionComprasService.Licitacion
            ];
            var titulos = new Dictionary<string, string>
            {
                [ClasificacionComprasService.Contrato] = "Con contrato",
                [ClasificacionComprasService.Exclusivo] = "Proveedor exclusivo",
                [ClasificacionComprasService.Menor1000] = "Menor a 1000 USD",
                [ClasificacionComprasService.Licitacion] = "Licitación"
            };

            return ids.Select(id => new CubetaResumen
            {
                Id = id,
                Titulo = titulos[id],
                Count = utiles.Count(i => S(i, "Cubeta") == id)
            }).ToList();
        }

        private static Dictionary<string, object?> EnriquecerTrazabilidad(Dictionary<string, object?> item)
        {
            string cubeta = S(item, "Cubeta") ?? "";
            string grupo = S(item, "Grupo") ?? "";
            string edad = Edad(S(item, "RANGO_FECHA"));
            int h = HashEstable((S(item, "SOLPED") ?? "") + "|" + (S(item, "NPOs") ?? ""));
            int slot = h % 5;

            string etapaId;
            string razonId;
            string razon;
            string etapa;

            if (cubeta == ClasificacionComprasService.Contrato)
            {
                etapaId = "sap";
                etapa = "Pedido SAP";
                razonId = "contrato_sap";
                razon = "Contrato no corrido en SAP";
            }
            else if (cubeta == ClasificacionComprasService.Exclusivo || cubeta == ClasificacionComprasService.Menor1000)
            {
                if (slot <= 1)
                {
                    etapaId = "cotizar";
                    etapa = "Enviar a cotizar";
                    razonId = "falta_enviar";
                    razon = "Falta enviar la cotización";
                }
                else if (slot == 2 || edad == "bad")
                {
                    etapaId = "esperar";
                    etapa = "Esperando oferta";
                    razonId = "esperando_proveedor";
                    razon = "Esperando respuesta del proveedor";
                }
                else
                {
                    etapaId = "confirmar";
                    etapa = "Confirmar usuario";
                    razonId = "confirmar_usuario";
                    razon = "Pendiente confirmación del usuario";
                }
            }
            else if (grupo.Contains("Sin historial", StringComparison.OrdinalIgnoreCase))
            {
                if (slot == 0)
                {
                    etapaId = "clasificar";
                    etapa = "Por clasificar";
                    razonId = "material_incompleto";
                    razon = "Material o dato incompleto";
                }
                else
                {
                    etapaId = "cotizar";
                    etapa = "Enviar a cotizar";
                    razonId = "sin_historial";
                    razon = "Sin historial: hay que licitar a 2 o más";
                }
            }
            else
            {
                etapaId = slot switch
                {
                    0 => "cotizar",
                    1 => "esperar",
                    2 => "esperar",
                    3 => "comparar",
                    _ => "confirmar"
                };
                etapa = etapaId switch
                {
                    "cotizar" => "Enviar a cotizar",
                    "esperar" => "Esperando oferta",
                    "comparar" => "En comparación",
                    _ => "Confirmar usuario"
                };
                razonId = etapaId switch
                {
                    "cotizar" => "falta_enviar",
                    "esperar" => "esperando_proveedor",
                    "comparar" => "en_comparacion",
                    _ => "confirmar_usuario"
                };
                razon = etapaId switch
                {
                    "cotizar" => "Falta enviar la cotización",
                    "esperar" => "Esperando respuesta del proveedor",
                    "comparar" => "Ofertas en comparación",
                    _ => "Pendiente confirmación del usuario"
                };
            }

            string fecha = S(item, "Accion_Usuario_Fecha") ?? "hace unos días";
            string traza = etapaId switch
            {
                "sap" => $"{fecha}: clasificada con contrato → pendiente correr pedido SAP",
                "esperar" => $"{fecha}: clasificada → cotización enviada → sin respuesta del proveedor",
                "confirmar" => $"{fecha}: ofertas listas → esperando que el usuario confirme SOLPED / material",
                "comparar" => $"{fecha}: llegaron ofertas → falta cuadro comparativo",
                "cotizar" => $"{fecha}: tratamiento definido → falta disparar la cotización",
                _ => $"{fecha}: ingresó → aún no se clasifica o falta un dato"
            };

            item["EtapaId"] = etapaId;
            item["Etapa"] = etapa;
            item["RazonId"] = razonId;
            item["Razon"] = razon;
            item["Trazabilidad"] = traza;
            return item;
        }

        private static IEnumerable<Dictionary<string, object?>> FiltrarPregunta(
            List<Dictionary<string, object?>> items,
            string prompt)
        {
            IEnumerable<Dictionary<string, object?>> q = items;
            string low = prompt.ToLowerInvariant();
            var nums = Numeros.Matches(prompt).Select(m => m.Value).ToList();

            if (nums.Count > 0)
            {
                q = q.Where(i => nums.Contains(S(i, "SOLPED") ?? ""));
            }

            foreach (var nombre in items.Select(i => S(i, "COMPRADOR_DESC")).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
            {
                string pila = nombre!.Split(' ')[0];
                if (pila.Length >= 4 && low.Contains(pila.ToLowerInvariant()))
                {
                    q = q.Where(i => string.Equals(S(i, "COMPRADOR_DESC"), nombre, StringComparison.OrdinalIgnoreCase));
                    break;
                }
            }

            if (low.Contains("22") || low.Contains("crítica") || low.Contains("critica") || low.Contains("vencid"))
            {
                q = q.Where(i => Edad(S(i, "RANGO_FECHA")) == "bad");
            }
            else if (low.Contains("14") || low.Contains("antigu") || low.Contains("trabad"))
            {
                q = q.Where(i => Edad(S(i, "RANGO_FECHA")) != "ok");
            }

            if (low.Contains("licit"))
            {
                q = q.Where(i => S(i, "Cubeta") == ClasificacionComprasService.Licitacion);
            }

            if (low.Contains("esperando") || low.Contains("oferta") || low.Contains("proveedor no"))
            {
                q = q.Where(i => S(i, "EtapaId") == "esperar");
            }

            if (low.Contains("confirm") || low.Contains("usuario"))
            {
                q = q.Where(i => S(i, "EtapaId") == "confirmar");
            }

            if (low.Contains("compar"))
            {
                q = q.Where(i => S(i, "EtapaId") == "comparar");
            }

            if (low.Contains("contrato") || low.Contains("sap"))
            {
                q = q.Where(i => S(i, "EtapaId") == "sap" || S(i, "Cubeta") == ClasificacionComprasService.Contrato);
            }

            if (low.Contains("cotiz") && !low.Contains("esperando"))
            {
                q = q.Where(i => S(i, "EtapaId") is "cotizar" or "esperar");
            }

            return q;
        }

        private static string Redactar(string prompt, List<Dictionary<string, object?>> hit, List<Dictionary<string, object?>> todos)
        {
            if (hit.Count == 0)
            {
                return "No encontré SOLPEDs con eso. Prueba con un comprador, una SOLPED o una etapa (esperando oferta, confirmar usuario, licitación).";
            }

            var nums = Numeros.Matches(prompt).Select(m => m.Value).ToList();
            if (nums.Count == 1 && hit.Count == 1)
            {
                var i = hit[0];
                return $"La SOLPED {S(i, "SOLPED")} de {S(i, "COMPRADOR_DESC")} está en «{S(i, "Etapa")}». Razón: {S(i, "Razon")}. Trazabilidad: {S(i, "Trazabilidad")}.";
            }

            var topEtapa = hit.GroupBy(i => S(i, "Etapa")).OrderByDescending(g => g.Count()).First();
            var topRazon = hit.GroupBy(i => S(i, "Razon")).OrderByDescending(g => g.Count()).First();
            return $"Encontré {hit.Count} SOLPED(s) de {todos.Count} pendientes. La mayoría está en «{topEtapa.Key}» ({topEtapa.Count()}). Motivo más frecuente: {topRazon.Key}.";
        }

        private static object Paso(string id, string titulo, List<Dictionary<string, object?>> items) => new
        {
            id,
            titulo,
            count = items.Count(i => S(i, "EtapaId") == id)
        };

        private async Task<List<Dictionary<string, object?>>> CargarFilasAsync()
        {
            if (_demo.Activo)
            {
                return _demo.TodasLasFilas();
            }

            try
            {
                return DataTableMapper.ToDictionaryList(await _sql.ObtenerJefeCompraAsync());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SQL no disponible; tablero con datos de prueba");
                return _demo.TodasLasFilas();
            }
        }

        private static List<string> ArmarSugerencias(List<Dictionary<string, object?>> items)
        {
            var lista = new List<string>();
            var top = items
                .GroupBy(i => S(i, "COMPRADOR_DESC") ?? "")
                .Select(g => new
                {
                    Nombre = g.Key,
                    Critica = g.Count(i => Edad(S(i, "RANGO_FECHA")) == "bad"),
                    ContratosVencidos = g.Count(i =>
                        Edad(S(i, "RANGO_FECHA")) == "bad"
                        && S(i, "Cubeta") == ClasificacionComprasService.Contrato)
                })
                .OrderByDescending(x => x.Critica)
                .FirstOrDefault();

            if (top is { Critica: > 0 })
            {
                lista.Add(top.ContratosVencidos > 0
                    ? $"{top.Nombre} tiene {top.Critica} en 22+ días; {top.ContratosVencidos} son contrato y se pueden correr hoy en SAP."
                    : $"{top.Nombre} tiene {top.Critica} SOLPEDs críticas (22+ días). Revisar qué las está trabando.");
            }

            var exclusivos = items
                .Where(i => S(i, "Cubeta") == ClasificacionComprasService.Exclusivo)
                .GroupBy(i => S(i, "RazSocial") ?? "Proveedor exclusivo")
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            if (exclusivos is not null && exclusivos.Count() > 0)
            {
                lista.Add($"Hay {exclusivos.Count()} exclusivos de {exclusivos.Key.Trim()}: un solo correo de cotización destrabaría el lote.");
            }

            var lic = items.Where(i => S(i, "Cubeta") == ClasificacionComprasService.Licitacion).ToList();
            if (lic.Count > 0)
            {
                var grupos = lic
                    .GroupBy(i => S(i, "Grupo") ?? "Sin historial")
                    .OrderByDescending(g => g.Count())
                    .ToList();
                string topNombres = string.Join(" y ", grupos.Take(2).Select(g => g.Key.Trim()));
                lista.Add($"Licitación: {lic.Count} SOLPEDs se agrupan en {grupos.Count} proveedores; priorizar {topNombres}.");
            }

            var contratos = items.Count(i => S(i, "Cubeta") == ClasificacionComprasService.Contrato);
            if (contratos > 0)
            {
                lista.Add($"{contratos} SOLPEDs con contrato siguen pendientes: deberían salir como pedido automático.");
            }

            var espera = items.Count(i => S(i, "EtapaId") == "esperar");
            if (espera > 0)
            {
                lista.Add($"{espera} SOLPEDs están esperando oferta del proveedor: conviene hacer seguimiento de cotización.");
            }

            decimal importe = items.Sum(i => ParseUsd(S(i, "ImporteUsd")));
            decimal usdExterno = items
                .Where(i => S(i, "EtapaId") is "esperar" or "confirmar")
                .Sum(i => ParseUsd(S(i, "ImporteUsd")));
            if (importe > 0 && usdExterno > 0)
            {
                int pct = (int)Math.Round(100m * usdExterno / importe);
                lista.Insert(0, $"{pct}% del USD está esperando al proveedor o al usuario, no al comprador.");
            }

            return lista.Take(5).ToList();
        }

        private static List<object> ArmarKpis(List<Dictionary<string, object?>> utiles)
        {
            int n = utiles.Count;
            var criticas = utiles.Where(i => Edad(S(i, "RANGO_FECHA")) == "bad").ToList();
            int nCrit = criticas.Count;
            decimal usdRiesgo = criticas.Sum(i => ParseUsd(S(i, "ImporteUsd")));
            int sla = n == 0 ? 0 : (int)Math.Round(100.0 * nCrit / n);
            int accion = utiles.Count(i => S(i, "EtapaId") is "clasificar" or "cotizar" or "sap");
            int espera = utiles.Count(i => S(i, "EtapaId") is "esperar" or "confirmar");
            int contrato = utiles.Count(i =>
                S(i, "EtapaId") == "sap" || S(i, "Cubeta") == ClasificacionComprasService.Contrato);

            string tonoSla = sla >= 30 ? "danger" : sla >= 15 ? "warn" : "ok";
            return
            [
                Kpi("backlog", "Backlog", n.ToString(), "todas", "brand", n, true,
                    "SOLPEDs abiertas en el tablero"),
                Kpi("riesgo", "USD en riesgo", FormatoUsdCorto(usdRiesgo), "criticas", "danger", nCrit, true,
                    "Importe de las críticas 22+ días"),
                Kpi("sla", "% SLA roto", sla + "%", "criticas", tonoSla, sla, true,
                    $"{nCrit} de {n} superan 22 días"),
                Kpi("accion", "Acción comprador", accion.ToString(), "accion", "brand", accion, true,
                    "Por clasificar, enviar a cotizar o correr SAP"),
                Kpi("espera", "Espera externa", espera.ToString(), "espera", "warn", espera, true,
                    "Esperando proveedor o confirmación de usuario"),
                Kpi("contrato", "Contrato no corrido", contrato.ToString(), "contrato", contrato > 0 ? "warn" : "ok", contrato, true,
                    "Hay contrato; falta pedido SAP")
            ];
        }

        private static object Kpi(
            string id, string titulo, string valor, string filtro, string tono, int semilla, bool bajarEsBueno, string hint)
        {
            int delta = HashEstable(id + "|" + semilla) % 19 - 7;
            bool bueno = delta == 0 || (bajarEsBueno ? delta < 0 : delta > 0);
            string flecha = delta > 0 ? "↑" : delta < 0 ? "↓" : "→";
            string vs = (delta > 0 ? "+" : "") + delta + "% vs sem. ant.";
            return new { id, titulo, valor, filtro, tono, delta, bueno, flecha, vs, hint };
        }

        private static string FormatoUsdCorto(decimal n)
        {
            if (n >= 1_000_000m) return "$" + (n / 1_000_000m).ToString("0.0", CultureInfo.InvariantCulture) + "M";
            if (n >= 1_000m) return "$" + (n / 1_000m).ToString("0.0", CultureInfo.InvariantCulture) + "k";
            return "$" + n.ToString("0", CultureInfo.InvariantCulture);
        }

        private static object Corto(Dictionary<string, object?> i) => new
        {
            solped = S(i, "SOLPED"),
            npos = S(i, "NPOs"),
            comprador = S(i, "COMPRADOR_DESC"),
            rango = S(i, "RANGO_FECHA"),
            cubeta = S(i, "Cubeta"),
            proveedor = S(i, "RazSocial"),
            descripcion = S(i, "Texto_Breve"),
            importeUsd = S(i, "ImporteUsd"),
            tratamiento = S(i, "Tratamiento"),
            grupo = S(i, "Grupo"),
            etapa = S(i, "Etapa"),
            etapaId = S(i, "EtapaId"),
            razon = S(i, "Razon"),
            trazabilidad = S(i, "Trazabilidad")
        };

        private static string Edad(string? rango)
        {
            string r = rango ?? "";
            if (r.Contains("22", StringComparison.OrdinalIgnoreCase)) return "bad";
            if (r.Contains("08", StringComparison.OrdinalIgnoreCase) || r.Contains("15", StringComparison.OrdinalIgnoreCase))
            {
                return "mid";
            }

            return "ok";
        }

        private static bool EsRuido(string? nombre) =>
            string.IsNullOrWhiteSpace(nombre) || nombre.Contains('@');

        private static decimal ParseUsd(string? texto)
        {
            if (decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out var v))
            {
                return v;
            }

            return 0;
        }

        private static int HashEstable(string texto)
        {
            int h = 17;
            foreach (char c in texto)
            {
                h = unchecked(h * 31 + c);
            }

            return Math.Abs(h);
        }

        private static string? S(Dictionary<string, object?> fila, string clave)
        {
            if (!fila.TryGetValue(clave, out object? valor) || valor is null)
            {
                return null;
            }

            string texto = Convert.ToString(valor)?.Trim() ?? "";
            return texto.Length == 0 ? null : texto;
        }
    }
}
