using SqlLinqer.Components.Render;
using SqlLinqer.Components.Update;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Outputs;
using SqlLinqer.Components.Generic;
using SqlLinqer.Extensions.StringExtensions;

namespace SqlLinqer.Queries.Core
{
    public class UpdateQuery : WhereQuery
    {
        protected internal UpdatesCollection Updates;
        protected internal OutputsCollection Outputs;

        public UpdateQuery(Table table) : base(table)
        {
            Updates = new UpdatesCollection();
            Outputs = new OutputsCollection();
        }

        public override string Render(ParameterCollection parameters, DbFlavor flavor)
        {
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
                    case DbFlavor.SqlServer:
                        return string.Join(" ", with, $"UPDATE {Table.Alias.DbWrap(flavor)} SET {assignments}",
                            Outputs.Render(flavor), joins, where);
                    case DbFlavor.MySql:
                        // MySQL puts joined tables before SET and has no OUTPUT/RETURNING clause.
                        Outputs.Render(flavor);
                        return string.Join(" ", with, $"UPDATE {joins.Substring(5)} SET {assignments}", where);
                    case DbFlavor.PostgreSql:
                        string targetAlias = Table.Alias;
                        string from = null;
                        if (Joins.HasJoinedTables(Table))
                        {
                            if (Table.PrimaryKey == null)
                                throw new System.NotSupportedException("Joined PostgreSQL updates require a primary key on the target model.");

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
