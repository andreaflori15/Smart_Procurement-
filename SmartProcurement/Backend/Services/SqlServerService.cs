using System.Data;
using Microsoft.Data.SqlClient;
using SmartProcurement.Models;

namespace SmartProcurement.Services
{
    public class SqlServerService
    {
        private readonly IConfiguration _configuration;

        public SqlServerService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Task<DataTable> ObtenerSolpedAsync(ConsultaFiltro filtro)
        {
            var (whereSql, parametros) = ArmarWhere(filtro);

            string query = $@"
SELECT TOP 100
    FP.Pedido                 AS NPedido,
    FP.Pedido_Posicion        AS NPos,
    FP.Proveedor_RazonSocial  AS Razon_Social,
    FP.Moneda                 AS Moneda,
    FP.Fecha_Pedido           AS Fecha,
    FP.GrupoCompras_Den       AS Comprador,
    FP.Pedido_Estado          AS Estado_Ped,
    FP.Solped                 AS NSOLPED,
    FP.TipoCompra_Den         AS [Tipo de compra Denominacion]
FROM Compras.FACT_PEDIDO FP
{whereSql}";

            return EjecutarQueryAsync(query, parametros);
        }

        public Task<DataTable> ObtenerCompradorAsync()
        {
            const string query = @"
select DISTINCT Accion_Usuario_Fecha ,Fecha_Carga ,SOLPED, SOLPED_Posicion AS NPOs, Centro, SOLPED.Material, Texto_Breve, Cantidad_Solicitada  ,
    Compras.Pro  , Compras.RazSocial, Proveedor.Email , COMPRADOR.COMPRADOR_DESC ,RTRIM(COMPRADOR.COMPRADOR_EMAIL) as COMPRADOR_EMAIL,
    CASE
        WHEN DATEDIFF(DAY, CAST(Accion_Usuario_Fecha AS DATE), CAST(GETDATE() AS DATE)) BETWEEN 0 AND 7
            THEN '01 - 07 dias'
        WHEN DATEDIFF(DAY, CAST(Accion_Usuario_Fecha AS DATE), CAST(GETDATE() AS DATE)) BETWEEN 8 AND 14
            THEN '08 - 14 dias'
        WHEN DATEDIFF(DAY, CAST(Accion_Usuario_Fecha AS DATE), CAST(GETDATE() AS DATE)) BETWEEN 15 AND 21
            THEN '15 - 21 dias'
        WHEN DATEDIFF(DAY, CAST(Accion_Usuario_Fecha AS DATE), CAST(GETDATE() AS DATE)) >= 22
            THEN '22 dias a mas'
        ELSE 'Fecha futura'    END AS RANGO_FECHA, Contrato
    from almacenes.FACT_FILTRO_SOLPED_DIARIA SOLPED
        LEFT JOIN
        (Select  material collate Modern_Spanish_CI_AS As Mat, proveedor Collate Modern_Spanish_CI_AS As Pro,
            Proveedor_RazonSocial collate Modern_Spanish_CI_AS As RazSocial
            from  POWERBI.compras.fact_pedido
        union
         Select  material Collate Modern_Spanish_CI_AS, proveedor Collate Modern_Spanish_CI_AS ,
            Proveedor_RazonSocial collate Modern_Spanish_CI_AS
            from  compras.fact_pedido) Compras  on Compras.Mat = SOLPED.Material
        LEFT JOIN COMPRAS.DIM_PROVEEDORES_CONTACTOS  Proveedor
            on Proveedor.CodigoSAP = Compras.Pro
        INNER JOIN COMPRAS.DIM_COMPRADORES  COMPRADOR   ON COMPRADOR.COMPRADOR = SOLPED.Comprador COLLATE Modern_Spanish_CI_AS
    where Accion_Solicitante_Desc = 'Comprar' and Pedido = ''
    oRDER BY COMPRADOR.COMPRADOR_DESC , cOMPRAS.RazSocial ,
    SOLPED, SOLPED_Posicion
";

            return EjecutarQueryAsync(query);
        }

