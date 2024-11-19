using System;
using System.Linq;
using System.Reflection;
using SqlLinqer.Components.Joins;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Where;
using SqlLinqer.Extensions.MemberInfoExtensions;
using SqlLinqer.Extensions.TypeExtensions;

namespace SqlLinqer.Modeling
{
    /// <summary>
    /// Used to label many to many realtionships
    /// </summary>
    public sealed class SqlBiDirectionalManyToMany : RelationshipAttribute
    {
        internal bool directTargets;

        /// <summary>
        /// The type that models the relational table
        /// </summary>
        public readonly Type JoiningType;
        /// <summary>
        /// The foreign key on the joining type the is the primary key of the left model
        /// </summary>
        public readonly string LeftJoiningKey;
        /// <summary>
        /// The foreign key on the joining type that is the primary key of the right model
        /// </summary>
        public readonly string RightJoiningKey;

        /// <summary>
        /// Used to label many to many realtionships
        /// </summary>
        /// <param name="joiningType">The type that models the relational table</param>
        /// <param name="leftJoiningKey">The foreign key on the joining type the is the primary key of the left model</param>
        /// <param name="rightJoiningKey">The foreign key on the joining type that is the primary key of the right model</param>
        public SqlBiDirectionalManyToMany(Type joiningType, string leftJoiningKey, string rightJoiningKey) : base(RelationshipType.ManyToMany)
        {
            directTargets = false;
            
            JoiningType = joiningType;
            LeftJoiningKey = leftJoiningKey;
            RightJoiningKey = rightJoiningKey;
        }

        /// <summary>
        /// The schema of the relational table
        /// </summary>
        public readonly string JoiningSchema;
        /// <summary>
        /// The name of the relational table
        /// </summary>
        public readonly string JoiningTable;
        /// <summary>
        /// The foreign key on the relational table the is the primary key of the left model
        /// </summary>
        public readonly string LeftForeignKey;
        /// <summary>
        /// The foreign key on the relational table the is the primary key of the right model
        /// </summary>
        public readonly string RightForeignKey;

        /// <summary>
        /// Used to label many to many realtionships
        /// </summary>
        /// <param name="joiningTable">The name of the relational table</param>
        /// <param name="leftForeignerKey">The foreign key on the relational table the is the primary key of the left model</param>
        /// <param name="rightForeignerKey">The foreign key on the relational table the is the primary key of the right model</param>
        public SqlBiDirectionalManyToMany(string joiningTable, string leftForeignerKey, string rightForeignerKey) : base(RelationshipType.ManyToMany)
        {
            directTargets = true;

            JoiningTable = joiningTable;
            LeftForeignKey = leftForeignerKey;
            RightForeignKey = rightForeignerKey;
        }
        /// <summary>
        /// Used to label many to many realtionships
        /// </summary>
        /// <param name="joiningTableSchema">The schema of the relational table</param>
        /// <param name="joiningTable">The name of the relational table</param>
        /// <param name="leftForeignerKey">The foreign key on the relational table the is the primary key of the left model</param>
        /// <param name="rightForeignerKey">The foreign key on the relational table the is the primary key of the right model</param>>
        public SqlBiDirectionalManyToMany(string joiningTableSchema, string joiningTable, string leftForeignerKey, string rightForeignerKey)
            : this(joiningTable, leftForeignerKey, rightForeignerKey)
        {
            JoiningSchema = joiningTableSchema;
        }

        public override Relationship ProcessRelationship(Table currentTable, UnprocessedRelationshipMember unprocessed)
        {
            Table parentTable = currentTable;

            if (parentTable.PrimaryKey == null)
                throw new FormatException($"Many to Many relationships require base table to have a primary key. {unprocessed.ParentType.Name} has no primary key.");

            Table middleTable;
            if (directTargets)
            {
                middleTable = new Table(JoiningSchema, JoiningTable);

                middleTable.Columns.AddColumn(new Column(middleTable, LeftForeignKey));
                middleTable.Columns.AddColumn(new Column(middleTable, RightForeignKey));
            }
            else
            {
                middleTable = new Table(JoiningType);
            }

            Column middle_left_fk = null;
            string left_fk = null;
            if (LeftForeignKey != null)
            {
                left_fk = LeftForeignKey;
                middle_left_fk = middleTable.Columns.GetAll().FirstOrDefault(x => x.ColumnName == left_fk);
            }
            else if (LeftJoiningKey != null)
            {
                left_fk = LeftJoiningKey;
                middle_left_fk = middleTable.Columns.TryGet(left_fk);
            }
            if (middle_left_fk == null)
                throw new FormatException($"Type of '{JoiningType.Name}' does not have a column '{left_fk}'.");

            Column middle_right_fk = null;
            string right_fk = null;
            if (RightForeignKey != null)
            {
                right_fk = RightForeignKey;
                middle_right_fk = middleTable.Columns.GetAll().FirstOrDefault(x => x.ColumnName == right_fk);
            }
            else if (RightJoiningKey != null)
            {
                right_fk = RightJoiningKey;
                middle_right_fk = middleTable.Columns.TryGet(right_fk);
            }
            if (middle_right_fk == null)
                throw new FormatException($"Type of '{JoiningType.Name}' does not have a column '{right_fk}'.");

            var dataTargetType = unprocessed.UnprocessedMember.Member.GetFieldOrPropValueType();
            dataTargetType = dataTargetType.GetItemTypeFromIEnumerable();
            if (dataTargetType == null)
                throw new FormatException($"Type of '{unprocessed.UnprocessedMember.Member.ReflectedType.Name}.{unprocessed.UnprocessedMember.Member.Name}' does not implement {nameof(System.Collections.IEnumerable)}");
            Table childTable = new Table(dataTargetType);

            if (childTable.PrimaryKey == null)
                throw new FormatException($"Many to Many relationships require base table to have a primary key. {dataTargetType.Name} has no primary key.");

            var dataTarget = new ReflectedColumn(currentTable, unprocessed.UnprocessedMember);
            var relationship = new Relationship(unprocessed.RelationshipType, parentTable.PrimaryKey, childTable.PrimaryKey, dataTarget, middle_left_fk);

            var parent_middle_join = new Join(parentTable, middleTable);
            var group1 = new WhereCollection(SqlWhereOp.OR);
            group1.AddComponent(new WhereTwoColumn(parentTable.PrimaryKey, middle_left_fk, SqlOp.EQ));
            group1.AddComponent(new WhereTwoColumn(parentTable.PrimaryKey, middle_right_fk, SqlOp.EQ));
            parent_middle_join.Condition.AddComponent(group1);
            relationship.Joins.Add(parent_middle_join);

            var middle_child_join = new Join(middleTable, childTable);
            middle_child_join.Condition.AddComponent(new WhereTwoColumn(parentTable.PrimaryKey, childTable.PrimaryKey, SqlOp.NOT));
            var group2 = new WhereCollection(SqlWhereOp.OR);
            group2.AddComponent(new WhereTwoColumn(middle_right_fk, childTable.PrimaryKey, SqlOp.EQ));
            group2.AddComponent(new WhereTwoColumn(middle_left_fk, childTable.PrimaryKey, SqlOp.EQ));
            middle_child_join.Condition.AddComponent(group2);
            relationship.Joins.Add(middle_child_join);

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

            middleTable.ParentRelationship = relationship;
            childTable.ParentRelationship = relationship;

            return relationship;
        }
    }
}
