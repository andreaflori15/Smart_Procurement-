namespace SmartProcurement.Models
{
    public class LoteEstado
    {
        public string LoteId { get; set; } = "";
        public string EstadoId { get; set; } = "sin_solicitar";
        public string Estado { get; set; } = "Sin solicitar";
        public int Ofertas { get; set; }
        public int OfertasNecesarias { get; set; } = 1;
        public string? Fecha { get; set; }
        public List<CotizacionArchivo> Archivos { get; set; } = [];
    }

    public class CotizacionArchivo
    {
        public string Nombre { get; set; } = "";
        public string Url { get; set; } = "";
        public string? Proveedor { get; set; }
        public string Fecha { get; set; } = "";
    }
}
