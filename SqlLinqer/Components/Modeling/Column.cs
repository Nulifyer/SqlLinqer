using System.Collections.Generic;
using System.Data;
using SqlLinqer.Extensions.StringExtensions;

namespace SqlLinqer.Components.Modeling
{
    public class Column
    {
        public string UUID
        { 
            get => $"{Table.UUID}.{ColumnName}".GetHashString();
        }

        public Table Table;
        public string ColumnName;
        public DbType? DbType;
        
        public string _alias;
        public string Alias 
        {
            get => _alias ?? UUID;
            set => _alias = value;
        }
        

        public Column(Table table, string column_name)
        {
            Table = table;
            ColumnName = column_name;
        }
        public Column(Table table, string column_name, string alias) : this(table, column_name)
        {
            Alias = alias;
        }

        public string Render(DbFlavor flavor)
        {
            return $"{Table.Alias?.DbWrap(flavor)}.{ColumnName?.DbWrap(flavor)}";
        }

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

        public override string ToString()
        {
            return $"{Table}.{ColumnName}";
        }
    }
}