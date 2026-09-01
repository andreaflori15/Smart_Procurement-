using System.Globalization;
using SmartProcurement.Models;

namespace SmartProcurement.Services
{
    public class ClasificacionComprasService
    {
        public const string Contrato = "contrato";
        public const string Exclusivo = "exclusivo";
        public const string Menor1000 = "menor1000";
        public const string Licitacion = "licitacion";

        private static readonly CultureInfo Peru = CultureInfo.GetCultureInfo("es-PE");
        private static readonly HashSet<string> PalabrasVacias = new(StringComparer.OrdinalIgnoreCase)
        {
            "para", "con", "tipo", "color", "und", "pza", "set", "pack", "azul", "rojo",
            "negro", "blanco", "amarillo", "verde", "gris", "mm", "cm", "kg", "the", "and"
        };

        public List<Dictionary<string, object?>> UnificarPendientes(IEnumerable<Dictionary<string, object?>> filas)
        {
            return filas
                .GroupBy(r => (
                    S(r, "SOLPED"),
                    S(r, "NPOs") ?? S(r, "NPos"),
                    S(r, "Centro"),
                    S(r, "Material")
                ))
                .Select(g => ArmarPendiente(g))
                .OrderBy(r => S(r, "SOLPED"))
                .ThenBy(r => S(r, "NPOs"))
                .ToList();
        }

        public (List<CubetaResumen> Cubetas, List<Dictionary<string, object?>> Items) Clasificar(
            IEnumerable<Dictionary<string, object?>> filas,
            bool simularContratos,
            IEnumerable<Dictionary<string, object?>>? catalogo = null)
        {
            var origen = filas as IList<Dictionary<string, object?>> ?? filas.ToList();
            var items = UnificarPendientes(origen)
                .Select(item => ClasificarItem(item, simularContratos))
                .ToList();
            EnriquecerLotes(items, catalogo ?? origen);

            var cubetas = new[]
            {
                Resumen(Contrato, "Con contrato", "Correr el pedido en SAP (automático).", "Correr pedido SAP", items),
                Resumen(Exclusivo, "Proveedor exclusivo", "Solicitar cotización a ese único proveedor.", "Solicitar cotización", items),
                Resumen(Menor1000, "Menor a 1000 USD", "Cotizar al proveedor de la última compra.", "Solicitar cotización", items),
                Resumen(Licitacion, "Licitación", "Agrupar por proveedor y cotizar a 2 o más.", "Armar licitación", items)
            }.ToList();

            return (cubetas, items);
        }

        private static Dictionary<string, object?> ArmarPendiente(IGrouping<(string?, string?, string?, string?), Dictionary<string, object?>> grupo)
        {
            var ordenadas = grupo
                .OrderByDescending(r => Fecha(S(r, "FecPed")))
                .ThenByDescending(r => Fecha(S(r, "Accion_Usuario_Fecha")))
                .ToList();

            var mejor = ordenadas[0];
            var emails = ordenadas
                .Select(r => S(r, "Email"))
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            decimal? precio = ParseDecimal(S(mejor, "PrecioNeto"));
            decimal? cantidad = ParseDecimal(S(mejor, "Cantidad_Solicitada"));
            decimal? importe = precio.HasValue && cantidad.HasValue ? precio * cantidad : precio;
            decimal? usd = AUsd(importe, S(mejor, "Moneda"));

            string? email = emails.Count == 0
                ? null
                : emails.Count == 1
                    ? emails[0]
                    : emails[0] + " (+" + (emails.Count - 1) + ")";

            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["SOLPED"] = S(mejor, "SOLPED"),
                ["NPOs"] = S(mejor, "NPOs") ?? S(mejor, "NPos"),
                ["Centro"] = S(mejor, "Centro"),
                ["Material"] = S(mejor, "Material"),
                ["Texto_Breve"] = S(mejor, "Texto_Breve"),
                ["Cantidad_Solicitada"] = S(mejor, "Cantidad_Solicitada"),
                ["Unidad_Medida"] = S(mejor, "Unidad_Medida") ?? S(mejor, "UNIDAD_MEDIDA"),
                ["Pro"] = S(mejor, "Pro"),
                ["RazSocial"] = S(mejor, "RazSocial"),
                ["Email"] = email,
                ["COMPRADOR_DESC"] = S(mejor, "COMPRADOR_DESC"),
                ["RANGO_FECHA"] = S(mejor, "RANGO_FECHA"),
                ["Accion_Usuario_Fecha"] = S(mejor, "Accion_Usuario_Fecha"),
                ["NumPed"] = S(mejor, "NumPed") ?? S(mejor, "NPedido"),
                ["FecPed"] = S(mejor, "FecPed"),
                ["Moneda"] = S(mejor, "Moneda") ?? S(mejor, "MONEDA"),
                ["PrecioNeto"] = S(mejor, "PrecioNeto"),
                ["ImporteUsd"] = usd?.ToString("0.00", CultureInfo.InvariantCulture),
                ["TipoCompra"] = S(mejor, "TipoCompra"),
                ["TipoCompra_Den"] = S(mejor, "TipoCompra_Den") ?? S(mejor, "Tipo de compra Denominacion"),
                ["Contrato"] = S(mejor, "Contrato")
            };
        }

