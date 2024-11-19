using System;
using System.Linq;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Select;
using SqlLinqer.Components.Ordering;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Withs;
using SqlLinqer.Components.Generic;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Components.Joins;
using System.Collections.Generic;
using System.Data;

namespace SqlLinqer.Queries.Core
{
    /// <summary>
    /// A SELECT query. Based around a root <see cref="Table"/>.
    /// </summary>
    public class SelectQuery : WhereQuery
    {
        /// <summary>
        /// If this is a sub query
        /// </summary>
        protected internal bool IsSubQuery;
        /// <summary>
        /// If to do DISTINCT
        /// </summary>
        protected internal bool Distinct;
        /// <summary>
        /// The select statements
        /// </summary>
        protected internal SelectCollection Selects;
        /// <summary>
        /// The order by statements
        /// </summary>
        protected internal OrderByCollection OrderBys;
        /// <summary>
        /// The paging controls (ex. TOP, LIMIT, OFFSET, etc)
        /// </summary>
        protected internal PagingControls PagingControls;

        /// <summary>
        /// A SELECT query. Based around a root <see cref="Table"/>.
        /// </summary>
        /// <param name="table">The root table of the query</param>
        public SelectQuery(Table table) : base(table)
        {
            IsSubQuery = false;
            Distinct = false;
            Selects = new SelectCollection();
            OrderBys = new OrderByCollection();
            PagingControls = new PagingControls();
        }
        internal SelectQuery(Table table, WithCollection root_with_collection) : this(table)
        {
            IsSubQuery = true;
            this.Withs = root_with_collection;
        }

        /// <summary>
        /// Adds the queries target table as the root table.
        /// </summary>
        protected internal void AddTableAsRoot()
        {
            Joins.AddRootTable(Table);
        }

        /// <summary>
        /// Add select statements to the query.
        /// The selected columns are based off the <see cref="SqlLinqer.Modeling.SqlAutoSelect"/> 
        /// and <see cref="SqlLinqer.Modeling.SqlAutoSelectExclude"/> attributes.
        /// </summary>
        /// <param name="recursive">If to do a full select auto recursively.</param>
        /// <param name="select_relationships">If to only select the relationships along with the auto columns.</param>
        protected internal void SelectAuto(bool recursive, bool select_relationships)
        {
            // find auto select cols
            var auto_select_cols = Table.Columns.GetAll<ReflectedColumn>()
                .Where(x => x.AutoOptions.Select);

            // add auto select cols to query
            foreach (var column in auto_select_cols)
                SelectColumn(column);

            if (select_relationships)
            {
                // find auto select relationships
                var auto_relationships = Table.Relationships.GetAll()
                    .Where(x => x.AutoOptions?.Select == true);

                // get current selected relationships
                var current_selected_relationshps_lk = Selects
                    .GetAll<SelectRelationship>()
                    .ToDictionary(x => x.Alias);

                // add relationships to query
                foreach (var rel in auto_relationships)
                {
                    SelectRelationship select_relationship;

                    // check existing
                    if (current_selected_relationshps_lk.ContainsKey(rel.DataTarget.Alias))
                    {
                        // get existing
                        select_relationship = current_selected_relationshps_lk[rel.DataTarget.Alias];
                    }
                    else
                    {
                        this.AddTableAsRoot();

                        // create new
                        select_relationship = new SelectRelationship(this, rel);
                        Selects.AddStatement(select_relationship);
                    }

                    select_relationship.SubQuery.SelectAuto(recursive, select_relationships: recursive);
                }
            }
        }

        /// <summary>
        /// Add select statements to the query.
        /// The selected columns are only the columns defined on the root model of the query.
        /// </summary>
        protected internal void SelectRootColumns()
        {
            foreach (var column in Table.Columns.GetAll())
            {
                SelectColumn(column);
            }
        }

