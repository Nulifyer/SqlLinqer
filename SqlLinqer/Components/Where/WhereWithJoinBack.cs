using System.Text;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Joins;
using SqlLinqer.Components.Withs;
using SqlLinqer.Queries.Core;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Extensions.StringExtensions;
using System.Linq;
using System.Collections.Generic;
using SqlLinqer.Extensions.IEnumerableExtentions;

namespace SqlLinqer.Components.Where
{
    /// <summary>Filters parent rows through a subquery over related tables.</summary>
    public class WhereWithJoinBack : WhereStatement
    {
        /// <summary>Whether the relationship subquery excludes matching parent rows.</summary>
        public readonly bool Inverted;
        /// <summary>The parent query filtered by the subquery.</summary>
        public readonly WhereQuery ParentQuery;
        /// <summary>The column connecting subquery results back to the parent.</summary>
        public readonly Column ReturnColumn;
        /// <summary>The relationship subquery used to filter parent rows.</summary>
        public readonly SelectQuery SubQuery;

        /// <summary>Creates a relationship subquery that includes or excludes matching parent rows.</summary>
        public WhereWithJoinBack(WhereQuery parent_query, Relationship relationship, bool inverted)
        {
            // setup the stuff needed for the with
            Inverted = inverted;
            ParentQuery = parent_query;
            ReturnColumn = relationship.LinkCol;
            SubQuery = new SelectQuery(relationship.LinkCol.Table, parent_query.Withs);

            Setup();
        }
        /// <summary>Creates a relationship subquery that includes or excludes matching parent rows.</summary>
        public WhereWithJoinBack(WhereQuery parent_query, Table table, bool invert)
        {
            if (table.PrimaryKey == null)
                throw new System.FormatException($"Can not build '{nameof(WhereWithJoinBack)}'. Table has no primary key.");

            // setup the stuff needed for the with
            Inverted = invert;
            ParentQuery = parent_query;
            ReturnColumn = table.PrimaryKey;
            SubQuery = new SelectQuery(table, parent_query.Withs);

            Setup();
        }
        private void Setup()
        {
            SubQuery.IsCompilationCopy = ParentQuery.IsCompilationCopy;
            SubQuery.DisableWiths();
            SubQuery.AddTableAsRoot();

            if (SubQuery.Table.ParentRelationship != null)
            {
                var rootTableJoin = SubQuery.Table.ParentRelationship.Joins.First(x => x.Child.UUID == SubQuery.Table.UUID);
                if (rootTableJoin.AdditionalCondition.Count > 0)
                {
                    foreach (var com in rootTableJoin.AdditionalCondition.GetAll())
                    {
                        var cols = com.GetColumnReferences();
                        bool allAreSameOrChildren = cols
                            .Select(x => x.Table)
                            .DistinctBy(x => x.UUID)
                            .All(x => x.IsSameOrChildOf(SubQuery.Table));

                        if (allAreSameOrChildren)
                        {
                            SubQuery.RootWhere.AddComponent(com);
                        }
                    }
                }
            }

            // select the DISTINCT parent col of the relationship
            // the joins are automatically going to add what is needed
            SubQuery.Distinct = true;
            SubQuery.SelectColumn(ReturnColumn, enable_sub_queries: false);
        }

        private WhereWithJoinBack(WhereWithJoinBack source)
        {
            Inverted = source.Inverted;
            ParentQuery = source.ParentQuery;
            ReturnColumn = source.ReturnColumn;
            SubQuery = new SelectQuery(source.SubQuery.Table, ParentQuery.Withs);
            SubQuery.IsCompilationCopy = ParentQuery.IsCompilationCopy;
            SubQuery.DisableWiths();
            SubQuery.AddTableAsRoot();
            SubQuery.Distinct = source.SubQuery.Distinct;
            SubQuery.RootWhere.ImportFrom(source.SubQuery.RootWhere);
            foreach (var select in source.SubQuery.Selects.GetAll())
                SubQuery.Selects.AddStatement(select);
            foreach (var table in source.SubQuery.Joins.GetAll())
                SubQuery.Joins.AddTable(table);
        }

        internal WhereWithJoinBack CopyForFinalize() => new WhereWithJoinBack(this);

        /// <summary>The preparation order used to sort predicates.</summary>
        public override int Order => 150;

        /// <summary>Throws because join-back predicates do not support direct inversion.</summary>
        /// <exception cref="System.NotSupportedException">Always thrown.</exception>
        public override IWhereComponent Invert() => throw new System.NotSupportedException();
        /// <inheritdoc/>
        public override IWhereComponent Finalize(WhereQuery query) => this;
        /// <inheritdoc/>
        public override bool TryCombineInto(WhereCollection current_collection, IWhereComponent parent_component)
        {
            if (
                parent_component is WhereWithJoinBack parent_where_with
                && this.Inverted == parent_where_with.Inverted
                && this.SubQuery.Table.IsSameOrChildOf(parent_where_with.SubQuery.Table)
            )
            {
                var wrap_collection = new WhereCollection(current_collection.WhereOp);
                wrap_collection.AddComponent(parent_where_with.SubQuery.RootWhere);
                wrap_collection.AddComponent(this.SubQuery.RootWhere);
                parent_where_with.SubQuery.RootWhere = wrap_collection;
                return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public override void Render(WhereQuery query, ParameterCollection parameters, DbFlavor flavor, StringBuilder builder)
        {
            var withQuery = SubQuery;
            if (Inverted)
            {
                withQuery = new WhereWithJoinBack(this).SubQuery;
                withQuery.RootWhere = withQuery.RootWhere.Invert();
            }
            var with = new With(withQuery, ParentQuery.Withs.NextFilterAlias());
            ParentQuery.Withs.AddWith(with);

            var join = new Join(query.Table, with.OutputTable);
            var output_col = with.OutputTable.Columns.GetAll().First();
            var parent_col = ReturnColumn.Table.ParentRelationship?.ParentCol ?? ReturnColumn;
            join.Condition.AddComponent(new WhereTwoColumn(output_col, parent_col, SqlOp.EQ));
            ParentQuery.Joins.AddJoinDirect(join);
            ParentQuery.Joins.AddTable(parent_col.Table);

            var newCondition = new WhereColumnValue(output_col, null, Inverted ? SqlOp.EQ : SqlOp.NOT, SqlSubQueryOp.ANY);
            newCondition.Render(query, parameters, flavor, builder);
        }

        /// <inheritdoc/>
        public override IEnumerable<Column> GetColumnReferences()
        {
            yield break;
        }
    }
}
