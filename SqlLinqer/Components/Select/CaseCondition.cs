using System.Text;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Where;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Select
{
    /// <summary>A conditional CASE branch, or an ELSE branch when its condition is null.</summary>
    public class CaseCondition
    {
        /// <summary>The WHEN predicate, or null for an ELSE branch.</summary>
        public WhereCollection Condition { get; protected set; }
        /// <summary>The column, expression, or value returned by this branch.</summary>
        public readonly object ReturnValue;

        /// <summary>Creates a CASE branch with an optional condition.</summary>
        public CaseCondition(Column column)
        {
            Condition = null;
            ReturnValue = column;
        }
        /// <summary>Creates a CASE branch with an optional condition.</summary>
        public CaseCondition(object return_value)
        {
            Condition = null;
            ReturnValue = return_value;
        }
        /// <summary>Creates a CASE branch with an optional condition.</summary>
        public CaseCondition(WhereCollection condition, Column column)
        {
            Condition = condition;
            ReturnValue = column;
        }
        /// <summary>Creates a CASE branch with an optional condition.</summary>
        public CaseCondition(WhereCollection condition, object return_value)
        {
            Condition = condition;
            ReturnValue = return_value;
        }

        /// <summary>
        /// Renders the object into a string.
        /// </summary>
        /// <param name="query">The current query</param>
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
            else if (ReturnValue is SelectStatement statement)
            {
                returnValue = statement.Render(query, parameters, flavor, false, false);
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
