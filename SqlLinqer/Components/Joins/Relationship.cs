using System.Text;
using System.Collections.Generic;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core.Abstract;
using System.Linq;

namespace SqlLinqer.Components.Joins
{
    public class Relationship
    {
        public readonly RelationshipType Type;
        public readonly Column ParentCol;
        public readonly Column ChildCol;
        public readonly Column DataTarget;
        public readonly Column LinkCol;
        public readonly List<Join> Joins;

        public Table ParentTable => ParentCol?.Table;
        public Table ChildTable => ChildCol?.Table;
        public Table DataTargetTable => DataTarget?.Table;
        public Join ParentTableJoin => Joins.First(x => x.Parent.UUID == ParentTable.UUID);
        public AutoOptions AutoOptions => (DataTarget as ReflectedColumn)?.AutoOptions;

        public Relationship(RelationshipType type, Column parent_col, Column child_col, Column data_target, Column link_col)
        {
            Type = type;
            ParentCol = parent_col;
            ChildCol = child_col;
            DataTarget = data_target;
            LinkCol = link_col;
            Joins = new List<Join>();
        }

        public List<Join> GetReversedJoins()
        {
            var reversed_joins = new List<Join>();

            foreach (var join in Joins)
            {
                var reversed = join.GetReversed();
                reversed_joins.Add(reversed);
            }

            reversed_joins = reversed_joins
                .OrderByDescending(x => x.Order)
                .ToList();

            return reversed_joins;
        }

        public void Render(JoinQuery query, ParameterCollection parameters, DbFlavor flavor, StringBuilder builder)
        {
            foreach (var join in Joins)
            {
                join.Render(query, parameters, flavor, builder);
            }
        }

        public override string ToString()
        {
            return $"{ParentCol} <{Type}> {ChildCol}";
        }
    }
}