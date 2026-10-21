using System.Data;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using SqlLinqer.Components.Render;
using SqlLinqer.Exceptions;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Regression;

internal static class ValueConversionRegressionTests
{
    private sealed record Case(string Name, object Source, object Expected, bool Success = true, string Token = null);
    private sealed record IntegerType(Type Type, BigInteger Min, BigInteger Max, Func<string, object> Parse);
    private static readonly IntegerType[] Integers =
    [
        new(typeof(sbyte), sbyte.MinValue, sbyte.MaxValue, s => sbyte.Parse(s, CultureInfo.InvariantCulture)),
        new(typeof(byte), byte.MinValue, byte.MaxValue, s => byte.Parse(s, CultureInfo.InvariantCulture)),
        new(typeof(short), short.MinValue, short.MaxValue, s => short.Parse(s, CultureInfo.InvariantCulture)),
        new(typeof(ushort), ushort.MinValue, ushort.MaxValue, s => ushort.Parse(s, CultureInfo.InvariantCulture)),
        new(typeof(int), int.MinValue, int.MaxValue, s => int.Parse(s, CultureInfo.InvariantCulture)),
        new(typeof(uint), uint.MinValue, uint.MaxValue, s => uint.Parse(s, CultureInfo.InvariantCulture)),
        new(typeof(long), long.MinValue, long.MaxValue, s => long.Parse(s, CultureInfo.InvariantCulture)),
        new(typeof(ulong), ulong.MinValue, ulong.MaxValue, s => ulong.Parse(s, CultureInfo.InvariantCulture)),
    ];
    private static readonly BigInteger[] Boundaries = Integers.SelectMany(t => new[] { t.Min - 1, t.Min, t.Min + 1, t.Max - 1, t.Max, t.Max + 1 })
        .Concat(new BigInteger[] { -1, 0, 1, 42, 9007199254740991L, 9007199254740992L, 9007199254740993L }).Distinct().ToArray();

    internal static void Run(Action<string, Action> test)
    {
        RegisterInteger<sbyte>(test); RegisterInteger<byte>(test);
        RegisterInteger<short>(test); RegisterInteger<ushort>(test);
        RegisterInteger<int>(test); RegisterInteger<uint>(test);
        RegisterInteger<long>(test); RegisterInteger<ulong>(test);
        RegisterNumber<decimal>(test, DecimalJson(), DecimalNative());
        RegisterNumber<float>(test, FloatJson(), FloatNative());
        RegisterNumber<double>(test, DoubleJson(), DoubleNative());
        RegisterValue<DateTime>(test, DateTimeCases(), DateTimeCases().Concat(NativeDateTimes()));
        RegisterValue<DateTimeOffset>(test, OffsetCases(), OffsetCases().Concat(NativeOffsets()));
        RegisterValue<TimeSpan>(test, TimeCases(), TimeCases().Concat(new Case[]
        {
            new("native time", TimeSpan.FromTicks(1234567), TimeSpan.FromTicks(1234567)),
            new("native time-only", new TimeOnly(13, 14, 15), new TimeSpan(13, 14, 15))
        }));
        RegisterValue<Guid>(test, GuidCases(), GuidCases().Append(new("native GUID", Guid.Empty, Guid.Empty)));
        RegisterValue<bool>(test, BooleanCases(), BooleanCases());
        RegisterValue<char>(test, CharacterCases(), CharacterCases());
        RegisterValue<SignedEnum>(test, EnumCases(), EnumCases());
        RegisterValue<UnsignedEnum>(test,
            [new("maximum unsigned enum", null, UnsignedEnum.Max, Token: ulong.MaxValue.ToString(CultureInfo.InvariantCulture)),
             new("unsigned enum underflow", null, null, false, "-1"), new("unsigned enum overflow", null, null, false, "18446744073709551616")],
            [new("maximum unsigned enum", ulong.MaxValue, UnsignedEnum.Max), new("unsigned enum underflow", -1, null, false)]);
        Register<string>(test, StringJson(), StringNative());
        Register<byte[]>(test,
            [new("binary", "AID/", new byte[] { 0, 128, 255 }), new("empty binary", "", Array.Empty<byte>()), new("bad base64", "!?", null, false)],
            [new("binary", new byte[] { 0, 128, 255 }, new byte[] { 0, 128, 255 }), new("empty binary", Array.Empty<byte>(), Array.Empty<byte>()),
             new("implicit base64 parsing rejected", "AID/", null, false)], sqliteHex: true);
        // These framework JSON converters are available on the test runtime. The
        // netstandard2.0 library does not promise native DateOnly/TimeOnly parsing.
        RegisterValue<DateOnly>(test, [new("date-only", "2024-02-29", new DateOnly(2024, 2, 29)), new("invalid date-only", "2023-02-29", null, false)],
            [new("native date-only", new DateOnly(2024, 2, 29), new DateOnly(2024, 2, 29)), new("native date-only parsing unsupported", "2024-02-29", null, false)]);
        RegisterValue<TimeOnly>(test, [new("time-only", "13:14:15.1234567", new TimeOnly(13, 14, 15).Add(TimeSpan.FromTicks(1234567))),
            new("invalid time-only", "25:00:00", null, false)],
            [new("native time-only", new TimeOnly(13, 14), new TimeOnly(13, 14)), new("native time-only parsing unsupported", "13:14:00", null, false)]);
        RegisterDateRoundTrips(test);
        RegisterLateFailures(test);
    }

