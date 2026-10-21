using System.Text;
using System.Collections.Generic;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core.Abstract;
using System.Linq;

namespace SqlLinqer.Components.Joins
{
    /// <summary>Mapped columns and joins connecting a parent model to related data.</summary>
    public class Relationship
    {
        /// <summary>The relationship cardinality and direction.</summary>
        public readonly RelationshipType Type;
        /// <summary>The join column on the parent model.</summary>
        public readonly Column ParentCol;
        /// <summary>The join column on the child model.</summary>
        public readonly Column ChildCol;
        /// <summary>The mapped member that receives the related data.</summary>
        public readonly Column DataTarget;
        /// <summary>The linking column used by a many-to-many relationship.</summary>
        public readonly Column LinkCol;
        /// <summary>The joins needed to traverse the relationship.</summary>
        public readonly List<Join> Joins;

        /// <summary>The table owning the parent join column.</summary>
        public Table ParentTable => ParentCol?.Table;
        /// <summary>The table owning the child join column.</summary>
        public Table ChildTable => ChildCol?.Table;
        /// <summary>The table owning the member that receives related data.</summary>
        public Table DataTargetTable => DataTarget?.Table;
        /// <summary>The join whose parent is the relationship's parent table.</summary>
        public Join ParentTableJoin => Joins.First(x => x.Parent.UUID == ParentTable.UUID);
        /// <summary>Automatic-operation settings for the reflected relationship member, if available.</summary>
        public AutoOptions AutoOptions => (DataTarget as ReflectedColumn)?.AutoOptions;

        /// <summary>Creates a relationship from its mapped columns and cardinality.</summary>
        public Relationship(RelationshipType type, Column parent_col, Column child_col, Column data_target, Column link_col)
        {
            Type = type;
            ParentCol = parent_col;
            ChildCol = child_col;
            DataTarget = data_target;
            LinkCol = link_col;
            Joins = new List<Join>();
        }

        /// <summary>Returns reversed joins ordered from the deepest child toward the parent.</summary>
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

        /// <summary>Renders this component for the selected database dialect.</summary>
        public void Render(JoinQuery query, ParameterCollection parameters, DbFlavor flavor, StringBuilder builder)
        {
            foreach (var join in Joins)
            {
                join.Render(query, parameters, flavor, builder);
            }
        }

        /// <summary>Returns a diagnostic description of this mapping.</summary>
        public override string ToString()
        {
            return $"{ParentCol} <{Type}> {ChildCol}";
        }
    }
}
