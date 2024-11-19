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
            string rendered_query = SubQuery.Render(parameters, flavor, forJson);
            rendered_query = $"({rendered_query})";

            // if is FOR JSON and is OneToOne, run JSON_QUERY to pull first element from array
            if (forJson)
            {
                switch (flavor)
                {
                    case DbFlavor.PostgreSql:
                    case DbFlavor.MySql:
                        return $"{Alias.DbWrapString(flavor)}, {rendered_query}";
                    case DbFlavor.SqlServer:
                        if (Relationship.Type == RelationshipType.OneToOne)
                        {
                            rendered_query = $"JSON_QUERY({rendered_query}, '$[0]')";
                        }
                        break;
                    default:
                        throw new NotSupportedException($"The {nameof(SqlLinqer.DbFlavor)} {flavor} is not supported by {nameof(SelectRelationship)}.{nameof(Render)}");
                }
            }

            string alias = include_alias ? $" AS {Alias.DbWrap(flavor)}" : null;
            return $"{rendered_query}{alias}";
        }

        /// <inheritdoc/>
        public override bool DoGroupBy() => true;

        /// <inheritdoc/>
        public override IEnumerable<string> RenderForGroupBy(SelectQuery query, ParameterCollection parameters, DbFlavor flavor)
            => new[] { Relationship.ParentCol.Render(flavor) };
    }
}