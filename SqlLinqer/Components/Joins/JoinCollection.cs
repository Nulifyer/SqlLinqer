using System.Linq;
using System.Text;
using System.Collections.Generic;
using SqlLinqer.Components.Generic;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Extensions.TypeExtensions;

namespace SqlLinqer.Components.Joins
{
    /// <summary>Collects explicit and relationship joins required by a query.</summary>
    public class JoinCollection : KeyedComponentCollectionBase<string, Table>
    {
        /// <summary>Explicit joins supplied to the query.</summary>
        protected DirectJoinCollection DirectJoins;
        /// <summary>The root table from which relationship joins are discovered.</summary>
        protected Table Root;

        /// <summary>Creates an empty join collection.</summary>
        public JoinCollection() : base()
        {
            DirectJoins = new DirectJoinCollection();
        }

        /// <summary>Adds an explicit join.</summary>
        public void AddJoinDirect(Join join)
        {
            DirectJoins.AddJoin(join);
        }
        /// <summary>Adds the supplied explicit joins.</summary>
        public void AddJoinsDirect(IEnumerable<Join> joins)
        {
            DirectJoins.AddJoins(joins);
        }
        /// <summary>Sets the root table used to discover relationship joins.</summary>
        public void AddRootTable(Table root)
        {
            Root = root;
            AddTable(Root);
        }
        /// <summary>Registers a table and the parent tables needed to join it.</summary>
        public void AddTable(Table table)
        {
            var current = table;
            while (current != null)
            {
                if (Root != null && !current.IsSameOrChildOf(Root))
                    break;

                AddComponent(current.UUID, current);
                current = current.ParentRelationship?.ParentTable;
            }
        }

        internal bool HasJoinedTables(Table root)
        {
            return GetAll().Any(x => x.UUID != root.UUID)
                || DirectJoins.GetAll().Any(x => x.Child.UUID != root.UUID);
        }

        internal bool ContainsLocalTable(Table table)
            => HasKey(table.UUID) || DirectJoins.GetAll().Any(x => x.Child.UUID == table.UUID);

        /// <summary>Renders this component for the selected database dialect.</summary>
        public string Render(JoinQuery query, ParameterCollection parameters, DbFlavor flavor)
        {
            var joins = GetAll()
                .OrderBy(x => x.Order)
                .SelectMany(x => x.ParentRelationship?.Joins.ToArray() ?? new[] { new Join(x) })
            ;

            foreach (var join in joins)
            {
                DirectJoins.AddJoin(join);
            }
            joins = DirectJoins.GetAll()
                .OrderBy(x =>
                {
                    if (x.Child.UUID == query.Table.UUID)
                        return 0;
                    else if (x.Child.ParentRelationship == null)
                        return 2;
                    else
                        return 1;
                });

            if (joins.Count() == 0)
                return null;

            var builder = new StringBuilder();
            builder.Append("FROM ");

            foreach (var join in joins)
            {
                join.Render(query, parameters, flavor, builder);
            }

            return builder.ToString();
        }

        /// <inheritdoc/>
        public override bool Stash()
        {
            if (base.Stash())
            {
                DirectJoins.Stash();
                return true;
            }
            return false;
        }
        /// <inheritdoc/>
        public override bool Unstash()
        {
            if (base.Unstash())
            {
                DirectJoins.Unstash();
                return true;
            }
            return false;
        }
    }
}
