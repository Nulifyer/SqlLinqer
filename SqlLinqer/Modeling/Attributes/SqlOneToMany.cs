using System;
using SqlLinqer.Components.Joins;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Where;
using SqlLinqer.Extensions.MemberInfoExtensions;
using SqlLinqer.Extensions.TypeExtensions;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Maps a related collection through the child model's foreign key. A selected relationship loads an empty collection when no rows match.
    /// Select the relationship explicitly or include it with SqlAutoSelect. Persist relationship changes through foreign keys or linking rows.
    /// </summary>
    public sealed class SqlOneToMany : RelationshipAttribute
    {
        /// <summary>
        /// The name of the base class's property/field that behaves as the primary key
        /// </summary>
        public readonly string LocalPrimaryKey;

        /// <summary>
        /// The name of joined class's property/field where the current class's primary key is the foreign key
        /// </summary>
        public readonly string JoiningForeignKey;

        /// <summary>
        /// Maps a related collection through the child model's foreign key. A selected relationship loads an empty collection when no rows match.
        /// </summary>
        /// <param name="joiningForeignKey">The child model's CLR foreign-key member name, usually supplied with nameof.</param>
        public SqlOneToMany(string joiningForeignKey) : base(RelationshipType.OneToMany)
        {
            JoiningForeignKey = joiningForeignKey;
        }

        /// <summary>
        /// Maps a related collection through the child model's foreign key. A selected relationship loads an empty collection when no rows match.
        /// </summary>
        /// <param name="localPrimaryKey">The root model's CLR key member name. The one-argument overload uses its SqlPrimaryKey member.</param>
        /// <param name="joiningForeignKey">The child model's CLR foreign-key member name, usually supplied with nameof.</param>
        public SqlOneToMany(string localPrimaryKey, string joiningForeignKey) : base(RelationshipType.OneToMany)
        {
            LocalPrimaryKey = localPrimaryKey;
            JoiningForeignKey = joiningForeignKey;
        }

        /// <inheritdoc/>
        public override Relationship ProcessRelationship(Table currentTable, UnprocessedRelationshipMember unprocessed)
        {
            ReflectedColumn primaryKey = null;
            if (LocalPrimaryKey != null)
            {
                primaryKey = currentTable.Columns.TryGet(LocalPrimaryKey) as ReflectedColumn;
                if (primaryKey == null)
                    throw new FormatException($"One to Many relationship error. '{unprocessed.ParentType.Name}' does not have a column '{LocalPrimaryKey}'.");
            }
            else
            {
                primaryKey = currentTable.PrimaryKey as ReflectedColumn;
                if (primaryKey == null)
                    throw new FormatException($"One to Many relationships require the base table to have a primary key. '{unprocessed.ParentType.Name}' does not have a primary key.");
            }

            var related_type = unprocessed.UnprocessedMember.Member.GetFieldOrPropValueType();
            related_type = related_type.GetItemTypeFromIEnumerable() ?? related_type;

            var related_table = new Table(related_type, currentTable.DefaultSchema);

            var fkColumn = related_table.Columns.TryGet(JoiningForeignKey);
            if (fkColumn == null)
                throw new FormatException($"One to Many relationship failed to find foreign key '{JoiningForeignKey}' column on type {related_type.Name}");

            var dataTarget = new ReflectedColumn(currentTable, unprocessed.UnprocessedMember);
            var relationship = new Relationship(unprocessed.RelationshipType, primaryKey, fkColumn, dataTarget, fkColumn);

            var join = new Join(currentTable, related_table);
            join.Condition.AddComponent(new WhereTwoColumn(primaryKey, fkColumn, SqlOp.EQ));

            relationship.Joins.Add(join);

            relationship.ApplyConditions(unprocessed.UnprocessedMember.Member);

            related_table.ParentRelationship = relationship;

            return relationship;
        }
    }
}
