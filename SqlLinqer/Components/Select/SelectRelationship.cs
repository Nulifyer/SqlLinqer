using System;
using System.Collections.Generic;
using System.Linq;
using SqlLinqer.Components.Joins;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Select
{
    /// <summary>A nested SELECT projection for a mapped relationship.</summary>
    public class SelectRelationship : SelectStatement
    {
        /// <summary>The relationship whose data is selected.</summary>
        public readonly Relationship Relationship;
        /// <summary>The query defining the nested related data.</summary>
        public readonly SelectQuery SubQuery;

        private string _alias;
        /// <inheritdoc/>
        public override string Alias => _alias ?? Relationship.DataTarget.Alias;

        /// <summary>Creates a nested relationship projection on the parent query.</summary>
        public SelectRelationship(SelectQuery parent_query, Relationship relationship, string alias = null)
        {
            _alias = alias;
            Relationship = relationship;
            SubQuery = new SelectQuery(Relationship.ChildTable, parent_query.Withs);
            SubQuery.DisableWiths();
            SubQuery.AddTableAsRoot();

            if (Relationship.Type == RelationshipType.OneToOne)
                SubQuery.PagingControls.SetTop(1);

            var joins = Relationship.GetReversedJoins()
                .Where(x => x.Child.IsSameOrAlsoUnderSameParent(SubQuery.Table));
            SubQuery.Joins.AddJoinsDirect(joins);
            SubQuery.RootWhere.AddComponent(Relationship.ParentTableJoin.Condition);
        }

        /// <inheritdoc/>
        public override string Render(SelectQuery query, ParameterCollection parameters, DbFlavor flavor, bool forJson, bool include_alias)
        {
            // Relationships remain JSON-valued columns even in tabular projections.
            string sql = SubQuery.Render(parameters, flavor, true);
            return SqlLinqer.Compilation.SqlDialect.For(flavor).Relationship(sql, Alias,
                Relationship.Type != RelationshipType.OneToOne, forJson, include_alias);
        }

        /// <inheritdoc/>
        public override bool DoGroupBy() => true;

        /// <inheritdoc/>
        public override IEnumerable<string> RenderForGroupBy(SelectQuery query, ParameterCollection parameters, DbFlavor flavor)
            => SubQuery.RootWhere.GetColumnReferences()
                .Where(column => !SubQuery.Joins.ContainsLocalTable(column.Table))
                .Select(column => column.Render(flavor))
                .Distinct();
    }
}
