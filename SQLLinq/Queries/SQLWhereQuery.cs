using SqlLinqer.Relationships;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace SqlLinqer.Queries
{
    /// <summary>
    /// <see cref="SQLWhereQuery{TObj}"/> is an abstract base object to maintain and construct the where statement of queries that use it.
    /// </summary>
    /// <typeparam name="TObj">The class that represents the database table and its relationships</typeparam>
    public abstract class SQLWhereQuery<TObj> : SQLBaseQuery<TObj> where TObj : new()
    {
        private readonly SQLWhereGroup<TObj> _whereGroup;
        private readonly Dictionary<string, SQLWith<TObj>> _withStatements;

        /// <summary>
        /// If the query context has any where statements
        /// </summary>
        protected bool HasWhereQuery
        {
            get { return _whereGroup != null && _whereGroup.Group.FirstOrDefault() != null; }
        }

        /// <summary>
        /// Begins a query with where statements
        /// </summary>
        /// <param name="recursionLevel">The recursion level to initialize the config with.</param>
        protected SQLWhereQuery(int recursionLevel = 1)
            : base(recursionLevel)
        {
            _whereGroup = new SQLWhereGroup<TObj>(SQLGroupOp.AND);
            _withStatements = new Dictionary<string, SQLWith<TObj>>();
        }

        private void SimplifyWhereGroups(SQLWhereGroup<TObj> whereGroup, SQLWhereGroup<TObj> parentGroup)
        {
            // don't process nulls
            if (whereGroup == null)
                return;

            // call on nested where groups
            var oldGroup = new List<ISQLWhere<TObj>>();
            oldGroup.AddRange(whereGroup.Group);
            oldGroup.ForEach(where =>
            {
                if (where is SQLWhereGroup<TObj> group)
                    SimplifyWhereGroups(group, whereGroup);
            });

            // if current group is same op as parent, simplify
            if (parentGroup != null && whereGroup.op == parentGroup.op)
            {
                parentGroup.Group.AddRange(whereGroup.Group);
                parentGroup.Group.Remove(whereGroup);
            }
        }
        private void GetJoins(SQLWhereGroup<TObj> whereGroup, Dictionary<string, SQLConfig> joins, SQLWith<TObj> withReference = null)
        {
            if (whereGroup == null || whereGroup.Group.Count() == 0)
            {
                if (withReference != null && withReference.NestedWith != null)
                {
                    var configToJoin = withReference.NestedWith.Config.ParentRelationship.Left.Config;
                    while (configToJoin != null)
                    {
                        if (!joins.ContainsKey(configToJoin.TableAlias))
                            joins.Add(configToJoin.TableAlias, configToJoin);

                        if (withReference != null && configToJoin.TableAlias == withReference.Config.ParentRelationship.Left.Config.TableAlias)
                            break;

                        configToJoin = configToJoin.ParentRelationship?.Left.Config;
                    }
                }

                return;
            }

            foreach (var where in whereGroup.Group)
            {
                if (where is SQLWhereGroup<TObj> group)
                {
                    GetJoins(group, joins);
                }
                else
                {
                    var stackTrace = new Dictionary<string, bool>();
                    var clause = (SQLWhereClause<TObj>)where;
                    var sqlMember = clause.column ?? GetMemberFromExpression(clause.expression);
                    var configToJoin = sqlMember.Config;
                    while (configToJoin != null)
                    {
                        if (withReference == null && configToJoin.ParentRelationship is SQLOneToManyRelationship) // remove tables that would be moved to with statements
                        {
                            stackTrace.Keys.ToList()
                                .ForEach(tbl =>
                                {
                                    if (joins.ContainsKey(tbl))
                                        joins.Remove(tbl);
                                });
                        }
                        else if (!joins.ContainsKey(configToJoin.TableAlias))
                        {
                            joins.Add(configToJoin.TableAlias, configToJoin);

                            // track the tables added during this loop
                            stackTrace.Add(configToJoin.TableAlias, true);
                        }

                        if (withReference != null && configToJoin.TableAlias == withReference.Config.ParentRelationship.Left.Config.TableAlias)
                            break;

                        configToJoin = configToJoin.ParentRelationship?.Left.Config;
                    }
                }
            }
        }
        private string RenderWithJoins(Dictionary<string, SQLConfig> joins, SQLWith<TObj> withReference)
        {
            if (!joins.ContainsKey(withReference.Config.TableAlias))
                joins.Add(withReference.Config.TableAlias, withReference.Config);

            var joinList = joins.Values
                .OrderBy(x => x.TableAlias);

            StringBuilder builder = new StringBuilder();

            foreach (var join in joinList)
            {
                if (join.TableAlias == withReference.Config.ParentRelationship.Left.Config.TableAlias)
                    builder.Append($"{Wrap(join.TableName)} as {Wrap(join.TableAlias)} ");
                else
                    builder.Append($"LEFT JOIN {Wrap(join.TableName)} AS {Wrap(join.TableAlias)} ON {Wrap(join.ParentRelationship.Left.Config.TableAlias)}.{Wrap(join.ParentRelationship.Left.SQLName)} = {Wrap(join.ParentRelationship.Right.Config.TableAlias)}.{Wrap(join.ParentRelationship.Right.SQLName)} ");
            }

            if (withReference.NestedWith != null)
                builder.Append($"LEFT JOIN {Wrap(withReference.NestedWith.Alias)} ON {Wrap(withReference.NestedWith.Alias)}.{Wrap(withReference.NestedWith.OutputCol)} = {Wrap(withReference.NestedWith.Config.ParentRelationship.Left.Config.TableAlias)}.{Wrap(withReference.NestedWith.Config.ParentRelationship.Left.SQLName)} ");

            return builder.ToString();
        }
        private void RenderWith(DbCommand cmd, List<SQLWith<TObj>> withReferences, ref uint start)
        {
            if (withReferences == null || withReferences.Count == 0)
                return;

            foreach (var with in withReferences)
            {
                var withBuilder = new StringBuilder();
                string selectedCol = $"{Wrap(with.Config.ParentRelationship.Left.Config.TableAlias)}.{Wrap(with.OutputCol)}";

                // selected column
                withBuilder.Append($"SELECT {selectedCol} FROM ");

                // render table joins
                var joins = new Dictionary<string, SQLConfig>();
                GetJoins(with.WhereGroup, joins, with);
                withBuilder.Append(RenderWithJoins(joins, with));

                // render where
                withBuilder.Append(" WHERE ");
                uint groupIdx = 0;
                RenderWhere(cmd, with.WhereGroup, withBuilder, ref start, ref groupIdx, with);

                // group by selected column and optionally include having statement for custom "ALL" Op.
                withBuilder.Append($"GROUP BY {selectedCol}{(with.MinHavingCount > -1 ? $" HAVING COUNT(*) >= {with.MinHavingCount}" : null)} ");

                // give with its alias
                with.CommandText = $"{Wrap(with.Alias)} AS ({withBuilder})";
            }

            // sort with references in order
            withReferences = withReferences
                .OrderByDescending(x => x.Config.RecursionLevel)
                .ThenByDescending(x => x.Order)
                .ToList();

            // render with string
            cmd.CommandText = $"WITH {string.Join(", ", withReferences.Select(x => x.CommandText))} " + cmd.CommandText;

            // include with join to main
            var joinBackBuilder = new StringBuilder();
            withReferences
                .Where(x => x.TopLevel).ToList()
                .ForEach(with => joinBackBuilder.Append($"LEFT JOIN {Wrap(with.Alias)} ON {Wrap(with.Alias)}.{Wrap(with.OutputCol)} = {Wrap(with.Config.ParentRelationship.Left.Config.TableAlias)}.{Wrap(with.Config.ParentRelationship.Left.SQLName)} "));
            cmd.CommandText += joinBackBuilder.ToString();
        }
        private string RenderClause(DbCommand cmd, ref uint start, SQLWhereClause<TObj> clause, string col = null, SQLWith<TObj> withReference = null)
        {
            var sqlMember = clause.column ?? GetMemberFromExpression(clause.expression);
            string placeholder = $"@W{++start}";
            if (col == null)
                col = $"{Wrap(sqlMember.Config.TableAlias)}.{Wrap(sqlMember.SQLName)}";

            if (withReference == null && (clause.op == SQLOp.ANY || clause.op == SQLOp.ALL))
            {
                throw new FormatException($"Member {sqlMember.Config.Type.FullName}.{sqlMember.Info.Name} is not defined as a one to many relationship with {(sqlMember.Config.ParentRelationship == null ? "null" : $"{sqlMember.Config.ParentRelationship?.Left.Config.Type}.{sqlMember.Config.ParentRelationship?.Left.Info.Name}")}");
            }

            if (clause.value == null && (clause.op == SQLOp.EQ || clause.op == SQLOp.NOT))
            {
                switch (clause.op)
                {
                    case SQLOp.NOT:
                        return $"({col} IS NOT NULL)";
                    case SQLOp.EQ:
                    default:
                        return $"({col} IS NULL)";
                }
            }
            else if (clause.value is IEnumerable && !(clause.value is string))
            {
                uint qCnt = 0;
                var placeholders = new List<string>();
                IEnumerable list = (clause.value as IEnumerable).Cast<object>();

                foreach (object value in list)
                {
                    string list_placeholder = $"{placeholder}_{++qCnt}";

                    DbParameter param = cmd.CreateParameter();
                    param.ParameterName = list_placeholder;
                    param.Value = value;

                    cmd.Parameters.Add(param);
                    placeholders.Add(list_placeholder);
                }

                switch (clause.op)
                {
                    case SQLOp.ANY:
                        break;
                    case SQLOp.ALL:
                        if (withReference.MinHavingCount == -1)
                            withReference.MinHavingCount = 0;
                        withReference.MinHavingCount += (int)qCnt;
                        break;
                    case SQLOp.NOTIN:
                        break;
                    default:
                        clause.op = SQLOp.IN;
                        break;
                }

                return $"({col} {clause.OpToString()} ({string.Join(",", placeholders)}))";
            }
            else if (clause.op == SQLOp.IN || clause.op == SQLOp.NOTIN)
            {
                DbParameter param = cmd.CreateParameter();
                param.ParameterName = placeholder;
                param.Value = clause.value;

                cmd.Parameters.Add(param);

                return $"({col} {clause.OpToString()} ({placeholder}))";
            }
            else
            {
                DbParameter param = cmd.CreateParameter();
                param.ParameterName = placeholder;
                param.Value = clause.value;

                cmd.Parameters.Add(param);

                return $"({col} {clause.OpToString()} {placeholder})";
            }
        }
        private void RenderWhere(DbCommand cmd, SQLWhereGroup<TObj> wheregroup, StringBuilder builder, ref uint start, ref uint groupIdx, SQLWith<TObj> withReference = null)
        {
            bool first = true;
            if (wheregroup != null && wheregroup.Group.Count() != 0)
                builder.Append("(");

            if (withReference != null && withReference.NestedWith != null)
            {
                builder.Append($"({Wrap(withReference.NestedWith.Alias)}.{Wrap(withReference.NestedWith.OutputCol)} IS NOT NULL)");
                first = false;
            }

            if (wheregroup == null || wheregroup.Group.Count() == 0)
                return;

            uint currentGroupIdx = ++groupIdx;
            foreach (var where in wheregroup.Group)
            {
                string op = null;
                switch (first)
                {
                    case false:
                        switch (wheregroup.op)
                        {
                            case SQLGroupOp.OR:
                                op = " OR ";
                                break;
                            default:
                                op = " AND ";
                                break;
                        }
                        break;
                    case true:
                        first = false;
                        break;
                }

                if (where is SQLWhereGroup<TObj> group)
                // where group recursive render
                {
                    builder.Append(op);
                    RenderWhere(cmd, group, builder, ref start, ref groupIdx, withReference);
                }
                else
                // where clause render
                {
                    string col = null;
                    var clause = (SQLWhereClause<TObj>)where;
                    var sqlMember = clause.column ?? GetMemberFromExpression(clause.expression);

                    bool existingWith = false;
                    SQLWith<TObj> clauseWith = null;
                    SQLWith<TObj> lastWith = null;
                    if (withReference == null)
                    {
                        var config = sqlMember.Config;
                        while (config.ParentRelationship != null)
                        {
                            if (config.ParentRelationship is SQLOneToManyRelationship)
                            {
                                SQLWith<TObj> with;
                                string groupTableAlias = $"{config.TableAlias}_{currentGroupIdx}";
                                if (_withStatements.ContainsKey(groupTableAlias))
                                {
                                    with = _withStatements[groupTableAlias];
                                    existingWith = true;
                                }
                                else
                                {
                                    with = new SQLWith<TObj>(wheregroup.op)
                                    {
                                        Alias = $"{config.TableAlias}_OTM_{start}",
                                        OutputCol = config.ParentRelationship.Left.SQLName,
                                        Order = start,
                                        Config = config,
                                        NestedWith = lastWith
                                    };
                                    _withStatements.Add(groupTableAlias, with);
                                }

                                if (clauseWith == null)
                                {
                                    clauseWith = with;
                                    with.WhereGroup.Add(clause);
                                }

                                lastWith = with;
                            }
                            config = config.ParentRelationship?.Left.Config;
                        }

                        if (lastWith != null)
                            lastWith.TopLevel = true;
                    }

                    if (lastWith != null)
                    {
                        if (existingWith)
                            continue;

                        clause = clause.Clone();
                        clause.op = SQLOp.NOT;
                        clause.value = null;
                        col = $"{Wrap(lastWith.Alias)}.{Wrap(lastWith.OutputCol)}";
                    }

                    builder.Append(op);
                    builder.Append(RenderClause(cmd, ref start, clause, col, withReference));
                }
            }

            builder.Append(")");
        }

        /// <summary>
        /// Add a where group to the where query
        /// </summary>
        /// <param name="whereGroup">the <see cref="SQLWhereGroup{TObj}"/> to add to the query</param>
        /// <returns>The current <see cref="SQLWhereQuery{TObj}"/> object</returns>
        protected SQLWhereQuery<TObj> Where(SQLWhereGroup<TObj> whereGroup)
        {
            _whereGroup.Add(whereGroup);
            return this;
        }
        /// <summary>
        /// Add a where clause to the where query
        /// </summary>
        /// <param name="expression">A expression that points to the property/field of the class the where clause applies to</param>
        /// <param name="value">The value the where clause is evaluating against</param>
        /// <param name="op">The <see cref="SQLOp"/> operator to apply to the clause</param>
        /// <returns>The current <see cref="SQLWhereQuery{TObj}"/> object</returns>
        protected SQLWhereQuery<TObj> Where(Expression<Func<TObj, object>> expression, object value, SQLOp op = SQLOp.EQ)
        {
            _whereGroup.Add(expression, value, op);
            return this;
        }
        /// <summary>
        /// Add a where clause to the where query
        /// </summary>
        /// <param name="column">The column's <see cref="SQLMemberInfo"/></param>
        /// <param name="value">The value or to the where clause is evaluating against</param>
        /// <param name="op">The <see cref="SQLOp"/> operator to apply to the clause</param>
        /// <returns>The current <see cref="SQLWhereQuery{TObj}"/> object</returns>
        protected SQLWhereQuery<TObj> Where(SQLMemberInfo column, object value, SQLOp op = SQLOp.EQ)
        {
            _whereGroup.Add(column, value, op);
            return this;
        }
        /// <summary>
        /// Renders the where query. Adds the where command text and parameterized values to the command.
        /// </summary>
        /// <param name="cmd">The command to add the where to</param>
        protected void RenderWhere(DbCommand cmd)
        {
            _withStatements.Clear();
            if (_whereGroup == null || _whereGroup.Group.Count() == 0)
                return;

            SimplifyWhereGroups(_whereGroup);

            uint start = 0, groupIdx = 0;
            StringBuilder whereBuilder = new StringBuilder();
            whereBuilder.Append(" WHERE ");

            RenderWhere(cmd, _whereGroup, whereBuilder, ref start, ref groupIdx);

            RenderWith(cmd, _withStatements.Select(x => x.Value).ToList(), ref start);

            cmd.CommandText += whereBuilder.ToString();
        }
        /// <summary>
        /// Adds to the from <see cref="Dictionary{TKey, TValue}"/> the additonal joins needed for the where query
        /// </summary>
        /// <param name="joins">A reference dictionary to track the joins</param>
        protected void GetJoins(Dictionary<string, SQLConfig> joins)
        {
            GetJoins(_whereGroup, joins);
        }
        /// <summary>
        /// Renders the joins of the query into a string which can be added to a <see cref="DbCommand"/>
        /// </summary>
        /// <param name="joins">A dictonary of joins</param>
        /// <returns>All of the <paramref name="joins"/> rendered into a string of left join statements</returns>
        protected string RenderJoins(Dictionary<string, SQLConfig> joins)
        {
            StringBuilder builder = new StringBuilder();

            var joinList = joins.Values
                .OrderBy(x => x.TableAlias);
            foreach (var join in joinList)
            {
                if (join.ParentRelationship == null)
                    builder.Append($"{Wrap(Config.TableName)} as {Wrap(Config.TableAlias)} ");
                else
                    builder.Append($"left join {Wrap(join.TableName)} as {Wrap(join.TableAlias)} on {Wrap(join.ParentRelationship.Left.Config.TableAlias)}.{Wrap(join.ParentRelationship.Left.SQLName)} = {Wrap(join.ParentRelationship.Right.Config.TableAlias)}.{Wrap(join.ParentRelationship.Right.SQLName)} ");
            }

            return builder.ToString();
        }
        /// <summary>
        /// Simplifies the structure of a where group recursivly
        /// </summary>
        protected void SimplifyWhereGroups(SQLWhereGroup<TObj> whereGroup)
        {
            SimplifyWhereGroups(whereGroup, null);
        }
    }
}
