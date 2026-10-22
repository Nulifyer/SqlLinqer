[Documentation](README.md) · [Project README](../README.md)

# Choosing SqlLinqer

SqlLinqer fits applications that need explicit SQL queries and nested C# results over an existing relational schema.
Map the tables once, select the related objects and collections a request needs, then execute the query.
The same member mappings support filters, reports, and targeted writes.

## A page with its related models

A search endpoint often needs a filter, a stable page, related data, and the unpaged total:

```csharp
var response = User.BeginSelect(connector)
    .SelectRootColumns()
    .Select(user => user.Manager)
    .Select(user => user.Permissions)
    .Where(user => user.Manager.Name, "Ada Lovelace", SqlOp.EQ)
    .OrderBy(user => user.Name, SqlDir.ASC)
    .ThenBy(user => user.Id, SqlDir.ASC)
    .Page(1, 20)
    .ExecuteWithTotalCount();

if (response.State != ResponseState.Valid)
    throw response.Error;

var users = response.Result;
var totalMatches = response.TotalResults;
```

This example uses the [sample models](getting-started/model-mapping.md) and a [configured connector](getting-started/connections.md).
Import `SqlLinqer` for operators and response states.
`Result` contains users with their selected manager and permissions populated.
The graph loads in one command; the count runs as a separate command.
Filtering through a relationship and selecting that relationship are separate decisions.

Use [optional filters](queries/select.md#add-optional-search-filters) to build the query from application input.
Use an [aggregate projection](queries/select.md#build-an-aggregate-projection) when a report needs summaries instead of complete models.

## How related results arrive

SqlLinqer uses database JSON functions to represent selected nested objects and collections.
It decodes that JSON into the mapped C# members. A missing selected object becomes `null`.
A selected collection with no matching rows becomes an empty collection.
Application JSON attributes and serializer settings do not change the SQL loading contract.

This moves graph construction into the generated SQL and its result mapper.
Database JSON generation and client decoding both cost work.
The number and size of children, indexes, selected columns, and database query plans affect the result.
One command is useful information about the execution strategy, not a performance guarantee.

Buffered execution returns a complete list.
Experimental [async streaming](execution/result-streaming.md) yields complete root models and their selected children.
A large collection inside one root still needs memory, and database sorting and aggregation may delay the first result.

## Adopt it for one query

SqlLinqer does not manage your schema. You choose the ADO.NET provider, configure connections, and issue writes explicitly.
Start with one read endpoint against your existing tables.
Inspect its SQL and bindings with [`Render()`](queries/sql-and-query-reuse.md), then compare the result with your current implementation.

The [generic builders accept ordinary classes](getting-started/model-mapping.md#use-ordinary-classes).
The model base classes provide `BeginSelect`, `BeginInsert`, and other convenience methods.
You can keep other data access code alongside SqlLinqer and pass a caller-owned transaction when operations must share one.
See the [transaction guide](execution/transactions.md) for connection ownership.

## Compare the query styles

[Dapper](https://github.com/DapperLib/Dapper) is a strong choice when you maintain SQL directly and want typed row mapping.
It supports multi-mapping and multiple result sets, so graph loading can also use a single batched command.
SqlLinqer adds model-based query construction and selection of mapped relationships.

[SqlKata](https://sqlkata.com/docs) builds SQL through a fluent API with table and column names.
Its execution package uses Dapper.
SqlLinqer uses mapped C# member expressions and loads the selected relationships into the result models.

[LINQ to DB](https://github.com/linq2db/linq2db) offers LINQ queries, association loading, attribute or fluent mappings, and explicit writes without change tracking.
It also has bulk operations, CTEs, and window functions.
Evaluate it alongside SqlLinqer when you want typed queries with direct control of database operations.
Compare the actual query definitions, generated SQL, and returned graphs for your workload.

[EF Core](https://learn.microsoft.com/en-us/ef/core/) combines LINQ queries with schema migrations, scaffolding, and tracked changes saved through `SaveChanges`.
It also supports queries without tracking and single-query or split-query relationship loading.
Choose based on how you want to query, update, and evolve the application database.
SqlLinqer's workflow uses explicit query builders and writes over a schema you manage.

## Measure the work you need

The [comparison suite](../SqlLinqer.Comparisons/README.md) runs equivalent SQLite workloads with all five libraries.
It includes flat reads, two selected collections, a filtered graph page with its total, and flat streaming.
It validates results and records SQL, command counts, latency, and managed allocations.
EF Core has both single-query and split-query cases. Dapper batches graph reads.

The suite is a local baseline. It does not measure remote server round trips, database server CPU, or network traffic.
Use the [recorded results and methodology](../SqlLinqer.Comparisons/results/README.md) to inspect the evidence, then measure representative queries on your deployment database.
