using System.Reflection;
using SqlLinqer.Components.Ordering;
using SqlLinqer.Components.Select;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Sqlite;

[SqlTable("items"), SqlAutoInsert, SqlAutoSelect]
public class Item
{
    [SqlPrimaryKey, SqlAutoInsertExclude] public int Id { get; set; }
    public string Name { get; set; }
    public string OtherName { get; set; }
    public bool Enabled { get; set; }
    public State Status { get; set; }
    public int? CategoryId { get; set; }
    public Guid Token { get; set; }
    public DateTime? Created { get; set; }
    public DateTimeOffset? OffsetDate { get; set; }
    public decimal Amount { get; set; }
    public byte[] Bytes { get; set; }
    [SqlOneToOne(nameof(CategoryId)), SqlAutoSelect] public Category Category { get; set; }
    [SqlOneToMany(nameof(Child.ItemId)), SqlAutoSelect] public List<Child> Children { get; set; }
}
public enum State { None, Active }
[SqlTable("categories"), SqlAutoSelect]
public class Category
{
    [SqlPrimaryKey] public int Id { get; set; }
    public string Name { get; set; }
}
[SqlTable("children"), SqlAutoSelect]
public class Child
{
    [SqlPrimaryKey] public int Id { get; set; }
    public int ItemId { get; set; }
    public string Name { get; set; }
    public int? CategoryId { get; set; }
    [SqlOneToOne(nameof(CategoryId)), SqlAutoSelect] public Category Category { get; set; }
}

[SqlTable("odd\"table")]
public class QuotedRow
{
    [SqlPrimaryKey] public int Id { get; set; }
    [SqlColumn("va\"lue")] public string Value { get; set; }
}
[SqlTable("guid_rows")]
public class GuidRow
{
    [SqlPrimaryKey(false)] public Guid Id { get; set; }
    public string Name { get; set; }
}
[SqlTable("defaults")]
public class DefaultRow
{
    [SqlPrimaryKey] public int Id { get; set; }
}
[SqlTable("items")]
public class NoKeyRow
{
    public string Name { get; set; }
    public int? CategoryId { get; set; }
    [SqlOneToOne(nameof(CategoryId))] public Category Category { get; set; }
}
public sealed class NestedPageQuery : SelectQuery<Item>
{
    public NestedPageQuery()
    {
        Select(x => x.Id); Select(x => x.Children);
        var relationship = Selects.GetAll<SelectRelationship>().Single();
        var table = relationship.Relationship.ChildTable;
        // Public components expose nested paging to derived query builders.
        var queryType = typeof(SqlLinqer.Queries.Core.SelectQuery);
        var paging = (PagingControls)queryType.GetField("PagingControls", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(relationship.SubQuery);
        paging.SetLimit(1, 1);
        var ordering = (OrderByCollection)queryType.GetField("OrderBys", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(relationship.SubQuery);
        ordering.AddStatement(new ColumnOrderBy(table.Columns.TryGet(nameof(Child.Name)), SqlDir.ASC));
    }
}

[SqlTable("words")]
public class WordRow
{
    [SqlPrimaryKey(false)] public int Id { get; set; }
    public string Name { get; set; }
}

[SqlTable("generated_rows")]
public class GeneratedRow
{
    [SqlPrimaryKey] public int Id { get; set; }
    [SqlAutoInsertExclude] public string Name { get; set; }
    [SqlAutoInsertExclude] public int Number { get; set; }
}
