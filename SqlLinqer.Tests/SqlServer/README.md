# SQL Server and Azure SQL Database

`DbFlavor.AzureSql` has numeric value `5` and uses SQL Server rendering. `SqlServer=0`, `MySql=1` and `PostgreSql=2` retain their values. The connector keeps `AzureSql` as its flavor. SQL syntax, parameters, JSON results, output rows, TVPs and transaction defaults follow SQL Server behavior.

The core library remains `netstandard2.0` and has no SqlClient dependency. The shared `SqlLinqer.Tests` `net10.0` executable references `Microsoft.Data.SqlClient` and `Microsoft.Data.SqlClient.Extensions.Azure`, both `7.1.1`. The Azure extension supplies the driver's Microsoft Entra authentication modes in version 7 and later. `System.Security.Cryptography.Pkcs` is pinned to `10.0.13` because the driver's transitive `9.0.18` produced NU1902 and NU1903 audit warnings. See the [SqlClient 7.1.1 release](https://github.com/dotnet/SqlClient/releases/tag/v7.1.1) and [namespace and package documentation](https://learn.microsoft.com/en-us/sql/connect/ado-net/introduction-microsoft-data-sqlclient-namespace?view=sql-server-ver17).

## Run the checks

Run from the repository root:

```bash
dotnet run --project SqlLinqer.Tests -- --suite regression
dotnet run --project SqlLinqer.Tests -- --suite sql-server
dotnet build SQLTest/SQLTest.csproj
dotnet build SQLTestFramework/SQLTestFramework.csproj
```

The regression suite checks SQL Server/Azure rendering, SQL shape, parameter metadata, transaction ownership, resource disposal and chunked JSON. The shared server suite always runs offline checks against real SqlClient connections and parameters plus Microsoft's ScriptDom parser in Azure mode. These checks never open a connection or acquire a token. ScriptDom validates syntax only. It does not bind database objects or execute queries.

Optional Azure execution uses `--suite sql-server --azure` with `--live` or `--require-live` and reads `SQLLINQER_AZURE_SQL_CONNECTION_STRING`. Normal SQL Server execution reads `SQLLINQER_TEST_SQLSERVER`. Inject a connection string for an existing, disposable test database through your shell or secret manager. No endpoint is discovered or provisioned. Both modes require an explicit database other than `master` and encryption. SQL Server mode permits a local self-signed certificate and checks SQL Server engine identity. Azure mode validates the certificate and requires `SERVERPROPERTY('EngineEdition') = 5`.

The shared behavior can be exercised locally through SQL Server Developer without requiring an Azure endpoint:

```bash
bash TestDBs/test-containers.sh --engine docker --sudo
```

Omit `--sudo` when your account can access Docker directly.
This runs every suite, including the shared fixtures with `DbFlavor.SqlServer`. There is one container runner and one test executable.
The regression and offline Azure checks continue to cover the Azure flavor's rendering and provider metadata.
Direct live Azure testing is optional. When you choose to run it, require execution explicitly:

```bash
dotnet run --project SqlLinqer.Tests -- --suite sql-server --azure --require-live
```

Without a live flag, the suite runs only local checks. With `--live`, an absent endpoint prints `SKIP live Azure SQL`; with `--require-live`, it also reports a failure and exits `1`. Any connection, query, assertion or cleanup failure exits `1`. An unknown flag exits `2`. Default developer execution may exit `0` after an explicit live skip. A skipped suite is never counted as a live pass. Error output names the check and exception type, with SqlException number/state/class when available. It does not print connection strings, token values, SQL text, identity details or provider messages.

The full live suite runs temporary-table tests and then creates one schema named `SqlLinqerAzure_<32 random hex digits>` in the explicitly selected database. It creates only its own `Rows` table, `IdList` type, `ReadRows`, `ReadJson`, and `TypedCallable` procedures and generated `TEMP_<id>` TVP types in that schema. No application tables, users, roles or cloud resources are changed. Cleanup runs in `finally`, drops only objects in that schema, and verifies that the schema was removed. Temporary tables are dropped in a separate `finally` and also expire with their connection. Cleanup failures are failures. Process termination or loss of connectivity can prevent cleanup; remove only the recorded `SqlLinqerAzure_...` schema's objects after inspecting them.

The test identity needs permission to connect and use temporary tables. Full mode also needs database `CREATE SCHEMA`, `CREATE TABLE`, `CREATE TYPE` and `CREATE PROCEDURE`, ownership or `ALTER` on its new schema, plus permission to execute its procedures and reference its types. Use an identity dedicated to the disposable database. The suite does not grant permissions. See [CREATE SCHEMA](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-schema-transact-sql?view=sql-server-ver17) and [table-valued parameters](https://learn.microsoft.com/en-us/sql/connect/ado-net/sql/table-valued-parameters?view=sql-server-ver17).

For an identity restricted to temporary tables:

```bash
dotnet run --project SqlLinqer.Tests -- --suite sql-server --azure --require-live --temp-only
```

`--temp-only` explicitly skips procedures, TVPs and the cross-connection tests for owned transactions. Use the full command for integration sign-off. Both modes validate CRUD, returned keys, typed JSON, Unicode/null values, one-to-one null/object results, one-to-many empty arrays, deeper nesting and ordering, paging/Top, grouped and Distinct totals, ungrouped aggregates, direct tabular rendering, CTEs, joined source updates and deletes with outputs, type round trips, truncate and caller rollback after success/failure. Full mode adds owned commit/rollback after a failed batch, TVP insert/output/key/null/decimal behavior, caller/owned TVP failures and type cleanup, procedure DataTable/JSON results, procedure TVPs including empty input, and owned truncate.

Direct Azure SQL execution and managed identity sign-in remain unverified in this environment.
SQL Server container results cover shared database behavior; they do not establish cloud authentication or service-specific behavior.

## Connect through SqlClient

```csharp
using Microsoft.Data.SqlClient;
using SqlLinqer;
using SqlLinqer.Connections;

using var template = new SqlConnection(connectionString);
var connector = new ReplicatorConnector(template, DbFlavor.AzureSql,
    new ConnectorOptions
    {
        ConnectionTimeout = TimeSpan.FromSeconds(30),
        CommandTimeout = TimeSpan.FromSeconds(30)
    });
```

`ReplicatorConnector` copies the provider type and connection string, then replaces connection timeout aliases with `Connect Timeout`. It preserves connection-string authentication and encryption settings. Set `ConnectorOptions.ConnectionTimeout` to preserve your intended login timeout. Command timeout is separate. Reads use no owned transaction. Writes use `ReadCommitted` when the connector owns the transaction. When you supply a transaction, the connector uses its connection and leaves commit, rollback and disposal to you.

Use these templates with your existing endpoint and database. Keep credentials out of source and logs. Each example requires encryption and certificate validation. The host must trust the certificate chain, and the endpoint name must match the certificate. Live Azure mode rejects `Encrypt=false` or `TrustServerCertificate=true`. See [encryption and certificate validation](https://learn.microsoft.com/en-us/sql/connect/ado-net/encryption-and-certificate-validation?view=sql-server-ver17).

SQL authentication:

```text
Server=tcp:<server>.database.windows.net,1433;Database=<database>;Authentication=Sql Password;User ID=<sql-login>;Password=<password>;Encrypt=true;TrustServerCertificate=false;Connect Timeout=30;
```

This needs an existing SQL login or contained database user with the required permissions and SQL authentication enabled for the endpoint. Build strings with `SqlConnectionStringBuilder` when a password contains delimiters. The Azure extension is not required for SQL password authentication.

Microsoft Entra local development:

```text
Server=tcp:<server>.database.windows.net,1433;Database=<database>;Authentication=Active Directory Default;Encrypt=true;TrustServerCertificate=false;Connect Timeout=30;
```

Install both matching `7.1.1` packages. Configure Microsoft Entra authentication on the existing database and grant the intended user access. Sign in through a supported development credential, for example Azure CLI with `az login`, using the intended tenant and identity. `Active Directory Default` uses the Azure Identity credential chain and may select another configured credential. It does not prompt through an interactive browser. To request a user sign-in instead, use `Authentication=Active Directory Interactive` with no password on a host that supports that interaction. The test executable only invokes such authentication after a live flag and the explicit connection-string variable are supplied.

Azure-hosted managed identity:

```text
Server=tcp:<server>.database.windows.net,1433;Database=<database>;Authentication=Active Directory Managed Identity;Encrypt=true;TrustServerCertificate=false;Connect Timeout=30;
```

The application host must expose an enabled managed identity, and that identity must have database access. For a user-assigned managed identity, add `User ID=<managed-identity-client-id>`. Omit `User ID` for a system-assigned identity. Do not supply a password. This mode does not give a developer workstation a managed identity. These authentication prerequisites follow [Microsoft's Entra authentication documentation](https://learn.microsoft.com/en-us/sql/connect/ado-net/sql/azure-active-directory-authentication?view=sql-server-ver17). The [Azure SQL .NET quickstart](https://learn.microsoft.com/en-us/azure/azure-sql/database/azure-sql-dotnet-quickstart?view=azuresql) explains endpoint access and the initial connection workflow. This suite never provisions those resources.

## Authentication supplied through connection properties

`ReplicatorConnector` does not preserve `AccessToken`, `AccessTokenCallback`, `SqlCredential` or other provider properties. Do not pass it a template that relies on those properties. Local provider tests explicitly cover this limitation.

Use `ConnectionFactoryConnector` to attach application-managed authentication to every new connection:

```csharp
using Microsoft.Data.SqlClient;
using SqlLinqer;
using SqlLinqer.Connections;

// Use your existing callback delegate. This declaration shows the expected type.
Func<SqlAuthenticationParameters, CancellationToken,
    Task<SqlAuthenticationToken>> callback = existingCallback;
var connector = new ConnectionFactoryConnector(
    () => new SqlConnection(connectionString) { AccessTokenCallback = callback },
    DbFlavor.AzureSql,
    new ConnectorOptions { CommandTimeout = TimeSpan.FromSeconds(30) });
```

The factory must return a fresh connection on every call. The connector opens and disposes connections it requests. Caller transactions use their own connection and bypass the factory. Configure login timeout in the factory's connection string. `ConnectorOptions.ConnectionTimeout` does not rewrite factory connections. `CommandTimeout` still applies. Local provider tests check callback delegate identity, separate new connections, `AccessToken` and `SqlCredential`; regression tests check disposal and caller ownership. The core has no reference to SqlClient or Azure Identity.

Reuse the same callback delegate and credential for connections that should share a pool. Use an encrypted, certificate-validated connection string compatible with callback authentication. Do not combine it with `Authentication`, integrated security, `AccessToken` or `SspiContextProvider`. Callback token acquisition, expiry and renewal belong to your application and SqlClient. The connector does not add token management. See [AccessTokenCallback guidance](https://learn.microsoft.com/en-us/sql/connect/ado-net/sql/azure-active-directory-authentication?view=sql-server-ver17#use-accesstokencallback).

SqlLinqer does not add automatic write retries. A connection failure can leave a write's outcome unknown; repeating it can duplicate writes. Choose any retry policy in the application with that operation's idempotency requirements in mind.

## Native behavior and limits

Azure SQL uses SQL Server's `EXEC [schema].[procedure] @name = @P1` syntax. Named parameters retain `DbType`, typed nulls and TVP type names. The existing `StoredProcedureQuery` expects a JSON array for `ExecuteJson`, or rows for `ExecuteDataTable`. [Typed callable models](../../docs/CALLABLES.md) add ordinary typed rows, multiple result sets, output parameters, and return-code binding. Local checks verify their SQL syntax and native parameter metadata. The optional schema suite also tests typed callable execution, defaults, explicit NULL, and caller transactions. Tabular queries that select relationships return JSON-valued columns for those relationships.

`ExecuteTvp`, `ExecuteTvpWithOutput` and returned-key TVP execution create a random database table type, insert through a Structured parameter, then drop the type on the same connection and transaction. Owned transactions commit all three operations or roll them back on failure. A usable caller transaction gets a best-effort type drop after an insert failure; if SQL Server marks it uncommittable, the caller must roll it back. The library never commits, rolls back or disposes caller transactions. These are real database types, not session-local types, so type-creation and schema permissions are required for every call. For an existing table type, use `StoredProcedureQuery.AddTvpParameter` instead.

For ordinary parameters, the existing `DateTime` default remains `DbType.DateTime`. Annotate a model timestamp with `[SqlColumn(nameof(Stamp), DbType.DateTime2)]` to preserve its full `datetime2(7)` precision, as the live type-round-trip model does. `DateTimeOffset` parameters retain their offset.

Generated TVP declarations use `DECIMAL(38,18)`, `DATETIME2(7)`, Unicode `NCHAR(1)`, `REAL` for `Single`, and SQL Server types for the remaining supported CLR values. Decimal values needing more than 18 fractional or 20 integral digits need a different table-type declaration; override `RenderTempType` for that shape or pass an existing type to a procedure. User-defined provider types, CLR enums and unsigned numeric types do not have complete automatic TVP mappings. Use supported CLR model types or an explicit DataTable/type with a procedure. SQL Server's ordinary parameter/VALUES limits still apply to non-TVP inserts; SqlLinqer does not automatically batch them.

SQL Server/Azure count rendering retains Distinct and aggregate projections in a derived table, removes paging/order, and places any CTE before the outer count. This counts groups or aggregate result rows rather than input rows. Other database dialects retain their existing count behavior on this branch.
