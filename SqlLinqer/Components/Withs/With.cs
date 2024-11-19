using System.Linq;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Select;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Queries.Core;
using SqlLinqer.Extensions.StringExtensions;
using System.Collections.Generic;
using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Components.Withs
{
    public class With
    {
        public readonly string Alias;
        public readonly SelectQuery Query;
        public readonly Table OutputTable;
        public readonly List<With> DependsOn;
        
        private int? _order;
        public int Order
        {
            get => _order ?? (DependsOn.Count > 0 ? DependsOn.Sum(x => x.Order) : 0);
            set => _order = value;
        }

        public With(SelectQuery query, string alias)
        {
            Query = query;
            Alias = alias;
            DependsOn = new List<With>();
            OutputTable = new Table(null, Alias, Alias);
            
            foreach (var select in Query.Selects.GetAll())
            {
                var with_col = new Column(OutputTable, select.Alias);
                OutputTable.Columns.AddColumn(with_col);
            }
        }

        public string Render(JoinQuery query, ParameterCollection parameters, DbFlavor flavor)
        {
            return $"{Alias.DbWrap(flavor)} AS ({Query.Render(parameters, flavor, false)})";
        }
    }
}