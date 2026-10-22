[Execution](EXECUTION.md)

# Result value conversions

Typed JSON results use the same conversion rules in `Execute()`, `ExecuteAsync()`, and `ExecuteAsyncStream()`.
Tabular model lists, scalar results, callable rows, and callable outputs share the native value converter.
JSON token types and native provider types can differ, so their accepted inputs are not identical.

## Numbers

All eight integer targets use checked range conversion: `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, and `ulong`.
Negative numbers cannot become unsigned integers. Overflow produces a mapping error.
Native decimal and floating-point values must have no fractional part before conversion to an integer.
The converter rejects fractional values instead of rounding or truncating them.

JSON integer targets accept integer number tokens and quoted integer text.
Fractional and exponent notation, including `1.0` and `1e2`, are rejected for JSON integer targets.
Native whole numbers such as `1.0m` can become integers because the provider has already supplied a numeric value.

`decimal`, `float`, and `double` targets accept compatible numbers and invariant numeric text, including exponent notation.
Group separators and localized decimal separators are rejected.
Numeric targets reject range overflow, infinity, and NaN.
Floating-point and decimal conversion can round values that exceed their precision. Very small values can underflow to zero.
Use an integer or decimal target when exact numeric values are required.
Integer JSON tokens retain their integer precision without an intermediate `double` conversion.

Database storage limits apply before mapping. SQLite INTEGER cannot retain unsigned values above `long.MaxValue`.
SQLite REAL and numeric expressions can lose decimal precision. A converter cannot recover precision lost by the database.

## Dates and times

Text parsing uses the invariant culture. It accepts `yyyy-MM-dd` and ISO or SQL timestamps with seconds.
Timestamp separators can be `T` or a space. Fractional seconds can contain one through seven digits.
An explicit timezone uses `Z`, `+HH:mm`, `-HH:mm`, `+HH`, or `-HH`.
Localized and ambiguous dates such as `02/03/2024` are rejected.
Invalid calendar dates, times, offsets, and unsupported formats produce mapping errors.

A date or timestamp without a timezone becomes `DateTimeKind.Unspecified` when read as `DateTime`.
A timestamp with an explicit timezone becomes a UTC `DateTime` representing the same instant.
A `DateTimeOffset` target requires an explicit offset or `Z` in text.
The converter does not assign the machine's timezone to timezone-free text.

Native `DateTime` values retain their ticks and `Kind` when the target is `DateTime`.
A native `DateTimeOffset` becomes its UTC instant when the target is `DateTime`.
A native UTC `DateTime` becomes a zero-offset `DateTimeOffset`.
A native local `DateTime` uses the machine's local offset when converted to `DateTimeOffset`.
A native unspecified `DateTime` cannot become `DateTimeOffset` because it does not identify an instant.
Tabular list mapping reads provider values directly, without a temporary `DataTable` that can remove `DateTime.Kind`.

Native date values converted to strings use the invariant round-trip `O` format.
Native `TimeSpan` values converted to strings use the invariant constant `c` format.
`TimeSpan` parsing accepts the constant format, including fractional seconds and day components.
JSON date values are already strings. A string target receives the database's text without reformatting.
String targets also accept JSON numbers and booleans, preserving the earlier SQL loader behavior.
Numbers retain their JSON token text, including precision, trailing zeroes, and exponent notation. Booleans become `true` or `false`.
JSON objects and arrays cannot load into string targets.
Date/string round trips retain timestamp precision and any explicit offset.
Reading offset-bearing text as `DateTime` normalizes it to UTC, so its wall-clock ticks can change.

These rules tighten earlier behavior. Offset-bearing text now produces UTC `DateTime` values instead of machine-local values.
Timezone-free text no longer becomes a `DateTimeOffset` through an implicit local offset.
Callers that need that interpretation must supply the timezone explicitly before conversion.

Provider storage still controls the available precision and timezone information.
SQL Server `datetime2(7)` supports seven fractional digits. The shared PostgreSQL, MySQL, and DuckDB timestamp fixture uses six.
PostgreSQL and DuckDB timezone-aware timestamps retain the instant. Their JSON text can use the database session's timezone.
The DuckDB native adapter restores UTC `DateTime.Kind` from `TIMESTAMPTZ` metadata before mapping.
SQLite and MySQL offset-text fixtures exercise explicit offset parsing rather than native offset storage.

## Other types and nulls

SQL NULL and JSON null map to nullable value types and reference types.
Null cannot become a non-nullable value type.
Missing JSON properties follow model construction defaults. Missing required tabular columns produce mapping errors.

GUID text is parsed as a GUID. Enum names are case-insensitive, and numeric enum values use checked underlying-type conversion.
Undefined enum numbers are retained when they fit the underlying type.
Boolean JSON accepts booleans, boolean text, and numeric values, where zero is false and nonzero is true.
A character requires one UTF-16 code unit. Binary JSON uses Base64, except SQLite typed projections, which use hex.
Native binary streams are copied to independent byte arrays before the reader closes.

On a runtime with framework support, JSON `DateOnly` and `TimeOnly` use their framework converters.
The library targets .NET Standard 2.0 and does not add string-to-`DateOnly` or string-to-`TimeOnly` conversion for native reads.
Native `DateOnly` values can become `DateTime` or invariant date text. Native `TimeOnly` values can become `TimeSpan` or invariant time text.
Provider-specific types and arbitrary custom conversion pairs are outside this contract.

## Failure behavior and verification

Buffered reads return `ResponseState.Error` without a partial result list after a mapping failure.
Streaming reads throw `SqlResponseException` during enumeration. Previously delivered rows remain delivered.
The incremental document parser can discover an error before yielding valid rows in its current input buffer.
Disposing enumeration releases the reader, command, and owned connection after a conversion error.

The regression matrix checks numeric source/target combinations, range boundaries, fractional values, invalid input, nulls, culture, and dates.
It exercises typed JSON through all three execution modes and tabular/scalar reads through synchronous and asynchronous execution.
Real-provider fixtures check native values and generated JSON against SQLite, DuckDB, SQL Server, PostgreSQL, and MySQL.
The SQLite suite runs both its bundled native library and the supported system-library variant.
The fixtures cover representative native database types. They do not enumerate every provider-specific SQL type or every possible value.

Run the matrix with:

```bash
dotnet run --project SqlLinqer.Tests -- --suite regression
bash TestDBs/test-containers.sh
```
