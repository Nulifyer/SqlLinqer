[Project README](../README.md) · [NuGet package](https://www.nuget.org/packages/SqlLinqer/)

# Using SqlLinqer

SqlLinqer builds SQL queries from your C# models and returns typed results.
The guides below show how to use the NuGet package in your application.

These guides describe the current repository. For a published package, use the documentation at its matching [release tag](https://github.com/Nulifyer/SqlLinqer/tags).

## Getting started

1. [Install the package and run a complete example](../SqlLinqer/README.md).
2. [Connect to your database](getting-started/connections.md), with an optional default connector or dependency injection.
3. [Map your models](getting-started/model-mapping.md) to tables, keys, columns, and relationships.
4. [Select rows](queries/select.md) and [check the response](execution/responses-and-errors.md).

## Queries

- [Select, filter, sort, and page results](queries/select.md), including related models and aggregate reports.
- [Insert rows](queries/insert.md) and read generated IDs.
- [Update rows](queries/update.md) by key or filter.
- [Delete rows](queries/delete.md) by key or filter.
- [Change relationships](queries/relationship-changes.md) to assign a manager or grant and remove permissions.
- [Call stored procedures](queries/stored-procedures.md) with input and output models, totals, and return codes.
- [Inspect SQL and reuse queries](queries/sql-and-query-reuse.md) without changing an existing query definition.

## Execution and results

- [Execute asynchronously and cancel queries](execution/sync-and-async.md).
- [Use transactions](execution/transactions.md) when several changes must succeed together.
- [Read responses and handle errors](execution/responses-and-errors.md), including totals, affected rows, and generated IDs.
- [Choose result property types](execution/value-conversions.md) for numbers, dates, nulls, and other database values.
- [Process large results with streaming](execution/result-streaming.md), an experimental alternative to loading a complete list.

## Contributing

To change the library, run its tests, or publish a package, see the separate [contributor documentation](contributing/README.md).
