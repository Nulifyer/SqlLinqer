using System.Collections.Generic;
using System.Data;
using SqlLinqer.Extensions.StringExtensions;

namespace SqlLinqer.Components.Modeling
{
    /// <summary>A database column, its owning table, and its query alias.</summary>
    public class Column
    {
        private string _uuid;
        private string _uuidTable;
        private string _uuidColumn;
        /// <summary>A stable query identifier derived from the table identity and column name.</summary>
        public string UUID
        {
            get
            {
                var tableId = Table.UUID;
                if (_uuid == null || _uuidTable != tableId || _uuidColumn != ColumnName)
                {
                    _uuid = Table.Identities.Column(tableId, ColumnName);
                    _uuidTable = tableId;
                    _uuidColumn = ColumnName;
                }
                return _uuid;
            }
        }

        /// <summary>The table owning the column.</summary>
        public Table Table;
        /// <summary>The database column name.</summary>
        public string ColumnName;
        /// <summary>An explicit parameter type, or null to let the provider infer it.</summary>
        public DbType? DbType;

        /// <summary>The explicit output alias, or null to use the generated column identifier.</summary>
        public string _alias;
        /// <summary>The explicit output alias, falling back to the generated column identifier.</summary>
        public string Alias
        {
            get => _alias ?? UUID;
            set => _alias = value;
        }


        /// <summary>Creates a column mapping from a table, column name, and any supplied alias.</summary>
        public Column(Table table, string column_name)
        {
            Table = table;
            ColumnName = column_name;
        }
        /// <summary>Creates a column mapping from a table, column name, and any supplied alias.</summary>
        public Column(Table table, string column_name, string alias) : this(table, column_name)
        {
            Alias = alias;
        }

        /// <summary>Renders this component for the selected database dialect.</summary>
        public string Render(DbFlavor flavor)
        {
            if (Table.NativeBindings != null && Table.NativeBindings.TryGetValue(ColumnName, out var expression))
                return expression;
            return $"{Table.Alias?.DbWrap(flavor)}.{ColumnName?.DbWrap(flavor)}";
        }

        /// <summary>Returns a column whose explicit alias is this column's generated identifier.</summary>
        public Column WithAliasAsUUID()
        {
            return new Column(Table, ColumnName, UUID);
        }

        /// <summary>
        /// Returns the path of this column
        /// </summary>
        /// <param name="root">The root table to stop at</param>
        public List<string> GetPath(Table root)
        {
            var path = Table.GetPath(root);
            path.Add(Alias);
            return path;
        }

        /// <summary>Returns a diagnostic description of this mapping.</summary>
        public override string ToString()
        {
            return $"{Table}.{ColumnName}";
        }
    }
}
