using System;
using System.Collections;
using System.Reflection;
using SqlLinqer.Components.Joins;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Where;
using SqlLinqer.Extensions.MemberInfoExtensions;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Maps a single related object through a local foreign key. A selected relationship loads null when no related row matches.
    /// Select the relationship explicitly or include it with SqlAutoSelect. Persist relationship changes through foreign keys or linking rows.
    /// </summary>
    public sealed class SqlOneToOne : RelationshipAttribute
    {
        /// <summary>
        /// The name of the base class's property/field that behaves as the foreign key
        /// </summary>
        public readonly string LocalForeignKey;

        /// <summary>
        /// The name of the base class's property/field that behaves as the foreign key
        /// </summary>
        public readonly string JoiningPrimaryKey;

        /// <summary>
        /// Maps a single related object through a local foreign key. A selected relationship loads null when no related row matches.
        /// </summary>
        /// <param name="localForeignKey">The CLR member name of the foreign key on the root model, usually supplied with nameof.</param>
        public SqlOneToOne(string localForeignKey) : base(RelationshipType.OneToOne)
        {
            LocalForeignKey = localForeignKey;
        }

        /// <summary>
        /// Maps a single related object through a local foreign key. A selected relationship loads null when no related row matches.
        /// </summary>
        /// <param name="localForeignKey">The CLR member name of the foreign key on the root model, usually supplied with nameof.</param>
        /// <param name="joiningPrimaryKey">The related model's CLR key member name. The one-argument overload uses its SqlPrimaryKey member.</param>
        public SqlOneToOne(string localForeignKey, string joiningPrimaryKey) : base(RelationshipType.OneToOne)
        {
            LocalForeignKey = localForeignKey;
            JoiningPrimaryKey = joiningPrimaryKey;
        }

        /// <inheritdoc/>
        public override Relationship ProcessRelationship(Table currentTable, UnprocessedRelationshipMember unprocessed)
        {
            var col_type = unprocessed.UnprocessedMember.Member.GetFieldOrPropValueType();
            if (typeof(IEnumerable).IsAssignableFrom(col_type) && col_type != typeof(string))
            {
                throw new FormatException($"One to One relationship error. '{unprocessed.ParentType.Name}.{unprocessed.UnprocessedMember.Member.Name}' should be a single instance not enumerable.");
            }

            var related_table = new Table(col_type, currentTable.DefaultSchema);

            ReflectedColumn primaryKey = null;
            if (JoiningPrimaryKey != null)
            {
                primaryKey = related_table.Columns.TryGet(JoiningPrimaryKey) as ReflectedColumn;
                if (primaryKey == null)
                    throw new FormatException($"One to One relationship error. '{unprocessed.ChildType.Name}' does not have a column '{JoiningPrimaryKey}'.");
            }
            else
            {
                primaryKey = related_table.PrimaryKey as ReflectedColumn;
                if (primaryKey == null)
                    throw new FormatException($"One to One relationships require the joined table to have a primary key. '{unprocessed.ChildType.Name}' does not have a primary key.");
            }

            var fkColumn = currentTable.Columns.TryGet(LocalForeignKey);
            if (fkColumn == null)
                throw new FormatException($"One to One relationship failed to find foreign key '{LocalForeignKey}' column on type {unprocessed.ParentType.Name}");

            var dataTarget = new ReflectedColumn(currentTable, unprocessed.UnprocessedMember);
            var relationship = new Relationship(unprocessed.RelationshipType, fkColumn, primaryKey, dataTarget, primaryKey);

            var join = new Join(currentTable, related_table);
            join.Condition.AddComponent(new WhereTwoColumn(fkColumn, primaryKey, SqlOp.EQ));

            relationship.Joins.Add(join);

            var additionalConditions = unprocessed.UnprocessedMember.Member.GetCustomAttributes<SqlCondition>();
            foreach (var condition in additionalConditions)
            {
                if (condition.ApplyToIdx < 0 || condition.ApplyToIdx > relationship.Joins.Count - 1)
                    throw new IndexOutOfRangeException($"Invalid join index when processing additional condittions for table relationship between '{relationship.ParentTable.Name}' - '{relationship.ChildTable.Name}'.");
                var apply_to_join = relationship.Joins[condition.ApplyToIdx];
                if (condition.Override == true)
                    apply_to_join.Condition.Clear();
                condition.AddToJoin(relationship, apply_to_join);
            }

            related_table.ParentRelationship = relationship;

            return relationship;
        }
    }
}
