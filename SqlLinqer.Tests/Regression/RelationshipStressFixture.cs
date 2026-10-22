using System.Data.Common;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.StringExtensions;

namespace SqlLinqer.Tests.Regression;

// Raw rows and ADO.NET setup are independent of SqlLinqer's writes and mapping.
// Missing foreign keys are deliberate, alongside SQL NULL and empty collections.
internal sealed class RelationshipStressFixture : IDisposable
{
    private readonly BaseConnector _connector;
    private readonly List<string> _created = new();
    internal string Schema => _connector.Options.DefaultSchema;
    internal readonly List<RootRow> Roots = new();
    internal readonly Dictionary<int, PersonRow> People = new();
    internal readonly List<ProjectRow> Projects = new();
    internal readonly List<TicketRow> Tickets = new();
    internal readonly Dictionary<int, PlaceRow> Regions = new();
    internal readonly Dictionary<int, PlaceRow> Categories = new();
    internal readonly List<(int RootId, int PersonId)> Links = new();

    internal RelationshipStressFixture(BaseConnector connector, int seed)
    {
        _connector = connector;
        Generate(seed);
        try { Create(); }
        catch (Exception setupError)
        {
            try { Dispose(); }
            catch (Exception cleanupError) { throw new AggregateException(setupError, cleanupError); }
            throw;
        }
    }

    private void Generate(int seed)
    {
        var random = new Random(seed);
        foreach (var row in new[] { new PlaceRow(1, "north", null), new(2, "west", 1), new(3, "east", 1),
            new(4, "remote", null), new(5, "north", 2), new(6, "missing parent", 999) })
            Regions.Add(row.Id, row);
        foreach (var row in new[] { new PlaceRow(1, "general", null), new(2, "urgent", 1), new(3, "routine", 1),
            new(4, "subtask", 2), new(5, "missing parent", 999) })
            Categories.Add(row.Id, row);
        for (int id = 1; id <= 40; id++)
            People.Add(id, new(id, id % 5 == 0 ? "same name" : $"person {id} O'Reilly 雪",
                id % 4 == 1 ? null : 1 + 4 * ((id - 1) / 4), Optional(id, 6)));
        for (int id = 1; id <= 32; id++)
        {
            int? owner = id % 6 == 0 ? null : id % 7 == 0 ? 999 : 1 + 4 * ((id - 1) % 10);
            if (owner is > 0 and < 999 && id % 3 == 0) owner++;
            int? backup = id % 5 == 0 ? null : id % 8 == 0 ? 999 : 2 + 4 * ((id - 1) % 10);
            if (id == 4) backup = owner;
            Roots.Add(new(id, $"root group {id % 4}", owner, backup));
            int projects = id == 1 ? 9 : id == 2 ? 0 : id == 3 ? 1 : random.Next(5);
            for (int index = 1; index <= projects; index++)
            {
                int projectId = id * 100 + index;
                Projects.Add(new(projectId, id, Optional(random.Next(1, 100), 40), random.Next(3)));
                int tickets = projectId == 101 ? 12 : random.Next(5);
                for (int ticket = 1; ticket <= tickets; ticket++)
                    Tickets.Add(new(projectId * 100 + ticket, projectId, Optional(random.Next(1, 100), 40),
                        Optional(random.Next(1, 100), 5), random.Next(4)));
            }
            if (id != 2)
                foreach (int person in Enumerable.Range(0, 1 + id % 4).Select(_ => random.Next(1, 41)).Distinct())
                    Links.Add((id, person));
        }
        // Insert in reverse key order so provider/default insertion order cannot
        // accidentally satisfy every requested nested ordering.
        Projects.Reverse();
        Tickets.Reverse();
        Links.Reverse();
    }

    private static int? Optional(int value, int maximum)
        => value % 7 == 0 ? null : value % 11 == 0 ? 999 : 1 + value % maximum;

    internal string Q(string name) => name.DbWrap(_connector.DbFlavor);
    internal string T(string name) => string.IsNullOrEmpty(Schema) ? Q(name) : Q(Schema) + "." + Q(name);

    private void Create()
    {
        using var connection = _connector.CreateConnection();
        connection.Open();
        string text = _connector.DbFlavor is DbFlavor.SqlServer or DbFlavor.AzureSql ? "NVARCHAR(80)" : "VARCHAR(80)";
        void Table(string name, string columns)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE TABLE {T(name)} ({columns})";
            command.ExecuteNonQuery();
            _created.Add(name);
        }
        Table("stress_rel_roots", $"{Q("Id")} INTEGER PRIMARY KEY, {Q("Name")} {text}, {Q("OwnerId")} INTEGER, {Q("BackupId")} INTEGER");
        Table("stress_rel_people", $"{Q("Id")} INTEGER PRIMARY KEY, {Q("Name")} {text}, {Q("ManagerId")} INTEGER, {Q("RegionId")} INTEGER");
        Table("stress_rel_projects", $"{Q("Id")} INTEGER PRIMARY KEY, {Q("RootId")} INTEGER, {Q("OwnerId")} INTEGER, {Q("Rank")} INTEGER");
        Table("stress_rel_tickets", $"{Q("Id")} INTEGER PRIMARY KEY, {Q("ProjectId")} INTEGER, {Q("AssigneeId")} INTEGER, {Q("CategoryId")} INTEGER, {Q("Rank")} INTEGER");
        foreach (string name in new[] { "stress_rel_regions", "stress_rel_categories" })
            Table(name, $"{Q("Id")} INTEGER PRIMARY KEY, {Q("Name")} {text}, {Q("ParentId")} INTEGER");
        Table("stress_rel_links", $"{Q("RootId")} INTEGER, {Q("PersonId")} INTEGER");

