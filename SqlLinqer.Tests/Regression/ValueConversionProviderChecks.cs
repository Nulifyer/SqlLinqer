using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Exceptions;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Regression;

// The generated fake-provider matrix checks CLR source/target combinations.
// These fixtures check the actual native values and JSON formats of each DB.
internal static class ValueConversionProviderChecks
{
    private const string Table = "sqllinqer_conversion_values";
    private static readonly DateTime Clock = new DateTime(2024, 2, 29, 13, 14, 15).AddTicks(1234560);
    private static readonly DateTimeOffset Offset = new(Clock, TimeSpan.FromHours(5.5));

    internal static void Run(BaseConnector connector, Action<string, Action> test)
    {
        string Q(string value) => value.DbWrap(connector.DbFlavor);
        string table = string.IsNullOrEmpty(connector.Options.DefaultSchema) ? Q(Table)
            : Q(connector.Options.DefaultSchema) + "." + Q(Table);
        string select = "SELECT " + Q("Value") + " FROM " + table;
        bool sqlServer = connector.DbFlavor is DbFlavor.SqlServer or DbFlavor.AzureSql;
        string timestamp = sqlServer ? "DATETIME2(7)" : connector.DbFlavor == DbFlavor.MySql ? "DATETIME(6)"
            : connector.DbFlavor == DbFlavor.Sqlite ? "TEXT" : "TIMESTAMP(6)";
        string offsetType = sqlServer ? "DATETIMEOFFSET(7)" : connector.DbFlavor is DbFlavor.PostgreSql or DbFlavor.DuckDb
            ? "TIMESTAMPTZ" : "VARCHAR(64)";

        Fixture("wide integer", "BIGINT", "9007199254740993", check =>
        {
            check.Value<long>(9007199254740993L); check.Value<ulong>(9007199254740993UL);
            check.Value<decimal>(9007199254740993m); check.Value<double>(9007199254740992d);
            check.Value<long?>(9007199254740993L); check.Fails<int>(); check.Fails<uint>();
        });
        Fixture("negative integer", "INTEGER", "-1", check =>
        {
            check.Value<sbyte>(-1); check.Value<short>(-1); check.Value<int>(-1); check.Value<long>(-1);
            check.Fails<byte>(); check.Fails<ushort>(); check.Fails<uint>(); check.Fails<ulong>();
        });
        Fixture("unsigned integer text boundary", "VARCHAR(64)", "'18446744073709551615'", check =>
        {
            check.Value<ulong>(ulong.MaxValue); check.Value<ulong?>(ulong.MaxValue);
            check.Fails<long>(); check.Fails<uint>();
        });
        Fixture("fractional numeric", "DECIMAL(18,6)", "123.125", check =>
        {
            check.Value<decimal>(123.125m); check.Value<float>(123.125f); check.Value<double>(123.125d);
            check.Fails<int>(); check.Fails<long>(); check.Fails<uint>();
        });
        Fixture("date", connector.DbFlavor == DbFlavor.Sqlite ? "TEXT" : "DATE", "'2024-02-29'", check =>
        {
            check.Value<DateTime>(Clock.Date); check.Value<DateTime?>(Clock.Date); check.Fails<DateTimeOffset>();
            check.Text(s => s.StartsWith("2024-02-29", StringComparison.Ordinal));
        });
        Fixture("timestamp", timestamp, "'2024-02-29 13:14:15.123456'", check =>
        {
            check.Value<DateTime>(Clock); check.Value<DateTime?>(Clock); check.Fails<DateTimeOffset>();
            check.Text(s => s.StartsWith("2024-02-29", StringComparison.Ordinal) && s.Contains("13:14:15.123456"));
        });
        Fixture("timestamp with offset", offsetType, "'2024-02-29T13:14:15.123456+05:30'", check =>
        {
            if (connector.DbFlavor is DbFlavor.PostgreSql or DbFlavor.DuckDb)
            {
                check.Instant<DateTimeOffset>(Offset); check.Instant<DateTimeOffset?>(Offset);
            }
            else { check.Value<DateTimeOffset>(Offset); check.Value<DateTimeOffset?>(Offset); }
            check.Value<DateTime>(Offset.UtcDateTime);
            check.Text(s => DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
                && value.UtcTicks == Offset.UtcTicks);
        });
        Fixture("timestamp text", "VARCHAR(64)", "'2024-02-29 13:14:15.123456+05:30'", check =>
        {
            check.Value<DateTime>(Offset.UtcDateTime); check.Value<DateTimeOffset>(Offset);
            check.Value<string>("2024-02-29 13:14:15.123456+05:30");
        });
        Fixture("time", connector.DbFlavor == DbFlavor.Sqlite ? "TEXT" : sqlServer ? "TIME(7)"
            : connector.DbFlavor == DbFlavor.DuckDb ? "TIME" : "TIME(6)", "'13:14:15.123456'", check =>
        {
            var time = new TimeSpan(13, 14, 15).Add(TimeSpan.FromTicks(1234560));
            check.Value<TimeSpan>(time); check.Value<TimeSpan?>(time);
            check.Text(s => s.Contains("13:14:15.123456"));
        });
        Fixture("invalid date text", "VARCHAR(64)", "'2023-02-29'", check =>
        { check.Value<string>("2023-02-29"); check.Fails<DateTime>(); check.Fails<DateTimeOffset>(); });
        Fixture("SQL NULL", "BIGINT", "NULL", check =>
        { check.Value<long?>(null); check.Value<DateTime?>(null); check.Value<DateTimeOffset?>(null); check.Value<string>(null); check.Fails<long>(); });

        void Fixture(string label, string type, string literal, Action<ReadCheck> assertions)
        {
            bool created = false;
            try
            {
                Result(connector.ExecuteNonQuery(new RenderedQuery($"CREATE TABLE {table} ({Q("Value")} {type} NULL)")));
                created = true;
                Result(connector.ExecuteNonQuery(new RenderedQuery($"INSERT INTO {table} ({Q("Value")}) VALUES ({literal})")));
                foreach (string mode in new[] { "Execute", "ExecuteAsync", "ExecuteAsyncStream" })
                    test($"native conversion {label}, {mode}", () => InvariantCheck(() => assertions(new(connector, select, mode, false))));
                foreach (string mode in new[] { "Execute", "ExecuteAsync" })
                    foreach (bool scalar in new[] { false, true })
                        test($"native conversion {label}, {(scalar ? "scalar" : "tabular")}, {mode}", () =>
                            InvariantCheck(() => assertions(new(connector, select, mode, true, scalar))));
            }
            finally { if (created) Result(connector.ExecuteNonQuery(new RenderedQuery("DROP TABLE " + table))); }
        }
    }

