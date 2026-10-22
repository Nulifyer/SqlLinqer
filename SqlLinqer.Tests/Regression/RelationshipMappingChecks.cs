using SqlLinqer.Components.Modeling;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Regression;

internal static class RelationshipMappingChecks
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (var schema in new[] { "tenant_one", "tenant_two" })
            foreach (DbFlavor flavor in Enum.GetValues<DbFlavor>())
                test($"many-to-many mappings propagate {schema} and retain explicit linking schemas for {flavor}", () =>
                {
                    var table = Table.GetCached<Person>(schema);
                    foreach (string name in new[] { nameof(Person.Peers), nameof(Person.TypedPeers), nameof(Person.DirectedPeers), nameof(Person.FixedPeers) })
                    {
                        var relationship = table.Relationships.TryGet(name);
                        Check(relationship.ParentTable.Schema == schema && relationship.ChildTable.Schema == schema,
                            "Relationship lost its default model schema: " + name);
                        Check(relationship.Joins[0].Child.Schema == (name == nameof(Person.FixedPeers) ? "fixed_links" : schema),
                            "Linking table lost its default or explicit schema: " + name);
                        var query = new SelectQuery<Person>(schema).SelectRootColumns().Select(new[] { name });
                        var rendered = query.Render(flavor);
                        Check(rendered.Text.Contains(relationship.ChildTable.RenderFullName(flavor))
                            && rendered.Text.Contains(relationship.Joins[0].Child.RenderFullName(flavor)), "Rendered SQL lost its mapped table names");
                    }
                });
        test("many-to-many mappings validate missing linking members before rendering SQL", () =>
        {
            Throws<FormatException>(() => Table.GetCached<InvalidLink>().Relationships.GetAll().ToArray());
            Throws<FormatException>(() => Table.GetCached<InvalidBiLink>().Relationships.GetAll().ToArray());
            Throws<IndexOutOfRangeException>(() => Table.GetCached<InvalidCondition>().Relationships.GetAll().ToArray());
        });
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    [SqlTable("relationship_people")]
    public sealed class Person
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public string Name { get; set; }
        [SqlBiDirectionalManyToMany("relationship_links", "from_id", "to_id")] public List<Person> Peers { get; set; }
        [SqlBiDirectionalManyToMany(typeof(Link), nameof(Link.FromId), nameof(Link.ToId))] public List<Person> TypedPeers { get; set; }
        [SqlManyToMany("relationship_links", "from_id", "to_id")] public List<Person> DirectedPeers { get; set; }
        [SqlBiDirectionalManyToMany("fixed_links", "relationship_links", "from_id", "to_id")] public List<Person> FixedPeers { get; set; }
        [SqlBiDirectionalManyToMany("relationship_links", "from_id", "to_id")]
        [SqlConditionChildConstant(nameof(Name), "allowed", SqlOp.EQ, apply_to_idx: 1)]
        public List<Person> FilteredPeers { get; set; }
    }
    [SqlTable("relationship_links")]
    public sealed class Link
    {
        [SqlColumn("from_id")] public int FromId { get; set; }
        [SqlColumn("to_id")] public int ToId { get; set; }
    }
    [SqlTable("relationship_people")]
    public sealed class InvalidLink
    {
        [SqlPrimaryKey] public int Id { get; set; }
        [SqlManyToMany(typeof(Link), "missing", nameof(Link.ToId))] public List<Person> Peers { get; set; }
    }
    [SqlTable("relationship_people")]
    public sealed class InvalidBiLink
    {
        [SqlPrimaryKey] public int Id { get; set; }
        [SqlBiDirectionalManyToMany(typeof(Link), nameof(Link.FromId), "missing")] public List<Person> Peers { get; set; }
    }
    [SqlTable("relationship_people")]
    public sealed class InvalidCondition
    {
        [SqlPrimaryKey] public int Id { get; set; }
        [SqlBiDirectionalManyToMany("relationship_links", "from_id", "to_id")]
        [SqlConditionChildConstant("Name", "allowed", SqlOp.EQ, apply_to_idx: 2)]
        public List<Person> Peers { get; set; }
    }
}
