namespace SqlLinqer
{
    // Resolve SQL syntax without changing the connector's database identity.
    internal static class SqlDialect
    {
        internal static DbFlavor Resolve(DbFlavor flavor)
            => flavor == DbFlavor.AzureSql ? DbFlavor.SqlServer : flavor;
    }
}
