using System;
using System.Reflection;
using SqlLinqer.Components.Joins;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Where;
using SqlLinqer.Extensions.MemberInfoExtensions;
using SqlLinqer.Extensions.TypeExtensions;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Used to label one to many realtionships
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
        /// Marks property/field as a one to many relationship
        /// </summary>
        /// <param name="joiningForeignKey">The name of joined class's property/field where the current class's primary key is the foreign key</param>
        public SqlOneToMany(string joiningForeignKey) : base(RelationshipType.OneToMany)
        {
            JoiningForeignKey = joiningForeignKey;
        }

        /// <summary>
        /// Marks property/field as a one to many relationship
        /// </summary>
        /// <param name="localPrimaryKey">The name of the base class's property/field that behaves as the primary key</param>
        /// <param name="joiningForeignKey">The name of joined class's property/field where the current class's primary key is the foreign key</param>
        public SqlOneToMany(string localPrimaryKey, string joiningForeignKey) : base(RelationshipType.OneToMany)
        {
            LocalPrimaryKey = localPrimaryKey;
            JoiningForeignKey = joiningForeignKey;
        }

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

            var related_table = new Table(related_type);

            var fkColumn = related_table.Columns.TryGet(JoiningForeignKey);
            if (fkColumn == null)
                throw new FormatException($"One to Many relationship failed to find foreign key '{JoiningForeignKey}' column on type {related_type.Name}");

            var dataTarget = new ReflectedColumn(currentTable, unprocessed.UnprocessedMember);
            var relationship = new Relationship(unprocessed.RelationshipType, primaryKey, fkColumn, dataTarget, fkColumn);

            var join = new Join(currentTable, related_table);
            join.Condition.AddComponent(new WhereTwoColumn(primaryKey, fkColumn, SqlOp.EQ));

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
