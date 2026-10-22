using System;
using SqlLinqer.Extensions.StringExtensions;

namespace SqlLinqer.Compilation
{
    internal sealed class SqlDialectCapabilities
    {
        internal bool RequiresOrderForOffset { get; }
        internal bool OrderingNeedsSelectedColumn { get; }
        internal bool HoistCountWiths { get; }
        internal SqlDialectCapabilities(bool requiresOrder, bool selectedOrder, bool hoistCount)
        { RequiresOrderForOffset = requiresOrder; OrderingNeedsSelectedColumn = selectedOrder; HoistCountWiths = hoistCount; }
    }

    internal abstract class SqlDialect
    {
        private static readonly SqlDialect[] Dialects = {
            new SqlServerDialect(DbFlavor.SqlServer), new SqlServerDialect(DbFlavor.AzureSql),
            new PostgresDialect(), new MySqlDialect(), new SqliteDialect(), new DuckDbDialect()
        };
        internal DbFlavor Flavor { get; }
        internal SqlDialectCapabilities Capabilities { get; }
        internal virtual bool JsonRows => true;
        protected SqlDialect(DbFlavor flavor, bool requiresOrder = false, bool selectedOrder = true, bool hoistCount = false)
        { Flavor = flavor; Capabilities = new SqlDialectCapabilities(requiresOrder, selectedOrder, hoistCount); }
        internal static SqlDialect For(DbFlavor flavor)
        {
            foreach (var dialect in Dialects) if (dialect.Flavor == flavor) return dialect;
            throw new NotSupportedException($"The database flavor {flavor} is not supported.");
        }

        internal virtual string Paging(int top, int limit, int offset)
        {
            if (top > 0) { limit = top; offset = 0; }
            return limit > 0 ? $" LIMIT {limit} OFFSET {offset} " : null;
        }
        protected virtual string Top(PreparedSelect select) => null;
        protected virtual string SelectSuffix(PreparedSelect select) => null;
        protected virtual string QuerySuffix(PreparedSelect select) => null;
        internal virtual string Render(PreparedSelect select)
        {
            return string.Join(" ", new[] {
                select.With, $"SELECT{(select.Distinct ? " DISTINCT" : null)}{Top(select)}",
                select.Select + SelectSuffix(select), select.From, select.Where, select.GroupBy, select.OrderBy,
                Paging(select.Top, select.Limit, select.Offset), QuerySuffix(select)
            });
        }
        internal virtual string RenderCount(PreparedSelect inner, string with)
            => $"SELECT COUNT(*) FROM ({Render(inner)}) AS {"SqlLinqerCount".DbWrap(Flavor)}";
        internal abstract string JsonProjection(string fields, bool distinct, bool nestedCollection);

        internal virtual PreparedSelect JsonCollection(string fields, string source, string order)
            => new PreparedSelect(null, JsonProjection(fields, false, true), source, null, null, null,
                false, true, true, true, 0, 0, 0);

        internal virtual string Relationship(string sql, string alias, bool collection, bool forJson, bool includeAlias)
        {
            string value = $"({sql})";
            if (forJson) return $"{alias.DbWrapString(Flavor)}, {value}";
            return value + (includeAlias ? $" AS {alias.DbWrap(Flavor)}" : null);
        }

        private sealed class SqlServerDialect : SqlDialect
        {
            internal SqlServerDialect(DbFlavor flavor) : base(flavor, true, true, true) { }
            internal override bool JsonRows => false;
            internal override string Paging(int top, int limit, int offset)
                => limit > 0 ? $" OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY" : null;
            protected override string Top(PreparedSelect select) => select.Top > 0 ? $" TOP {select.Top}" : null;
            protected override string QuerySuffix(PreparedSelect select) => select.ForJson ? "FOR JSON AUTO, INCLUDE_NULL_VALUES" : null;
            internal override string JsonProjection(string fields, bool distinct, bool nestedCollection) => fields;
            internal override string RenderCount(PreparedSelect inner, string with)
                => $"{with} SELECT COUNT(*) FROM ({Render(inner)}) AS [SqlLinqerCount]";
            internal override string Relationship(string sql, string alias, bool collection, bool forJson, bool includeAlias)
            {
                string value = collection ? $"JSON_QUERY(COALESCE(({sql}), '[]'))" : $"JSON_QUERY(({sql}), '$[0]')";
                return value + (includeAlias ? $" AS {alias.DbWrap(Flavor)}" : null);
            }
        }

