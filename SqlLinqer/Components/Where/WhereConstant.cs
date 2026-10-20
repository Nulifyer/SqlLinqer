using System.Collections.Generic;
using System.Text;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core.Abstract;

namespace SqlLinqer.Components.Where
{
    internal sealed class WhereConstant : WhereStatement
    {
        private readonly bool _value;

        internal WhereConstant(bool value) { _value = value; }
        public override int Order => 0;
        public override IWhereComponent Invert() => new WhereConstant(!_value);
        public override IWhereComponent Finalize(WhereQuery query) => this;
        public override bool TryCombineInto(WhereCollection collection, IWhereComponent parent) => false;
        public override IEnumerable<Column> GetColumnReferences() => new Column[0];

        public override void Render(WhereQuery query, ParameterCollection parameters, DbFlavor flavor, StringBuilder builder)
        {
            builder.Append(_value ? "1 = 1" : "1 = 0");
        }
    }
}
