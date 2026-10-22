using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Select;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Regression;

internal static class RelationshipStressChecks
{
    private static readonly string[] Paths =
    {
        "Owner", "Owner.Manager", "Owner.Manager.Region", "Owner.Manager.Region.Parent",
        "Owner.Reports", "Owner.Reports.Region", "Owner.Reports.Tickets",
        "Owner.Reports.Tickets.Category", "Owner.Reports.Tickets.Category.Parent",
        "Backup", "Backup.Region", "Projects", "Projects.Owner", "Projects.Owner.Region",
        "Projects.Tickets", "Projects.Tickets.Assignee", "Projects.Tickets.Assignee.Manager",
        "Projects.Tickets.Category", "Projects.Tickets.Category.Parent",
        "Members", "Members.Manager", "Members.Region", "Members.Reports", "Members.Reports.Region"
    };

    private static SelectQuery<StressRoot> Graph(string schema, bool ordered)
    {
        var query = new SelectQuery<StressRoot>(schema).SelectRootColumns();
        foreach (string path in Paths) query.Select(path.Split('.'));
        if (ordered) query.OrderByAuto(true);
        return query.OrderBy(x => x.Id, SqlDir.ASC);
    }

    internal static void Run(BaseConnector connector, Action<string, Action> test)
    {
        foreach (int seed in new[] { 17, 20261022 })
        {
            using var data = new RelationshipStressFixture(connector, seed);
            var recording = new RecordingConnector(connector);
            foreach (string mode in new[] { "sync", "async", "stream" })
            {
                void Case(string name, Action action) => test($"relationship stress: {name}, seed {seed}, {mode}", action);
                foreach (bool ordered in new[] { false, true })
                {
                    Case(ordered ? "ordered complete graph" : "complete graph in one command", () =>
                    {
                        recording.Commands.Clear();
                        var rows = ComplexRelationshipChecks.Read(Graph(data.Schema, ordered), recording, mode);
                        Equal(data.Roots.Select(x => data.Graph(x, ordered)).ToList(), rows, ordered);
                        Check(recording.Commands.Count == 1, "Loading the graph must execute exactly one command");
                    });
                }
                Case("same source projected twice with independent shapes", () =>
                {
                    var query = new SelectQueryInitializer<StressRoot>(data.Schema).BuildQuery(q => new
                    {
                        Id = q.Select(x => x.Id),
                        Main = q.Select(x => x.Owner, x => x.Manager),
                        Other = q.Select(x => x.Owner, x => x.Region)
                    }).OrderBy(x => x.Id, SqlDir.ASC);
                    var expected = data.Roots.Select(root =>
                    {
                        var main = data.Person(root.OwnerId);
                        if (main != null) main.Manager = data.Person(main.ManagerId);
                        var other = data.Person(root.OwnerId);
                        if (other != null) other.Region = data.Region(other.RegionId);
                        return new { root.Id, Main = main, Other = other };
                    }).ToList();
                    Equal(expected, ComplexRelationshipChecks.Read(query, recording, mode));
                });
                foreach (bool negate in new[] { false, true })
                {
                    Case(negate ? "deep anti-existence filter" : "deep existence filter preserves selected children", () =>
                    {
                        var expected = data.Roots.Where(x => data.HasCategoryParent(x, 1)).ToList();
                        Check(data.ReferenceFilteredIds(1).SequenceEqual(expected.Select(x => x.Id)), "Native EXISTS disagrees with fixture oracle");
                        if (negate) expected = data.Roots.Except(expected).ToList();
                        var query = Graph(data.Schema, false)
                            .Where(x => x.Projects.First().Tickets.First().Category.Parent.Id, 1, negate ? SqlOp.NOT : SqlOp.EQ);
                        Equal(expected.Select(x => data.Graph(x, false)).ToList(), ComplexRelationshipChecks.Read(query, recording, mode));
                        query.Page(1, 3);
                        var page = mode == "sync" ? query.ExecuteWithTotalCount(recording)
                            : query.ExecuteWithTotalCountAsync(recording).GetAwaiter().GetResult();
                        if (page.State != ResponseState.Valid) throw page.Error;
                        Check(page.TotalResults == expected.Count, "Deep filter changed the root total count");
                        Equal(expected.Take(3).Select(x => data.Graph(x, false)).ToList(), page.Result);
                    });
                }
                Case("OR across a nullable root key and a deep collection", () =>
                {
                    var query = Graph(data.Schema, false);
                    var alternatives = query.NewWhere(SqlWhereOp.OR)
                        .Where(x => x.OwnerId, SqlNull.NULL, SqlOp.EQ)
                        .Where(x => x.Projects.First().Tickets.First().Category.Parent.Id, 1, SqlOp.EQ);
                    query.Where(alternatives);
                    var expected = data.Roots.Where(x => x.OwnerId == null || data.HasCategoryParent(x, 1));
                    Equal(expected.Select(x => data.Graph(x, false)).ToList(), ComplexRelationshipChecks.Read(query, recording, mode));
                });
                foreach (bool distinct in new[] { false, true })
                {
                    Case(distinct ? "nested DISTINCT pages" : "nested pages at two collection levels", () =>
                    {
                        var query = Graph(data.Schema, true);
                        var projects = query.Selects.GetAll<SelectRelationship>().Single(x => x.Alias == nameof(StressRoot.Projects)).SubQuery;
                        projects.PagingControls.SetPage(2, 3);
                        projects.Distinct = distinct;
                        var tickets = projects.Selects.GetAll<SelectRelationship>().Single(x => x.Alias == nameof(StressProject.Tickets)).SubQuery;
                        tickets.PagingControls.SetPage(2, 2);
                        tickets.Distinct = distinct;
                        var expected = data.Roots.Select(root =>
                        {
                            var graph = data.Graph(root, true);
                            graph.Projects = graph.Projects.Skip(3).Take(3).ToList();
                            foreach (var project in graph.Projects) project.Tickets = project.Tickets.Skip(2).Take(2).ToList();
                            return graph;
                        }).ToList();
                        Equal(expected, ComplexRelationshipChecks.Read(query, recording, mode), true);
                    });
                }
                Case("root paging counts roots rather than nested fan-out", () =>
                {
                    var query = Graph(data.Schema, true).Page(2, 7);
                    recording.Commands.Clear();
                    // Streaming exercises the page separately; total count uses the
                    // buffered async contract, since streams do not expose totals.
                    var response = mode == "sync" ? query.ExecuteWithTotalCount(recording)
                        : query.ExecuteWithTotalCountAsync(recording).GetAwaiter().GetResult();
                    if (response.State != ResponseState.Valid) throw response.Error;
                    Check(response.TotalResults == data.Roots.Count, "Total count multiplied roots by nested relationships");
                    Equal(data.Roots.Skip(7).Take(7).Select(x => data.Graph(x, true)).ToList(), response.Result, true);
                    Check(recording.Commands.Count == 2, "Paged graph and total must use exactly two commands");
                    if (mode == "stream") Equal(response.Result, ComplexRelationshipChecks.Read(query, recording, mode), true);
                    var empty = Graph(data.Schema, false).Page(100, 7).ExecuteWithTotalCount(recording);
                    if (empty.State != ResponseState.Valid) throw empty.Error;
                    Check(empty.Result.Count == 0 && empty.TotalResults == data.Roots.Count, "Empty page lost total count");
                });
                Case("explicit finite cycle traverses back to the correct root", () =>
                {
                    var query = new SelectQuery<StressRoot>(data.Schema).SelectRootColumns()
                        .Select(x => x.Projects).Select(x => x.Projects.First().Tickets)
                        .Select(x => x.Projects.First().Tickets.First().Project)
                        .Select(x => x.Projects.First().Tickets.First().Project.Root)
                        .Select(x => x.Projects.First().Tickets.First().Project.Root.Owner)
                        .OrderBy(x => x.Id, SqlDir.ASC);
                    var expected = data.Roots.Select(root =>
                    {
                        var result = data.Root(root);
                        result.Projects = data.Projects.Where(x => x.RootId == root.Id).Select(project =>
                        {
                            var value = data.Project(project);
                            value.Tickets = data.Tickets.Where(x => x.ProjectId == project.Id).Select(ticket =>
                            {
                                var leaf = data.Ticket(ticket);
                                leaf.Project = data.Project(project);
                                leaf.Project.Root = data.Root(root);
                                leaf.Project.Root.Owner = data.Person(root.OwnerId);
                                return leaf;
                            }).ToList();
                            return value;
                        }).ToList();
                        return result;
                    }).ToList();
                    Equal(expected, ComplexRelationshipChecks.Read(query, recording, mode));
                });
            }
            test($"relationship stress: deferred stream captures every nested query, seed {seed}", () =>
            {
                var query = Graph(data.Schema, true);
                var stream = query.ExecuteAsyncStream(recording);
                query.Where(x => x.Id, -1, SqlOp.EQ);
                async Task Collect()
                {
                    var rows = new List<StressRoot>();
                    await foreach (var row in stream) rows.Add(row);
                    Equal(data.Roots.Select(x => data.Graph(x, true)).ToList(), rows, true);
                }
                Collect().GetAwaiter().GetResult();
                Check(ComplexRelationshipChecks.Read(query, recording, "sync").Count == 0,
                    "Fixture mutation did not distinguish captured and current graphs");
            });
            test($"relationship stress: parallel repeated graph compilation and execution, seed {seed}", () =>
            {
                var query = Graph(data.Schema, true);
                string before = query.Render(connector.DbFlavor).Text;
                var expected = data.Roots.Select(x => data.Graph(x, true)).ToList();
                recording.Commands.Clear();
                Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 4 }, index =>
                {
                    Check(query.Render(connector.DbFlavor).Text == before, "Repeated compilation changed SQL");
                    Equal(expected, ComplexRelationshipChecks.Read(query, recording, index % 2 == 0 ? "sync" : "async"), true);
                });
                Check(recording.Commands.Count == 8, "Parallel graph execution issued extra commands");
            });
        }
    }

    // Compare every scalar and selected relationship, with a useful failure path.
    // Unordered collections are canonicalized by primary key; ordered cases retain
    // their original sequence, including tie-breaking keys at every depth.
    private static void Equal<T>(T expected, T actual, bool ordered = false)
    {
        Compare(JsonSerializer.SerializeToNode(expected), JsonSerializer.SerializeToNode(actual), "$", ordered);
    }

    private static void Compare(JsonNode expected, JsonNode actual, string path, bool ordered)
    {
        if (expected is JsonObject left && actual is JsonObject right)
        {
            Check(left.Count == right.Count, path + ": object member counts differ");
            foreach (var field in left)
            {
                Check(right.ContainsKey(field.Key), path + ": missing " + field.Key);
                Compare(field.Value, right[field.Key], path + "." + field.Key, ordered);
            }
        }
        else if (expected is JsonArray first && actual is JsonArray second)
        {
            Check(first.Count == second.Count, $"{path}: expected {first.Count} elements, got {second.Count}");
            IEnumerable<JsonNode> Sort(JsonArray rows) => !ordered && rows.All(x => x is JsonObject obj && obj["Id"] != null)
                ? rows.OrderBy(x => x["Id"].GetValue<int>()) : rows;
            foreach (var pair in Sort(first).Zip(Sort(second)).Select((pair, index) => (pair.First, pair.Second, index)))
                Compare(pair.First, pair.Second, $"{path}[{pair.index}]", ordered);
        }
        else Check(JsonNode.DeepEquals(expected, actual), $"{path}: expected {expected?.ToJsonString() ?? "null"}, got {actual?.ToJsonString() ?? "null"}");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private sealed class RecordingConnector(BaseConnector inner) : BaseConnector(inner.DbFlavor, inner.Options)
    {
        internal readonly ConcurrentQueue<RenderedQuery> Commands = new();
        public override DbConnection CreateConnection() => inner.CreateConnection();
        public override bool JsonResultsRowByRow() => inner.JsonResultsRowByRow();
        protected override SqlResponse<T> ExecuteCommand<T>(RenderedQuery query, Func<SqlResponse<T>> action)
        { Commands.Enqueue(query); return base.ExecuteCommand(query, action); }
        protected override Task<SqlResponse<T>> ExecuteCommandAsync<T>(RenderedQuery query, Func<Task<SqlResponse<T>>> action, CancellationToken token)
        { Commands.Enqueue(query); return base.ExecuteCommandAsync(query, action, token); }
        protected override IAsyncEnumerable<T> ExecuteCommandAsyncStream<T>(RenderedQuery query, Func<CancellationToken, IAsyncEnumerable<T>> action, CancellationToken token)
        { Commands.Enqueue(query); return base.ExecuteCommandAsyncStream(query, action, token); }
    }
}
