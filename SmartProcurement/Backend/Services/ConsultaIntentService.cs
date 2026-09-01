#pragma warning disable OPENAI001

using System.Text.Json;
using System.Text.RegularExpressions;
using OpenAI.Responses;
using SmartProcurement.Models;

namespace SmartProcurement.Services
{
    public class ConsultaIntentService
    {
        private static readonly Regex Numeros = new(@"\d{6,12}", RegexOptions.Compiled);
        private static readonly Regex PalabraSolped = new(@"\bsolpeds?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PalabraPedido = new(@"\bpedidos?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PalabraProveedor = new(@"\bproveedores?\b|\braz[oó]n\s+social\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IConfiguration _configuration;
        private readonly ILogger<ConsultaIntentService> _logger;

        public ConsultaIntentService(IConfiguration configuration, ILogger<ConsultaIntentService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<ConsultaFiltro> ResolverAsync(string? prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return Invalido("Escribe qué quieres buscar.");
            }

            var local = IntentarParseLocal(prompt);
            if (local is { Valido: true })
            {
                _logger.LogInformation("Consulta local: {Prompt}. Tipo {Tipo}. Valores {Valores}", prompt, local.Tipo, local.Valores);
                return local;
            }

            return await InterpretarConIaAsync(prompt);
        }

        internal static ConsultaFiltro? IntentarParseLocal(string prompt)
        {
            var nums = Numeros.Matches(prompt)
                .Select(m => m.Value)
                .Distinct()
                .ToList();

            bool hablaSolped = PalabraSolped.IsMatch(prompt);
            bool hablaPedido = PalabraPedido.IsMatch(prompt);
            bool hablaProveedor = PalabraProveedor.IsMatch(prompt);

            if (hablaSolped && nums.Count > 0 && !hablaPedido && !hablaProveedor)
            {
                return Validar("SOLPED", nums, usoIa: false);
            }

            if (hablaPedido && nums.Count > 0 && !hablaSolped && !hablaProveedor)
            {
                return Validar("PEDIDO", nums, usoIa: false);
            }

            if (hablaProveedor && !hablaSolped && !hablaPedido)
            {
                string nombre = ExtraerNombreProveedor(prompt);
                if (nombre.Length >= 2)
                {
                    return Validar("PROVEEDOR", [nombre], usoIa: false);
                }
            }

            if (nums.Count > 0 && !hablaProveedor && !hablaSolped && !hablaPedido)
            {
                if (nums.All(n => n.StartsWith('6')))
                {
                    return Validar("SOLPED", nums, usoIa: false);
                }

                if (nums.All(n => n.StartsWith('1')))
                {
                    return Validar("PEDIDO", nums, usoIa: false);
                }
            }

            return null;
        }

        private async Task<ConsultaFiltro> InterpretarConIaAsync(string prompt)
        {
            string? apiKey = _configuration["OpenAI:ApiKey"];

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return Invalido("No pude interpretar esa búsqueda en automático. Prueba con un número de SOLPED o pedido, o configura OpenAI:ApiKey.");
            }

            var client = new ResponsesClient(apiKey);
            string entrada = Instrucciones + "\n\nConsulta del usuario:\n" + prompt;

            ResponseResult response = await client.CreateResponseAsync("gpt-5.6", entrada);
            string resultado = response.GetOutputText().Trim();

            _logger.LogInformation("Consulta IA: {Prompt}. JSON: {Json}", prompt, resultado);

            var filtro = ParsearRespuestaIa(resultado);
            filtro.UsoIa = true;
            filtro.RespuestaIa = resultado;
            return filtro;
        }

        internal static ConsultaFiltro ParsearRespuestaIa(string resultado)
        {
            if (string.Equals(resultado, "INVALIDO", StringComparison.OrdinalIgnoreCase))
            {
                return Invalido("Esa búsqueda no es de SOLPED, pedido o proveedor. Prueba con un número de SOLPED o el nombre del proveedor.");
            }

            string json = ExtraerJson(resultado);

            IaJson? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<IaJson>(json, JsonOptions);
            }
            catch (JsonException)
            {
                return Invalido("No entendí la consulta. Intenta con un ejemplo de abajo.", resultado);
            }

            if (parsed is null || string.Equals(parsed.Tipo, "INVALIDO", StringComparison.OrdinalIgnoreCase))
            {
                return Invalido("Esa búsqueda no es de SOLPED, pedido o proveedor. Prueba con un número de SOLPED o el nombre del proveedor.", resultado);
            }

            return Validar(parsed.Tipo ?? "", parsed.Valores ?? [], usoIa: true, respuestaIa: resultado);
        }

