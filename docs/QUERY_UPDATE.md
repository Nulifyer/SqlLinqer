[README](../README.md)

# Update rows

These examples use the [sample models](BUILDING_A_DATAMODEL.md) and a [configured connector](SETUP_A_CONNECTION.md).
Import `SqlLinqer` for operators and response states. See [response handling](RESPONSES.md) for success and error examples.

## Update a user by primary key

Set the ID of the user to update:

```csharp
var currentUser = new User
{
    Id = "<existing-user-id>",
    Email = "newEmail@company.com"
};

var query = User.BeginUpdate(connector)
    .UpdateNonDefaults(currentUser)
    .Where(x => x.Id, currentUser.Id, SqlOp.EQ);
```

The instance method creates the same assignments and primary-key filter:

```csharp
var instanceQuery = currentUser.BeginUpdateNonDefaults(connector);
```

`UpdateNonDefaults` copies non-null values from the object. Use `UpdateAuto` to apply the model's automatic-update settings instead.

## Set a column to null

Use an explicit assignment to clear a nullable column:

```csharp
var clearManagerQuery = User.BeginUpdate(connector)
    .Update(x => x.ManagerId, (string)null)
    .Where(x => x.Id, currentUser.Id, SqlOp.EQ);
```

To include null for `ManagerId` whenever `UpdateNonDefaults` runs, add this attribute to the existing model member:

```csharp
[SqlColumn("manager"), SqlUpdateDefaultAlways]
public string ManagerId { get; set; }
```

## Update rows that match a filter

Add a filter before executing an update. A query without a filter updates every row in the table.

```csharp
var domainQuery = User.BeginUpdate(connector)
    .Update(x => x.Email, "noreply@company.com")
    .Where(x => x.Email, "%@company.com", SqlOp.LIKE);
```

## Apply a numeric operation

Use `BasicMathOperation` to change a numeric column. For example, define a counter model:

```csharp
using SqlLinqer.Modeling;

[SqlTable("UsageCounters")]
public class UsageCounter : SqlLinqerPrimaryKeyObject<UsageCounter, int>
{
    [SqlPrimaryKey]
    public int Id { get; set; }

    public int Count { get; set; }
}
```

```csharp
var counterQuery = UsageCounter.BeginUpdate(connector)
    .Update(x => x.Count, new SqlLinqer.Components.Update.BasicMathOperation(
        SqlLinqer.Components.Update.BasicMathOperation.MathOp.Add, 1))
    .Where(x => x.Id, 1, SqlOp.EQ);
```

## Define a custom operation

Inherit from `UpdateOperation` to define an assignment. This SQL Server example replaces an email domain:

```csharp
using System;
using SqlLinqer;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Update;
using SqlLinqer.Queries.Core;

public class ReplaceEmailDomain : UpdateOperation
{
    private readonly string newDomain;

    public ReplaceEmailDomain(string newDomain)
    {
        this.newDomain = newDomain;
    }

    public override string Render(
        UpdateQuery query,
        UpdateStatement statement,
        ParameterCollection parameters,
        DbFlavor flavor)
    {
        if (flavor != DbFlavor.SqlServer && flavor != DbFlavor.AzureSql)
            throw new NotSupportedException("This operation requires SQL Server or Azure SQL.");

        var target = statement.UpdateColumn.Render(flavor);
        var placeholder = parameters.AddParameter(newDomain);
        return $"{target} = CONCAT(SUBSTRING({target}, 1, CHARINDEX('@', {target}) - 1), {placeholder})";
    }
}
```

Apply it to emails that have the old domain:

```csharp
var replaceDomainQuery = User.BeginUpdate(connector)
    .Update(x => x.Email, new ReplaceEmailDomain("@newcompany.com"))
    .Where(x => x.Email, "%@company.com", SqlOp.LIKE);
```

## Execute an update

```csharp
var response = query.Execute(connector);
```

If `response.State` is `ResponseState.Valid`, `response.Result` contains the affected-row count. Otherwise, inspect `response.Error`.