    private static void RegisterInteger<T>(Action<string, Action> test) where T : struct
    {
        var target = Integers.Single(t => t.Type == typeof(T));
        bool Fits(BigInteger n) => n >= target.Min && n <= target.Max;
        var json = Boundaries.SelectMany(n => new[]
        {
            new Case("number " + n, null, n, Fits(n), n.ToString(CultureInfo.InvariantCulture)),
            new Case("quoted " + n, n.ToString(CultureInfo.InvariantCulture), n, Fits(n))
        }).Concat(new[] { "1.0", "1.5", "-0.5", "1e2", "1e-100", "1e100", "true", "{}", "[]" }
            .Select(s => new Case("non-integer token " + s, null, null, false, s)))
          .Concat(new[] { "", "bad", "1.5", "NaN", "Infinity" }.Select(s => new Case("invalid text " + s, s, null, false))).ToArray();
        var native = Integers.SelectMany(source => Boundaries.Where(n => n >= source.Min && n <= source.Max)
            .Select(n => new Case(source.Type.Name + " " + n, source.Parse(n.ToString(CultureInfo.InvariantCulture)), n, Fits(n))))
            .Concat(Boundaries.Select(n => new Case("BigInteger " + n, n, n, Fits(n))))
            .Concat(Boundaries.Select(n => new Case("text " + n, n.ToString(CultureInfo.InvariantCulture), n, Fits(n))))
            .Concat(new Case[]
            {
                new("whole decimal", 42m, new BigInteger(42), Fits(42)),
                new("whole double", 42d, new BigInteger(42), Fits(42)),
                new("whole float", 42f, new BigInteger(42), Fits(42)),
                new("large whole double", 9223372036854775808d, BigInteger.Parse("9223372036854775808"), Fits(BigInteger.Parse("9223372036854775808"))),
                new("large whole float", 4294967296f, new BigInteger(4294967296L), Fits(4294967296L)),
                new("fractional decimal", 1.5m, null, false), new("fractional double", -0.5d, null, false),
                new("fractional float", 0.5f, null, false), new("double range overflow", double.MaxValue, null, false),
                new("decimal range overflow", decimal.MaxValue, null, false), new("float range overflow", float.MaxValue, null, false),
                new("NaN", double.NaN, null, false), new("infinity", float.PositiveInfinity, null, false),
                new("negative infinity", double.NegativeInfinity, null, false), new("invalid number", "bad", null, false),
            }).Concat(Boundaries.Select(n => new Case("decimal boundary " + n, (decimal)n, n, Fits(n)))).ToArray();
        RegisterNumber<T>(test, json, native);
    }

