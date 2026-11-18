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
                Paging(select.Top, select.Limit, select.Offset), select.Lock
            });
        }

        // A row lock renders in one of two places. PostgreSQL and MySQL end the statement with a locking clause.
        // SQL Server and Azure SQL put table hints on the root table. A dialect without row locks rejects the clause.
        internal virtual string RowLockClause(SqlLinqer.Components.Select.RowLock rowLock, string rootAlias, bool joined)
            => throw new NotSupportedException($"{Flavor} does not support row locks on a SELECT.");
        internal virtual string RowLockHint(SqlLinqer.Components.Select.RowLock rowLock) => null;

        private static string LockingRead(SqlLinqer.Components.Select.RowLock rowLock, string rootAlias, bool joined)
            => string.Join(" ", new[] { rowLock.Shared ? "FOR SHARE" : "FOR UPDATE", joined ? "OF " + rootAlias : null,
                SqlLinqer.Extensions.EnumExtensions.EnumExtensions.GetDescription(rowLock.Wait) }.Where(part => !string.IsNullOrEmpty(part)));

        // The database server's current time, as a write assigns it and a filter compares it.
        // The column selects the text form on a database that stores times as text.
        internal abstract string ServerTime(SqlLinqer.Components.Modeling.Column column = null);

        // A query that returns the current UTC time as a timestamp without a zone.
        internal virtual string ServerTimeRead => "SELECT " + ServerTime();

        // The single place a value becomes SQL: a server value renders as an expression, and any other value binds a parameter.
        internal string Value(object value, SqlLinqer.Components.Modeling.Column column, SqlLinqer.Components.Render.ParameterCollection parameters,
            System.Data.DbType? dbType = null)
            => value is SqlTime ? ServerTime(column) : parameters.AddParameter(value, dbType);

        // True when the value renders as an expression and binds no parameter.
        internal static bool IsServerValue(object value) => value is SqlTime;

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
            internal override string ServerTime(SqlLinqer.Components.Modeling.Column column = null) => "SYSUTCDATETIME()";
            internal override string UnorderedWindow => "ORDER BY (SELECT NULL)";
            internal override string PreserveDerivedOrder(PreparedSelect select, string sql)
                => select.Top == 0 && select.Limit <= 0 && !string.IsNullOrEmpty(select.OrderBy) ? sql + " OFFSET 0 ROWS" : sql;
            internal override string RowLockClause(SqlLinqer.Components.Select.RowLock rowLock, string rootAlias, bool joined) => null;
            // REPEATABLEREAD holds shared locks until the transaction ends, without the key-range locks of HOLDLOCK.
            internal override string RowLockHint(SqlLinqer.Components.Select.RowLock rowLock)
                => $"WITH ({(rowLock.Shared ? "REPEATABLEREAD" : "UPDLOCK")}, ROWLOCK"
                    + (rowLock.Wait == SqlLockWait.SKIPLOCKED ? ", READPAST" : rowLock.Wait == SqlLockWait.NOWAIT ? ", NOWAIT" : null) + ")";
            internal override string Correlate(string sql, string alias, string barrierAlias)
                => $"CROSS APPLY (SELECT TOP (9223372036854775807) * FROM ({sql}) AS {barrierAlias.DbWrap(Flavor)}) AS {alias.DbWrap(Flavor)}";
        }

        private sealed class PostgresDialect : SqlDialect
        {
            internal PostgresDialect() : base(DbFlavor.PostgreSql) { }
            internal override string ServerTime(SqlLinqer.Components.Modeling.Column column = null) => "CURRENT_TIMESTAMP";
            internal override string ServerTimeRead => "SELECT CURRENT_TIMESTAMP AT TIME ZONE 'UTC'";
            internal override string RowLockClause(SqlLinqer.Components.Select.RowLock rowLock, string rootAlias, bool joined) => LockingRead(rowLock, rootAlias, joined);
        }

        private sealed class MySqlDialect : SqlDialect
        {
            internal MySqlDialect() : base(DbFlavor.MySql) { }
            internal override string ServerTime(SqlLinqer.Components.Modeling.Column column = null) => "UTC_TIMESTAMP(6)";
            internal override string RowLockClause(SqlLinqer.Components.Select.RowLock rowLock, string rootAlias, bool joined) => LockingRead(rowLock, rootAlias, joined);
            internal override string Correlate(string sql, string alias, string barrierAlias)
                => $"JOIN LATERAL (SELECT * FROM ({sql}) AS {barrierAlias.DbWrap(Flavor)} LIMIT 18446744073709551615) AS {alias.DbWrap(Flavor)} ON TRUE";
        }
        private sealed class SqliteDialect : SqlDialect
        {
            internal SqliteDialect() : base(DbFlavor.Sqlite, selectedOrder: false) { }
            internal override bool UsesLocalChildren => true;
            // SQLite stores times as text and compares them as text. Match the text Microsoft.Data.Sqlite writes
            // for a bound value, so a server-assigned time sorts and compares with application times.
            internal override string ServerTime(SqlLinqer.Components.Modeling.Column column = null)
            {
                var type = (column as SqlLinqer.Components.Modeling.ReflectedColumn)?.ValueType;
                bool offset = (Nullable.GetUnderlyingType(type ?? typeof(object)) ?? type) == typeof(DateTimeOffset);
                return offset ? "strftime('%Y-%m-%d %H:%M:%f+00:00', 'now')" : "strftime('%Y-%m-%d %H:%M:%f', 'now')";
            }
        }

        private sealed class DuckDbDialect : SqlDialect
        {
            internal DuckDbDialect() : base(DbFlavor.DuckDb) { }
            // DuckDB resolves a bare CURRENT_TIMESTAMP in a conflict assignment as a column name.
            internal override string ServerTime(SqlLinqer.Components.Modeling.Column column = null) => "now()";
            // DuckDB reports microseconds since the Unix epoch.
            internal override string ServerTimeRead => "SELECT epoch_us(now())";
        }
    }
}
