using System;
using System.Data;
using System.Linq;
using System.Collections.Generic;
using SqlLinqer.Components.Render;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Extensions.TypeExtensions;
using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Components.Insert
{
    /// <summary>
    /// A collection of rows to be inserted into a table
    /// </summary>
    public class InsertRowCollection
    {
        /// <summary>
        /// A collection of rows to be inserted into a table
        /// </summary>
        protected readonly List<object> Rows;
        /// <summary>
        /// A collection columns
        /// </summary>
        protected readonly Dictionary<string, ReflectedColumn> Columns;

        /// <summary>
        /// The number of rows
        /// </summary>
        public int Count { get => Rows.Count; }

        /// <summary>
        /// A collection of rows to be inserted into a table
        /// </summary>
        public InsertRowCollection() : base()
        {
            Rows = new List<object>();
            Columns = new Dictionary<string, ReflectedColumn>();
        }

        /// <summary>
        /// Adds a column header to the collection
        /// </summary>
        /// <param name="column">The column to add</param>
        public void AddColumn(ReflectedColumn column)
        {
            if (!Columns.ContainsKey(column.UUID))
                Columns.Add(column.UUID, column);
        }
        /// <summary>
        /// Add a row to the collection
        /// </summary>
        /// <param name="obj">The object to add</param>
        public void AddRow(object obj) => Rows.Add(obj);
        /// <summary>
        /// Add a rows to the collection
        /// </summary>
        /// <param name="objs">The objects to add</param>
        public void AddRows(IEnumerable<object> objs) => Rows.AddRange(objs);

        /// <summary>
        /// Returns the required batch size given the size of the data and the given parameter limit
        /// </summary>
        /// <param name="parameterLimit">The known limit of parameters</param>
        public int GetBatchSize(int parameterLimit)
        {
            int batchCount = (int)Math.Ceiling(((double)Columns.Count * Rows.Count) / parameterLimit);
            return Rows.Count / batchCount;
        }

        /// <summary>
        /// Returns a create and drop query for a Sql type with the passed type id using the model of the data in the collection
        /// </summary>
        /// <param name="flavor">The type of Sql database being used</param>
        /// <param name="typeName">The string name of the Sql Type being used for this insert</param>
        public (string Create, string Drop) RenderTempType(DbFlavor flavor, string typeName)
        {
            var typedCols = Columns.Values
                .Select(x => $"{Environment.NewLine}\t{x.ColumnName.DbWrap(flavor)} {x.ValueType.CSharpTypeToSql(flavor)} NULL");
            string createStr = $"CREATE TYPE {typeName.DbWrap(flavor)} AS TABLE ({string.Join(",", typedCols)})";
            string dropStr = $"DROP TYPE {typeName.DbWrap(flavor)}";
            return (createStr, dropStr);
        }
        /// <summary>
        /// Returns the column string for the inserted rows
        /// </summary>
        /// <param name="flavor">The type of Sql database being used</param>
        public string RenderColumns(DbFlavor flavor)
        {
            var renderedColumns = Columns.Values
                .Select(x => $"{Environment.NewLine}\t{x.ColumnName.DbWrap(flavor)}");
            return $"{string.Join(",", renderedColumns)}";
        }
        /// <summary>
        /// Returns the row as a rendered Sql string
        /// </summary>
        /// <param name="parameters">The parameter collection being used for the current query</param>
        /// <param name="flavor">The type of Sql database being used</param>
        public string RenderAsRows(ParameterCollection parameters, DbFlavor flavor)
        {
            var insertRows = Rows.Select(obj => {
                var row = new InsertRow();                
                foreach (var column in Columns.Values)
                {
                    var value = new InsertRowValue(column.ColumnName, column.GetValue(obj), column.DbType);
                    row.Values.Add(value);
                }
                return row;
            });

            var renderedRows = insertRows
                .Select(x => $"{Environment.NewLine}\t{x.Render(parameters, flavor)}");
            return string.Join(",", renderedRows);
        }
        /// <summary>
        /// Adds all the rows and value to the passed datatable
        /// </summary>
        public DataTable RenderAsDataTable()
        {
            var dataTable = new DataTable();

            foreach (var c in Columns.Values)
            {
                dataTable.Columns.Add(c.ColumnName, c.ValueType);
            }

            foreach (var obj in Rows)
            {
                var dRow = dataTable.NewRow();

                foreach (var column in Columns.Values)
                {
                    var value = column.GetValue(obj);
                    dRow[column.ColumnName] = value == null ? DBNull.Value : value;
                }

                dataTable.Rows.Add(dRow);
            }

            return dataTable;
        }
    }
}