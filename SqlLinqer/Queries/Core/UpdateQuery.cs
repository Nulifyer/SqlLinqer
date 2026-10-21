using SqlLinqer.Components.Render;
using SqlLinqer.Components.Update;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Outputs;
using SqlLinqer.Components.Generic;
using SqlLinqer.Extensions.StringExtensions;

namespace SqlLinqer.Queries.Core
{
    /// <summary>Builds an UPDATE command for a mapped table.</summary>
    public class UpdateQuery : WhereQuery
    {
        /// <summary>The column assignments to apply.</summary>
        protected internal UpdatesCollection Updates;
        /// <summary>The modified-row output projection.</summary>
        protected internal OutputsCollection Outputs;

        /// <summary>Creates an UPDATE query for the supplied table.</summary>
        public UpdateQuery(Table table) : base(table)
        {
            Updates = new UpdatesCollection();
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
                this.Updates,
                this.Outputs,
            };

            foreach (var item in stashables)
                item?.Stash();

            try
            {
                if (Updates.Count == 0)
                    throw new System.InvalidOperationException("An update query must contain at least one assignment.");

                Joins.AddRootTable(Table);
                string where = RootWhere.Render(this, parameters, flavor);
                string assignments = Updates.RenderAssignments(this, parameters, flavor);
                string with = Withs.Render(this, parameters, flavor);
                string joins = Joins.Render(this, parameters, flavor);

                switch (flavor)
                {
                    case DbFlavor.Sqlite:
                        foreach (var update in Updates.GetAll())
                        {
                            if (update.UpdateColumn.Table.UUID != Table.UUID)
                                throw new System.NotSupportedException("SQLite updates can assign only columns on the target table.");
                        }
                        string sqliteTargetAlias = Table.Alias;
                        string sqliteFrom = null;
                        if (Joins.HasJoinedTables(Table))
                        {
                            if (Table.PrimaryKey == null)
                                throw new System.NotSupportedException("Joined SQLite updates require a primary key on the target model.");
                            // Keep the original source joins intact, including LEFT JOIN nulls.
                            // SQLite chooses one arbitrary source row if several match a target.
                            sqliteTargetAlias = "target_" + Table.UUID;
                            string match = $"{sqliteTargetAlias.DbWrap(flavor)}.{Table.PrimaryKey.ColumnName.DbWrap(flavor)} = {Table.PrimaryKey.Render(flavor)}";
                            where = string.IsNullOrEmpty(where) ? $"WHERE {match}" : $"WHERE {match} AND ({where.Substring(6)})";
                            sqliteFrom = joins;
                        }
                        return string.Join(" ", with, $"UPDATE {Table.RenderFullName(flavor)} AS {sqliteTargetAlias.DbWrap(flavor)} SET {assignments}",
                            sqliteFrom, where, Outputs.Render(flavor));
                    case DbFlavor.SqlServer:
                    case DbFlavor.AzureSql:
                        return string.Join(" ", with, $"UPDATE {Table.Alias.DbWrap(flavor)} SET {assignments}",
                            Outputs.Render(flavor), joins, where);
                    case DbFlavor.MySql:
                        // MySQL puts joined tables before SET and has no OUTPUT/RETURNING clause.
                        Outputs.Render(flavor);
                        return string.Join(" ", with, $"UPDATE {joins.Substring(5)} SET {assignments}", where);
                    case DbFlavor.DuckDb:
                    case DbFlavor.PostgreSql:
                        string targetAlias = Table.Alias;
                        string from = null;
                        if (Joins.HasJoinedTables(Table))
                        {
                            if (Table.PrimaryKey == null)
                                throw new System.NotSupportedException(flavor == DbFlavor.DuckDb
                                    ? "Joined DuckDB updates require a primary key on the target model."
                                    : "Joined PostgreSQL updates require a primary key on the target model.");

                            if (flavor == DbFlavor.DuckDb)
                            {
                                // Reject ambiguity inside the statement, before any write can
                                // commit. Native UPDATE FROM otherwise picks an arbitrary match
                                // and may return the same target key more than once.
                                string duplicateKeys = $"SELECT {Table.PrimaryKey.Render(flavor)} {joins} {where} GROUP BY {Table.PrimaryKey.Render(flavor)} HAVING COUNT(*) > 1";
                                string unique = $"CASE WHEN EXISTS ({duplicateKeys}) THEN error('Joined DuckDB updates require at most one source match per target primary key.') ELSE TRUE END";
                                where = string.IsNullOrEmpty(where) ? $"WHERE {unique}" : $"{where} AND ({unique})";
                            }

                            // Keep the source joins intact, including LEFT JOIN semantics.
                            targetAlias = "target_" + Table.UUID;
                            string match = $"{targetAlias.DbWrap(flavor)}.{Table.PrimaryKey.ColumnName.DbWrap(flavor)} = {Table.PrimaryKey.Render(flavor)}";
                            where = string.IsNullOrEmpty(where) ? $"WHERE {match}" : $"WHERE {match} AND ({where.Substring(6)})";
                            from = joins;
                        }
                        return string.Join(" ", with, $"UPDATE {Table.RenderFullName(flavor)} AS {targetAlias.DbWrap(flavor)} SET {assignments}",
                            from, where, Outputs.Render(flavor, targetAlias));
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
