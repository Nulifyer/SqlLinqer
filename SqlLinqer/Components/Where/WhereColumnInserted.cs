using System.Collections.Generic;
using System.Text;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Update;
using SqlLinqer.Queries.Core.Abstract;

namespace SqlLinqer.Components.Where
{
    /// <summary>
    /// A condition of an insert's conflict action that compares the existing row's value with the value the insert supplied for the same column.
    /// </summary>
    public class WhereColumnInserted : WhereStatement
    {
        /// <summary>The compared column. The existing row's value is on the left.</summary>
        public readonly Column Column;
        /// <summary>The comparison operator.</summary>
        public readonly SqlOp Operator;

        /// <summary>Creates a comparison between a column's existing value and its inserted value.</summary>
        public WhereColumnInserted(Column column, SqlOp op)
        {
            Column = column;
            Operator = op;
        }

        /// <summary>The preparation order used to sort predicates.</summary>
        public override int Order => 150;

        /// <inheritdoc/>
        public override IWhereComponent Invert() => new WhereColumnInserted(Column, Operator.GetInvert());

        /// <inheritdoc/>
        public override IWhereComponent Finalize(WhereQuery query) => this;

        /// <inheritdoc/>
        public override bool TryCombineInto(WhereCollection current_collection, IWhereComponent parent_component) => false;

        /// <inheritdoc/>
        public override void Render(WhereQuery query, ParameterCollection parameters, DbFlavor flavor, StringBuilder builder)
        {
            builder.Append($"{Column.Render(flavor)} {Operator.GetOpString(flavor)} {ColumnInsertedUpdate.RenderInserted(Column, flavor)}");
        }

        /// <inheritdoc/>
        public override IEnumerable<Column> GetColumnReferences()
        {
            yield return Column;
        }
    }
}