        private sealed class PostgresDialect : SqlDialect
        {
            internal PostgresDialect() : base(DbFlavor.PostgreSql) { }
            internal override string JsonProjection(string fields, bool distinct, bool nestedCollection)
            {
                string type = distinct ? "jsonb" : "json";
                string value = $"{type}_build_object( {fields} )";
                return nestedCollection ? $"COALESCE({type}_agg( {value} ), '[]'::{type})" : value;
            }
            internal override PreparedSelect JsonCollection(string fields, string source, string order)
                => new PreparedSelect(null, $"COALESCE(json_agg(json_build_object({fields}) {order}), '[]'::json)",
                    source, null, null, null, false, true, true, true, 0, 0, 0);
        }

        private sealed class MySqlDialect : SqlDialect
        {
            internal MySqlDialect() : base(DbFlavor.MySql) { }
            internal override string JsonProjection(string fields, bool distinct, bool nestedCollection)
            {
                string value = $"JSON_OBJECT( {fields} )";
                return nestedCollection ? $"COALESCE(JSON_ARRAYAGG( {value} ), JSON_ARRAY())" : value;
            }
            internal override PreparedSelect JsonCollection(string fields, string source, string order)
            {
                if (string.IsNullOrEmpty(order)) return base.JsonCollection(fields, source, order);
                // Ordinary JSON_ARRAYAGG has undefined element order in MySQL.
                // The full window frame builds the complete ordered collection;
                // the scalar subquery and COALESCE also cover an empty source.
                string select = $"COALESCE((SELECT JSON_ARRAYAGG(JSON_OBJECT({fields})) OVER "
                    + $"({order} ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) {source} LIMIT 1), JSON_ARRAY())";
                return new PreparedSelect(null, select, null, null, null, null, false, true, true, true, 0, 0, 0);
            }
        }

        private sealed class SqliteDialect : SqlDialect
        {
            internal SqliteDialect() : base(DbFlavor.Sqlite, selectedOrder: false) { }
            internal override string JsonProjection(string fields, bool distinct, bool nestedCollection) => $"json_object({fields})";
            protected override string SelectSuffix(PreparedSelect select) => select.ForJson ? " AS \"__sqlite_json\"" : null;
            internal override string Render(PreparedSelect select)
            {
                string sql = base.Render(select);
                return select.ForJson && select.IsSubQuery && select.Collection
                    ? $"SELECT json_group_array(json(\"__sqlite_json\")) FROM ({sql})" : sql;
            }
            internal override string RenderCount(PreparedSelect inner, string with) => $"SELECT count(*) FROM ({Render(inner)})";
            internal override string Relationship(string sql, string alias, bool collection, bool forJson, bool includeAlias)
            {
                string value = $"json(({sql}))";
                return forJson ? $"{alias.DbWrapString(Flavor)}, {value}" : value + (includeAlias ? $" AS {alias.DbWrap(Flavor)}" : null);
            }
        }

        private sealed class DuckDbDialect : SqlDialect
        {
            internal DuckDbDialect() : base(DbFlavor.DuckDb) { }
            internal override string JsonProjection(string fields, bool distinct, bool nestedCollection) => $"json_object({fields}) AS \"__sqlinqer_json\"";
            internal override string Render(PreparedSelect select)
            {
                string sql = base.Render(select);
                return select.ForJson && select.IsSubQuery && select.Collection
                    ? $"SELECT COALESCE(json_group_array(\"__sqlinqer_json\"), '[]'::JSON) FROM ({sql}) AS \"__sqlinqer_rows\"" : sql;
            }
            internal override string RenderCount(PreparedSelect inner, string with) => $"SELECT COUNT(*) FROM ({Render(inner)}) AS \"__sqlinqer_count\"";
        }
    }
}
