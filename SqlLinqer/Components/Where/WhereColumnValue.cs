using System.Linq;
using System.Text;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Extensions.EnumExtensions;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System;

namespace SqlLinqer.Components.Where
{
    public class WhereColumnValue : WhereStatement
    {
        public readonly Column Column;
        public readonly object Value;
        public readonly SqlOp Operator;
        public readonly SqlSubQueryOp SubQueryOp;

        public WhereColumnValue(Column column, object value, SqlOp op, SqlSubQueryOp sub_query_op)
        {
            Column = column;
            Value = value;
            Operator = op;
            SubQueryOp = sub_query_op;
        }

        public override int Order => 100;

        public override IWhereComponent Invert()
        {
            return new WhereColumnValue(Column, Value, Operator.GetInvert(), SubQueryOp);
        }

        public override IWhereComponent Finalize(WhereQuery query)
        {
            if (query == null || query.Withs == null)
                return this;

            var col_path = Column.GetPath(query.Table);
            var rel_path = col_path.ToArray().Reverse().Skip(1).Reverse();
            var rel_many = query.Table.FindFirstManyRelationshipFromPath(rel_path);
            if (rel_many != null)
            {
                bool inverted;
                switch (Operator)
                {
                    case SqlOp.NOT:
                    case SqlOp.NOTLIKE:
                    case SqlOp.NOTLIKEWORD:
                    case SqlOp.NOTLIKEORNULL:
                    case SqlOp.NOTLIKEWORDORNULL:
                        inverted = true;
                        break;
                    default:
                        inverted = false;
                        break;
                }

                var where_with = new WhereWithJoinBack(query, rel_many, inverted);
                where_with.SubQuery.RootWhere.AddComponent(this);
                return where_with;
            }
            else if (Value != null && typeof(IEnumerable).IsAssignableFrom(Value.GetType()) && Value.GetType() != typeof(string))
            {
                switch (Operator)
                {
                    case SqlOp.EQ:
                    case SqlOp.NOT:
                        return this;
                    default:
                        var values = Value as IEnumerable;
                        var groupOp = SubQueryOp == SqlSubQueryOp.ANY ? SqlWhereOp.OR : SqlWhereOp.AND;
                        var group = new WhereCollection(groupOp);
                        foreach (var val in values)
                        {
                            group.AddComponent(new WhereColumnValue(this.Column, val, Operator, SubQueryOp));
                        }
                        return group;
                }
            }
            else
            {
                switch (Operator)
                {
                    case SqlOp.NOTLIKEORNULL:
                    case SqlOp.NOTLIKEWORDORNULL:
                        SqlOp newOp;
                        switch (Operator)
                        {
                            case SqlOp.NOTLIKEWORDORNULL:
                                newOp = SqlOp.NOTLIKEWORD;
                                break;
                            default:
                                newOp = SqlOp.NOTLIKE;
                                break;
                        }
                        var groupORNULL = new WhereCollection(SqlWhereOp.OR);
                        groupORNULL.AddComponent(new WhereColumnValue(this.Column, null, SqlOp.EQ, SubQueryOp));
                        groupORNULL.AddComponent(new WhereColumnValue(this.Column, this.Value, newOp, SubQueryOp));
                        return groupORNULL.FinalizeStatements(query);
                    case SqlOp.LIKEWORD:
                    case SqlOp.NOTLIKEWORD:
                        var likeOp = Operator == SqlOp.LIKEWORD ? SqlOp.LIKE : SqlOp.NOTLIKE;
                        var groupOp = Operator == SqlOp.LIKEWORD ? SqlWhereOp.OR : SqlWhereOp.AND;
                        var group = new WhereCollection(groupOp);

                        group.AddComponent(new WhereColumnValue(this.Column, this.Value, likeOp, SqlSubQueryOp.ANY));
                        group.AddComponent(new WhereColumnValue(this.Column, $"{this.Value}[^0-9A-z]%", likeOp, SqlSubQueryOp.ANY));
                        group.AddComponent(new WhereColumnValue(this.Column, $"%[^0-9A-z]{this.Value}", likeOp, SqlSubQueryOp.ANY));
                        group.AddComponent(new WhereColumnValue(this.Column, $"%[^0-9A-z]{this.Value}[^0-9A-z]%", likeOp, SqlSubQueryOp.ANY));

                        return group;
                    default:
                        return this;
                }
            }
        }
        public override bool TryCombineInto(WhereCollection current_collection, IWhereComponent parent_component) => false;

