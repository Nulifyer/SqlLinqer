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
    public class JoinCollection : KeyedComponentCollectionBase<string, Table>
    {
        protected DirectJoinCollection DirectJoins;
        protected Table Root;

        public JoinCollection() : base()
        {
            DirectJoins = new DirectJoinCollection();
        }

        public void AddJoinDirect(Join join)
        {
            DirectJoins.AddJoin(join);
        }
        public void AddJoinsDirect(IEnumerable<Join> joins)
        {
            DirectJoins.AddJoins(joins);
        }
        public void AddRootTable(Table root)
        {
            Root = root;
            AddTable(Root);
        }
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

        public override bool Stash()
        {
            if (base.Stash())
            {
                DirectJoins.Stash();
                return true;
            }
            return false;
        }
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