using System;
using System.Security.Cryptography;
using System.Text;

namespace SqlLinqer.Compilation
{
    // Identity is the complete rendered SQL contract, not the CLR type or this request's row count.
    internal sealed class ReusableTvpType
    {
        internal const string Prefix = "SqlLinqerTvp_v1_";
        internal const string OwnershipProperty = "SqlLinqer.TvpContract.v1";
        internal const string GenerationProperty = "SqlLinqer.TvpGeneration.v1";
        internal const string Placeholder = "__SqlLinqerTvpContract";
        internal readonly string Schema;
        internal readonly string Name;
        internal readonly string Hash;
        internal readonly string TypeName;
        internal readonly bool Managed;
        private readonly string _create;
        private readonly string _schemaQuery;

        internal ReusableTvpType(string schema, string definition, string schemaQuery, string existingName = null)
        {
            ValidateIdentifier(schema, nameof(schema));
            Schema = schema;
            var header = "CREATE TYPE " + Quote(Placeholder);
            if (!definition.StartsWith(header + " AS TABLE ", StringComparison.Ordinal))
                throw new NotSupportedException("Reusable TVPs require a CREATE TYPE AS TABLE definition.");
            var canonical = definition.Replace("\r\n", "\n").Replace('\r', '\n');
            using (var sha = SHA256.Create())
                Hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes("v1\n" + canonical))).Replace("-", "").ToLowerInvariant();
            Managed = existingName == null;
            Name = existingName ?? Prefix + Hash;
            ValidateIdentifier(Name, nameof(existingName));
            TypeName = Quote(Schema) + "." + Quote(Name);
            _create = "CREATE TYPE " + TypeName + definition.Substring(header.Length);
            _schemaQuery = schemaQuery;
        }

        internal string EnsureSql(bool leases = false)
        {
            var sql = new StringBuilder();
            if (Managed)
            {
                // Serialize catalog provisioning with registry maintenance before taking type locks.
                // Cold contracts across schemas must not create catalog objects in competing transactions.
                sql.Append(TvpLeaseSql.CatalogLock());
                if (leases) sql.Append(TvpLeaseSql.Initialize(Schema));
            }
            sql.Append(LockSql(Managed ? Hash : Schema.ToUpperInvariant() + "." + Name.ToUpperInvariant(), "Shared"));
            sql.Append("DECLARE @created int = 0;\nDECLARE @type int = TYPE_ID(").Append(Literal(TypeName)).Append(");\n");
            sql.Append("IF @type IS NULL BEGIN\n");
            if (Managed)
            {
                sql.Append("EXEC sys.sp_releaseapplock @Resource=").Append(Literal(LockResource(Hash))).Append(",@LockOwner='Transaction';\n")
                    .Append(LockSql(Hash).Replace("DECLARE @lock int; ", ""))
                    .Append("SET @type=TYPE_ID(").Append(Literal(TypeName)).Append("); IF @type IS NULL BEGIN\n");
                sql.Append("EXEC(").Append(Literal(_create)).Append(");\n")
                    .Append("EXEC sys.sp_addextendedproperty @name=").Append(Literal(OwnershipProperty))
                    .Append(", @value=").Append(Literal(Hash))
                    .Append(", @level0type=N'SCHEMA', @level0name=").Append(Literal(Schema))
                    .Append(", @level1type=N'TYPE', @level1name=").Append(Literal(Name)).Append(";\n")
                    .Append("DECLARE @generation nvarchar(36)=CONVERT(nvarchar(36),NEWID());\nEXEC sys.sp_addextendedproperty @name=")
                    .Append(Literal(GenerationProperty)).Append(", @value=@generation, @level0type=N'SCHEMA', @level0name=")
                    .Append(Literal(Schema)).Append(", @level1type=N'TYPE', @level1name=").Append(Literal(Name)).Append(";\nSET @created=1; END;\n");
            }
            else sql.Append(";THROW 50000, 'The configured existing TVP type does not exist.', 1;\n");
            sql.Append("END;\nSET @type=TYPE_ID(").Append(Literal(TypeName)).Append(");\n");
            if (Managed)
                sql.Append("IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=6 AND major_id=@type AND minor_id=0 AND name=")
                    .Append(Literal(OwnershipProperty)).Append(" AND DATALENGTH(CONVERT(nvarchar(max),value))=128 AND CONVERT(nvarchar(max),value)=").Append(Literal(Hash))
                    .Append(") OR NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=6 AND major_id=@type AND minor_id=0 AND name=")
                    .Append(Literal(GenerationProperty)).Append(" AND TRY_CONVERT(uniqueidentifier,value) IS NOT NULL)")
                    .Append(" BEGIN; THROW 50000, 'The reusable TVP name is occupied by an unowned or incompatible type.', 1; END;\n");
            sql.Append("DECLARE @object int=(SELECT type_table_object_id FROM sys.table_types WHERE user_type_id=@type);\n")
                .Append("IF @object IS NULL BEGIN; THROW 50000, 'The configured TVP type is not a table type.', 1; END;\n");
            var common = "column_id AS ordinal, name COLLATE Latin1_General_100_BIN2 AS name, CONVERT(int,system_type_id) AS system_type_id";
            var expected = "column_ordinal AS ordinal, name COLLATE Latin1_General_100_BIN2 AS name, system_type_id";
            if (Managed)
            {
                common += ",max_length,precision,scale,is_nullable,collation_name COLLATE Latin1_General_100_BIN2 AS collation_name";
                expected += ",max_length,precision,scale,is_nullable,collation_name COLLATE Latin1_General_100_BIN2 AS collation_name";
            }
            var actualQuery = "SELECT " + common + " FROM sys.columns WHERE object_id=@object";
            var expectedQuery = "SELECT " + expected + " FROM sys.dm_exec_describe_first_result_set(" + Literal(_schemaQuery) + ",NULL,0)";
            sql.Append("IF EXISTS (").Append(actualQuery).Append(" EXCEPT ").Append(expectedQuery)
                .Append(") OR EXISTS (").Append(expectedQuery).Append(" EXCEPT ").Append(actualQuery)
                .Append(") BEGIN; THROW 50000, 'The TVP columns do not match the mapped SQL contract.', 1; END;\n");
            if (Managed)
                sql.Append("IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=@object AND (is_identity=1 OR is_computed=1 OR default_object_id<>0)) ")
                    .Append("OR EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=@object AND index_id<>0) ")
                    .Append("OR EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=@object) ")
                    .Append("BEGIN; THROW 50000, 'The reusable TVP has unexpected constraints.', 1; END;\n");
            return sql.Append("SELECT @created;").ToString();
        }

        internal static string LockSql(string identity, string mode = "Exclusive", int timeout = 15000, bool skipBusy = false)
        {
            return "DECLARE @lock int; EXEC @lock=sys.sp_getapplock @Resource=" + Literal(LockResource(identity))
                + ",@LockMode='" + mode + "',@LockOwner='Transaction',@LockTimeout=" + timeout + ";\n"
                + (skipBusy ? "IF @lock<0 BEGIN SELECT 0; RETURN; END;\n" : "IF @lock<0 BEGIN; THROW 50000, 'Could not acquire the TVP type provisioning lock.', 1; END;\n");
        }

        internal static string LockResource(string identity)
        {
            using (var sha = SHA256.Create())
                return "SqlLinqer.Tvp.v1:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "");
        }

        internal static string DropSql(Modeling.SqlTvpTypeInfo type, bool expiredOnly = false)
        {
            ValidateIdentifier(type.Schema, nameof(type.Schema)); ValidateIdentifier(type.Name, nameof(type.Name));
            if (type.ContractHash == null || type.ContractHash.Length != 64 || type.Name != Prefix + type.ContractHash || !Guid.TryParse(type.Generation, out _))
                throw new ArgumentException("The selected type is not a managed reusable TVP.", nameof(type));
            var name = Quote(type.Schema) + "." + Quote(type.Name);
            return TvpLeaseSql.CatalogLock() + LockSql(type.ContractHash, timeout: expiredOnly ? 0 : 15000, skipBusy: expiredOnly)
                + "DECLARE @type int=TYPE_ID(" + Literal(name) + ");\n"
                + "IF @type IS NULL BEGIN SELECT 0; RETURN; END;\n"
                + "IF @type<>" + type.TypeId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " OR NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=6 AND major_id=@type AND minor_id=0 AND name="
                + Literal(OwnershipProperty) + " AND DATALENGTH(CONVERT(nvarchar(max),value))=128 AND CONVERT(nvarchar(max),value)=" + Literal(type.ContractHash) + ")\n"
                + " OR NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=6 AND major_id=@type AND minor_id=0 AND name="
                + Literal(GenerationProperty) + " AND CONVERT(nvarchar(36),value)=" + Literal(type.Generation) + ")\n"
                + (expiredOnly ? "BEGIN SELECT 0; RETURN; END;\n" : "BEGIN; THROW 50000, 'The selected TVP type has changed or is not owned by SqlLinqer.', 1; END;\n")
                + (expiredOnly ? TvpLeaseSql.CheckRetirement(type) : "")
                + "DROP TYPE " + name + ";\n" + TvpLeaseSql.Remove(type) + "SELECT 1;";
        }

        internal static void ValidateIdentifier(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128) throw new ArgumentException("A SQL identifier must contain 1 through 128 characters.", parameter);
        }
        internal static string Quote(string value) => "[" + value.Replace("]", "]]") + "]";
        internal static string Literal(string value) => "N'" + value.Replace("'", "''") + "'";
    }
}