        private static Dictionary<string, object?> ClasificarItem(Dictionary<string, object?> item, bool simularContratos)
        {
            bool exclusivo = EsExclusivo(S(item, "TipoCompra"), S(item, "TipoCompra_Den"));
            bool historial = !string.IsNullOrWhiteSpace(S(item, "Pro"))
                || !string.IsNullOrWhiteSpace(S(item, "RazSocial"))
                || !string.IsNullOrWhiteSpace(S(item, "NumPed"));
            bool contratoReal = !string.IsNullOrWhiteSpace(S(item, "Contrato"));
            bool contrato = contratoReal || (simularContratos && historial && !exclusivo && HashEstable(S(item, "SOLPED") + "|" + S(item, "NPOs")) % 8 == 0);

            decimal? usd = ParseDecimal(S(item, "ImporteUsd"));
            bool menor1000 = historial && usd.HasValue && usd.Value < 1000m;

            string cubeta;
            string tratamiento;
            string accion;
            string grupo;

            if (contrato)
            {
                cubeta = Contrato;
                tratamiento = "Correr el pedido en SAP (automático).";
                accion = "Correr pedido SAP";
                grupo = S(item, "RazSocial") ?? "Con contrato";
                if (!contratoReal)
                {
                    item["Contrato"] = "SIM-" + (S(item, "SOLPED") ?? "");
                }
            }
            else if (exclusivo)
            {
                cubeta = Exclusivo;
                tratamiento = "Solicitar cotización a ese único proveedor.";
                accion = "Solicitar cotización";
                grupo = S(item, "RazSocial") ?? "Proveedor exclusivo";
            }
            else if (menor1000)
            {
                cubeta = Menor1000;
                tratamiento = "Cotizar al proveedor de la última compra.";
                accion = "Solicitar cotización";
                grupo = S(item, "RazSocial") ?? "Última compra";
            }
            else
            {
                cubeta = Licitacion;
                tratamiento = historial
                    ? "Agrupar por proveedor y cotizar a 2 o más."
                    : "Sin historial: cotizar a 2 o más proveedores.";
                accion = "Armar licitación";
                grupo = historial ? (S(item, "RazSocial") ?? "Por definir") : "Sin historial";
            }

            item["Cubeta"] = cubeta;
            item["Tratamiento"] = tratamiento;
            item["Accion"] = accion;
            item["Grupo"] = grupo;
            return item;
        }

        private static void EnriquecerLotes(
            List<Dictionary<string, object?>> items,
            IEnumerable<Dictionary<string, object?>> catalogo)
        {
            var porMaterial = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var porFamilia = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var porToken = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var fila in catalogo)
            {
                string? proveedor = S(fila, "RazSocial");
                if (string.IsNullOrWhiteSpace(proveedor))
                {
                    continue;
                }

                string material = S(fila, "Material") ?? "";
                Agregar(porMaterial, material, proveedor);
                Agregar(porFamilia, Familia(material), proveedor);
                foreach (string token in Tokens(S(fila, "Texto_Breve")))
                {
                    Agregar(porToken, token, proveedor);
                }
            }

