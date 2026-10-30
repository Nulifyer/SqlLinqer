using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Native;
using SqlLinqer.Components.Ordering;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Select;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.EnumExtensions;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Compilation
{
    // Compiles one detached SELECT graph. Parent selection and ordinal assignment
    // occur once, before its header and descendant branches are expanded.
    internal sealed class NativeSelectCompiler
    {
        private sealed class Unsupported : Exception { internal Unsupported(string message) : base(message) { } }
        private sealed class Node
        {
            internal SelectQuery Query;
            internal NativeResultNode Result;
            internal SelectStatement[] Columns;
            internal Table[] Tables;
            internal string TypeSource;
            internal readonly Dictionary<string, string> Hidden = new Dictionary<string, string>();
        }
        private readonly DbFlavor flavor;
        private readonly SqlDialect dialect;
        private readonly ParameterCollection parameters;
        private readonly List<Node> nodes = new List<Node>();
        private readonly NativeResultPlan plan = new NativeResultPlan();
        private sealed class TableReferences
        {
            internal readonly List<Column> Columns = new List<Column>();
            internal readonly HashSet<Column> SeenColumns = new HashSet<Column>(IdentityComparer<Column>.Instance);
            internal readonly HashSet<Table> Instances = new HashSet<Table>(IdentityComparer<Table>.Instance);
        }
        private sealed class IdentityComparer<T> : IEqualityComparer<T> where T : class
        {
            internal static readonly IdentityComparer<T> Instance = new IdentityComparer<T>();
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
        }
        private Dictionary<string, TableReferences> referenceIndex;
        private string prefix = "__slq_";
        private string Q(string name) => name.DbWrap(flavor);
        private string Field(string table, string name) => Q(table) + "." + Q(name);
        private string Position(Node node) => Field(prefix + "n" + node.Result.Id, prefix + "pos");
        private string Null(int ordinal) => Field(prefix + "nulls", prefix + "v" + ordinal);

        private NativeSelectCompiler(DbFlavor flavor, ParameterCollection parameters)
        { this.flavor = flavor; dialect = SqlDialect.For(flavor); this.parameters = parameters; parameters.SetFlavor(flavor); }

        internal static RenderedQuery Compile(SelectQuery source, DbFlavor flavor, ParameterCollection bindings = null, bool count = false)
            => CompileWorking(SnapshotGraph.Capture(source), flavor, bindings, count);

        internal static RenderedQuery CompileWorking(SelectQuery working, DbFlavor flavor, ParameterCollection bindings = null, bool count = false)
        {
            try
            {
                if (count) { working.PagingControls.Reset(); working.OrderBys.Clear(); }
                var result = new RenderedQuery { Parameters = bindings ?? new ParameterCollection(flavor), QueryName = working.QueryName, CommandRole = count ? "Count" : "Select", IsReadOnlySelect = true };
                if (!count) result.ResultShape = SqlResultShape.Capture(working);
                var compiler = new NativeSelectCompiler(flavor, result.Parameters);
                bool graph = working.Selects.GetAll().Any(x => x is SelectRelationship);
                if (!graph)
                {
                    if (count) { result.Text = SelectPreparation.Count(working, new RenderContext(result.Parameters, compiler.dialect)); return result; }
                    var prepared = SelectPreparation.Prepare(working, new RenderContext(result.Parameters, compiler.dialect));
                    var fields = working.Selects.GetAll().ToArray();
                    if (fields.Length == 0) throw new Unsupported("Native SELECT requires a projection.");
                    var root = new NativeResultNode
                    {
                        Id = 0,
                        Depth = 0,
                        Scalars = fields.Select((x, i) => new NativeResultScalar { Alias = x.Alias, Ordinal = i }).ToArray()
                    };
                    result.NativePlan = new NativeResultPlan { Root = root, Nodes = new[] { root }, PayloadCount = fields.Length };
                    result.Text = compiler.dialect.Render(prepared);
                    return result;
                }
                compiler.AddNode(working, null, null);
                compiler.Prepare(!count);
                if (count)
                {
                    string rows = compiler.Rows(compiler.nodes[0]);
                    string with = working.Withs?.Render(working, result.Parameters, flavor);
                    result.Text = with + " SELECT COUNT(*) FROM (" + rows + ") AS " + compiler.Q("__slq_count");
                    return result;
                }
                result.Text = compiler.dialect.UsesLocalChildren
                    ? compiler.RenderLocal(result)
                    : compiler.Render();
                result.NativePlan = compiler.plan;
                return result;
            }
            catch (Unsupported e)
            {
                throw new NotSupportedException(e.Message);
            }
        }

        private Node AddNode(SelectQuery query, Node parent, SelectRelationship relationship)
        {
            if (!dialect.UsesLocalChildren) query.IsSubQuery = true;
            var selected = query.Selects.GetAll().ToArray();
            if (selected.Select(x => x.Alias).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Length)
                throw new Unsupported("Nested native sources require unambiguous derived-column aliases.");
            if (query.OrderBys.Statements.Any(x => !(x is ColumnOrderBy) && !(x is ProjectionOrderBy) && !(x is SubQueryOrderBy)))
                throw new Unsupported("Nested native execution requires a supported ordering expression.");
            var scalar = selected.Where(x => !(x is SelectRelationship)).ToArray(); var children = selected.OfType<SelectRelationship>().ToArray();
            var result = new NativeResultNode
            {
                Id = nodes.Count,
                Parent = parent?.Result,
                Depth = parent == null ? 0 : parent.Result.Depth + 1,
                Alias = relationship?.Alias,
                IsCollection = relationship != null && relationship.Relationship.Type != RelationshipType.OneToOne
            };
            result.Scalars = scalar.Select((x, i) => new NativeResultScalar { Alias = x.Alias, Ordinal = plan.PayloadCount++ }).ToArray();
            var node = new Node { Query = query, Result = result, Columns = scalar }; nodes.Add(node);
            if (parent != null) { result.EdgeIndex = parent.Result.Children.Count; parent.Result.Children.Add(result); }
            for (int i = 0; i < children.Length; i++)
            {
                AddNode(children[i].SubQuery, node, children[i]);
            }
            query.Selects.Clear(); foreach (var column in scalar) query.Selects.AddStatement(column);
            return node;
        }

        private void Prepare(bool prepareResults)
        {
            plan.Root = nodes[0].Result; plan.Nodes = nodes.Select(x => x.Result).ToArray();
            plan.Depth = nodes.Max(x => x.Result.Depth); plan.IsGraph = true;
            while (nodes.Any(n => n.Query.Selects.GetAll().Any(s => s.Alias.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                || AllReferences().Select(c => c.Table).Concat(nodes.Select(n => n.Query.Table))
                    .Any(t => t.Alias?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true)) prefix += "_";
            foreach (var node in nodes)
            {
                foreach (var order in node.Query.OrderBys.Statements.OfType<SubQueryOrderBy>())
                    node.Query.Selects.AddStatement(new NativeOrderKey(order, prefix + "o" + node.Result.Id + "_" + node.Query.Selects.Count));
                var discovery = SnapshotGraph.Capture(node.Query);
                SelectPreparation.Prepare(discovery, new RenderContext(new ParameterCollection(flavor), dialect));
                node.Tables = discovery.Joins.NativeLocalTables.ToArray();
                if (node.Query.OrderBys.Count == 0)
                    foreach (var order in discovery.OrderBys.Statements) node.Query.OrderBys.AddStatement(order);
                if (prepareResults && !dialect.UsesLocalChildren && node.Columns.Length > 0)
                    node.TypeSource = PrepareTypeSource(node, discovery);

            }
            var forwarded = nodes.ToDictionary(n => n,
                n => n.Query.Distinct ? new List<Column>() : n.Query.OrderBys.Statements.OfType<ColumnOrderBy>().Select(o => o.Column).ToList());
            foreach (var descendant in nodes)
            {
                var locals = new HashSet<string>(descendant.Tables.Select(t => t.UUID));
                var inputs = References(descendant).Where(c => !locals.Contains(c.Table.UUID)).ToArray();
                foreach (var owner in Ancestors(descendant)) forwarded[owner].AddRange(inputs);
            }
            foreach (var node in nodes)
            {
                var locals = new HashSet<string>(node.Tables.Select(t => t.UUID));
                // Forward only external descendant inputs and local order keys.
                // Aggregate arguments and predicates are not grouping dimensions.
                var references = forwarded[node];
                foreach (var column in references.Where(c => locals.Contains(c.Table.UUID)).GroupBy(c => c.UUID).Select(g => g.First()))
                {
                    string alias = prefix + "h" + node.Result.Id + "_" + node.Hidden.Count;
                    node.Hidden.Add(column.UUID, alias); node.Query.Selects.AddStatement(new SelectColumn(column, alias));
                }
            }
            foreach (var node in nodes.Where(n => n.Query.Distinct))
                foreach (var order in node.Query.OrderBys.Statements)
                    if (order is SubQueryOrderBy || order is ColumnOrderBy column
                        && !node.Hidden.ContainsKey(column.Column.UUID)
                        && node.Query.Selects.TryGet(column.Column.Alias) == null
                        && !node.Columns.OfType<SelectColumn>().Any(c => c.Column.UUID == column.Column.UUID))
                        throw new Unsupported("DISTINCT ordering must use a selected scalar column or a required relationship key.");
            referenceIndex = new Dictionary<string, TableReferences>(StringComparer.Ordinal);
            foreach (var node in nodes) RegisterReferences(node);

        }

        private void RegisterReferences(Node node)
        {
            foreach (var table in node.Tables) ReferencesFor(table).Instances.Add(table);
            foreach (var column in References(node))
            {
                var entry = ReferencesFor(column.Table);
                entry.Instances.Add(column.Table);
                if (entry.SeenColumns.Add(column)) entry.Columns.Add(column);
            }
        }
        private TableReferences ReferencesFor(Table table)
        {
            if (!referenceIndex.TryGetValue(table.UUID, out var entry))
                referenceIndex.Add(table.UUID, entry = new TableReferences());
            return entry;
        }
        private void ClearLocalBindings(Node node)
        {
            foreach (var table in node.Tables)
                foreach (var instance in ReferencesFor(table).Instances) instance.NativeBindings = null;
        }

        private static int nodeIndex(Node node, string alias) => Array.FindIndex(node.Query.Selects.GetAll().ToArray(), x => x.Alias == alias) - node.Columns.Length;

        private static IEnumerable<Column> SelectReferences(SelectStatement statement)
        {
            if (statement is SelectColumn column) return new[] { column.Column };
            if (statement is SelectFuncStatement function) return function.Args.SelectMany(arg => arg is Column col ? new[] { col }
                : arg is SelectStatement sub ? SelectReferences(sub) : arg is IEnumerable<Column> columns ? columns : Array.Empty<Column>());
            if (statement is CaseStatement conditional) return conditional.Conditions.SelectMany(c => c.Condition?.GetColumnReferences() ?? Array.Empty<Column>())
                .Concat(conditional.Conditions.SelectMany(c => c.ReturnValue is Column col ? new[] { col } : c.ReturnValue is SelectStatement sub ? SelectReferences(sub) : Array.Empty<Column>()));
            return Array.Empty<Column>();
        }

        private sealed class NativeOrderKey : SelectStatement
        {
            internal readonly SubQueryOrderBy Order;
            private readonly string alias;
            internal NativeOrderKey(SubQueryOrderBy order, string alias) { Order = order; this.alias = alias; }
            public override string Alias => alias;
            public override bool DoGroupBy() => true;
            public override string Render(SelectQuery query, ParameterCollection parameters, DbFlavor flavor, bool includeAlias)
            {
                string expression = Order.Render(query, parameters, flavor);
                expression = expression.Substring(0, expression.Length - Order.Dir.GetDescription().Length).TrimEnd();
                return expression + (includeAlias ? " AS " + alias.DbWrap(flavor) : "");
            }
            public override IEnumerable<string> RenderForGroupBy(SelectQuery query, ParameterCollection parameters, DbFlavor flavor)
                => Order.Relationship.ParentTableJoin.Condition.GetColumnReferences().Where(c => c.Table.UUID == Order.Relationship.ParentTable.UUID).Select(c => c.Render(flavor));
        }

        private string RenderLocal(RenderedQuery rootCommand)
        {
            plan.LocalQueries = true;
            foreach (var node in nodes)
                PrepareLocalCommand(node, node.Result.Parent == null ? rootCommand : new RenderedQuery());
            return rootCommand.Text;
        }

        private void PrepareLocalCommand(Node node, RenderedQuery command)
        {
            foreach (var table in AllReferences().Select(c => c.Table).Concat(nodes.SelectMany(n => n.Tables))) table.NativeBindings = null;
            node.Result.LocalQuery = command;
            node.Result.HiddenCount = node.Query.Selects.Count - node.Columns.Length;
            var ancestry = new Stack<NativeResultNode>(); var ancestor = node.Result.Parent;
            while (ancestor != null) { ancestry.Push(ancestor); ancestor = ancestor.Parent; }
            while (ancestry.Count > 0)
            {
                var owner = nodes[ancestry.Pop().Id];
                var values = new Dictionary<string, string>();
                foreach (var hidden in owner.Hidden)
                {
                    string parameter = command.Parameters.AddParameter(null);
                    values.Add(hidden.Key, parameter);
                    node.Result.Inputs.Add(new NativeInputBinding { Parameter = parameter, Node = owner.Result.Id, HiddenIndex = nodeIndex(owner, hidden.Value) });
                }
                foreach (var table in AllReferences().Select(c => c.Table).Where(t => owner.Tables.Any(o => o.UUID == t.UUID)).Distinct())
                    table.NativeBindings = AllReferences().Where(c => c.Table.UUID == table.UUID && values.ContainsKey(c.UUID))
                        .GroupBy(c => c.ColumnName).ToDictionary(g => g.Key, g => values[g.First().UUID]);
            }
            foreach (var table in AllReferences().Select(c => c.Table).Where(t => node.Tables.Any(local => local.UUID == t.UUID))) table.NativeBindings = null;
            var prepared = SelectPreparation.Prepare(node.Query, new RenderContext(command.Parameters, dialect));
            command.Text = dialect.Render(prepared);
        }

        private string Rows(Node node)
        {
            ClearLocalBindings(node);
            var ancestor = node.Result.Parent;
            var ancestors = new Stack<Node>();
            while (ancestor != null) { ancestors.Push(nodes[ancestor.Id]); ancestor = ancestor.Parent; }
            while (ancestors.Count > 0) Bind(ancestors.Pop());
            ClearLocalBindings(node);
            // WITH is rendered once for the final command, including SQLite counts.
            node.Query.IsSubQuery = true;
            var prepared = SelectPreparation.Prepare(node.Query, new RenderContext(parameters, dialect));
            RegisterReferences(node);
            prepared = new PreparedSelect(null, prepared.Select, prepared.From, prepared.Where, prepared.GroupBy, prepared.OrderBy,
                prepared.Distinct, prepared.Top, prepared.Limit, prepared.Offset);
            string sql = dialect.PreserveDerivedOrder(prepared, dialect.Render(prepared));
            string source = prefix + "s" + node.Result.Id;
            var orders = OrderFields(node, source);
            string ordering = orders.Count > 0 ? "ORDER BY " + string.Join(",", orders)
                : dialect.UnorderedWindow;
            return $"SELECT {Q(source)}.*,ROW_NUMBER() OVER ({ordering}) AS {Q(prefix + "pos")} FROM ({sql}) AS {Q(source)}";
        }

        private List<string> OrderFields(Node node, string source)
        {
            var orders = new List<string>();
            foreach (var order in node.Query.OrderBys.Statements)
            {
                if (order is ColumnOrderBy column)
                {
                    orders.Add((node.Hidden.TryGetValue(column.Column.UUID, out string alias) ? Field(source, alias) : node.Columns.OfType<SelectColumn>().FirstOrDefault(c => c.Column.UUID == column.Column.UUID) is SelectColumn selected ? Field(source, selected.Alias) : node.Query.Selects.TryGet(column.Column.Alias) != null ? Field(source, column.Column.Alias) : column.Column.Render(flavor)) + " " + column.Dir.GetDescription());
                }
                else if (order is ProjectionOrderBy projection) orders.Add(Field(source, projection.Alias) + " ASC");
                else if (order is SubQueryOrderBy related)
                {
                    string alias = node.Query.Selects.GetAll().OfType<NativeOrderKey>().Single(k => ReferenceEquals(k.Order, related)).Alias;
                    orders.Add(Field(source, alias) + " " + related.Dir.GetDescription());
                }
                else throw new Unsupported("Unsupported native ordering expression.");
            }
            return orders;
        }

        private void Bind(Node node)
        {
            foreach (var table in node.Tables)
            {
                var entry = ReferencesFor(table);
                var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var column in entry.Columns)
                    if (node.Hidden.TryGetValue(column.UUID, out var alias)) bindings[column.ColumnName] = Field(prefix + "n" + node.Result.Id, alias);
                foreach (var instance in entry.Instances)
                    instance.NativeBindings = bindings;
            }
        }
        private static IEnumerable<Column> References(Node n) => n.Columns.SelectMany(SelectReferences)
            .Concat(n.Query.RootWhere.GetColumnReferences()).Concat(n.Query.Joins.NativeReferences)
            .Concat(n.Query.OrderBys.Statements.OfType<ColumnOrderBy>().Select(o => o.Column));
        private IEnumerable<Column> AllReferences() => nodes.SelectMany(References);

        private string Header(Node node)
        {
            var fields = new List<string> { node.Result.Id + " AS " + Q(prefix + "kind") };
            for (int depth = 1; depth <= plan.Depth; depth++)
            {
                var ancestor = node.Result;
                while (ancestor != null && ancestor.Depth != depth) ancestor = ancestor.Parent;
                fields.Add((ancestor == null ? "0" : Position(nodes[ancestor.Id])) + " AS " + Q(prefix + "p" + depth));
            }
            foreach (var other in nodes)
            {
                for (int i = 0; i < other.Result.Scalars.Length; i++)
                {
                    int ordinal = other.Result.Scalars[i].Ordinal;
                    string value = other == node ? Field(prefix + "n" + node.Result.Id, other.Result.Scalars[i].Alias) : Null(ordinal);
                    fields.Add(value + " AS " + Q(prefix + "v" + ordinal));
                }

            }
            return "SELECT " + string.Join(",", fields);
        }

        private string Expand(Node node)
        {
            var branches = new List<string> { Header(node) };
            foreach (var child in node.Result.Children) branches.Add(Subtree(nodes[child.Id]));
            return string.Join(" UNION ALL ", branches);
        }
        private string Subtree(Node node)
        {
            string rows = Rows(node); Bind(node);
            string records = Expand(node); string alias = prefix + "e" + node.Result.Id;
            return $"SELECT {Q(alias)}.* FROM ({rows}) AS {Q(prefix + "n" + node.Result.Id)} {Apply(records, alias)}";
        }
        private string Apply(string sql, string alias)
            => dialect.Correlate(sql, alias, prefix + "a" + alias);

        private IEnumerable<Node> Ancestors(Node node)
        {
            var chain = new Stack<Node>();
            var parent = node.Result.Parent;
            while (parent != null) { chain.Push(nodes[parent.Id]); parent = parent.Parent; }
            return chain;
        }

        private void ClearBindings()
        {
            foreach (var entry in referenceIndex.Values)
                foreach (var table in entry.Instances) table.NativeBindings = null;
        }

        private string Render()
        {
            string typed = TypedNulls();
            string rootRows = Rows(nodes[0]);
            Bind(nodes[0]);
            string records = Expand(nodes[0]);
            ClearBindings();
            string with = nodes[0].Query.Withs?.Render(nodes[0].Query, parameters, flavor);
            string raw = prefix + "records";
            string count = dialect.NativeCount;
            string framed = $"SELECT {Q(raw)}.*,CASE WHEN {Field(raw, prefix + "kind")}=0 THEN {count} OVER () END AS {Q(prefix + "count")} FROM ({records}) AS {Q(raw)}";
            string output = prefix + "out";
            var fields = new List<string> { Position(nodes[0]) + " AS " + Q(prefix + "root"), Field(output, prefix + "kind"), Field(output, prefix + "count") };
            for (int d = 1; d <= plan.Depth; d++) fields.Add(Field(output, prefix + "p" + d));
            for (int v = 0; v < plan.PayloadCount; v++) fields.Add(Field(output, prefix + "v" + v));
            string select = $"SELECT {string.Join(",", fields)} FROM ({rootRows}) AS {Q(prefix + "n0")} LEFT JOIN ({typed}) AS {Q(prefix + "nulls")} ON 1=1 {Apply(framed, output)} ORDER BY {Position(nodes[0])}";
            return with + " " + select;
        }

        private string PrepareTypeSource(Node node, SelectQuery probe)
        {
            // Discovery already owns a detached source. Finish its empty type
            // probe now and retain only SQL, releasing the connected graph.
            probe.IsSubQuery = true; probe.RootWhere.Clear();
            var prepared = SelectPreparation.Prepare(probe, new RenderContext(parameters, dialect));
            string from = prepared.From;
            var included = new HashSet<string>(probe.Joins.NativeLocalTables.Select(t => t.UUID));
            var ancestor = node.Result.Parent;
            while (ancestor != null)
            {
                foreach (var table in nodes[ancestor.Id].Tables.Where(t => AllReferences().Any(c => c.Table.UUID == t.UUID) && included.Add(t.UUID)))
                    from += " CROSS JOIN " + table.RenderFullName(flavor) + " AS " + Q(table.Alias);
                ancestor = ancestor.Parent;
            }
            var empty = new PreparedSelect(null, prepared.Select, from, "WHERE 1=0", prepared.GroupBy, null, false, 0, 0, 0);
            string alias = prefix + "type" + node.Result.Id;
            return "(" + dialect.Render(empty) + ") AS " + Q(alias);
        }

        private string TypedNulls()
        {
            // An empty source supplies each payload column's actual provider SQL
            // type, length, collation, and precision. Guessed CLR casts can change
            // decimal scale or coerce binary/date values across UNION branches.
            var sources = new List<string>(); var columns = new List<string>();
            foreach (var node in nodes.Where(n => n.Columns.Length > 0))
            {
                string alias = prefix + "type" + node.Result.Id;
                sources.Add(node.TypeSource);
                for (int i = 0; i < node.Columns.Length; i++)
                    columns.Add(Field(alias, node.Columns[i].Alias) + " AS " + Q(prefix + "v" + node.Result.Scalars[i].Ordinal));
            }
            return "SELECT " + (columns.Count == 0 ? "1 AS " + Q(prefix + "unused") : string.Join(",", columns))
                + (sources.Count == 0 ? "" : " FROM " + string.Join(" CROSS JOIN ", sources)) + " WHERE 1=0";
        }
    }
}