        using var transaction = connection.BeginTransaction();
        void Insert(string name, string[] columns, IEnumerable<object[]> source)
        {
            foreach (var batch in source.Chunk(100))
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                var rows = new List<string>();
                foreach (var row in batch)
                {
                    var placeholders = new List<string>();
                    foreach (var value in row)
                    {
                        string id = "p" + command.Parameters.Count;
                        string placeholder = (_connector.DbFlavor == DbFlavor.DuckDb ? "$" : "@") + id;
                        var parameter = command.CreateParameter();
                        parameter.ParameterName = _connector.DbFlavor == DbFlavor.DuckDb ? id : placeholder;
                        parameter.Value = value ?? DBNull.Value;
                        command.Parameters.Add(parameter);
                        placeholders.Add(placeholder);
                    }
                    rows.Add("(" + string.Join(",", placeholders) + ")");
                }
                command.CommandText = $"INSERT INTO {T(name)} ({string.Join(",", columns.Select(Q))}) VALUES {string.Join(",", rows)}";
                command.ExecuteNonQuery();
            }
        }
        Insert("stress_rel_roots", new[] { "Id", "Name", "OwnerId", "BackupId" }, Roots.Select(x => new object[] { x.Id, x.Name, x.OwnerId, x.BackupId }));
        Insert("stress_rel_people", new[] { "Id", "Name", "ManagerId", "RegionId" }, People.Values.Select(x => new object[] { x.Id, x.Name, x.ManagerId, x.RegionId }));
        Insert("stress_rel_projects", new[] { "Id", "RootId", "OwnerId", "Rank" }, Projects.Select(x => new object[] { x.Id, x.RootId, x.OwnerId, x.Rank }));
        Insert("stress_rel_tickets", new[] { "Id", "ProjectId", "AssigneeId", "CategoryId", "Rank" }, Tickets.Select(x => new object[] { x.Id, x.ProjectId, x.AssigneeId, x.CategoryId, x.Rank }));
        Insert("stress_rel_regions", new[] { "Id", "Name", "ParentId" }, Regions.Values.Select(x => new object[] { x.Id, x.Name, x.ParentId }));
        Insert("stress_rel_categories", new[] { "Id", "Name", "ParentId" }, Categories.Values.Select(x => new object[] { x.Id, x.Name, x.ParentId }));
        Insert("stress_rel_links", new[] { "RootId", "PersonId" }, Links.Select(x => new object[] { x.RootId, x.PersonId }));
        transaction.Commit();
    }

    internal StressRoot Root(RootRow row) => new() { Id = row.Id, Name = row.Name, OwnerId = row.OwnerId, BackupId = row.BackupId };
    internal StressPerson Person(int? id) => id.HasValue && People.TryGetValue(id.Value, out var row)
        ? new() { Id = row.Id, Name = row.Name, ManagerId = row.ManagerId, RegionId = row.RegionId } : null;
    internal StressRegion Region(int? id, bool parent = false)
    {
        if (!id.HasValue || !Regions.TryGetValue(id.Value, out var row)) return null;
        return new() { Id = row.Id, Name = row.Name, ParentId = row.ParentId, Parent = parent ? Region(row.ParentId) : null };
    }
    internal StressCategory Category(int? id, bool parent = false)
    {
        if (!id.HasValue || !Categories.TryGetValue(id.Value, out var row)) return null;
        return new() { Id = row.Id, Name = row.Name, ParentId = row.ParentId, Parent = parent ? Category(row.ParentId) : null };
    }
    internal StressProject Project(ProjectRow row) => new() { Id = row.Id, RootId = row.RootId, OwnerId = row.OwnerId, Rank = row.Rank };
    internal StressTicket Ticket(TicketRow row) => new() { Id = row.Id, ProjectId = row.ProjectId, AssigneeId = row.AssigneeId, CategoryId = row.CategoryId, Rank = row.Rank };

    internal StressRoot Graph(RootRow row, bool ordered)
    {
        StressPerson WithRegion(int? id)
        {
            var person = Person(id);
            if (person != null) person.Region = Region(person.RegionId);
            return person;
        }
        StressTicket WithCategory(TicketRow ticket)
        {
            var result = Ticket(ticket);
            result.Category = Category(ticket.CategoryId, true);
            return result;
        }
        var root = Root(row);
        root.Owner = Person(row.OwnerId);
        if (root.Owner != null)
        {
            root.Owner.Manager = WithRegion(root.Owner.ManagerId);
            if (root.Owner.Manager?.Region != null) root.Owner.Manager.Region.Parent = Region(root.Owner.Manager.Region.ParentId);
            root.Owner.Reports = People.Values.Where(x => x.ManagerId == root.Owner.Id).OrderByDescending(x => x.Id).Select(report =>
            {
                var person = WithRegion(report.Id);
                person.Tickets = OrderTickets(Tickets.Where(x => x.AssigneeId == report.Id), ordered).Select(WithCategory).ToList();
                return person;
            }).ToList();
        }
        root.Backup = WithRegion(row.BackupId);
        root.Projects = OrderProjects(Projects.Where(x => x.RootId == row.Id), ordered).Select(project =>
        {
            var result = Project(project);
            result.Owner = WithRegion(project.OwnerId);
            result.Tickets = OrderTickets(Tickets.Where(x => x.ProjectId == project.Id), ordered).Select(ticket =>
            {
                var value = WithCategory(ticket);
                value.Assignee = Person(ticket.AssigneeId);
                if (value.Assignee != null) value.Assignee.Manager = Person(value.Assignee.ManagerId);
                return value;
            }).ToList();
            return result;
        }).ToList();
        root.Members = Links.Where(x => x.RootId == row.Id).OrderByDescending(x => x.PersonId).Select(link =>
        {
            var person = WithRegion(link.PersonId);
            person.Manager = Person(person.ManagerId);
            person.Reports = People.Values.Where(x => x.ManagerId == link.PersonId).OrderByDescending(x => x.Id)
                .Select(report => WithRegion(report.Id)).ToList();
            return person;
        }).ToList();
        return root;
    }

    internal static IEnumerable<ProjectRow> OrderProjects(IEnumerable<ProjectRow> rows, bool ordered)
        => ordered ? rows.OrderByDescending(x => x.Rank).ThenBy(x => x.Id) : rows.OrderBy(x => x.Id);
    internal static IEnumerable<TicketRow> OrderTickets(IEnumerable<TicketRow> rows, bool ordered)
        => ordered ? rows.OrderBy(x => x.Rank).ThenByDescending(x => x.Id) : rows.OrderBy(x => x.Id);
    internal bool HasCategoryParent(RootRow root, int parent)
        => Projects.Where(x => x.RootId == root.Id).Any(project => Tickets.Where(x => x.ProjectId == project.Id)
            .Any(ticket => ticket.CategoryId.HasValue && Categories.TryGetValue(ticket.CategoryId.Value, out var category)
                && category.ParentId == parent && Categories.ContainsKey(parent)));

    internal List<int> ReferenceFilteredIds(int parent)
    {
        using var connection = _connector.CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        string C(string alias, string column) => alias + "." + Q(column);
        command.CommandText = $"SELECT {C("r", "Id")} FROM {T("stress_rel_roots")} r WHERE EXISTS ("
            + $"SELECT 1 FROM {T("stress_rel_projects")} p JOIN {T("stress_rel_tickets")} t ON {C("t", "ProjectId")} = {C("p", "Id")} "
            + $"JOIN {T("stress_rel_categories")} c ON {C("c", "Id")} = {C("t", "CategoryId")} "
            + $"JOIN {T("stress_rel_categories")} parent ON {C("parent", "Id")} = {C("c", "ParentId")} "
            + $"WHERE {C("p", "RootId")} = {C("r", "Id")} AND {C("parent", "Id")} = {parent}) ORDER BY {C("r", "Id")}";
        using var reader = command.ExecuteReader();
        var values = new List<int>();
        while (reader.Read()) values.Add(Convert.ToInt32(reader.GetValue(0)));
        return values;
    }

    public void Dispose()
    {
        if (_created.Count == 0) return;
        using var connection = _connector.CreateConnection();
        connection.Open();
        var errors = new List<Exception>();
        foreach (string name in _created.AsEnumerable().Reverse())
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"DROP TABLE {T(name)}";
                command.ExecuteNonQuery();
            }
            catch (Exception error) { errors.Add(error); }
        }
        _created.Clear();
        if (errors.Count != 0) throw new AggregateException("Relationship stress fixture cleanup failed.", errors);
    }

    internal sealed record RootRow(int Id, string Name, int? OwnerId, int? BackupId);
    internal sealed record PersonRow(int Id, string Name, int? ManagerId, int? RegionId);
    internal sealed record ProjectRow(int Id, int RootId, int? OwnerId, int Rank);
    internal sealed record TicketRow(int Id, int ProjectId, int? AssigneeId, int? CategoryId, int Rank);
    internal sealed record PlaceRow(int Id, string Name, int? ParentId);
}
