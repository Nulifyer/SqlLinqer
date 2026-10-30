[Documentation](../README.md) · [Project README](../../README.md)

# Inspect SQL and reuse queries

Use `Render` to inspect the SQL for a query before executing it.
Use `Capture` when you need to keep a query definition while continuing to change its builder.
These examples use the [sample models](../getting-started/model-mapping.md) and a [configured connector](../getting-started/connections.md).

## Inspect generated SQL

```csharp
var query = User.BeginSelect(connector)
    .SelectRootColumns()
    .Where(x => x.Name, "Ada", SqlOp.EQ)
    .OrderBy(x => x.Id, SqlDir.ASC)
    .Page(2, 20);

var rendered = query.Render(connector);
Console.WriteLine(rendered.Text);
```

`Render` returns SQL and its parameters without opening a database connection.
Pass `connector` to use the same database dialect as execution.

For local debugging, `rendered.GetTextWithParameterValues()` shows values in the SQL text.
This text can contain sensitive application data. Use the parameterized `RenderedQuery` when executing commands.

## Keep a query definition

A query builder changes when you add filters, selections, or limits.
Call `Capture()` to keep its current definition:

```csharp
var snapshot = query.Capture();
query.Top(1);

var page = snapshot.Render(connector);
var total = snapshot.RenderForCount(connector.DbFlavor);
```

The captured page still uses the original filter and page size.
`RenderForCount` counts matches before root paging and ordering. For grouped or distinct projections, it counts the projected results.
Rendering a count does not change the captured page.

To execute the captured select:

```csharp
var response = await connector.ExecuteReaderModelAsync<User>(
    snapshot.Render(connector),
    cancellationToken: cancellationToken);
if (response.State != ResponseState.Valid)
    throw response.Error;

var users = response.Result;
```

For an ordinary paged query with a total, use [ExecuteWithTotalCount](select.md#execute-a-typed-select).
You do not need to capture or render it yourself.

## Share a captured query

Finish building the query before calling `Capture`.
A captured query can be rendered repeatedly or shared by callers that need the same definition.
Do not change a builder while another caller is capturing or executing it.
Do not share a mutable external parameter collection between concurrent renders.

Capture copies standard parameter values such as arrays, binary buffers, and `DataTable` values.
Keep custom provider objects and extension-owned reference values unchanged for the captured query's lifetime.
Large input collections cost memory when captured. Capture once if you intend to reuse the definition.

## Inspect selected fields

`RenderedQuery.ResultShape` describes the fields selected by a regular select query.
Use it when application code needs field names, CLR types, or nested object and collection information:

```csharp
var rendered = User.BeginSelect(connector)
    .SelectRootColumns()
    .Select(x => x.Permissions)
    .Render(connector);

foreach (var member in rendered.ResultShape.Members)
    Console.WriteLine($"{member.Name}: {member.Kind}");
```

A missing one-to-one relationship loads as `null`. A collection with no matches loads as an empty collection.
Commands other than regular selects, including count-only commands, can have a null `ResultShape`.

Keep the `RenderedQuery` together and pass it to the model reader shown above. Rebuilding a command from `Text` alone loses the information needed to load nested results.