    private sealed class ReadCheck(BaseConnector connector, string select, string mode, bool native, bool scalar = false)
    {
        internal void Value<T>(T expected)
        {
            T actual = Read<T>();
            bool equal = expected is DateTime date ? actual is DateTime d && d.Ticks == date.Ticks && d.Kind == date.Kind
                : expected is DateTimeOffset offset ? actual is DateTimeOffset o && o.EqualsExact(offset)
                : EqualityComparer<T>.Default.Equals(expected, actual);
            if (!equal) throw new Exception($"{typeof(T)}: expected {expected}, got {actual}");
        }
        internal void Text(Func<string, bool> predicate)
        { if (!predicate(Read<string>())) throw new Exception("Database date text was not preserved"); }
        internal void Instant<T>(DateTimeOffset expected)
        {
            if (Read<T>() is not DateTimeOffset actual || actual.UtcTicks != expected.UtcTicks)
                throw new Exception("Database timestamp instant changed");
        }
        internal void Fails<T>()
        {
            try { Read<T>(); }
            catch (SqlResponseException error) when (MappingError(error)) { return; }
            throw new Exception("Expected mapping failure into " + typeof(T));
        }
        private T Read<T>()
        {
            if (native)
            {
                var query = new RenderedQuery(select);
                if (scalar) return Result(mode == "Execute" ? connector.ExecuteScalar<T>(query)
                    : connector.ExecuteScalarAsync<T>(query).GetAwaiter().GetResult());
                var rows = mode == "Execute" ? connector.ExecuteReaderList<Row<T>>(query)
                    : connector.ExecuteReaderListAsync<Row<T>>(query).GetAwaiter().GetResult();
                return Result(rows).Single().Value;
            }
            var typed = new SelectQuery<Row<T>>(connector.Options.DefaultSchema).SelectRootColumns();
            if (mode == "ExecuteAsyncStream") return ReadStream(typed.ExecuteAsyncStream(connector)).GetAwaiter().GetResult();
            return Result(mode == "Execute" ? typed.Execute(connector) : typed.ExecuteAsync(connector).GetAwaiter().GetResult()).Single().Value;
        }
    }

    private static bool MappingError(Exception error)
    {
        for (; error != null; error = error.InnerException)
            if (error is JsonException or FormatException or OverflowException or InvalidCastException) return true;
        return false;
    }
    private static T Result<T>(SqlResponse<T> response)
    { if (response.State == ResponseState.Error) throw response.Error; return response.Result; }
    private static async Task<T> ReadStream<T>(IAsyncEnumerable<Row<T>> stream)
    {
        var values = new List<T>();
        await foreach (var row in stream) values.Add(row.Value);
        return values.Single();
    }
    private static void InvariantCheck(Action action)
    {
        var previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); action(); }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [SqlTable(Table)]
    private sealed class Row<T> { public T Value { get; set; } }
}
