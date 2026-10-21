using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Select;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.DuckDb;

public enum Kind { First, Second }
[SqlTable("people"),SqlAutoInsert]
public class Person
{
    [SqlPrimaryKey,SqlAutoInsertExclude] public int Id {get;set;}
    public string Name {get;set;}
    public bool Enabled {get;set;}
    public Kind Kind {get;set;}
    public Guid Token {get;set;}
    public DateTime Born {get;set;}
    public decimal Amount {get;set;}
    public byte[] Payload {get;set;}
    public string Note {get;set;}
    public int CategoryId {get;set;}
    [SqlOneToOne(nameof(CategoryId))] public Category Category {get;set;}
    [SqlOneToMany(nameof(Child.PersonId))] public List<Child> Children {get;set;}
}
[SqlTable("categories")]
public class Category { [SqlPrimaryKey] public int Id {get;set;} public string Name {get;set;} }
[SqlTable("children")]
public class Child { [SqlPrimaryKey] public int Id {get;set;} public int PersonId {get;set;} public string Name {get;set;} }
[SqlTable("people")]
public class MemoryPerson { public int Id {get;set;} public string Name {get;set;} }
public class ConfigurableSelect<T> : SelectQuery<T> where T:class
{
    public void Add(SelectStatement statement) => Selects.AddStatement(statement);
    public SqlLinqer.Queries.Core.SelectQuery Relationship(string alias) => Selects.GetAll<SelectRelationship>().Single(x=>x.Alias==alias).SubQuery;
}

public class GroupSummary { public string Tail {get;set;} public long Total {get;set;} }
public class ImportedParameters : ParameterCollection
{
    public ImportedParameters() : base(DbFlavor.DuckDb) { }
    public void Import(ParameterCollectionBase source) => ImportParametersFrom(source);
}
[SqlTable("typed_values"), SqlAutoInsert]
public class TypedValues
{
    public int Id {get;set;}
    public bool? Flag {get;set;}
    public Kind? Kind {get;set;}
    public Guid? Token {get;set;}
    public DateTime? Day {get;set;}
    public DateTimeOffset? Stamp {get;set;}
    public decimal? Value {get;set;}
    public byte[] Payload {get;set;}
}

public class NumericSummary { public double Value {get;set;} }
[SqlTable("envelopes")]
public class Envelope
{
    [SqlPrimaryKey] public int Id {get;set;}
    public int PersonId {get;set;}
    [SqlOneToOne(nameof(PersonId))] public Person Person {get;set;}
}

[SqlTable("byte_values")]
public class ByteValue { public byte Id {get;set;} }

[SqlTable("write_people")]
public class WritePerson
{
    [SqlPrimaryKey] public int Id {get;set;}
    public int CategoryId {get;set;}
    public string Note {get;set;}
    [SqlOneToOne(nameof(CategoryId))] public Category Category {get;set;}
}
[SqlTable("default_keys"),SqlAutoInsert]
public class DefaultKey { [SqlPrimaryKey,SqlAutoInsertExclude] public long Id {get;set;} }
[SqlTable("guid_keys"),SqlAutoInsert]
public class GuidKey { [SqlPrimaryKey,SqlAutoInsertExclude] public Guid Id {get;set;} }
[SqlTable("words")]
public class Word { public int Id {get;set;} public string Name {get;set;} }

public class CustomPredicate : SqlLinqer.Components.Where.WhereColumnValue
{
    public CustomPredicate(Column column):base(column,100,SqlOp.EQ,SqlSubQueryOp.ANY) { }
    public override SqlLinqer.Components.Where.IWhereComponent Finalize(SqlLinqer.Queries.Core.Abstract.WhereQuery query)
        => new SqlLinqer.Components.Where.WhereColumnValue(Column, Array.Empty<int>(), SqlOp.EQ, SqlSubQueryOp.ANY);
}
