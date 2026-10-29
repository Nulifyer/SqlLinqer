namespace SqlLinqer.Modeling
{
    /// <summary>Identifies a persisted TVP type owned by SqlLinqer. Cleanup requires all consumers to be stopped.</summary>
    public sealed class SqlTvpTypeInfo
    {
        /// <summary>The database containing this type.</summary>
        public string Database { get; set; }
        /// <summary>The schema containing this type.</summary>
        public string Schema { get; set; }
        /// <summary>The unqualified type name.</summary>
        public string Name { get; set; }
        /// <summary>The stable SHA-256 SQL contract fingerprint.</summary>
        public string ContractHash { get; set; }
        /// <summary>The creation generation, used to protect replacements even if a database type ID is recycled.</summary>
        public string Generation { get; set; }
        /// <summary>The database type ID, used to reject stale cleanup requests.</summary>
        public int TypeId { get; set; }
    }
}
