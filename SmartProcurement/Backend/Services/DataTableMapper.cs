using System.Data;

namespace SmartProcurement.Services
{
    public static class DataTableMapper
    {
        public static List<Dictionary<string, object?>> ToDictionaryList(DataTable tabla)
        {
            var lista = new List<Dictionary<string, object?>>(tabla.Rows.Count);

            foreach (DataRow fila in tabla.Rows)
            {
                var registro = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                foreach (DataColumn columna in tabla.Columns)
                {
                    registro[columna.ColumnName] =
                        fila[columna] == DBNull.Value ? null : fila[columna];
                }

                lista.Add(registro);
            }

            return lista;
        }
    }
}
