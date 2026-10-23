using System;
using System.Collections.Generic;
using SqlLinqer.Components.Joins;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Where;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Select
{
    /// <summary>A per-parent collection aggregate evaluated independently of other outer query joins.</summary>
    public sealed class SelectRelatedAggregate : SelectStatement
    {
        private readonly SelectQuery child;
        private readonly Relationship relationship;
        private readonly string alias;

        /// <summary>Creates an independent collection count or sum, returning zero for an empty collection.</summary>
        /// <param name="relationship">A direct mapped one-to-many relationship, optionally reached through single-object relationships.</param>
        /// <param name="function">COUNT_BIG for child row count, or SUM for a numeric child column.</param>
        /// <param name="alias">The result member name.</param>
        /// <param name="value">The child column to sum. Null for a row count.</param>
        /// <param name="filter">Optional additional predicates bound to the relationship's child table.</param>
        public SelectRelatedAggregate(Relationship relationship, SqlFunc function, string alias, Column value = null, IWhereComponent filter = null)
        {
            this.relationship = relationship ?? throw new ArgumentNullException(nameof(relationship));
            this.alias = alias ?? throw new ArgumentNullException(nameof(alias));
            if (relationship.Type == RelationshipType.OneToOne || relationship.Joins.Count != 1)
                throw new NotSupportedException("Related aggregates require a direct mapped one-to-many relationship.");
            if (function != SqlFunc.COUNT_BIG && function != SqlFunc.SUM)
                throw new ArgumentException("Related aggregates support COUNT_BIG and SUM.", nameof(function));
            if (function == SqlFunc.SUM && (value == null || value.Table.UUID != relationship.ChildTable.UUID))
                throw new ArgumentException("The sum column must belong to the collection's child table.", nameof(value));
            child = new SelectQuery(relationship.ChildTable);
            child.AddTableAsRoot();
            child.DisableWiths();
            child.RootWhere.AddComponent(relationship.ParentTableJoin.Condition);
            child.RootWhere.AddComponent(relationship.ParentTableJoin.AdditionalCondition);
            if (filter != null) child.RootWhere.AddComponent(filter);
            child.Selects.AddStatement(new SelectFuncStatement(function, "value", function == SqlFunc.COUNT_BIG ? (object)1 : value));
        }

        /// <inheritdoc/>
        public override string Alias => alias;
        /// <inheritdoc/>
        public override bool DoGroupBy() => true;
        /// <inheritdoc/>
        public override IEnumerable<string> RenderForGroupBy(SelectQuery query, ParameterCollection parameters, DbFlavor flavor)
        {
            query.Joins.AddTable(relationship.ParentTable);
            yield return relationship.ParentCol.Render(flavor);
        }
        /// <inheritdoc/>
        public override string Render(SelectQuery query, ParameterCollection parameters, DbFlavor flavor, bool for_json, bool include_alias)
        {
            query.AddTableAsRoot();
            query.Joins.AddTable(relationship.ParentTable);
            string value = "COALESCE((" + child.Render(parameters, flavor, false) + "), 0)";
            if (for_json && flavor != DbFlavor.SqlServer && flavor != DbFlavor.AzureSql)
                return alias.DbWrapString(flavor) + ", " + value;
            return value + (include_alias ? " AS " + alias.DbWrap(flavor) : "");
        }
    }
}