        /// <summary>
        /// Add a column to the select. 
        /// If sub queries are enabled instead of joining tables to the root, they are created as sub queries to those tables recursively.
        /// If sub queries are disabled then the column's table and related parent tables are joined to the root query directly.
        /// </summary>
        /// <param name="column">The column to add to the select.</param>
        /// <param name="output_alias">The name of the output of the sub query.</param>
        /// <param name="enable_sub_queries">Enable/Disable sub queries</param>
        protected internal void SelectColumn(Column column, string output_alias = null, bool enable_sub_queries = true)
        {
            if (column == null)
                throw new ArgumentNullException(nameof(column));

            var path = column.GetPath(Table);
            if (!enable_sub_queries || path.Count == 1)
            {
                Selects.AddStatement(new SelectColumn(column, output_alias));
            }
            else if (path.Count > 1)
            {
                // check for existing sub query for path
                var sub_query_select = Selects
                    .GetAll<SelectRelationship>()
                    .FirstOrDefault(x => x.Relationship.DataTarget.Alias == path.First());

                // sub query not found, create
                if (sub_query_select == null)
                {
                    var relationship = Table.Relationships.GetAll().FirstOrDefault(x => x.DataTarget.Alias == path.First());
                    if (relationship == null)
                        throw new SqlLinqer.Exceptions.SqlPathNotFoundException(Table, path);

                    this.AddTableAsRoot();
                    sub_query_select = new SelectRelationship(this, relationship, output_alias);
                    Selects.AddStatement(sub_query_select);
                }

                sub_query_select.SubQuery.SelectColumn(column);
            }
            else
            {
                throw new FormatException("Invalid Column Path.");
            }
        }

        /// <summary>
        /// Add a relationship to the select as a set of sub queries.
        /// </summary>
        /// <param name="relationship">The relationship to add to the select.</param>
        /// <param name="output_alias">The name of the output of the sub query.</param>
        protected internal void SelectRelationship(Relationship relationship, string output_alias = null)
        {
            if (relationship == null)
                throw new ArgumentNullException(nameof(relationship));

            foreach (var col in relationship.ChildTable.Columns.GetAll())
            {
                SelectColumn(col, output_alias: output_alias);
            }
        }

        private IEnumerable<ReflectedColumn> GetTableAutoOrderCols(Table table, bool relationships = true)
        {
            var cols = new List<ReflectedColumn>();

            if (
                relationships
                && table.ParentRelationship.DataTarget is ReflectedColumn rCol
                && rCol.AutoOptions.OrderBy
            )
            {
                cols.AddRange(table.ParentRelationship.Joins
                    .Where(j => j.Child.UUID != table.UUID)
                    .SelectMany(j => GetTableAutoOrderCols(j.Child, false)));
            }

            cols.AddRange(table.Columns.GetAll<ReflectedColumn>()
                .Where(x => x.AutoOptions.OrderBy)
                .OrderBy(x => x.AutoOptions.OrderByInfo.Order));

            return cols;
        }
        private void OrderByAuto(bool recursive, bool include_parentrel_join_orderbys = false)
        {
            // find auto order cols
            var auto_order_cols = GetTableAutoOrderCols(Table, include_parentrel_join_orderbys);

            if (auto_order_cols.Count() > 0)
            {
                OrderBys.Clear();
                foreach (var col in auto_order_cols)
                {
                    ThenBy(col, col.AutoOptions.OrderByInfo.Dir, !IsSubQuery, true);
                }
            }

            if (recursive)
            {
                var current_selected_relationshps = Selects
                    .GetAll<SelectRelationship>();
                foreach (var select in current_selected_relationshps)
                {
                    select.SubQuery.OrderByAuto(recursive, true);
                }
            }
        }
        protected internal void OrderByAuto(bool recursive)
        {
            OrderByAuto(recursive, false);
        }
        protected internal void OrderBy(Column column, SqlDir dir, bool enable_sub_queries, bool do_uuid_alias)
        {
            OrderBys.Clear();
            ThenBy(column, dir, enable_sub_queries, do_uuid_alias);
        }
        protected internal void ThenBy(Column column, SqlDir dir, bool enable_sub_queries, bool do_uuid_alias)
        {
            var path = column.GetPath(Table);
            var rel_many = enable_sub_queries
                ? Table.FindFirstManyRelationshipFromPath(path)
                : null;
            if (rel_many != null)
            {
                OrderBys.AddStatement(new SubQueryOrderBy(rel_many, column, dir));
            }
            else
            {
                if (do_uuid_alias)
                {
                    Column new_col = column;
                    var stm = Selects.TryGet(new_col.Alias);
                    if (stm == null || (stm is SelectColumn colStm && colStm.Column.UUID != new_col.UUID))
                    {
                        new_col = column.WithAliasAsUUID();
                        SelectColumn(new_col, enable_sub_queries: false);
                    }
                    OrderBys.AddStatement(new ColumnOrderBy(new_col, dir));
                }
                else
                {
                    OrderBys.AddStatement(new ColumnOrderBy(column, dir));
                }
            }
        }