        internal static ConsultaFiltro Validar(string tipo, IEnumerable<string> valores, bool usoIa, string? respuestaIa = null)
        {
            tipo = (tipo ?? "").Trim().ToUpperInvariant();
            if (tipo is not ("SOLPED" or "PEDIDO" or "PROVEEDOR"))
            {
                return Invalido("No entendí la consulta. Intenta con un ejemplo de abajo.", respuestaIa);
            }

            var limpios = new List<string>();

            foreach (string crudo in valores)
            {
                if (tipo is "SOLPED" or "PEDIDO")
                {
                    string n = Regex.Replace(crudo ?? "", @"\D", "");
                    if (n.Length is >= 6 and <= 12)
                    {
                        limpios.Add(n);
                    }
                }
                else
                {
                    string t = Regex.Replace(crudo ?? "", @"[%_\[\]]", "");
                    t = Regex.Replace(t, @"\s+", " ").Trim();
                    if (t.Length is >= 2 and <= 80)
                    {
                        limpios.Add(t);
                    }
                }
            }

            limpios = limpios
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToList();

            if (limpios.Count == 0)
            {
                return Invalido("No encontré un número o nombre válido para buscar.", respuestaIa);
            }

            return new ConsultaFiltro
            {
                Valido = true,
                Tipo = tipo,
                Valores = limpios,
                UsoIa = usoIa,
                RespuestaIa = respuestaIa
            };
        }

        private static string ExtraerNombreProveedor(string texto)
        {
            string limpio = PalabraProveedor.Replace(texto, " ");
            limpio = Regex.Replace(
                limpio,
                @"\b(quiero|necesito|busco|buscar|contengan|contiene|que|con|el|la|los|las|de|del|un|una|por|nombre)\b",
                " ",
                RegexOptions.IgnoreCase);
            return Regex.Replace(limpio, @"\s+", " ").Trim();
        }

        private static string ExtraerJson(string texto)
        {
            texto = texto.Trim();
            if (texto.StartsWith("```", StringComparison.Ordinal))
            {
                var lineas = texto.Split('\n');
                texto = string.Join('\n', lineas.Skip(1).TakeWhile(l => !l.TrimStart().StartsWith("```", StringComparison.Ordinal)));
            }

            int inicio = texto.IndexOf('{');
            int fin = texto.LastIndexOf('}');
            if (inicio >= 0 && fin > inicio)
            {
                return texto[inicio..(fin + 1)];
            }

            return texto;
        }

        private static ConsultaFiltro Invalido(string mensaje, string? respuestaIa = null) => new()
        {
            Valido = false,
            Mensaje = mensaje,
            RespuestaIa = respuestaIa
        };

        private sealed class IaJson
        {
            public string? Tipo { get; set; }
            public List<string>? Valores { get; set; }
        }

        private const string Instrucciones = """
Eres un clasificador de búsquedas de compras.

Devuelve SOLO un JSON válido, sin markdown y sin explicaciones.

Formatos permitidos:
{"tipo":"SOLPED","valores":["6000004069"]}
{"tipo":"PEDIDO","valores":["1000000002"]}
{"tipo":"PROVEEDOR","valores":["ACME"]}
{"tipo":"INVALIDO"}

Tipos:
- SOLPED: números de solicitud de pedido.
- PEDIDO: números de pedido / orden de compra.
- PROVEEDOR: nombre o parte del nombre del proveedor.

Reglas:
1. No generes SQL.
2. No inventes números.
3. SOLPED y PEDIDO: solo dígitos.
4. PROVEEDOR: texto a buscar, sin % ni operadores.
5. Varios valores van en el array valores.
6. Si no es claramente SOLPED, PEDIDO o PROVEEDOR, devuelve {"tipo":"INVALIDO"}.

Ejemplos:
Usuario: necesito la solped 6000004069 y 6000004071
{"tipo":"SOLPED","valores":["6000004069","6000004071"]}

Usuario: quiero los pedidos 1000000002 y 1000000003
{"tipo":"PEDIDO","valores":["1000000002","1000000003"]}

Usuario: quiero proveedores que contengan ACME
{"tipo":"PROVEEDOR","valores":["ACME"]}

Usuario: quiero información de las facturas
{"tipo":"INVALIDO"}
""";
    }
}

#pragma warning restore OPENAI001
