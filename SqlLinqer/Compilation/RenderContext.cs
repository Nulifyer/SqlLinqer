using SqlLinqer.Components.Render;

namespace SqlLinqer.Compilation
{
    internal sealed class RenderContext
    {
        internal ParameterCollection Parameters { get; }
        internal SqlDialect Dialect { get; }
        internal RenderContext(ParameterCollection parameters, SqlDialect dialect)
        { Parameters = parameters; Dialect = dialect; parameters.SetFlavor(dialect.Flavor); }
    }

    // Prepared fragments belong to one compilation. No builder is retained by the compiler.
    internal sealed class PreparedSelect
    {
        internal readonly string With, Select, From, Where, GroupBy, OrderBy;
        internal readonly bool Distinct, ForJson, IsSubQuery, Collection;
        internal readonly int Top, Limit, Offset;
        internal PreparedSelect(string with, string select, string from, string where, string groupBy, string orderBy,
            bool distinct, bool forJson, bool isSubQuery, bool collection, int top, int limit, int offset)
        {
            With = with; Select = select; From = from; Where = where; GroupBy = groupBy; OrderBy = orderBy;
            Distinct = distinct; ForJson = forJson; IsSubQuery = isSubQuery; Collection = collection;
            Top = top; Limit = limit; Offset = offset;
        }
    }
}