        /// <inheritdoc/>
        public override string Render(ParameterCollection parameters, DbFlavor flavor)
        {
            return Render(parameters, flavor, true);
        }
        /// <summary>
        /// Renders the query into a string.
        /// </summary>
        /// <param name="parameters">The parameter collection to use</param>
        /// <param name="flavor">The type of Sql database being used</param>
        /// <param name="forJson">Include the FOR JSON statement at the end</param>
        public string Render(ParameterCollection parameters, DbFlavor flavor, bool forJson)
        {
            var stashables = new IStashable[]
            {
                this.RootWhere,
                this.Joins,
                this.Selects,
                this.OrderBys,
                this.PagingControls,
            };

            foreach (var item in stashables)
                item?.Stash();

            if (!IsSubQuery)
                this.Withs?.Stash();

            if (this.PagingControls.Limit > 0 && this.OrderBys.Count == 0)
            {
                var column = Table.PrimaryKey ?? Table.Columns.GetAll().First();
                OrderBy(column, SqlDir.ASC, true, true);
            }

            string where = this.RootWhere.Render(this, parameters, flavor);            
            string select = this.Selects.Render(this, parameters, flavor, forJson);
            string with = !IsSubQuery
                ? this.Withs?.Render(this, parameters, flavor)
                : null;
            string groupBy = this.Selects.RenderGroupBy(this, parameters, flavor);
            string joins = this.Joins.Render(this, parameters, flavor);
            string order_by = this.OrderBys.Render(this, parameters, flavor);
            string paging_controls = this.PagingControls.Render(flavor);

            foreach (var item in stashables)
                item?.Unstash();

            if (!IsSubQuery)
                this.Withs?.Unstash();

            return string.Join(" ", new[]
            {
                with,
                $"SELECT{(Distinct ? " DISTINCT" : null)}{(PagingControls.Top > 0 ? $" TOP {PagingControls.Top}" : null)}",
                select,
                joins,
                where,
                groupBy,
                order_by,
                paging_controls,
                (forJson && flavor == DbFlavor.SqlServer ? "FOR JSON AUTO, INCLUDE_NULL_VALUES" : null),
            });
        }

        /// <summary>
        /// Renders the query into a string that returns the total number of restuls.
        /// </summary>
        /// <param name="parameters">The parameter collection to use</param>
        /// <param name="flavor">The type of Sql database being used</param>
        public string RenderForCount(ParameterCollection parameters, DbFlavor flavor)
        {
            var stashables = new IStashable[]
            {
                this.RootWhere,
                this.Joins,
                this.Selects,
                this.OrderBys,
                this.PagingControls,
            };

            foreach (var item in stashables)
                item?.Stash();

            if (!IsSubQuery)
                this.Withs?.Stash();

            // render to setup joins
            this.RootWhere.Render(this, parameters, flavor);
            this.Selects.Render(this, parameters, flavor, false);
            
            // clear non-needed stuff
            this.PagingControls.Reset();
            this.Selects.Clear();
            this.OrderBys.Clear();

            // add count statement
            this.Selects.AddStatement(new SelectFuncStatement(SqlFunc.COUNT, StringExtensions.GetRandomHashString(), 1));
            
            // render count query
            string rendered = Render(parameters, flavor, false);

            foreach (var item in stashables)
                item?.Unstash();

            if (!IsSubQuery)
                this.Withs?.Unstash();

            return rendered;
        }
    }
}