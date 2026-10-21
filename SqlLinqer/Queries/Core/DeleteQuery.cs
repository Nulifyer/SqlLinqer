using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Components.Generic;
using SqlLinqer.Components.Outputs;

namespace SqlLinqer.Queries.Core
{
    /// <summary>Builds a DELETE command for a mapped table.</summary>
    public class DeleteQuery : WhereQuery
    {
        /// <summary>The modified-row output projection.</summary>
        protected internal OutputsCollection Outputs;
        
        /// <summary>Creates a DELETE query for the supplied table.</summary>
        public DeleteQuery(Table table) : base(table)
        {
            Outputs = new OutputsCollection();
        }

        /// <inheritdoc/>
        public override string Render(ParameterCollection parameters, DbFlavor flavor)
        {
            var working = IsCompilationCopy ? this : SqlLinqer.Compilation.SnapshotGraph.Capture(this);
            return working.RenderCore(parameters, flavor);
        }

        private string RenderCore(ParameterCollection parameters, DbFlavor flavor)
        {
            parameters.SetFlavor(flavor);
            var stashables = new IStashable[]
            {
                this.RootWhere,
                this.Withs,
                this.Joins,
                this.Outputs,
            };

            foreach (var item in stashables)
                item?.Stash();

            try
            {
                Joins.AddRootTable(Table);
                string where = RootWhere.Render(this, parameters, flavor);
                string with = Withs.Render(this, parameters, flavor);
                string joins = Joins.Render(this, parameters, flavor);

                switch (flavor)
                {
                    case DbFlavor.Sqlite:
                        if (Joins.HasJoinedTables(Table))
                        {
                            if (Table.PrimaryKey == null)
                                throw new System.NotSupportedException("Joined SQLite deletes require a primary key on the target model.");
                            where = $"WHERE {Table.PrimaryKey.Render(flavor)} IN (SELECT {Table.PrimaryKey.Render(flavor)} {joins} {where})";
                        }
                        return string.Join(" ", with, $"DELETE FROM {Table.RenderFullName(flavor)} AS {Table.Alias.DbWrap(flavor)}",
                            where, Outputs.Render(flavor));
                    case DbFlavor.SqlServer:
                    case DbFlavor.AzureSql:
                        return string.Join(" ", with, $"DELETE {Table.Alias.DbWrap(flavor)}", Outputs.Render(flavor), joins, where);
                    case DbFlavor.MySql:
                        Outputs.Render(flavor);
                        return string.Join(" ", with, $"DELETE {Table.Alias.DbWrap(flavor)}", joins, where);
                    case DbFlavor.DuckDb:
                        string deleteAlias = Table.Alias;
                        if (Joins.HasJoinedTables(Table))
                        {
                            if (Table.PrimaryKey == null)
                                throw new System.NotSupportedException("Joined DuckDB deletes require a primary key on the target model.");
                            deleteAlias = "target_" + Table.UUID;
                            // Native DELETE USING can attempt to remove an indexed row twice
                            // for duplicate source matches. Select each target key only once.
                            where = $"WHERE {deleteAlias.DbWrap(flavor)}.{Table.PrimaryKey.ColumnName.DbWrap(flavor)} IN (SELECT DISTINCT {Table.PrimaryKey.Render(flavor)} {joins} {where})";
                        }
                        return string.Join(" ", with, $"DELETE FROM {Table.RenderFullName(flavor)} AS {deleteAlias.DbWrap(flavor)}", where, Outputs.Render(flavor, deleteAlias));
                    case DbFlavor.PostgreSql:
                        string targetAlias = Table.Alias;
                        string usingClause = null;
                        if (Joins.HasJoinedTables(Table))
                        {
                            if (Table.PrimaryKey == null)
                                throw new System.NotSupportedException("Joined PostgreSQL deletes require a primary key on the target model.");

                            targetAlias = "target_" + Table.UUID;
                            string match = $"{targetAlias.DbWrap(flavor)}.{Table.PrimaryKey.ColumnName.DbWrap(flavor)} = {Table.PrimaryKey.Render(flavor)}";
                            where = string.IsNullOrEmpty(where) ? $"WHERE {match}" : $"WHERE {match} AND ({where.Substring(6)})";
                            usingClause = "USING " + joins.Substring(5);
                        }
                        return string.Join(" ", with, $"DELETE FROM {Table.RenderFullName(flavor)} AS {targetAlias.DbWrap(flavor)}",
                            usingClause, where, Outputs.Render(flavor, targetAlias));
                    default:
                        throw new System.NotSupportedException($"The database flavor {flavor} is not supported.");
                }
            }
            finally
            {
                foreach (var item in stashables)
                    item?.Unstash();
            }
        }
    }
}
