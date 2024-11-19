# SQL Linqer

### What is it used for?

This library is used to create application using a type of SQL database without the need to write queries as strings and maintain a strong set of data models that represent the data. This makes it much easier to develope complex database queries without the need of the developer to perform complex operations and it easier to update the code as all the developer needs to do is update the one data model and all queries will be updated.

### Why not just use Microsoft's Enity Framework (EF)?

This is more of a personal preference. They way this library behaves is difference than EF and might not have all of the functionality that EF does. EF can sometimes be cumbersome to handle as it forces a certain developer style on the stucture of the data and I find this style of use to be easier. This includes modification, extending the functionality, and allowing for more complex queries.

## How to build a data model:

[Setup a connection](docs/SETUP_A_CONNECTION.md)

## How to build a data model:

[Building a data model](docs/BUILDING_A_DATAMODEL.md)

## How to query:

[Create/Insert Queries](docs/QUERY_CREATE.md)

[Read/Select Queries](docs/QUERY_READ.md)

[Update Queries](docs/QUERY_UPDATE.md)

[Delete Queries](docs/QUERY_DELETE.md)

## Quick Example

```C#
var connector = new ReplicatorConnector(new SqlConnection("<conn string>"), DbFlavor.SqlServer);

SqlResponse<List<User>> response = User
    .BeginSelect()
    .Top(10)
    .SelectRootColumns()
    .Select(x => x.Permissions)
    .Where(x => x.Permissions.First().name, "% admin", SqlOp.NOTLIKE)
    .OrderBy(x => x.Manger.Name, SqlDir.ASC)
    .OrderBy(x => x.Name, SqlDir.ASC)
    .Execute(connector);

if (response.State == ResponseState.Valid)
{
    List<User> users = response.Results;
}
else
{
    System.Diagnostics.Debug.WriteLine(response.Error?.Message);
    System.Diagnostics.Debug.WriteLine((response.Error as SqlResponseException).Query.GetTextWithParameterValues());
}
```