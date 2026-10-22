using System;
using System.Linq;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Where;
using SqlLinqer.Extensions.MemberInfoExtensions;
using SqlLinqer.Extensions.TypeExtensions;

namespace SqlLinqer.Components.Joins
{
    internal static class ManyToManyRelationship
    {
        internal static Relationship Create(Table parent, UnprocessedRelationshipMember member, Type joiningType,
            string joiningSchema, string joiningTable, string leftKey, string rightKey, bool directTargets, bool bidirectional)
        {
            if (parent.PrimaryKey == null)
                throw new FormatException($"Many to Many relationships require base table to have a primary key. {member.ParentType.Name} has no primary key.");

            var middle = directTargets ? new Table(joiningSchema ?? parent.DefaultSchema, joiningTable)
                : new Table(joiningType, parent.DefaultSchema);
            if (directTargets)
            {
                middle.Columns.AddColumn(new Column(middle, leftKey));
                middle.Columns.AddColumn(new Column(middle, rightKey));
            }
            Column FindKey(string key)
                => (directTargets ? middle.Columns.GetAll().FirstOrDefault(column => column.ColumnName == key) : middle.Columns.TryGet(key))
                    ?? throw new FormatException($"Type of '{joiningType?.Name ?? joiningTable}' does not have a column '{key}'.");
            var left = FindKey(leftKey);
            var right = FindKey(rightKey);
            var target = member.UnprocessedMember.Member;
            var childType = target.GetFieldOrPropValueType().GetItemTypeFromIEnumerable()
                ?? throw new FormatException($"Type of '{target.ReflectedType.Name}.{target.Name}' does not implement {nameof(System.Collections.IEnumerable)}");
            var child = new Table(childType, parent.DefaultSchema);
            if (child.PrimaryKey == null)
                throw new FormatException($"Many to Many relationships require base table to have a primary key. {childType.Name} has no primary key.");

            var relationship = new Relationship(member.RelationshipType, parent.PrimaryKey, child.PrimaryKey,
                new ReflectedColumn(parent, member.UnprocessedMember), left);
            var parentJoin = new Join(parent, middle);
            var childJoin = new Join(middle, child);
            if (bidirectional)
            {
                var parents = new WhereCollection(SqlWhereOp.OR);
                parents.AddComponent(new WhereTwoColumn(parent.PrimaryKey, left, SqlOp.EQ));
                parents.AddComponent(new WhereTwoColumn(parent.PrimaryKey, right, SqlOp.EQ));
                parentJoin.Condition.AddComponent(parents);
                childJoin.Condition.AddComponent(new WhereTwoColumn(parent.PrimaryKey, child.PrimaryKey, SqlOp.NOT));
                var children = new WhereCollection(SqlWhereOp.OR);
                children.AddComponent(new WhereTwoColumn(right, child.PrimaryKey, SqlOp.EQ));
                children.AddComponent(new WhereTwoColumn(left, child.PrimaryKey, SqlOp.EQ));
                childJoin.Condition.AddComponent(children);
            }
            else
            {
                parentJoin.Condition.AddComponent(new WhereTwoColumn(parent.PrimaryKey, left, SqlOp.EQ));
                childJoin.Condition.AddComponent(new WhereTwoColumn(right, child.PrimaryKey, SqlOp.EQ));
            }
            relationship.Joins.Add(parentJoin);
            relationship.Joins.Add(childJoin);
            relationship.ApplyConditions(target);
            middle.ParentRelationship = relationship;
            child.ParentRelationship = relationship;
            return relationship;
        }
    }
}