    private static void RegisterNumber<T>(Action<string, Action> test, IEnumerable<Case> json, IEnumerable<Case> native) where T : struct
        => RegisterValue<T>(test, json, native);

    private static void RegisterValue<T>(Action<string, Action> test, IEnumerable<Case> json, IEnumerable<Case> native) where T : struct
    {
        Register<T>(test, json, native);
        Register<T?>(test, json, native);
    }

    private static void Register<T>(Action<string, Action> test, IEnumerable<Case> json, IEnumerable<Case> native, bool sqliteHex = false)
    {
        bool nullable = !typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null;
        Case[] jsonCases = json.Append(new("null", null, null, nullable)).ToArray();
        foreach (DbFlavor flavor in Enum.GetValues<DbFlavor>())
        {
            Case[] cases = sqliteHex && flavor == DbFlavor.Sqlite ? jsonCases.Select(c =>
                c.Expected is byte[] bytes ? c with { Source = Convert.ToHexString(bytes) } : c).ToArray() : jsonCases;
            foreach (string mode in new[] { "Execute", "ExecuteAsync", "ExecuteAsyncStream" })
                test($"conversion JSON {TypeName<T>()}, {flavor}, {mode}: {cases.Length} cases", () =>
                    UnderCulture(() => { foreach (var c in cases) CheckJson<T>(c, flavor, mode); }));
        }
        if (native == null) return;
        Case[] nativeCases = native.Append(new("SQL NULL", DBNull.Value, null, nullable)).ToArray();
        foreach (bool asynchronous in new[] { false, true })
            foreach (bool scalar in new[] { false, true })
                test($"conversion {(scalar ? "scalar" : "tabular")} {TypeName<T>()}, async={asynchronous}: {nativeCases.Length} cases", () =>
                    UnderCulture(() => { foreach (var c in nativeCases) CheckNative<T>(c, asynchronous, scalar); }));
    }

    private static void CheckJson<T>(Case c, DbFlavor flavor, string mode)
    {
        string row = "{\"Value\":" + (c.Token ?? JsonSerializer.Serialize(c.Source)) + "}";
        var db = new FakeDatabase { JsonRows = flavor is DbFlavor.SqlServer or DbFlavor.AzureSql ? ["[", row, "]"] : [row] };
        var connector = new FakeConnector(db, flavor);
        var query = new SelectQuery<ValueRow<T>>().SelectRootColumns();
        object value = null;
        Exception error = null;
        if (mode == "ExecuteAsyncStream")
        {
            try { value = Single(query.ExecuteAsyncStream(connector)).GetAwaiter().GetResult().Value; }
            catch (SqlResponseException e) { error = e; }
        }
        else
        {
            var response = mode == "Execute" ? query.Execute(connector) : query.ExecuteAsync(connector).GetAwaiter().GetResult();
            if (response.State == ResponseState.Error)
            {
                error = response.Error;
                Check(response.Result == null, c, "Buffered conversion exposed partial results");
            }
            else value = response.Result.Single().Value;
        }
        Verify(c, value, error);
        Check(db.Connections.Single().IsDisposed && db.Executed.Single().IsDisposed, c, "Conversion leaked owned resources");
        if (mode != "Execute") Check(db.DisposedReaders == 1, c, "Async conversion leaked its reader");
    }

