using System.Linq;
using System.Text;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Extensions.EnumExtensions;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System;

namespace SqlLinqer.Components.Where
{
    /// <summary>Compares a mapped column with a value or subquery.</summary>
    public class WhereColumnValue : WhereStatement
    {
        /// <summary>The column being compared.</summary>
        public readonly Column Column;
        /// <summary>The comparison value or subquery.</summary>
        public readonly object Value;
        /// <summary>The comparison operator.</summary>
        public readonly SqlOp Operator;
        /// <summary>The quantifier used when comparing against a subquery.</summary>
        public readonly SqlSubQueryOp SubQueryOp;

        /// <summary>Creates a column comparison with a value or quantified subquery.</summary>
        public WhereColumnValue(Column column, object value, SqlOp op, SqlSubQueryOp sub_query_op)
        {
            Column = column;
            bool binary = value is byte[] && ((column as ReflectedColumn)?.ValueType == typeof(byte[])
                || column.DbType == System.Data.DbType.Binary);
            Value = value is IEnumerable values && !(value is string) && !binary
                ? values.Cast<object>().ToArray()
                : value;
            Operator = op;
            SubQueryOp = sub_query_op;
        }

        /// <summary>The preparation order used to sort predicates.</summary>
        public override int Order => 100;

        /// <inheritdoc/>
        public override IWhereComponent Invert()
        {
            return new WhereColumnValue(Column, Value, Operator.GetInvert(), SubQueryOp);
        }

        /// <inheritdoc/>
        public override IWhereComponent Finalize(WhereQuery query) => FinalizeForDialect(query, null);

        internal IWhereComponent FinalizeForDialect(WhereQuery query, DbFlavor? flavor)
        {
            if (Value is object[] items && items.Length == 0)
                return new WhereConstant(SubQueryOp == SqlSubQueryOp.ALL);

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
            else if (Value != null && typeof(IEnumerable).IsAssignableFrom(Value.GetType()) && Value.GetType() != typeof(string) && !(Value is byte[]))
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
                        return groupORNULL.FinalizeStatements(query, flavor);
                    case SqlOp.LIKEWORD:
                    case SqlOp.NOTLIKEWORD:
                        if (flavor == DbFlavor.DuckDb || flavor == DbFlavor.Sqlite)
                            return this;
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
        /// <inheritdoc/>
        public override bool TryCombineInto(WhereCollection current_collection, IWhereComponent parent_component) => false;

        private static string WordPattern(string value)
        {
            // Match the library's existing ASCII 0-9/A-z boundaries and LIKE wildcards.
            var pattern = new StringBuilder("(^|[^0-9A-z])");
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\' && i + 1 < value.Length)
                    pattern.Append(System.Text.RegularExpressions.Regex.Escape(value[++i].ToString()));
                else if (c == '%')
                    pattern.Append(".*");
                else if (c == '_')
                    pattern.Append('.');
                else
                    pattern.Append(System.Text.RegularExpressions.Regex.Escape(c.ToString()));
            }
            return pattern.Append("($|[^0-9A-z])").ToString();
        }

        /// <inheritdoc/>
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
                if (flavor == DbFlavor.Sqlite)
                {
                    if (!((Operator == SqlOp.EQ && SubQueryOp == SqlSubQueryOp.ANY)
                        || (Operator == SqlOp.NOT && SubQueryOp == SqlSubQueryOp.ALL)))
                        throw new NotSupportedException("SQLite quantified subqueries support only IN and NOT IN.");
                    subquery.DisableWiths();
                    builder.Append($"{columnTarget} {(Operator == SqlOp.EQ ? "IN" : "NOT IN")} ({subquery.Render(parameters, flavor, false)})");
                    return;
                }
                subquery.DisableWiths();
                string rendered = subquery.Render(parameters, flavor, false);



                builder.Append($"{Column.Render(flavor)} {Operator.GetDescription()} {SubQueryOp.GetDescription()} ({rendered})");
                return;
            }
            else if (typeof(IEnumerable).IsAssignableFrom(hanldedValue.GetType()) && hanldedValue.GetType() != typeof(string) && !(hanldedValue is byte[]))
            {
                var values = hanldedValue as IEnumerable;

                int count = 0;
                foreach (var _ in values)
                    ++count;

                if (count == 0)
                {
                    builder.Append(SubQueryOp == SqlSubQueryOp.ALL ? "1 = 1" : "1 = 0");
                    return;
                }
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

            if (flavor == DbFlavor.DuckDb && (Operator == SqlOp.LIKEWORD || Operator == SqlOp.NOTLIKEWORD || Operator == SqlOp.NOTLIKEWORDORNULL))
            {
                string pattern = parameters.AddParameter(hanldedValue == null ? null : WordPattern(Convert.ToString(hanldedValue)));
                string match = $"regexp_matches({columnTarget}, {pattern}, 's')";
                builder.Append(Operator == SqlOp.LIKEWORD ? match : Operator == SqlOp.NOTLIKEWORD
                    ? $"NOT {match}" : $"({columnTarget} IS NULL OR NOT {match})");
                return;
            }

            switch (Operator)
            {
                case SqlOp.LIKEWORD:
                case SqlOp.NOTLIKEWORD:
                    if (flavor == DbFlavor.Sqlite)
                    {
                        string pattern = hanldedValue == null ? null : Convert.ToString(hanldedValue, System.Globalization.CultureInfo.InvariantCulture);
                        string wordPattern = parameters.AddParameter(pattern == null ? null : SqliteWordPattern(pattern));
                        string lowerPattern = parameters.AddParameter(pattern == null ? null : SqliteWordPattern(
                            new string(pattern.Select(c => c >= 'A' && c <= 'Z' ? (char)(c + ('a' - 'A')) : c).ToArray())));
                        // GLOB supplies the ASCII boundary class missing from SQLite LIKE.
                        // Probe LIKE case behavior on this connection before folding ASCII text.
                        string target = $"(' ' || {columnTarget} || ' ')";
                        string foldedTarget = $"(CASE WHEN 'a' LIKE 'A' THEN lower({target}) ELSE {target} END)";
                        string foldedPattern = $"(CASE WHEN 'a' LIKE 'A' THEN {lowerPattern} ELSE {wordPattern} END)";
                        builder.Append($"{foldedTarget} {(Operator == SqlOp.NOTLIKEWORD ? "NOT GLOB" : "GLOB")} {foldedPattern}");
                        return;
                    }
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
                    if ((flavor == DbFlavor.Sqlite || flavor == DbFlavor.DuckDb) && (Operator == SqlOp.LIKE || Operator == SqlOp.NOTLIKE))
                        builder.Append(" ESCAPE '\\'");
                    return;
            }
        }

        private static string SqliteWordPattern(string pattern)
        {
            var glob = new StringBuilder("*[^0-9A-z]");
            for (int i = 0; i < pattern.Length; i++)
            {
                char value = pattern[i];
                bool literal = value == '\\';
                if (literal)
                {
                    // A dangling LIKE escape yields an invalid GLOB that never matches.
                    if (++i == pattern.Length)
                        return "[";
                    value = pattern[i];
                }
                if (!literal && value == '%') glob.Append('*');
                else if (!literal && value == '_') glob.Append('?');
                else if (value == '*') glob.Append("[*]");
                else if (value == '?') glob.Append("[?]");
                else if (value == '[') glob.Append("[[]");
                else glob.Append(value);
            }
            return glob.Append("[^0-9A-z]*").ToString();
        }

        /// <inheritdoc/>
        public override IEnumerable<Column> GetColumnReferences()
        {
            yield return Column;
        }
    }
}
