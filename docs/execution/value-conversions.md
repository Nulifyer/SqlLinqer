[Documentation](../README.md) · [Project README](../../README.md)

# Choose result property types

Match your result properties to the values returned by the database.
The rules below apply to selects, raw SQL reads, and stored-procedure rows and outputs.
They explain accepted values and common causes of mapping errors.

## Allow SQL NULL

Use a nullable value type when the database can return SQL NULL:

```csharp
public class UserSummary
{
    public int Id { get; set; }
    public int? ManagerId { get; set; }
    public decimal? Balance { get; set; }
    public string Name { get; set; }
}
```

SQL NULL can populate a nullable value type or a reference type.
It cannot populate a non-nullable value type such as `int` or `DateTime`.
Missing JSON properties keep model construction defaults. Tabular reads require a column for each mapped member.
Use `SqlIgnore` for members that the query does not return.

## Choose a numeric type

| Property type | Accepted values and limits |
| --- | --- |
| Integer types, from `sbyte` through `ulong` | Whole numbers within the target range. Negative values cannot become unsigned integers. |
| `decimal` | Compatible numbers and invariant numeric text. Use it when decimal precision matters. |
| `float`, `double` | Compatible numbers and invariant numeric text. Values can lose precision when converted. |

Out-of-range values, infinity, and NaN produce mapping errors.
Converting a fractional value to an integer also produces an error. SqlLinqer does not round or truncate it.
Decimal and floating-point conversions can round values beyond their precision. Very small values can become zero.

Numeric text must use a period as the decimal separator and cannot contain group separators.
For example, `1234.50` is accepted, while `1,234.50` is rejected.

When a database returns JSON, integer properties accept integer numbers and quoted integer text.
JSON forms such as `1.0` and `1e2` are rejected for integer properties.
For an ordinary column value, a provider-supplied whole decimal such as `1.0m` can become an integer.
Decimal and floating-point properties accept exponent notation.

Choose database column types that preserve the precision you need.
SQLite INTEGER cannot hold unsigned values above `long.MaxValue`, and SQLite REAL can lose decimal precision before SqlLinqer reads it.

## Choose a date or time type

| Property type | Behavior |
| --- | --- |
| `DateTime` | Text without a timezone loads with `Kind = Unspecified`. Text with `Z` or an offset loads as UTC. |
| `DateTimeOffset` | Text must contain an explicit offset or `Z`. No machine-local timezone is assumed. |
| `TimeSpan` | Text uses the constant format, such as `1.02:03:04.5000000`. |

Accepted date text includes `yyyy-MM-dd` and timestamps with seconds, using `T` or a space between the date and time.
Fractional seconds can contain one through seven digits.
Timezone suffixes can be `Z`, `+HH:mm`, `-HH:mm`, `+HH`, or `-HH`.
For example, `2026-10-21T14:30:00.1234567-04:00` contains both precision and an explicit timezone.
Ambiguous localized dates such as `02/03/2024` produce mapping errors.

When an ordinary column supplies a native `DateTime`, a `DateTime` property retains its value and `Kind`.
A native `DateTimeOffset` becomes UTC when assigned to a `DateTime` property.
A UTC `DateTime` becomes a zero-offset `DateTimeOffset`, and a local `DateTime` uses the machine's local offset.
An unspecified `DateTime` cannot become `DateTimeOffset` because it does not identify an instant.

Use `DateTimeOffset` when your application needs to retain an explicit offset.
Use UTC `DateTime` when your application works with instants normalized to UTC.
The database and provider determine the available timestamp precision and timezone information.

JSON `DateOnly` and `TimeOnly` support follows the runtime's converters.
Native string-to-`DateOnly` and string-to-`TimeOnly` conversion is not provided.
A native `DateOnly` can become `DateTime` or invariant date text.
A native `TimeOnly` can become `TimeSpan` or invariant time text.

## Strings and other types

| Property type | Behavior |
| --- | --- |
| `string` | Accepts JSON strings, numbers, and booleans. Objects and arrays cannot become strings. |
| `Guid` | Accepts GUID text. |
| An enum | Accepts names without regard to case, or numbers within its underlying type's range. Undefined numbers are retained. |
| `bool` | JSON accepts booleans, boolean text, and numbers. Zero is false, and nonzero is true. |
| `char` | Requires one UTF-16 code unit. |
| `byte[]` | Loads binary data. JSON uses Base64, except SQLite typed projections, which use hex. |

JSON numbers loaded into strings retain their text, including trailing zeroes and exponent notation.
Booleans become `true` or `false`.
Native dates converted to strings use invariant round-trip `O` format, and native `TimeSpan` values use constant `c` format.
Date strings already returned as JSON text are left unchanged when loaded into string properties.
Binary results remain usable after the query completes.
Provider-specific types and arbitrary custom conversions are not supported automatically.

SQL mappings control database loading. API JSON attributes and converters do not change these rules.
See [model mapping](../getting-started/model-mapping.md#share-a-class-between-the-database-and-api) for using the same model in SQL and API responses.

## Handle mapping errors

`Execute()` and `ExecuteAsync()` return `ResponseState.Error` when a value cannot populate its target property.
The response does not contain a partial result list. Check `Error` before reading `Result`.

[Streaming](result-streaming.md) throws `SqlResponseException` during enumeration.
Rows already delivered to your application remain processed.