    private static void CheckNative<T>(Case c, bool asynchronous, bool scalar)
    {
        var db = new FakeDatabase { ScalarFactory = () => c.Source, ReaderFactory = _ => Table(c.Source).CreateDataReader() };
        var connector = new FakeConnector(db);
        var query = new RenderedQuery("SELECT Value");
        if (scalar)
        {
            var response = asynchronous ? connector.ExecuteScalarAsync<T>(query).GetAwaiter().GetResult() : connector.ExecuteScalar<T>(query);
            Verify(c, response.Result, response.Error);
            Check(response.State == (c.Success ? ResponseState.Valid : ResponseState.Error), c, "Wrong scalar response state");
        }
        else
        {
            var response = asynchronous ? connector.ExecuteReaderListAsync<ValueRow<T>>(query).GetAwaiter().GetResult() : connector.ExecuteReaderList<ValueRow<T>>(query);
            Verify(c, response.State == ResponseState.Valid ? response.Result.Single().Value : null, response.Error);
            if (!c.Success) Check(response.Result == null, c, "Tabular conversion exposed partial results");
        }
        Check(db.Connections.Single().IsDisposed && db.Executed.Single().IsDisposed, c, "Native conversion leaked resources");
    }

    private static DataTable Table(object value)
    {
        var table = new DataTable();
        var column = table.Columns.Add("Value", value == null || value == DBNull.Value ? typeof(string) : value.GetType());
        if (value is DateTime date)
            column.DateTimeMode = date.Kind switch { DateTimeKind.Utc => DataSetDateTime.Utc, DateTimeKind.Local => DataSetDateTime.Local, _ => DataSetDateTime.Unspecified };
        table.Rows.Add(value ?? DBNull.Value);
        return table;
    }

