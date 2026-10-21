using System;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Core;
using SqlLinqer.Queries.Core.Abstract;

namespace SqlLinqer.Compilation
{
    /// <summary>
    /// A detached query definition that supports repeated or concurrent rendering without changing its source builder.
    /// Copies query structure, arrays, binary buffers, and DataTables. Other provider-specific reference values must remain immutable. Each render creates fresh working state and bindings.
    /// </summary>
    public sealed class QuerySnapshot
    {
        private readonly BaseQuery definition;

        internal QuerySnapshot(BaseQuery query) { definition = SnapshotGraph.Capture(query); }

        /// <summary>
        /// Renders the captured definition with fresh SQL bindings. Later changes to the original builder remain excluded.
        /// Concurrent rendering requires external provider-specific parameter objects to remain immutable.
        /// </summary>
        /// <param name="flavor">The database dialect.</param>
        /// <returns>A new rendered command with independent bindings and any available logical result shape.</returns>
        public RenderedQuery Render(DbFlavor flavor) => RenderQuery(definition, flavor);

        /// <summary>
        /// Renders the captured definition with fresh SQL bindings. Later changes to the original builder remain excluded.
        /// Concurrent rendering requires external provider-specific parameter objects to remain immutable.
        /// </summary>
        /// <param name="connector">The connector supplying the dialect. Its schema settings do not remap the captured definition.</param>
        /// <returns>A new rendered command with independent bindings and any available logical result shape.</returns>
        public RenderedQuery Render(BaseConnector connector)
        {
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            return Render(connector.DbFlavor);
        }

        /// <summary>
        /// Renders a count of the captured root projection before paging, preserving DISTINCT, grouping, and nested query paging.
        /// </summary>
        /// <param name="flavor">The database dialect for the count command.</param>
        /// <returns>A fresh parameterized count command. It does not alter future data renders.</returns>
        /// <exception cref="System.InvalidOperationException">The captured definition is not a SELECT query.</exception>
        public RenderedQuery RenderForCount(DbFlavor flavor)
        {
            if (!(definition is SelectQuery select)) throw new InvalidOperationException("Only SELECT snapshots have a total count.");
            var result = new RenderedQuery();
            result.Text = RenderSelect(SnapshotGraph.Capture(select), result.Parameters, flavor, true, true);
            return result;
        }

        internal static RenderedQuery RenderQuery(BaseQuery source, DbFlavor flavor)
        {
            var working = SnapshotGraph.Capture(source);
            var result = new RenderedQuery();
            result.Transactional = (flavor == DbFlavor.Sqlite || flavor == DbFlavor.DuckDb)
                && (working is InsertQuery || working is UpdateQuery || working is DeleteQuery);
            result.Text = working.Render(result.Parameters, flavor);
            if (working is SelectQuery select)
            {
                result.ResultShape = SqlResultShape.Capture(select);
            }
            return result;
        }

        internal static string RenderSelect(SelectQuery source, ParameterCollection parameters, DbFlavor flavor, bool forJson, bool count)
        {
            var working = source.IsCompilationCopy ? source : SnapshotGraph.Capture(source);
            var context = new RenderContext(parameters, SqlDialect.For(flavor));
            return count ? SelectPreparation.Count(working, context)
                : context.Dialect.Render(SelectPreparation.Prepare(working, context, forJson));
        }
    }
}
