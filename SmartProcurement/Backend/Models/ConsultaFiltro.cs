namespace SmartProcurement.Models
{
    public class ConsultaFiltro
    {
        public bool Valido { get; set; }
        public string? Mensaje { get; set; }
        public string Tipo { get; set; } = "";
        public List<string> Valores { get; set; } = [];
        public bool UsoIa { get; set; }
        public string? RespuestaIa { get; set; }
    }
}