            foreach (var item in items)
            {
                string cubeta = S(item, "Cubeta") ?? "";
                string? propio = S(item, "RazSocial");
                string material = S(item, "Material") ?? "";
                string familia = Familia(material);
                var sugeridos = new List<string>();

                void Sumar(IEnumerable<string>? fuente)
                {
                    if (fuente is null)
                    {
                        return;
                    }

                    foreach (string p in fuente)
                    {
                        if (!string.IsNullOrWhiteSpace(p)
                            && !sugeridos.Exists(x => x.Equals(p, StringComparison.OrdinalIgnoreCase)))
                        {
                            sugeridos.Add(p.Trim());
                        }
                    }
                }

                if (cubeta == Licitacion)
                {
                    if (!string.IsNullOrWhiteSpace(propio))
                    {
                        Sumar([propio]);
                    }

                    if (porMaterial.TryGetValue(material, out var mismos))
                    {
                        Sumar(mismos);
                    }

                    if (porFamilia.TryGetValue(familia, out var deFamilia))
                    {
                        Sumar(deFamilia);
                    }

                    foreach (string token in Tokens(S(item, "Texto_Breve")))
                    {
                        if (porToken.TryGetValue(token, out var deMarca))
                        {
                            Sumar(deMarca);
                        }
                    }

                    if (sugeridos.Count > 4)
                    {
                        sugeridos = sugeridos.Take(4).ToList();
                    }
                }
                else if (!string.IsNullOrWhiteSpace(propio))
                {
                    sugeridos.Add(propio.Trim());
                }

                string loteId;
                string loteNombre;
                string motivo;

                if (cubeta == Contrato || cubeta == Exclusivo || cubeta == Menor1000)
                {
                    string clave = string.IsNullOrWhiteSpace(propio) ? "sin-proveedor" : propio.Trim();
                    loteId = cubeta + "|" + clave;
                    loteNombre = clave == "sin-proveedor" ? "Sin proveedor" : clave;
                    motivo = cubeta == Contrato
                        ? "Contrato: agrupar y correr el pedido en SAP."
                        : cubeta == Exclusivo
                            ? "Proveedor exclusivo: una sola cotización."
                            : "Menor a 1000 USD: cotizar al de la última compra.";
                }
                else if (!string.IsNullOrWhiteSpace(propio))
                {
                    loteId = Licitacion + "|" + propio.Trim();
                    loteNombre = propio.Trim();
                    motivo = sugeridos.Count >= 2
                        ? "Historial de este proveedor + alternativas de la misma marca o grupo artículo."
                        : "Última compra en este proveedor; hace falta un segundo para licitar.";
                }
                else
                {
                    loteId = Licitacion + "|fam|" + familia;
                    loteNombre = "Grupo artículo " + familia;
                    motivo = sugeridos.Count >= 2
                        ? "Sin compra previa: proveedores sugeridos por marca o grupo artículo parecido."
                        : "Sin historial: hay que buscar 2 o más proveedores.";
                }

                item["LoteId"] = loteId;
                item["LoteNombre"] = loteNombre;
                item["MotivoSugerencia"] = motivo;
                item["ProveedoresSugeridos"] = sugeridos;
            }
        }

        private static void Agregar(Dictionary<string, HashSet<string>> mapa, string clave, string proveedor)
        {
            if (string.IsNullOrWhiteSpace(clave))
            {
                return;
            }

            if (!mapa.TryGetValue(clave, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                mapa[clave] = set;
            }

            set.Add(proveedor.Trim());
        }

        private static string Familia(string material)
        {
            string m = material.Trim();
            if (m.Length >= 5)
            {
                return m[..5];
            }

            return m.Length == 0 ? "sinfamilia" : m;
        }

        private static IEnumerable<string> Tokens(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                yield break;
            }

            foreach (string crudo in texto.Split(
                [' ', '/', '-', ',', '.', ';', '(', ')', '+'],
                StringSplitOptions.RemoveEmptyEntries))
            {
                string t = crudo.Trim();
                if (t.Length < 4 || PalabrasVacias.Contains(t) || t.All(char.IsDigit))
                {
                    continue;
                }

                yield return t.ToUpperInvariant();
            }
        }

        private static CubetaResumen Resumen(
            string id,
            string titulo,
            string tratamiento,
            string accion,
            List<Dictionary<string, object?>> items) => new()
        {
            Id = id,
            Titulo = titulo,
            Tratamiento = tratamiento,
            Accion = accion,
            Count = items.Count(i => string.Equals(S(i, "Cubeta"), id, StringComparison.OrdinalIgnoreCase))
        };

        private static bool EsExclusivo(string? tipo, string? den)
        {
            if (string.Equals(tipo?.Trim(), "5", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return (den ?? "").Contains("exclusiv", StringComparison.OrdinalIgnoreCase);
        }

        private static decimal? AUsd(decimal? importe, string? moneda)
        {
            if (!importe.HasValue)
            {
                return null;
            }

            string m = (moneda ?? "").Trim().ToUpperInvariant();
            if (m.Contains("PEN") || m.Contains("SOL"))
            {
                return Math.Round(importe.Value / 3.75m, 2);
            }

            return Math.Round(importe.Value, 2);
        }

        private static decimal? ParseDecimal(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }

            texto = texto.Trim();
            if (decimal.TryParse(texto, NumberStyles.Number, Peru, out var valor))
            {
                return valor;
            }

            if (decimal.TryParse(texto.Replace(",", "."), NumberStyles.Number, CultureInfo.InvariantCulture, out valor))
            {
                return valor;
            }

            return null;
        }

        private static DateTime Fecha(string? texto)
        {
            if (DateTime.TryParseExact(texto, "dd/MM/yyyy", Peru, DateTimeStyles.None, out var fecha))
            {
                return fecha;
            }

            return DateTime.MinValue;
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
            return texto.Length == 0 || texto.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? null : texto;
        }
    }
}
