using System;
using System.Globalization;
using SqlLinqer.Modeling;
using static SqlLinqer.Compilation.ReusableTvpType;

namespace SqlLinqer.Compilation
{
    // Leases select idle candidates. Transaction-owned application locks establish whether deletion is safe.
    internal static class TvpLeaseSql
    {
        internal const string Table = "_SqlLinqerTvpLeases_v1";
        private const string Owner = "SqlLinqer.TvpLeaseRegistry.v1";
        internal static string Name(string schema) => Quote(schema) + "." + Quote(Table);
        private static string Exists(string schema) => "OBJECT_ID(" + Literal(Name(schema)) + ",N'U') IS NOT NULL";
        private static string Owned(string schema) => "EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID("
            + Literal(Name(schema)) + ") AND minor_id=0 AND name=" + Literal(Owner) + " AND CONVERT(nvarchar(max),value)=N'1')";

        // Catalog metadata is database-wide. Use one lock order across schemas: catalog -> type -> lease rows.
        // Cleanup never waits for an active type; executions retain only their type guard.
        internal static string CatalogLock(string mode = "Exclusive")
            => LockSql("catalog", mode).Replace("@lock", "@catalogLock");

        internal static string Initialize(string schema)
            => LockSql("registry:" + schema.ToUpperInvariant()).Replace("@lock", "@registryLock")
            + "IF NOT (" + Exists(schema) + ") BEGIN CREATE TABLE " + Name(schema)
            + " (TypeName nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL, Generation uniqueidentifier NOT NULL, NodeId uniqueidentifier NOT NULL,"
            + " LeaseUntil datetime2(7) NOT NULL, RetireAfter datetime2(7) NOT NULL, PRIMARY KEY (TypeName,Generation,NodeId));"
            + " EXEC sys.sp_addextendedproperty @name=" + Literal(Owner) + ",@value=N'1',@level0type=N'SCHEMA',@level0name="
            + Literal(schema) + ",@level1type=N'TABLE',@level1name=" + Literal(Table) + "; END;\n"
            + "IF NOT (" + Owned(schema) + ") BEGIN; THROW 50000, 'The TVP lease registry name is occupied by an unowned table.', 1; END;\n";

        // Dynamic SQL prevents compilation from binding a registry table that has not been enabled in this schema.
        internal static string Renew(ReusableTvpType type, Guid node, int leaseSeconds, int retentionSeconds)
        {
            var table = Name(type.Schema);
            var body = "DECLARE @generation uniqueidentifier=(SELECT TRY_CONVERT(uniqueidentifier,value) FROM sys.extended_properties WHERE class=6 AND major_id=TYPE_ID("
                + Literal(type.TypeName) + ") AND minor_id=0 AND name=" + Literal(GenerationProperty) + ");"
                + "IF @generation IS NULL BEGIN; THROW 50000, 'The managed TVP ownership generation is missing.', 1; END;"
                + "DECLARE @until datetime2(7)=DATEADD(second," + leaseSeconds.ToString(CultureInfo.InvariantCulture) + ",SYSUTCDATETIME());"
                + "DECLARE @retire datetime2(7)=DATEADD(second," + retentionSeconds.ToString(CultureInfo.InvariantCulture) + ",@until);"
                + "DELETE FROM " + table + " WHERE TypeName=" + Literal(type.Name) + " AND (Generation<>@generation OR (NodeId<>"
                + Literal(node.ToString()) + " AND RetireAfter<SYSUTCDATETIME()));"
                + "UPDATE " + table + " SET LeaseUntil=@until,RetireAfter=@retire WHERE TypeName=" + Literal(type.Name)
                + " AND Generation=@generation AND NodeId=" + Literal(node.ToString()) + ";"
                + "IF @@ROWCOUNT=0 INSERT INTO " + table + " VALUES (" + Literal(type.Name) + ",@generation," + Literal(node.ToString()) + ",@until,@retire);";
            return CatalogLock("Shared") + LockSql(type.Hash, "Shared") + LockSql("lease:" + type.Hash).Replace("@lock", "@leaseLock") + "IF TYPE_ID(" + Literal(type.TypeName) + ") IS NULL BEGIN SELECT 0; RETURN; END;\n"
                + "IF " + Exists(type.Schema) + " BEGIN IF NOT (" + Owned(type.Schema)
                + ") BEGIN; THROW 50000, 'The TVP lease registry is not owned by SqlLinqer.', 1; END; EXEC(" + Literal(body) + "); END; SELECT 1;";
        }