        public override void Render(WhereQuery query, ParameterCollection parameters, DbFlavor flavor, StringBuilder builder)
        {
            object hanldedValue = Value;

            query?.Joins.AddTable(Column.Table);

            string columnTarget = Column.Render(flavor);

            if (hanldedValue == null)
            {
                switch (Operator)
                {
                    case SqlOp.EQ:
                        builder.Append($"{columnTarget} IS NULL");
                        return;
                    case SqlOp.NOT:
                        builder.Append($"{columnTarget} IS NOT NULL");
                        return;
                }
            }
            else if (hanldedValue is Queries.Core.SelectQuery subquery)
            {
                subquery.DisableWiths();
                string rendered = subquery.Render(parameters, flavor, false); 



                builder.Append($"{Column.Render(flavor)} {Operator.GetDescription()} {SubQueryOp.GetDescription()} ({rendered})");
                return;
            }
            else if (typeof(IEnumerable).IsAssignableFrom(hanldedValue.GetType()) && hanldedValue.GetType() != typeof(string))
            {
                var values = hanldedValue as IEnumerable;

                int count = 0;
                foreach (var _ in values)
                    ++count;

                if (count == 1)
                {
                    foreach (var val in values)
                    {
                        hanldedValue = val;
                        break;
                    }
                }
                else
                {
                    switch (Operator)
                    {
                        case SqlOp.EQ:
                        case SqlOp.NOT:
                            var placeholders = new List<string>();
                            foreach (var val in values)
                            {
                                placeholders.Add(parameters.AddParameter(val));
                            }
                            string op_str = (Operator == SqlOp.EQ ? SqlArrayOp.IN : SqlArrayOp.NOTIN).GetDescription();
                            builder.Append($"{Column.Render(flavor)} {op_str} ({string.Join(",", placeholders)})");
                            return;
                        default:
                            var groupOp = SubQueryOp == SqlSubQueryOp.ANY ? SqlWhereOp.OR : SqlWhereOp.AND;
                            var group = new WhereCollection(groupOp);
                            foreach (var val in values)
                            {
                                group.AddComponent(new WhereColumnValue(this.Column, val, Operator, SubQueryOp));
                            }
                            builder.Append($"({group.Render(parameters, flavor)})");
                            return;
                    }
                }
            }

            switch (Operator)
            {
                case SqlOp.LIKEWORD:
                case SqlOp.NOTLIKEWORD:
                    var likeOp = Operator == SqlOp.LIKEWORD ? SqlOp.LIKE : SqlOp.NOTLIKE;
                    var groupOp = Operator == SqlOp.LIKEWORD ? SqlWhereOp.OR : SqlWhereOp.AND;
                    var group = new WhereCollection(groupOp);

                    group.AddComponent(new WhereColumnValue(this.Column, hanldedValue, likeOp, SqlSubQueryOp.ANY));
                    group.AddComponent(new WhereColumnValue(this.Column, $"{hanldedValue}[^0-9A-z]%", likeOp, SqlSubQueryOp.ANY));
                    group.AddComponent(new WhereColumnValue(this.Column, $"%[^0-9A-z]{hanldedValue}", likeOp, SqlSubQueryOp.ANY));
                    group.AddComponent(new WhereColumnValue(this.Column, $"%[^0-9A-z]{hanldedValue}[^0-9A-z]%", likeOp, SqlSubQueryOp.ANY));

                    builder.Append($"({group.Render(parameters, flavor)})");
                    return;
                default:
                    string placeholder = parameters.AddParameter(hanldedValue);
                    builder.Append($"{Column.Render(flavor)} {Operator.GetDescription()} {placeholder}");
                    return;
            }
        }

        public override IEnumerable<Column> GetColumnReferences()
        {
            yield return Column;
        }
    }
}