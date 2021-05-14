using SqlLinqer.Relationships;
using System;
using System.Collections.Generic;
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
    public sealed class SQLSelectWhereQuery<TObj> : SQLWhereQuery<TObj> where TObj : SqlLinqerObject<TObj>, new()
    {
        private readonly Dictionary<string, SQLMemberInfo> _selectedColumns;
        private readonly List<(SQLMemberInfo column, SQLDir dir)> _orderBy;
        private readonly SQLOptions _options;
        private readonly Dictionary<string, SQLConfig> _joins;

        internal SQLSelectWhereQuery(int recursionLevel = 1)
            : base(recursionLevel)
        {
            _options = new SQLOptions();
            _selectedColumns = new Dictionary<string, SQLMemberInfo>();
            _orderBy = new List<(SQLMemberInfo column, SQLDir dir)>();
            _joins = new Dictionary<string, SQLConfig>();

            SelectOneToOneRecursive(Config);
        }
        internal SQLSelectWhereQuery(params Expression<Func<TObj, object>>[] columns)
            : base(recursionLevel: 1)
        {
            _options = new SQLOptions();
            _selectedColumns = new Dictionary<string, SQLMemberInfo>();
            _orderBy = new List<(SQLMemberInfo column, SQLDir dir)>();
            _joins = new Dictionary<string, SQLConfig>();

            Select(columns);
        }

        /// <summary>
        /// Select a number of columns
        /// </summary>
        /// <param name="expressions">A number of expressions that point to the columns to select</param>
        /// <returns>The current <see cref="SQLUpdateWhereQuery{TObj}"/> object</returns>
        public SQLSelectWhereQuery<TObj> Select(params Expression<Func<TObj, object>>[] expressions)
        {
            expressions
                .Where(e =>
                {
                    try
                    {
                        SQLMemberInfo column = GetMemberFromExpression(e);
                        string key = column.ColumnAlias;
                        if (!_selectedColumns.ContainsKey(key))
                            _selectedColumns.Add(key, column);

                        return false;
                    }
                    catch (Exception)
                    {
                        return true;
                    }
                })
                .ToList()
                .ForEach(e =>
                {
                    UpgradeConfig(1);

                    SQLRelationship rel = GetRelationshipFromExpression(e);
                    if (rel.Right.Config.PrimaryKey != null)
                    {
                        var column = rel.Right.Config.PrimaryKey;
                        string key = column.ColumnAlias;
                        if (!_selectedColumns.ContainsKey(key))
                            _selectedColumns.Add(key, column);
                    }
                    rel.Right.Config.Columns
                    .ForEach(column =>
                    {
                        string key = column.ColumnAlias;
                        if (_selectedColumns.ContainsKey(key)) return;
                        _selectedColumns.Add(key, column);
                    });
                    JoinParentConfigsRecursive(rel);
                });

            return this;
        }

        /// <summary>
        /// Add a where group to the where query
        /// </summary>
        /// <param name="whereGroup">the <see cref="SQLWhereGroup{TObj}"/> to add to the query</param>
        /// <returns>The current <see cref="SQLUpdateWhereQuery{TObj}"/> object</returns>
        public new SQLSelectWhereQuery<TObj> Where(SQLWhereGroup<TObj> whereGroup)
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
        public new SQLSelectWhereQuery<TObj> Where(Expression<Func<TObj, object>> expression, object value, SQLOp op = SQLOp.EQ)
        {
            base.Where(expression, value, op);
            return this;
        }

        /// <summary>
        /// First order by this column
        /// </summary>
        /// <param name="expression">The column to order by</param>
        /// <param name="dir">The direction of the order by</param>
        /// <returns>The current <see cref="SQLSelectWhereQuery{TObj}"/> object</returns>
        public SQLSelectWhereQuery<TObj> OrderBy(Expression<Func<TObj, object>> expression, SQLDir dir = SQLDir.ASC)
        {
            _orderBy.Clear();
            _orderBy.Add((GetMemberFromExpression(expression), dir));
            return this;
        }
        /// <summary>
        /// Then order by after the previous order by
        /// </summary>
        /// <param name="expression">The column to order by</param>
        /// <param name="dir">The direction of the order by</param>
        /// <returns>The current <see cref="SQLSelectWhereQuery{TObj}"/> object</returns>
        public SQLSelectWhereQuery<TObj> ThenBy(Expression<Func<TObj, object>> expression, SQLDir dir = SQLDir.ASC)
        {
            _orderBy.Add((GetMemberFromExpression(expression), dir));
            return this;
        }

        /// <summary>
        /// Selects only the distinct rows from the results.
        /// A distinct row is where all column values do not match all column values of another row
        /// </summary>
        /// <returns>The current <see cref="SQLSelectWhereQuery{TObj}"/> object</returns>
        public SQLSelectWhereQuery<TObj> Distinct(bool distinct = true)
        {
            _options.distinct = distinct;
            return this;
        }
        /// <summary>
        /// Returns a sub set of the full result set from the database
        /// </summary>
        /// <param name="page">The page to return</param>
        /// <param name="pageSize">The size of each page</param>
        /// <returns>The current <see cref="SQLSelectWhereQuery{TObj}"/> object</returns>
        public SQLSelectWhereQuery<TObj> Page(int page, int pageSize)
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
        /// <returns>The current <see cref="SQLSelectWhereQuery{TObj}"/> object</returns>
        public SQLSelectWhereQuery<TObj> Top(int count)
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
        public SQLSelectResponse<List<TObj>> Run(SqlLinqerConnector connector = null, bool GetTotalResults = true)
        {
            SetConnector(connector);

            var result = new SQLSelectResponse<List<TObj>>();

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
                if (Config.PrimaryKey != null)
                    _orderBy.Add((Config.PrimaryKey, SQLDir.ASC));
                else
                    _orderBy.Add((Config.Columns.First(), SQLDir.ASC));
            }

            DbCommand cmd = conn.CreateCommand();
            DbCommand countcmd = null;

            GetTotalResults = GetTotalResults && _options.pageSize > 0;
            if (GetTotalResults && DBType == DBType.MYSQL)
                countcmd = conn.CreateCommand();

            GetJoins(_joins);

            if (!_options.distinct.HasValue)
            {
                var otmJoins = _joins
                    .Select(j => j.Value).Where(c => c.ParentRelationship is SQLOneToManyRelationship)
                    .ToDictionary(x => x.TableAlias, x => x);
                if (_selectedColumns.Any(c => otmJoins.ContainsKey(c.Value.Config.TableAlias)))
                    _options.distinct = true;
            }

            cmd.CommandText = $"SELECT{((_options.distinct ?? false) ? " DISTINCT" : null)} {GetColumnStr()} ";

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

            if (_orderBy.Count() > 0)
            {
                StringBuilder builder = new StringBuilder();
                foreach (var (column, dir) in _orderBy)
                {
                    if (builder.Length == 0)
                        builder.Append(" ORDER BY ");
                    else
                        builder.Append(",");

                    builder.Append($"{Wrap(column.ColumnAlias)} {(dir == SQLDir.ASC ? "ASC" : "DESC")}");
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
                result.Result = PopulateObject(response.Result, useColumnAlias: true);
                result.TotalResults = result.Result.Count;
            }
            catch (Exception err)
            {
                return result.SetError(new SQLResponseException(cmd.CommandText, innerException: err));
            }

            if (GetTotalResults)
            {
                if (DBType != DBType.MYSQL)
                {
                    try
                    {
                        result.TotalResults = Convert.ToInt64(response.Result.Rows[0]["TotalResults"]);
                    }
                    catch (Exception err)
                    {
                        result.Error = new SQLResponseException(cmd.CommandText, innerException: err);
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

        private void SelectOneToOneRecursive(SQLConfig config)
        {
            config.Columns
                .ForEach(column =>
                {
                    string key = column.ColumnAlias;
                    if (_selectedColumns.ContainsKey(key)) return;
                    _selectedColumns.Add(key, column);
                });

            if (config.PrimaryKey != null)
            {
                string key = config.PrimaryKey.ColumnAlias;
                if (!_selectedColumns.ContainsKey(key))
                    _selectedColumns.Add(key, config.PrimaryKey);
            }

            config.OneToOne.ForEach(oto => SelectOneToOneRecursive(oto.Right.Config));
        }
        private string GetColumnStr()
        {
            var columnList = _selectedColumns.Select(x => x.Value);

            var columns = new List<string>();

            foreach (var column in columnList)
            {
                columns.Add($"{Wrap(column.Config.TableAlias)}.{Wrap(column.SQLName)} AS {Wrap(column.ColumnAlias)}");
            }

            return string.Join(",", columns);
        }
        private new void GetJoins(Dictionary<string, SQLConfig> joins)
        {
            var columnList = _selectedColumns.Select(x => x.Value);
            columnList = _orderBy.Select(x => x.column).Concat(columnList);

            foreach (var column in columnList)
            {
                if (joins.ContainsKey(column.Config.TableAlias)) continue;
                joins.Add(column.Config.TableAlias, column.Config);
            }

            base.GetJoins(joins);
        }
        private void JoinParentConfigsRecursive(SQLRelationship relationship)
        {
            if (relationship == null) return;
            if (!_joins.ContainsKey(relationship.Left.Config.TableAlias))
                _joins.Add(relationship.Left.Config.TableAlias, relationship.Left.Config);
            JoinParentConfigsRecursive(relationship.Left.Config.ParentRelationship);
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
