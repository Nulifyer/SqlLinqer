using System;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Core;
using SqlLinqer.Queries.Core.Abstract;

namespace SqlLinqer.Compilation
{
    /// <summary>
    /// An isolated query definition that can be rendered repeatedly without changing its builder.
    /// Arrays and DataTables are copied. Other provider-specific parameter objects must remain immutable.
    /// </summary>
    public sealed class QuerySnapshot
    {
        private readonly BaseQuery definition;

        internal QuerySnapshot(BaseQuery query) { definition = SnapshotGraph.Capture(query); }

        /// <summary>Renders with fresh working state and parameter bindings.</summary>
        public RenderedQuery Render(DbFlavor flavor) => RenderQuery(definition, flavor);

        /// <summary>Renders for the connector's database dialect.</summary>
        public RenderedQuery Render(BaseConnector connector)
        {
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            return Render(connector.DbFlavor);
        }

        /// <summary>Counts the unpaged root projection, preserving nested paging, DISTINCT, and grouping.</summary>
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