        internal static string Guard(ReusableTvpType type)
            => LockSql(type.Hash, "Shared") + "IF TYPE_ID(" + Literal(type.TypeName) + ") IS NULL BEGIN EXEC sys.sp_releaseapplock @Resource="
                + Literal(LockResource(type.Hash)) + ",@LockOwner='Transaction'; SELECT 0; RETURN; END; SELECT 1;";

        internal static string Candidates(string schema, string inventory)
        {
            var select = "DELETE TOP (1000) l FROM " + Name(schema) + " l WHERE NOT EXISTS (SELECT 1 FROM sys.table_types t JOIN sys.extended_properties g ON g.class=6 AND g.major_id=t.user_type_id AND g.minor_id=0 AND g.name="
                + Literal(GenerationProperty) + " WHERE t.schema_id=SCHEMA_ID(" + Literal(schema) + ") AND t.name COLLATE Latin1_General_100_BIN2=l.TypeName AND TRY_CONVERT(uniqueidentifier,g.value)=l.Generation);"
                + "SELECT TOP (100) t.* FROM (" + inventory.Replace(" ORDER BY s.name,t.name;", "")
                + ") t(DatabaseName,SchemaName,TypeName,ContractHash,TypeId,Generation) JOIN " + Name(schema)
                + " l ON l.TypeName=t.TypeName COLLATE Latin1_General_100_BIN2 AND l.Generation=TRY_CONVERT(uniqueidentifier,t.Generation)"
                + " WHERE t.SchemaName=" + Literal(schema)
                + " AND NOT EXISTS (SELECT 1 FROM sys.parameter_type_usages WHERE user_type_id=t.TypeId)"
                + " AND NOT EXISTS (SELECT 1 FROM sys.sql_expression_dependencies WHERE referenced_class=6 AND referenced_id=t.TypeId)"
                + " GROUP BY t.DatabaseName,t.SchemaName,t.TypeName,t.ContractHash,t.TypeId,t.Generation HAVING MAX(l.RetireAfter)<=SYSUTCDATETIME() ORDER BY MAX(l.RetireAfter);";
            return CatalogLock() + "IF " + Exists(schema) + " BEGIN IF NOT (" + Owned(schema)
                + ") BEGIN; THROW 50000, 'The TVP lease registry is not owned by SqlLinqer.', 1; END; EXEC(" + Literal(select) + "); END;";
        }

        internal static string CheckRetirement(SqlTvpTypeInfo type)
        {
            var body = "IF NOT EXISTS (SELECT 1 FROM " + Name(type.Schema) + " WHERE TypeName=" + Literal(type.Name)
                + " AND Generation=" + Literal(type.Generation) + ") OR EXISTS (SELECT 1 FROM " + Name(type.Schema)
                + " WHERE TypeName=" + Literal(type.Name) + " AND Generation=" + Literal(type.Generation)
                + " AND RetireAfter>SYSUTCDATETIME()) SET @eligible=0;";
            return "IF NOT (" + Owned(type.Schema) + ") BEGIN SELECT 0; RETURN; END; DECLARE @eligible bit=1;"
                + "EXEC sys.sp_executesql " + Literal(body) + ",N'@eligible bit OUTPUT',@eligible OUTPUT;"
                + "IF @eligible=0 OR EXISTS (SELECT 1 FROM sys.parameter_type_usages WHERE user_type_id=@type)"
                + " OR EXISTS (SELECT 1 FROM sys.sql_expression_dependencies WHERE referenced_class=6 AND referenced_id=@type) BEGIN SELECT 0; RETURN; END;\n";
        }

        internal static string Remove(SqlTvpTypeInfo type)
            => "IF " + Owned(type.Schema) + " EXEC(" + Literal("DELETE FROM " + Name(type.Schema) + " WHERE TypeName="
                + Literal(type.Name) + " AND Generation=" + Literal(type.Generation) + ";") + ");\n";
    }
}