        public Task<DataTable> ObtenerJefeCompraAsync()
        {
            const string query = @"
select DISTINCT Accion_Usuario_Fecha ,SOLPED, SOLPED_Posicion AS NPOs, Centro,
    SOLPED.Material, Texto_Breve, Cantidad_Solicitada  , uNIDAD_MEDIDA, mONEDA, iMPORTE,
    Compras.Pro  , Compras.RazSocial , COMPRADOR.COMPRADOR_DESC ,     CASE
        WHEN DATEDIFF(DAY, CAST(Accion_Usuario_Fecha AS DATE), CAST(GETDATE() AS DATE)) BETWEEN 0 AND 7
            THEN '01 - 07 dias'
        WHEN DATEDIFF(DAY, CAST(Accion_Usuario_Fecha AS DATE), CAST(GETDATE() AS DATE)) BETWEEN 8 AND 14
            THEN '08 - 14 dias'
        WHEN DATEDIFF(DAY, CAST(Accion_Usuario_Fecha AS DATE), CAST(GETDATE() AS DATE)) BETWEEN 15 AND 21
            THEN '15 - 21 dias'
        WHEN DATEDIFF(DAY, CAST(Accion_Usuario_Fecha AS DATE), CAST(GETDATE() AS DATE)) >= 22
            THEN '22 dias a mas'
        ELSE 'Fecha futura'    END AS RANGO_FECHA
    from almacenes.FACT_FILTRO_SOLPED_DIARIA SOLPED
        LEFT JOIN
        (Select  material collate Modern_Spanish_CI_AS As Mat, proveedor Collate Modern_Spanish_CI_AS As Pro,
            Proveedor_RazonSocial collate Modern_Spanish_CI_AS As RazSocial
            from  POWERBI.compras.fact_pedido
        union
         Select  material Collate Modern_Spanish_CI_AS, proveedor Collate Modern_Spanish_CI_AS ,
            Proveedor_RazonSocial collate Modern_Spanish_CI_AS
            from  compras.fact_pedido) Compras  on Compras.Mat = SOLPED.Material
        LEFT JOIN COMPRAS.DIM_PROVEEDORES_CONTACTOS  Proveedor
            on Proveedor.CodigoSAP = Compras.Pro
        INNER JOIN COMPRAS.DIM_COMPRADORES  COMPRADOR   ON COMPRADOR.COMPRADOR = SOLPED.Comprador COLLATE Modern_Spanish_CI_AS
    where Accion_Solicitante_Desc = 'Comprar' and Pedido = ''
";

            return EjecutarQueryAsync(query);
        }

        private static (string WhereSql, List<SqlParameter> Parametros) ArmarWhere(ConsultaFiltro filtro)
        {
            if (filtro is not { Valido: true } || filtro.Valores.Count == 0)
            {
                throw new ArgumentException("El filtro de consulta no es válido.");
            }

            string columna = filtro.Tipo.ToUpperInvariant() switch
            {
                "SOLPED" => "FP.Solped",
                "PEDIDO" => "FP.Pedido",
                "PROVEEDOR" => "FP.Proveedor_RazonSocial",
                _ => throw new ArgumentException($"Tipo de filtro no permitido: {filtro.Tipo}")
            };

            var parametros = new List<SqlParameter>(filtro.Valores.Count);

            if (filtro.Tipo.Equals("PROVEEDOR", StringComparison.OrdinalIgnoreCase))
            {
                var likes = new List<string>(filtro.Valores.Count);
                for (int i = 0; i < filtro.Valores.Count; i++)
                {
                    string nombre = "@p" + i;
                    likes.Add($"{columna} LIKE {nombre}");
                    parametros.Add(new SqlParameter(nombre, SqlDbType.NVarChar, 200)
                    {
                        Value = "%" + filtro.Valores[i] + "%"
                    });
                }

                return ("WHERE " + string.Join(" OR ", likes), parametros);
            }

            var nombres = new List<string>(filtro.Valores.Count);
            for (int i = 0; i < filtro.Valores.Count; i++)
            {
                string nombre = "@p" + i;
                nombres.Add(nombre);
                parametros.Add(new SqlParameter(nombre, SqlDbType.VarChar, 32)
                {
                    Value = filtro.Valores[i]
                });
            }

            return ($"WHERE {columna} IN ({string.Join(", ", nombres)})", parametros);
        }

        private async Task<DataTable> EjecutarQueryAsync(string query, IEnumerable<SqlParameter>? parametros = null)
        {
            string? cadenaConexion = _configuration.GetConnectionString("SQLServer");

            if (string.IsNullOrWhiteSpace(cadenaConexion))
            {
                throw new InvalidOperationException("Falta ConnectionStrings:SQLServer.");
            }

            var datos = new DataTable();

            await using var conexion = new SqlConnection(cadenaConexion);
            await conexion.OpenAsync();

            await using var comando = new SqlCommand(query, conexion)
            {
                CommandType = CommandType.Text,
                CommandTimeout = 120
            };

            if (parametros is not null)
            {
                foreach (var parametro in parametros)
                {
                    comando.Parameters.Add(parametro);
                }
            }

            await using var lector = await comando.ExecuteReaderAsync();
            datos.Load(lector);

            return datos;
        }
    }
}
