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
    public class SelectRelationship : SelectStatement
    {
        public readonly Relationship Relationship;
        public readonly SelectQuery SubQuery;

        private string _alias;
        public override string Alias => _alias ?? Relationship.DataTarget.Alias;

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
            => new[] { Relationship.ParentCol.Render(flavor) };
    }
}
