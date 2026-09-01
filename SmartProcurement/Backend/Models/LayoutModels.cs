using System.Text.Json.Serialization;

namespace SmartProcurement.Models
{
    public class LayoutStore
    {
        public List<LayoutRegistro> Layouts { get; set; } = [];
    }

    public class LayoutRegistro
    {
        public string Id { get; set; } = "";
        public string NombreLayout { get; set; } = "";
        public string Prompt { get; set; } = "";
        public string TipoEsquema { get; set; } = "tablero-completo";
        public DateTime FechaCreacion { get; set; }
        public DateTime FechaActualizacion { get; set; }
        public Dictionary<string, object?>? Esquema { get; set; }
    }

    public class LayoutHistorialStore
    {
        public List<LayoutHistorialEntrada> Historial { get; set; } = [];
    }

    public class LayoutHistorialEntrada
    {
        public string LayoutId { get; set; } = "";
        public string FechaGrabacion { get; set; } = "";
        public string TipoEsquema { get; set; } = "tablero-completo";
        public Dictionary<string, object?>? Esquema { get; set; }
    }

    public class GrabarLayoutRequest
    {
        public string NombreLayout { get; set; } = "";
        public string Prompt { get; set; } = "";
        public Dictionary<string, object?>? Esquema { get; set; }
    }

    public class GrabarHistorialRequest
    {
        public string LayoutId { get; set; } = "";
        public Dictionary<string, object?>? Esquema { get; set; }
    }
}
