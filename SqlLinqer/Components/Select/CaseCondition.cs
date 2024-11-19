using System.Text;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Where;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Select
{
    public class CaseCondition
    {
        public WhereCollection Condition { get; protected set; }
        public readonly object ReturnValue;

        public CaseCondition(Column column)
        {
            Condition = null;
            ReturnValue = column;
        }
        public CaseCondition(object return_value)
        {
            Condition = null;
            ReturnValue = return_value;
        }
        public CaseCondition(WhereCollection condition, Column column)
        {
            Condition = condition;
            ReturnValue = column;
        }
        public CaseCondition(WhereCollection condition, object return_value)
        {
            Condition = condition;
            ReturnValue = return_value;
        }

        /// <summary>
        /// Renders the object into a string.
        /// </summary>
        /// <param name="query">The query current query</param>
        /// <param name="parameters">The parameter collection to use</param>
        /// <param name="flavor">The type of Sql database being used</param>
        /// <param name="builder">The string builder to render to</param>
        public void Render(SelectQuery query, ParameterCollection parameters, DbFlavor flavor, StringBuilder builder)
        {
            string rendered_where = Condition?.Render(query, parameters, flavor, include_where: false);

            string returnValue;
            if (ReturnValue is Column column)
            {
                query.Joins.AddTable(column.Table);
                returnValue = column.Render(flavor);
            }
            else
            {
                returnValue = parameters.AddParameter(ReturnValue);
            }

            if (rendered_where != null)
                builder.Append($"when {rendered_where} then {returnValue}");
            else
                builder.Append($"else {returnValue}");
        }
    }
}