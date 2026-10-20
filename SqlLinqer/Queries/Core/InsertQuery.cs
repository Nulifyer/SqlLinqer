using System.Data;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Insert;
using SqlLinqer.Components.Outputs;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Components.Generic;

namespace SqlLinqer.Queries.Core
{
    public class InsertQuery : TableQuery
    {
        protected internal InsertRowCollection Inserts;
        protected internal OutputsCollection Outputs;

        public InsertQuery(Table table) : base(table)
        {
            Inserts = new InsertRowCollection();
            Outputs = new OutputsCollection();
        }

        /// <summary>
        /// Render the create and drop queries of the temp type
        /// </summary>
        /// <param name="flavor">The type of Sql database being used</param>
        /// <param name="typeName">The name of the SQL type to use</param>
        /// <returns></returns>
        public virtual (string Create, string Drop) RenderTempType(DbFlavor flavor, string typeName)
        {
            flavor = SqlDialect.Resolve(flavor);
            if (flavor != DbFlavor.SqlServer)
                throw new System.NotSupportedException("Table-valued parameters are supported only for SQL Server.");
            return Inserts.RenderTempType(flavor, typeName);
        }
        /// <inheritdoc/>
        public override string Render(ParameterCollection parameters, DbFlavor flavor)
        {
            flavor = SqlDialect.Resolve(flavor);
            var stashables = new IStashable[]
            {
                this.Outputs,
            };

            foreach (var item in stashables)
                item?.Stash();

            try
            {
                if (flavor == DbFlavor.Sqlite && Inserts.Count == 0)
                    throw new System.InvalidOperationException("An insert query must contain at least one row.");
                if (flavor == DbFlavor.Sqlite && string.IsNullOrWhiteSpace(Inserts.RenderColumns(flavor)))
                {
                    if (Inserts.Count != 1)
                        throw new System.NotSupportedException("SQLite DEFAULT VALUES inserts support one row per query.");
                    return $"INSERT INTO {Table.RenderFullName(flavor)} DEFAULT VALUES {Outputs.Render(flavor)}";
                }
                string inserts = this.Inserts.RenderAsRows(parameters, flavor);
                string columns = this.Inserts.RenderColumns(flavor);
                string outputs = this.Outputs.Render(flavor);

                return string.Join(" ", new[]
                {
                    $"INSERT INTO {Table.RenderFullName(flavor)}",
                    $"({columns})",
                    flavor == DbFlavor.SqlServer ? outputs : null,
                    "VALUES",
                    inserts,
                    (flavor == DbFlavor.PostgreSql || flavor == DbFlavor.Sqlite) ? outputs : null,
                });
            }
            finally
            {
                foreach (var item in stashables)
                    item?.Unstash();
            }
        }
        /// <summary>
        /// Render the create and drop queries of the temp type
        /// </summary>
        /// <param name="existingCollection">The parameter collection to use</param>
        /// <param name="flavor">The type of Sql database being used</param>
        /// <param name="schema">The type of Sql database being used</param>
        /// <returns></returns>
        public (string Query, string TempTypeId) RenderTVP(ParameterCollection existingCollection, DbFlavor flavor, string schema)
        {
            flavor = SqlDialect.Resolve(flavor);
            if (flavor != DbFlavor.SqlServer)
                throw new System.NotSupportedException("Table-valued parameters are supported only for SQL Server.");

            string tempTypeId =
                (schema != null ? $"{schema.DbWrap(flavor)}." : null)
                + $"TEMP_{StringExtensions.GetRandomHashString()}".DbWrap(flavor);

            var dataTable = Inserts.RenderAsDataTable();
            string datatable_placeholder = existingCollection.AddTvpParameter(dataTable, tempTypeId);

            string columns = this.Inserts.RenderColumns(flavor);
            string outputs = this.Outputs.Render(flavor);

            string query = string.Join(" ", new[]
            {
                $"INSERT INTO {Table.RenderFullName(flavor)}",
                $"({columns})",
                outputs,
                $"SELECT {columns} FROM {datatable_placeholder}",
            });

            return (query, tempTypeId);
        }
    }
}
