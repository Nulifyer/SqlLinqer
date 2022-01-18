using SqlLinqer.Relationships;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace SqlLinqer.Queries
{
    /// <summary>
    /// Select query with where statements
    /// </summary>
    /// <typeparam name="TObj">The class that represents the database table and its relationships</typeparam>
    /// <typeparam name="TResult">The return type of the query</typeparam>
    public sealed class SQLSelectWhereAggregateQuery<TObj, TResult> : SQLWhereQuery<TObj> where TObj : new()
    {
        private readonly Dictionary<(SQLFunc, string), (SQLFunc Func, SQLMemberInfo Column)> _selectedColumns;
        private readonly Dictionary<string, (SQLFunc Func, string ColumnAlias)> _anonToColumn;
        private readonly List<(SQLFunc Func, SQLMemberInfo Column, SQLDir Dir)> _orderBy;
        private readonly SQLOptions _options;
        private readonly Dictionary<string, SQLConfig> _joins;

        /// <summary>
        /// Select query with where statements
        /// </summary>
        public SQLSelectWhereAggregateQuery(NewExpression new_expression)
            : base(recursionLevel: 1)
        {
            _options = new SQLOptions();
            _selectedColumns = new Dictionary<(SQLFunc, string), (SQLFunc, SQLMemberInfo)>();
            _anonToColumn = new Dictionary<string, (SQLFunc, string)>();
            _orderBy = new List<(SQLFunc, SQLMemberInfo, SQLDir)>();
            _joins = new Dictionary<string, SQLConfig>();

            for (int i = 0; i < new_expression.Arguments.Count; ++i)
            {
                var arg = new_expression.Arguments[i];
                var anon_member = new_expression.Members[i];

                if (arg is MemberExpression memExpr)
                {
                    var column = GetMemberFromExpression(memExpr);
                    var key = (SQLFunc.NONE, column.ColumnAlias);
                    if (!_selectedColumns.ContainsKey(key))
                    {
                        _selectedColumns.Add(key, (SQLFunc.NONE, column));
                        _anonToColumn.Add(anon_member.Name, key);
                    }
                }
                else if (
                    arg is MethodCallExpression methodCall
                    && methodCall.Arguments.Count == 1
                    && methodCall.Arguments.First() is UnaryExpression unaryArg
                    && unaryArg.Operand is Expression<Func<TObj, object>> expression
                )
                {
                    string call_name = methodCall.Method.GetBaseDefinition().Name;
                    var sql_func_method = typeof(SqlLinqerObject<TObj>).GetMethod(call_name);

                    // is a valid SQLLinqer function call
                    if (sql_func_method != null)
                    {
                        SQLFunc func;
                        try
                        {
                            func = FindSQLFuncFromName(call_name);
                        }
                        catch (Exception)
                        {
                            throw new Exception($"{call_name} is not a valid SQL Function or is not supported by this library.");
                        }

                        var column = GetMemberFromExpression(expression);

                        switch (func)
                        {
                            case SQLFunc.DATE_TO_UNIXTIMESTAMP_SEC:
                            case SQLFunc.DATE_TO_UNIXTIMESTAMP_MS:
                                if (column.MemberUnderlyingType != typeof(DateTime) && column.MemberUnderlyingType != typeof(DateTime?))
                                    throw new InvalidOperationException($"Cannot convert {column.MemberUnderlyingType.FullName} to {typeof(DateTime).FullName}");
                                break;
                        }

                        var key = (func, column.ColumnAlias);
                        if (!_selectedColumns.ContainsKey(key))
                        {
                            _selectedColumns.Add(key, (func, column));
                            _anonToColumn.Add(anon_member.Name, key);
                        }

                        continue;
                    }

                    throw new Exception($"{call_name} is not a valid SQL Function or is not supported by this library.");
                }
                else
                {
                    throw new InvalidOperationException($"The expression {arg} is invalid in this context as it can't be converted to T-SQL.");
                }
            }
        }

        /// <summary>
        /// Add a where group to the where query
        /// </summary>
        /// <param name="whereGroup">the <see cref="SQLWhereGroup{TObj}"/> to add to the query</param>
        /// <returns>The current <see cref="SQLUpdateWhereQuery{TObj}"/> object</returns>
        public new SQLSelectWhereAggregateQuery<TObj, TResult> Where(SQLWhereGroup<TObj> whereGroup)
        {
            base.Where(whereGroup);
            return this;
        }
        /// <summary>
        /// Add a where clause to the where query
        /// </summary>
        /// <param name="expression">A expression that points to the property/field of the class the where clause applies to</param>
        /// <param name="value">The value or to the where clause is evaluating against</param>
        /// <param name="op">The <see cref="SQLOp"/> operator to apply to the clause</param>
        /// <returns>The current <see cref="SQLUpdateWhereQuery{TObj}"/> object</returns>
        public new SQLSelectWhereAggregateQuery<TObj, TResult> Where(Expression<Func<TObj, object>> expression, object value, SQLOp op = SQLOp.EQ)
        {
            base.Where(expression, value, op);
            return this;
        }

        /// <summary>
        /// First order by this column
        /// </summary>
        /// <param name="expression">The column to order by</param>
        /// <param name="dir">The direction of the order by</param>
        /// <returns>The current <see cref="SQLSelectWhereAggregateQuery{TObj, TResult}"/> object</returns>
        public SQLSelectWhereAggregateQuery<TObj, TResult> OrderBy(Expression<Func<TResult, object>> expression, SQLDir dir = SQLDir.ASC)
        {
            _orderBy.Clear();
            ThenBy(expression, dir);
            return this;
        }
        /// <summary>
        /// Then order by after the previous order by
        /// </summary>
        /// <param name="expression">The column to order by</param>
        /// <param name="dir">The direction of the order by</param>
        /// <returns>The current <see cref="SQLSelectWhereAggregateQuery{TObj, TResult}"/> object</returns>
        public SQLSelectWhereAggregateQuery<TObj, TResult> ThenBy(Expression<Func<TResult, object>> expression, SQLDir dir = SQLDir.ASC)
        {
            var anon_member = GetMember(expression);
            string anon_name = anon_member.Member.Name;
            var col_key = _anonToColumn[anon_name];
            var resulted_col = _selectedColumns[col_key];
            _orderBy.Add((resulted_col.Func, resulted_col.Column, dir));
            return this;
        }

        /// <summary>
        /// Selects only the distinct rows from the results.
        /// A distinct row is where all column values do not match all column values of another row
        /// </summary>
        /// <returns>The current <see cref="SQLSelectWhereAggregateQuery{TObj, TResult}"/> object</returns>
        public SQLSelectWhereAggregateQuery<TObj, TResult> Distinct(bool distinct = true)
        {
            _options.distinct = distinct;
            return this;
        }
        /// <summary>
        /// Returns a sub set of the full result set from the database
        /// </summary>
        /// <param name="page">The page to return</param>
        /// <param name="pageSize">The size of each page</param>
        /// <returns>The current <see cref="SQLSelectWhereAggregateQuery{TObj, TResult}"/> object</returns>
        public SQLSelectWhereAggregateQuery<TObj, TResult> Page(int page, int pageSize)
        {
            _options.page = page;
            _options.pageSize = pageSize;
            _options.top = 0;
            return this;
        }
        /// <summary>
        /// Select only the top <paramref name="count"/> of results
        /// </summary>
        /// <param name="count">The number of results</param>
        /// <returns>The current <see cref="SQLSelectWhereAggregateQuery{TObj, TResult}"/> object</returns>
        public SQLSelectWhereAggregateQuery<TObj, TResult> Top(int count)
        {
            _options.page = 0;
            _options.pageSize = 0;
            _options.top = count;
            return this;
        }

        /// <summary>
        /// Execute the query and return results
        /// </summary>
        /// <param name="connector">The <see cref="SqlLinqerConnector"/> to use. If null the <see cref="Default.Connector"/> is used instead.</param>
        /// <param name="GetTotalResults">If to include the total possible results in the query, setting to false may improve performace</param>
        /// <returns>A <see cref="SQLResponse{T}"/> containing the results or the error</returns>
        public SQLSelectResponse<List<TResult>> Run(SqlLinqerConnector connector = null, bool GetTotalResults = true)
        {
            SetConnector(connector);

            var result = new SQLSelectResponse<List<TResult>>();

            DbConnection conn;
            try
            {
                conn = CreateConnection();
            }
            catch (Exception err)
            {
                return result.SetError(err);
            }

            // build command string
            if (_options.pageSize > 0 && (_orderBy == null || _orderBy.Count == 0))
            {
                throw new FormatException("You must order by a column in order to page the result set.");
            }

            DbCommand cmd = conn.CreateCommand();
            DbCommand countcmd = null;

            GetTotalResults = GetTotalResults && _options.pageSize > 0;
            if (GetTotalResults && DBType == DBType.MYSQL)
                countcmd = conn.CreateCommand();

            GetJoins(_joins);

            cmd.CommandText = $"SELECT{((_options.distinct ?? false) ? " DISTINCT" : null)}{(_options.top > 0 ? $" TOP {_options.top}" : null)} {GetColumnStr()} ";

            if (GetTotalResults && DBType != DBType.MYSQL)
                cmd.CommandText += ",COUNT(*) OVER() AS TotalResults";

            cmd.CommandText += $" FROM {RenderJoins(_joins)} ";

            RenderWhere(cmd);

            if (countcmd != null)
            {
                countcmd.CommandText += $"SELECT COUNT(*) as {Wrap("count")} FROM ({cmd.CommandText}) as {Wrap("query")}";

                countcmd.Parameters.AddRange(
                    cmd.Parameters
                        .Cast<ICloneable>()
                        .Select(x => x.Clone() as DbParameter)
                        .Where(x => x != null)
                        .ToArray()
                );
            }

            var non_grouped_functions = new[]
            {
                SQLFunc.NONE,
                SQLFunc.DATE_TO_UNIXTIMESTAMP_SEC,
                SQLFunc.DATE_TO_UNIXTIMESTAMP_MS
            };
            var group_by_cols = _selectedColumns.Values
                .Where(x => non_grouped_functions.Contains(x.Func));
            if (group_by_cols.Count() > 0)
            {
                cmd.CommandText += " GROUP BY " + string.Join(", ", group_by_cols.Select(x => $"{Wrap(x.Column.Config.TableAlias)}.{Wrap(x.Column.SQLName)}"));
            }

            if (_orderBy.Count() > 0)
            {
                StringBuilder builder = new StringBuilder();
                foreach (var (func, column, dir) in _orderBy)
                {
                    if (builder.Length == 0)
                        builder.Append(" ORDER BY ");
                    else
                        builder.Append(",");

                    switch (func)
                    {
                        case SQLFunc.NONE:
                            builder.Append($"{Wrap(column.ColumnAlias)} {(dir == SQLDir.ASC ? "ASC" : "DESC")}");
                            break;
                        default:
                            builder.Append($"{Wrap($"{column.ColumnAlias}_FUNC#{func}")} {(dir == SQLDir.ASC ? "ASC" : "DESC")}");
                            break;
                    }
                }
                cmd.CommandText += builder.ToString();
            }

            if (_options.pageSize > 0)
            {
                if (_options.page < 1) _options.page = 1;

                int startIdx = (_options.page - 1) * _options.pageSize;
                int endIdx = startIdx + _options.pageSize;

                switch (DBType)
                {
                    case DBType.PostgreSQL:
                        cmd.CommandText += $" LIMIT {_options.pageSize} OFFSET {startIdx}";
                        break;
                    case DBType.MYSQL:
                        cmd.CommandText += $" LIMIT {startIdx}, {_options.pageSize}";
                        break;
                    case DBType.OracleSQL:
                    case DBType.SQLServer:
                        cmd.CommandText += $" OFFSET {startIdx} ROWS FETCH NEXT {_options.pageSize} ROWS ONLY";
                        break;
                }
            }

            SQLResponse<DataTable> response = ExecuteReader(cmd);
            if (response.State == ResponseState.Error)
                return result.SetError(response.Error);

            try
            {
                result.Result = PopulateObject(response.Result);
                result.TotalResults = result.Result.Count;
            }
            catch (Exception err)
            {
                return result.SetError(new SQLResponseException(cmd, innerException: err));
            }

            if (GetTotalResults)
            {
                if (DBType != DBType.MYSQL)
                {
                    try
                    {
                        result.TotalResults = response.Result.Rows.Count < 1 ? 0 : Convert.ToInt64(response.Result.Rows[0]["TotalResults"]);
                    }
                    catch (Exception err)
                    {
                        result.Error = new SQLResponseException(cmd, innerException: err);
                    }
                }
                else
                {
                    SQLResponse<long> countResponse = ExecuteScalar<long>(countcmd);
                    if (countResponse.State == ResponseState.Error)
                        return result.SetError(response.Error);

                    result.TotalResults = countResponse.Result;
                }
            }

            return result;
        }

        private SQLFunc FindSQLFuncFromName(string call_name)
        {
            var sql_func_method = typeof(SqlLinqerObject<TObj>).GetMethod(call_name);

            // is a valid SQLLinqer function call
            if (sql_func_method != null)
            {
                switch (call_name)
                {
                    case nameof(SqlLinqerObject<TObj>.SQLAvg): return SQLFunc.AVG;
                    case nameof(SqlLinqerObject<TObj>.SQLChecksumAgg): return SQLFunc.CHECKSUM_AGG;
                    case nameof(SqlLinqerObject<TObj>.SQLCount): return SQLFunc.COUNT;
                    case nameof(SqlLinqerObject<TObj>.SQLCountBig): return SQLFunc.COUNT_BIG;
                    case nameof(SqlLinqerObject<TObj>.SQLMax): return SQLFunc.MAX;
                    case nameof(SqlLinqerObject<TObj>.SQLMin): return SQLFunc.MIN;
                    case nameof(SqlLinqerObject<TObj>.SQLStdev): return SQLFunc.STDEV;
                    case nameof(SqlLinqerObject<TObj>.SQLStdevp): return SQLFunc.STDEVP;
                    case nameof(SqlLinqerObject<TObj>.SQLDateToUnixtimestampSec): return SQLFunc.DATE_TO_UNIXTIMESTAMP_SEC;
                    case nameof(SqlLinqerObject<TObj>.SQLDateToUnixtimestampMs): return SQLFunc.DATE_TO_UNIXTIMESTAMP_MS;
                }
            }

            throw new Exception($"{call_name} is not a valid SQL Function or is not supported by this library.");
        }

        private string GetColumnStr()
        {
            var columnList = _selectedColumns.Select(x => x.Value);

            var columns = new List<string>();

            foreach ((SQLFunc func, SQLMemberInfo column) in columnList)
            {
                switch (func)
                {
                    case SQLFunc.NONE:
                        columns.Add($"{Wrap(column.Config.TableAlias)}.{Wrap(column.SQLName)} AS {Wrap(column.ColumnAlias)}");
                        break;
                    case SQLFunc.DATE_TO_UNIXTIMESTAMP_SEC:
                        columns.Add($"(case when {Wrap(column.Config.TableAlias)}.{Wrap(column.SQLName)} IS NULL then NULL else DATEDIFF_BIG(second, {{d '1970-01-01'}}, {Wrap(column.Config.TableAlias)}.{Wrap(column.SQLName)}) end) AS {Wrap($"{column.ColumnAlias}_FUNC#{func}")}");
                        break;
                    case SQLFunc.DATE_TO_UNIXTIMESTAMP_MS:
                        columns.Add($"(case when {Wrap(column.Config.TableAlias)}.{Wrap(column.SQLName)} IS NULL then NULL else DATEDIFF_BIG(millisecond, {{d '1970-01-01'}}, {Wrap(column.Config.TableAlias)}.{Wrap(column.SQLName)}) end) AS {Wrap($"{column.ColumnAlias}_FUNC#{func}")}");
                        break;
                    case SQLFunc.AVG:
                        columns.Add($"AVG({Wrap(column.Config.TableAlias)}.{Wrap(column.SQLName)}) AS {Wrap($"{column.ColumnAlias}_FUNC#{func}")}");
                        break;
                    case SQLFunc.CHECKSUM_AGG:
                        columns.Add($"CHECKSUM_AGG({Wrap(column.Config.TableAlias)}.{Wrap(column.SQLName)}) AS {Wrap($"{column.ColumnAlias}_FUNC#{func}")}");
                        break;
                    case SQLFunc.COUNT:
                        columns.Add($"COUNT({Wrap(column.Config.TableAlias)}.{Wrap(column.SQLName)}) AS {Wrap($"{column.ColumnAlias}_FUNC#{func}")}");
                        break;
                    case SQLFunc.COUNT_BIG:
                        columns.Add($"COUNT_BIG({Wrap(column.Config.TableAlias)}.{Wrap(column.SQLName)}) AS {Wrap($"{column.ColumnAlias}_FUNC#{func}")}");
                        break;
                    case SQLFunc.MAX:
                        columns.Add($"MAX({Wrap(column.Config.TableAlias)}.{Wrap(column.SQLName)}) AS {Wrap($"{column.ColumnAlias}_FUNC#{func}")}");
                        break;
                    case SQLFunc.MIN:
                        columns.Add($"MIN({Wrap(column.Config.TableAlias)}.{Wrap(column.SQLName)}) AS {Wrap($"{column.ColumnAlias}_FUNC#{func}")}");
                        break;
                    case SQLFunc.STDEV:
                        columns.Add($"STDEV({Wrap(column.Config.TableAlias)}.{Wrap(column.SQLName)}) AS {Wrap($"{column.ColumnAlias}_FUNC#{func}")}");
                        break;
                    case SQLFunc.STDEVP:
                        columns.Add($"STDEVP({Wrap(column.Config.TableAlias)}.{Wrap(column.SQLName)}) AS {Wrap($"{column.ColumnAlias}_FUNC#{func}")}");
                        break;
                    default:
                        throw new NotImplementedException("This function has not been implemented.");
                }
            }

            return string.Join(",", columns);
        }
        private new void GetJoins(Dictionary<string, SQLConfig> joins)
        {
            var columnList = _selectedColumns.Values.Select(x => x.Column);

            foreach (var column in columnList)
            {
                if (joins.ContainsKey(column.Config.TableAlias)) continue;
                joins.Add(column.Config.TableAlias, column.Config);
                JoinParentConfigsRecursive(column.Config, joins);
            }

            base.GetJoins(joins);
        }
        private static void JoinParentConfigsRecursive(SQLConfig config, Dictionary<string, SQLConfig> joins)
        {
            if (config.ParentRelationship == null) return;

            config = config.ParentRelationship.Left.Config;
            if (!joins.ContainsKey(config.TableAlias))
                joins.Add(config.TableAlias, config);

            JoinParentConfigsRecursive(config, joins);
        }

        private List<TResult> PopulateObject(DataTable table)
        {
            var type = typeof(TResult);
            var objs = new List<TResult>();
            foreach (DataRow row in table.Rows)
            {
                objs.Add((TResult)PopulateObject(type, row));
            }
            return objs;
        }
        private object PopulateObject(Type type, DataRow row)
        {
            object[] args = _anonToColumn.Keys
                .Select(anon_member =>
                {
                    var anon_col = _anonToColumn[anon_member];
                    var anon_prop = type.GetProperty(anon_member);
                    string col = anon_col.ColumnAlias;
                    if (anon_col.Func != SQLFunc.NONE)
                        col = $"{col}_FUNC#{anon_col.Func}";

                    if (row.Table.Columns.Contains(col))
                    {
                        try
                        {
                            if (row[col].GetType() == anon_prop.PropertyType)
                                return (object)row[col];
                            else
                            {
                                TypeConverter typeConverter = TypeDescriptor.GetConverter(anon_prop.PropertyType);
                                try
                                {
                                    return (object)row[col] == DBNull.Value ? default : typeConverter.ConvertFrom(row[col]);
                                }
                                catch (Exception)
                                {
                                    return (object)typeConverter.ConvertFrom(row[col].ToString());
                                }
                            }
                        }
                        catch (ArgumentException err)
                        {
                            throw new FormatException($"Failed to set the value of {type.FullName}.{anon_prop.Name}", err);
                        }
                    }

                    throw new Exception($"Failed to set the value of {type.FullName}.{anon_prop.Name}");
                })
                .ToArray();

            return Activator.CreateInstance(type, args);
        }

        private class SQLOptions
        {
            public int top;
            public int page;
            public int pageSize;
            public bool? distinct;

            internal SQLOptions()
            {
                top = 0;
                page = 0;
                pageSize = 0;
            }
        }
    }
}
