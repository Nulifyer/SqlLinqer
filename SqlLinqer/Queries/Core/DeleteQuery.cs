using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Components.Generic;
using SqlLinqer.Components.Outputs;

namespace SqlLinqer.Queries.Core
{
    public class DeleteQuery : WhereQuery
    {
        protected internal OutputsCollection Outputs;
        
        public DeleteQuery(Table table) : base(table)
        {
            Outputs = new OutputsCollection();
        }

        /// <inheritdoc/>
        public override string Render(ParameterCollection parameters, DbFlavor flavor)
        {
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
                    case DbFlavor.SqlServer:
                        return string.Join(" ", with, $"DELETE {Table.Alias.DbWrap(flavor)}", Outputs.Render(flavor), joins, where);
                    case DbFlavor.MySql:
                        Outputs.Render(flavor);
                        return string.Join(" ", with, $"DELETE {Table.Alias.DbWrap(flavor)}", joins, where);
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