    private static void Verify(Case c, object actual, Exception error)
    {
        Check(c.Success == (error == null), c, c.Success ? "Unexpected conversion error: " + error : "Expected conversion failure, got " + actual);
        if (!c.Success)
        {
            bool mappingError = false;
            for (var cause = error; cause != null; cause = cause.InnerException)
                mappingError |= cause is JsonException or FormatException or OverflowException or InvalidCastException or ArgumentException;
            Check(mappingError, c, "Expected a conversion error, got " + error);
            return;
        }
        bool equal = c.Expected switch
        {
            BigInteger integer => actual != null && BigInteger.Parse(Convert.ToString(actual, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) == integer,
            DateTime date => actual is DateTime d && d.Ticks == date.Ticks && d.Kind == date.Kind,
            DateTimeOffset offset => actual is DateTimeOffset d && d.EqualsExact(offset),
            byte[] bytes => actual is byte[] b && b.SequenceEqual(bytes),
            _ => Equals(actual, c.Expected),
        };
        Check(equal, c, $"Expected {c.Expected} ({c.Expected?.GetType().Name}), got {actual} ({actual?.GetType().Name})");
    }

    private static void Check(bool condition, Case c, string message)
    { if (!condition) throw new Exception(c.Name + ": " + message); }

    private static void UnderCulture(Action action)
    {
        var previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); action(); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static string TypeName<T>() => Nullable.GetUnderlyingType(typeof(T)) is Type t ? t.Name + "?" : typeof(T).Name;

    private static async Task<T> Single<T>(IAsyncEnumerable<T> stream)
    {
        var rows = new List<T>();
        await foreach (var row in stream) rows.Add(row);
        return rows.Single();
    }

    private static IEnumerable<Case> DecimalJson()
    {
        foreach (decimal value in new[] { decimal.MinValue, decimal.MaxValue, -1.25m, 0m, 1.25m, 9007199254740993m, 0.0000000000000000000000000001m })
        {
            yield return new("number " + value, value, value);
            yield return new("quoted " + value, value.ToString(CultureInfo.InvariantCulture), value);
        }
        yield return new("decimal overflow", null, null, false, "79228162514264337593543950336");
        yield return new("invalid decimal", "NaN", null, false);
        yield return new("localized decimal rejected", "1,25", null, false);
        yield return new("grouped decimal rejected", "1,234.5", null, false);
        yield return new("decimal exponent", "1e2", 100m);
        yield return new("decimal underflow", "1e-100", 0m);
    }

    private static IEnumerable<Case> DecimalNative()
        => DecimalJson().Where(c => c.Token == null).Concat(IntegralSources(n => (decimal)n))
            .Concat(new Case[] { new("float to decimal", 1.25f, 1.25m), new("double to decimal", -1.25d, -1.25m),
                new("double overflow", double.MaxValue, null, false), new("NaN", double.NaN, null, false),
                new("BigInteger decimal", BigInteger.Parse("79228162514264337593543950335"), decimal.MaxValue),
                new("BigInteger overflow", BigInteger.Pow(10, 40), null, false) });

    private static IEnumerable<Case> FloatJson()
    {
        foreach (float value in new[] { float.MinValue, float.MaxValue, float.Epsilon, -1.25f, 0f, 1.25f })
        {
            yield return new("number " + value, value, value);
            yield return new("quoted " + value, value.ToString("R", CultureInfo.InvariantCulture), value);
        }
        yield return new("mantissa rounding", 16777217, 16777216f);
        yield return new("underflow", null, 0f, Token: "1e-100");
        foreach (string token in new[] { "1e100", "-1e100", "\"NaN\"", "\"Infinity\"", "\"-Infinity\"", "\"bad\"", "\"1,25\"" })
            yield return new("invalid or overflowing float " + token, null, null, false, token);
    }

    private static IEnumerable<Case> FloatNative()
        => FloatJson().Where(c => c.Token == null).Concat(IntegralSources(n => (float)n)).Concat(new Case[]
        {
            new("decimal to float", 1.25m, 1.25f), new("double to float", -1.25d, -1.25f),
            new("localized float rejected", "1,25", null, false),
            new("narrowing overflow", double.MaxValue, null, false), new("NaN", float.NaN, null, false),
            new("infinity", float.PositiveInfinity, null, false), new("BigInteger float", BigInteger.Pow(2, 80), (float)BigInteger.Pow(2, 80)),
            new("BigInteger float overflow", BigInteger.Pow(10, 100), null, false)
        });

    private static IEnumerable<Case> DoubleJson()
    {
        foreach (double value in new[] { double.MinValue, double.MaxValue, double.Epsilon, -1.25d, 0d, 1.25d })
        {
            yield return new("number " + value, value, value);
            yield return new("quoted " + value, value.ToString("R", CultureInfo.InvariantCulture), value);
        }
        yield return new("mantissa rounding", 9007199254740993L, 9007199254740992d);
        foreach (string token in new[] { "1e1000", "-1e1000", "\"NaN\"", "\"Infinity\"", "\"-Infinity\"", "\"bad\"", "\"1,25\"" })
            yield return new("invalid or overflowing double " + token, null, null, false, token);
    }

    private static IEnumerable<Case> DoubleNative()
        => DoubleJson().Where(c => c.Token == null).Concat(IntegralSources(n => double.Parse(n.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture))).Concat(new Case[]
        {
            new("decimal to double", 1.25m, 1.25d), new("float to double", -1.25f, -1.25d),
            new("localized double rejected", "1,25", null, false),
            new("NaN", double.NaN, null, false), new("infinity", double.NegativeInfinity, null, false),
            new("BigInteger double", BigInteger.Pow(2, 100), (double)BigInteger.Pow(2, 100)),
            new("BigInteger double overflow", BigInteger.Pow(10, 1000), null, false)
        });

    private static IEnumerable<Case> IntegralSources(Func<BigInteger, object> expected)
        => Integers.SelectMany(t => new[] { t.Min, t.Max }.Select(n => new Case(t.Type.Name + " boundary " + n,
            t.Parse(n.ToString(CultureInfo.InvariantCulture)), expected(n))));

    private static readonly DateTime Clock = new DateTime(2024, 2, 29, 13, 14, 15).AddTicks(1234567);
    private static readonly DateTimeOffset Offset = new(Clock, TimeSpan.FromHours(5.5));
    private static IEnumerable<Case> DateTimeCases()
    {
        yield return new("date-only", "2024-02-29", Clock.Date);
        foreach (string separator in new[] { "T", " " })
        {
            yield return new("clock " + separator, "2024-02-29" + separator + "13:14:15.1234567", Clock);
            yield return new("UTC " + separator, "2024-02-29" + separator + "13:14:15.1234567Z", DateTime.SpecifyKind(Clock, DateTimeKind.Utc));
            yield return new("offset " + separator, "2024-02-29" + separator + "13:14:15.1234567+05:30", Offset.UtcDateTime);
            yield return new("negative offset " + separator, "2024-02-29" + separator + "13:14:15.1234567-04:00", Clock.AddHours(4).ToUniversalClock());
        }
        yield return new("minimum", "0001-01-01T00:00:00", DateTime.MinValue);
        yield return new("maximum", "9999-12-31T23:59:59.9999999", DateTime.MaxValue);
        for (int digits = 1; digits <= 7; digits++)
            yield return new("fractional precision " + digits, "2024-02-29T13:14:15." + "1234567"[..digits],
                new DateTime(2024, 2, 29, 13, 14, 15).AddTicks(long.Parse("1234567"[..digits].PadRight(7, '0'), CultureInfo.InvariantCulture)));
        yield return new("numeric epoch rejected", 0L, null, false);
        foreach (string invalid in new[] { "2023-02-29", "2024-02-30", "2024-02-29T25:00:00", "2024-02-29T13:14:15+15:00",
            "29/02/2024", "02/03/2024", "", "tomorrow", "2024-02-29T13:14:15.12345678" })
            yield return new("invalid date " + invalid, invalid, null, false);
    }

    private static IEnumerable<Case> NativeDateTimes()
        => [new("native clock", Clock, Clock), new("native UTC", DateTime.SpecifyKind(Clock, DateTimeKind.Utc), DateTime.SpecifyKind(Clock, DateTimeKind.Utc)),
            new("native date-only to datetime", new DateOnly(2024, 2, 29), Clock.Date),
            new("offset to UTC datetime", Offset, Offset.UtcDateTime), new("numeric epoch not implicit", 0L, null, false)];

    private static IEnumerable<Case> OffsetCases()
    {
        yield return new("explicit offset", "2024-02-29T13:14:15.1234567+05:30", Offset);
        yield return new("SQL offset", "2024-02-29 13:14:15.1234567+05:30", Offset);
        yield return new("UTC offset", "2024-02-29T13:14:15.1234567Z", new DateTimeOffset(Clock, TimeSpan.Zero));
        yield return new("hour-only offset", "2024-02-29 13:14:15-04", new DateTimeOffset(2024, 2, 29, 13, 14, 15, TimeSpan.FromHours(-4)));
        yield return new("maximum offset", "2024-02-29T13:14:15+14:00", new DateTimeOffset(2024, 2, 29, 13, 14, 15, TimeSpan.FromHours(14)));
        yield return new("minimum offset date", "0001-01-01T00:00:00Z", DateTimeOffset.MinValue);
        yield return new("maximum offset date", "9999-12-31T23:59:59.9999999Z", DateTimeOffset.MaxValue);
        yield return new("numeric epoch rejected", 0L, null, false);
        foreach (string invalid in new[] { "2024-02-29", "2024-02-29T13:14:15", "2024-02-29T13:14:15+14:01", "2023-02-29T13:14:15Z", "", "bad" })
            yield return new("ambiguous or invalid offset " + invalid, invalid, null, false);
    }

    private static IEnumerable<Case> NativeOffsets()
        => [new("native offset", Offset, Offset), new("UTC datetime to offset", DateTime.SpecifyKind(Clock, DateTimeKind.Utc), new DateTimeOffset(Clock, TimeSpan.Zero)),
            new("local datetime to offset", DateTime.SpecifyKind(Clock, DateTimeKind.Local), new DateTimeOffset(DateTime.SpecifyKind(Clock, DateTimeKind.Local))),
            new("unspecified datetime rejected", Clock, null, false), new("numeric epoch not implicit", 0L, null, false)];

    private static IEnumerable<Case> TimeCases()
        => [new("time", "13:14:15.1234567", new TimeSpan(13, 14, 15).Add(TimeSpan.FromTicks(1234567))),
            new("negative interval", "-1.02:03:04.1234567", -(new TimeSpan(1, 2, 3, 4).Add(TimeSpan.FromTicks(1234567)))),
            new("invalid time", "25:99:00", null, false)];
    private static IEnumerable<Case> GuidCases()
        => [new("GUID", "59dd2343-40a9-4adb-b99b-0c9f6bdf53ba", new Guid("59dd2343-40a9-4adb-b99b-0c9f6bdf53ba")), new("invalid GUID", "bad", null, false)];
    private static IEnumerable<Case> BooleanCases()
        => [new("true", true, true), new("false", false, false), new("one", 1, true), new("zero", 0, false),
            new("negative nonzero", -2, true), new("boolean text", "TrUe", true), new("numeric text rejected", "1", null, false), new("invalid boolean", "yes", null, false)];
    private static IEnumerable<Case> CharacterCases()
        => [new("unicode char", "雪", '雪'), new("empty char", "", null, false), new("multiple chars", "ab", null, false)];
    private static IEnumerable<Case> EnumCases()
        => [new("enum name", "active", SignedEnum.Active), new("enum number", 1, SignedEnum.Active),
            new("undefined enum value retained", 2, (SignedEnum)2), new("enum overflow", 32768, null, false), new("invalid enum", "bad", null, false)];
    private static IEnumerable<Case> StringJson()
        => [new("date string unchanged", "2024-02-29 13:14:15.1234567+05:30", "2024-02-29 13:14:15.1234567+05:30"),
            new("unicode string", "雪😀", "雪😀"), new("empty string", "", ""), new("number not implicit text", 42, null, false)];
    private static IEnumerable<Case> StringNative()
        => [new("clock to roundtrip string", Clock, "2024-02-29T13:14:15.1234567"),
            new("UTC to roundtrip string", DateTime.SpecifyKind(Clock, DateTimeKind.Utc), "2024-02-29T13:14:15.1234567Z"),
            new("offset to roundtrip string", Offset, "2024-02-29T13:14:15.1234567+05:30"), new("number to invariant text", 1.25m, "1.25"),
            new("text unchanged", "雪😀", "雪😀"), new("interval text", new TimeSpan(1, 2, 3), "01:02:03"),
            new("native date-only text", new DateOnly(2024, 2, 29), "2024-02-29"),
            new("native time-only text", new TimeOnly(13, 14), "13:14:00.0000000")];

    private static void RegisterDateRoundTrips(Action<string, Action> test)
    {
        foreach (bool asynchronous in new[] { false, true })
        {
            test($"conversion native date/string round trips, async={asynchronous}", () => UnderCulture(() =>
            {
                CheckRoundTrip(Clock);
                CheckRoundTrip(DateTime.SpecifyKind(Clock, DateTimeKind.Utc));
                CheckRoundTrip(Offset);

                void CheckRoundTrip<T>(T value)
                {
                    foreach (bool scalar in new[] { false, true })
                    {
                        var source = new FakeConnector(new FakeDatabase
                        { ScalarFactory = () => value, ReaderFactory = _ => Table(value).CreateDataReader() });
                        var query = new RenderedQuery("SELECT Value");
                        string text = scalar ? (asynchronous ? source.ExecuteScalarAsync<string>(query).GetAwaiter().GetResult() : source.ExecuteScalar<string>(query)).Result
                            : (asynchronous ? source.ExecuteReaderListAsync<ValueRow<string>>(query).GetAwaiter().GetResult() : source.ExecuteReaderList<ValueRow<string>>(query)).Result.Single().Value;
                        CheckNative<T>(new("date/string round trip", text, value), asynchronous, scalar);
                    }
                }
            }));
            test($"conversion tabular preserves mixed DateTime kinds, async={asynchronous}", () =>
            {
                DateTime[] values = [Clock, DateTime.SpecifyKind(Clock, DateTimeKind.Utc), DateTime.SpecifyKind(Clock, DateTimeKind.Local)];
                var db = new FakeDatabase
                {
                    ReaderFactory = _ =>
                {
                    var table = new DataTable(); table.Columns.Add("Value", typeof(object));
                    foreach (var value in values) table.Rows.Add(value);
                    return table.CreateDataReader();
                }
                };
                var connector = new FakeConnector(db);
                var query = new RenderedQuery("SELECT Value");
                var response = asynchronous ? connector.ExecuteReaderListAsync<ValueRow<DateTime>>(query).GetAwaiter().GetResult()
                    : connector.ExecuteReaderList<ValueRow<DateTime>>(query);
                if (response.State == ResponseState.Error) throw response.Error;
                for (int i = 0; i < values.Length; i++)
                    if (response.Result[i].Value.Kind != values[i].Kind || response.Result[i].Value.Ticks != values[i].Ticks)
                        throw new Exception("Tabular mapping changed DateTime.Kind or ticks");
            });
        }
    }

    private static void RegisterLateFailures(Action<string, Action> test)
    {
        foreach (DbFlavor flavor in Enum.GetValues<DbFlavor>())
            foreach (string mode in new[] { "Execute", "ExecuteAsync", "ExecuteAsyncStream" })
                test($"conversion error after valid row preserves {mode} failure contract for {flavor}", () =>
                {
                    string[] rows = ["{\"Value\":1}", "{\"Value\":2147483648}"];
                    var db = new FakeDatabase { JsonRows = flavor is DbFlavor.SqlServer or DbFlavor.AzureSql ? ["[" + string.Join(",", rows) + "]"] : rows };
                    var connector = new FakeConnector(db, flavor);
                    var query = new SelectQuery<ValueRow<int>>().SelectRootColumns();
                    if (mode == "ExecuteAsyncStream")
                    {
                        int delivered = 0;
                        async Task Read()
                        { await foreach (var row in query.ExecuteAsyncStream(connector)) { if (row.Value != 1) throw new Exception("Wrong first value"); delivered++; } }
                        try { Read().GetAwaiter().GetResult(); throw new Exception("Expected streaming conversion failure"); }
                        catch (SqlResponseException e) when (e.InnerException is JsonException) { }
                        // The document parser can discover an error in its current
                        // buffer before yielding that buffer's valid prefix.
                        if (flavor is not (DbFlavor.SqlServer or DbFlavor.AzureSql) && delivered != 1)
                            throw new Exception("Row-form stream did not deliver the valid prefix");
                    }
                    else
                    {
                        var response = mode == "Execute" ? query.Execute(connector) : query.ExecuteAsync(connector).GetAwaiter().GetResult();
                        if (response.State != ResponseState.Error || response.Result != null) throw new Exception("Buffered failure exposed partial results");
                    }
                    if (!db.Connections.Single().IsDisposed || !db.Executed.Single().IsDisposed) throw new Exception("Mapping failure leaked resources");
                });
    }

    private static DateTime ToUniversalClock(this DateTime date) => DateTime.SpecifyKind(date, DateTimeKind.Utc);
    private enum SignedEnum : short { Active = 1 }
    private enum UnsignedEnum : ulong { Max = ulong.MaxValue }
    [SqlTable("conversion_values")]
    private sealed class ValueRow<T> { public T Value { get; set; } }
}
