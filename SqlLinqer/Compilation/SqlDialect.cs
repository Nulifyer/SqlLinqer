using System;
using System.Collections.Generic;
using System.Linq;
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

        internal virtual string Render(PreparedSelect select)
        {
            return string.Join(" ", new[] {
                select.With, $"SELECT{(select.Distinct ? " DISTINCT" : null)}{Top(select)}",
                select.Select, select.From, select.Where, select.GroupBy, select.OrderBy,
                Paging(select.Top, select.Limit, select.Offset)
            });
        }
        internal virtual string RenderCount(PreparedSelect inner, string with)
            => $"SELECT COUNT(*) FROM ({Render(inner)}) AS {"SqlLinqerCount".DbWrap(Flavor)}";

        internal virtual bool UsesLocalChildren => false;
        internal virtual string NativeCount => "COUNT(*)";
        internal virtual string UnorderedWindow => "";
        internal virtual string PreserveDerivedOrder(PreparedSelect select, string sql) => sql;
        internal virtual string Correlate(string sql, string alias, string barrierAlias)
            => $"CROSS JOIN LATERAL (SELECT * FROM ({sql}) AS {barrierAlias.DbWrap(Flavor)} OFFSET 0) AS {alias.DbWrap(Flavor)}";
        private sealed class SqlServerDialect : SqlDialect
        {
            internal SqlServerDialect(DbFlavor flavor) : base(flavor, true, true, true) { }
            internal override string Paging(int top, int limit, int offset)
                => limit > 0 ? $" OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY" : null;
            protected override string Top(PreparedSelect select) => select.Top > 0 ? $" TOP {select.Top}" : null;
            internal override string RenderCount(PreparedSelect inner, string with)
                => $"{with} SELECT COUNT(*) FROM ({Render(inner)}) AS [SqlLinqerCount]";
            internal override string NativeCount => "COUNT_BIG(*)";
            internal override string UnorderedWindow => "ORDER BY (SELECT NULL)";
            internal override string PreserveDerivedOrder(PreparedSelect select, string sql)
                => select.Top == 0 && select.Limit <= 0 && !string.IsNullOrEmpty(select.OrderBy) ? sql + " OFFSET 0 ROWS" : sql;
            internal override string Correlate(string sql, string alias, string barrierAlias)
                => $"CROSS APPLY (SELECT TOP (9223372036854775807) * FROM ({sql}) AS {barrierAlias.DbWrap(Flavor)}) AS {alias.DbWrap(Flavor)}";
        }

        private sealed class PostgresDialect : SqlDialect
        {
            internal PostgresDialect() : base(DbFlavor.PostgreSql) { }
        }

        private sealed class MySqlDialect : SqlDialect
        {
            internal MySqlDialect() : base(DbFlavor.MySql) { }
            internal override string Correlate(string sql, string alias, string barrierAlias)
                => $"JOIN LATERAL (SELECT * FROM ({sql}) AS {barrierAlias.DbWrap(Flavor)} LIMIT 18446744073709551615) AS {alias.DbWrap(Flavor)} ON TRUE";
        }
        private sealed class SqliteDialect : SqlDialect
        {
            internal SqliteDialect() : base(DbFlavor.Sqlite, selectedOrder: false) { }
            internal override bool UsesLocalChildren => true;
        }

        private sealed class DuckDbDialect : SqlDialect
        {
            internal DuckDbDialect() : base(DbFlavor.DuckDb) { }
        }
    }
}
