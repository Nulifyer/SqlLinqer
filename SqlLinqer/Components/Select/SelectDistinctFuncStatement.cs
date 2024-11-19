namespace SqlLinqer.Components.Select
{
    public class SelectDistinctFuncStatement : SelectFuncStatement
    {
        public SelectDistinctFuncStatement(SqlFunc func, bool distinct, string alias, params object[] args) : base(func, alias, args)
        {
            if (distinct)
                ArgPrefix = "DISTINCT ";
        }
    }
}