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
    /// <summary>A common table expression and its dependencies.</summary>
    public class With
    {
        /// <summary>The common table expression name.</summary>
        public readonly string Alias;
        /// <summary>The SELECT query defining the common table expression.</summary>
        public readonly SelectQuery Query;
        /// <summary>The mapped table representing the expression's output columns.</summary>
        public readonly Table OutputTable;
        /// <summary>The common table expressions required before this expression.</summary>
        public readonly List<With> DependsOn;

        private int? _order;
        /// <summary>The dependency depth used to order common table expressions.</summary>
        public int Order
        {
            get => _order ?? (DependsOn.Count > 0 ? DependsOn.Sum(x => x.Order) : 0);
            set => _order = value;
        }

        /// <summary>Creates a named common table expression from a SELECT query.</summary>
        public With(SelectQuery query, string alias)
        {
            Query = query;
            Alias = alias;
            DependsOn = new List<With>();
            OutputTable = new Table(null, Alias, Alias);

            foreach (var select in Query.Selects.GetAll())
            {
                var with_col = new Column(OutputTable, select.Alias, select.Alias);
                OutputTable.Columns.AddColumn(with_col);
            }
        }

        /// <summary>Renders this component for the selected database dialect.</summary>
        public string Render(JoinQuery query, ParameterCollection parameters, DbFlavor flavor)
        {
            return $"{Alias.DbWrap(flavor)} AS ({Query.Render(parameters, flavor, false)})";
        }
    }
}
