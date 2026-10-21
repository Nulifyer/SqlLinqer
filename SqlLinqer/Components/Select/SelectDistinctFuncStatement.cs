namespace SqlLinqer.Components.Select
{
    /// <summary>A SQL function expression with an optional DISTINCT argument modifier.</summary>
    public class SelectDistinctFuncStatement : SelectFuncStatement
    {
        /// <summary>Creates a function expression with an optional DISTINCT modifier.</summary>
        public SelectDistinctFuncStatement(SqlFunc func, bool distinct, string alias, params object[] args) : base(func, alias, args)
        {
            if (distinct)
                ArgPrefix = "DISTINCT ";
        }
    }
}
