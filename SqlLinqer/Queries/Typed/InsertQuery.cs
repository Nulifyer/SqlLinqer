using System;
using System.Linq;
using System.Data.Common;
using System.Linq.Expressions;
using System.Collections.Generic;
using SqlLinqer.Connections;
using SqlLinqer.Components.Outputs;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Interfaces;
using SqlLinqer.Extensions.MemberExpressionExtensions;
using SqlLinqer.Components.Render;
using System.Data;

namespace SqlLinqer.Queries.Typed
{
    /// <summary>
    /// A INSERT query. Based around a type.
    /// </summary>
    /// <typeparam name="T">The type serving as the root data model</typeparam>
    public class InsertQuery<T>
        : Core.InsertQuery
        , IQueryTyped
        , IQueryTypedInsert<InsertQuery<T>, T>
        , IQueryTypedOutput<InsertQuery<T>, T>
        , IQueryExecutable<int>
        , IQueryExecutableTvp<int>
        , IQueryExecutableWithOutput
        where T : class
    {
        /// <summary>
        /// A INSERT query. Based around a type.
        /// </summary>
        public InsertQuery(string default_schema = null) : this(Table.GetCached<T>(default_schema)) { }
        /// <summary>
        /// A INSERT query. Based around a type.
        /// </summary>
        /// <param name="table">The root table of the query</param>
        public InsertQuery(Table table) : base(table)
        {

        }

        private IEnumerable<ReflectedColumn> SetupInsertAutoColumns()
        {
            var InsertAutoCols = Table.Columns
                .GetAll<ReflectedColumn>()
                .Where(x => x.AutoOptions.Insert);

            foreach (var col in InsertAutoCols)
                Inserts.AddColumn(col);

            return InsertAutoCols;
        }
        /// <inheritdoc/>
        public InsertQuery<T> InsertAuto(T obj)
        {
            var cols = SetupInsertAutoColumns();
            Inserts.AddRow(obj);
            return this;
        }
        /// <inheritdoc/>
        public InsertQuery<T> InsertAuto(IEnumerable<T> objs)
        {
            var cols = SetupInsertAutoColumns();
            Inserts.AddRows(objs);
            return this;
        }
        /// <inheritdoc/>
        public InsertQuery<T> Output<TProperty>(Expression<Func<T, TProperty>> expression)
        {
            var path = expression.GetMemberPath();
            var column = Table.FindColumnFromPath(path);
            if (column == null)
                throw new SqlLinqer.Exceptions.SqlPathNotFoundException(Table, path);
            else if (column.Table.UUID != Table.UUID)
            {
                throw new FormatException($"Colunn is not a root column from {Table}");
            }

            Outputs.AddOutput(new ColumnOutput(column, "Inserted"));
            return this;
        }

        /// <inheritdoc/>
        public SqlResponse<int> Execute(DbTransaction transaction = null)
        {
            return Execute(SqlLinqer.Default.Connector, transaction);
        }
        /// <inheritdoc/>
        public SqlResponse<int> Execute(BaseConnector connector, DbTransaction transaction = null)
        {
            return ExecuteAndCapturePrimaryKeys(connector, transaction, null);
        }

        /// <inheritdoc/>
        public SqlResponse<int> ExecuteTvp(DbTransaction existingTransaction = null)
        {
            return ExecuteTvp(SqlLinqer.Default.Connector, existingTransaction);
        }
        /// <inheritdoc/>
        public SqlResponse<int> ExecuteTvp(BaseConnector connector, DbTransaction existingTransaction = null)
        {
            return ExecuteTvpAndCapturePrimaryKeys(connector, existingTransaction, null);
        }

        /// <inheritdoc/>
        public SqlResponse<DataTable> ExecuteWithOutput(DbTransaction transaction = null)
        {
            return ExecuteWithOutput(SqlLinqer.Default.Connector, transaction);
        }
        /// <inheritdoc/>
        public SqlResponse<DataTable> ExecuteWithOutput(BaseConnector connector, DbTransaction transaction = null)
        {
            if (connector == null)
                throw new ArgumentNullException("Either pass a connector or set the default connector.", nameof(connector));
            var rendered = this.Render(connector);
            return connector.ExecuteReaderDataTable(rendered, transaction);
        }

        /// <inheritdoc cref="ExecuteTvp(DbTransaction)"/>
        public SqlResponse<DataTable> ExecuteTvpWithOutput(DbTransaction transaction = null)
        {
            return ExecuteTvpWithOutput(SqlLinqer.Default.Connector, transaction);
        }
        /// <inheritdoc cref="ExecuteTvp(BaseConnector, DbTransaction)"/>
        public SqlResponse<DataTable> ExecuteTvpWithOutput(BaseConnector connector, DbTransaction transaction = null)
        {
            return ExecuteTvpCore(connector, transaction,
                (query, tx) => connector.ExecuteReaderDataTable(query, tx));
        }

        internal SqlResponse<int> ExecuteAndCapturePrimaryKeys(BaseConnector connector, DbTransaction existingTransaction = null, List<object> primaryKeys = null)
        {
            if (connector == null)
                throw new ArgumentNullException("Either pass a connector or set the default connector.", nameof(connector));

            if (this.Inserts.Count == 0)
            {
                var zero_res = new SqlResponse<int>();
                zero_res.SetResult(0);
                return zero_res;
            }

            var rendered = this.Render(connector);

            var response = new SqlResponse<int>();
            ISqlResponse executedRes;
            if (primaryKeys == null)
            {
                var resNon = connector.ExecuteNonQuery(rendered, existingTransaction);
                if (resNon.State == ResponseState.Valid)
                    response.SetResult(resNon.Result);
                executedRes = resNon;
            }
            else
            {
                var primaryKeyData = connector.ExecuteReaderDataTable(rendered, existingTransaction);
                if (primaryKeyData.State == ResponseState.Valid)
                {
                    foreach (System.Data.DataRow row in primaryKeyData.Result.Rows)
                    {
                        primaryKeys.Add(row[0]);
                    }
                    response.SetResult(primaryKeys.Count);
                }
                executedRes = primaryKeyData;
            }

            if (executedRes.State == ResponseState.Error)
                response.SetError(executedRes.Error);

            return response;
        }
        internal SqlResponse<int> ExecuteTvpAndCapturePrimaryKeys(BaseConnector connector, DbTransaction existingTransaction = null, List<object> primaryKeys = null)
        {
            if (connector == null)
                throw new ArgumentNullException(nameof(connector));
            if (Inserts.Count == 0)
                return new SqlResponse<int>(0);
            return ExecuteTvpCore(connector, existingTransaction, (query, tx) =>
            {
                if (primaryKeys == null)
                    return connector.ExecuteNonQuery(query, tx);
                var rows = connector.ExecuteReaderDataTable(query, tx);
                if (rows.State == ResponseState.Error)
                {
                    var failed = new SqlResponse<int>();
                    failed.SetError(rows.Error);
                    return failed;
                }
                using (var table = rows.Result)
                    foreach (DataRow row in table.Rows)
                        primaryKeys.Add(row[0]);
                return new SqlResponse<int>(primaryKeys.Count);
            });
        }

        private SqlResponse<TResult> ExecuteTvpCore<TResult>(BaseConnector connector, DbTransaction existingTransaction,
            Func<RenderedQuery, DbTransaction, SqlResponse<TResult>> execute)
        {
            if (connector == null)
                throw new ArgumentNullException(nameof(connector));

            var tempQuery = new RenderedQuery();
            var (query, tempTypeId) = RenderTVP(tempQuery.Parameters, connector.DbFlavor, Table.Schema);
            tempQuery.Text = query;
            var (createType, dropType) = RenderTempType(connector.DbFlavor, tempTypeId);
            var response = new SqlResponse<TResult>();
            DbConnection conn = null;
            DbTransaction transaction = null;
            bool typeCreated = false;
            try
            {
                conn = existingTransaction == null
                    ? connector.CreateConnection()
                    : existingTransaction.Connection ?? throw new InvalidOperationException("The supplied transaction has no active connection.");
                if (conn.State != ConnectionState.Open)
                    conn.Open();
                transaction = existingTransaction ?? conn.BeginTransaction(IsolationLevel.ReadCommitted);

                var create = connector.ExecuteNonQuery(new RenderedQuery(createType), transaction);
                if (create.State == ResponseState.Error) throw create.Error;
                typeCreated = true;
                response = execute(tempQuery, transaction);
                if (response.State == ResponseState.Error) throw response.Error;
                var drop = connector.ExecuteNonQuery(new RenderedQuery(dropType), transaction);
                if (drop.State == ResponseState.Error) throw drop.Error;
                typeCreated = false;
                if (existingTransaction == null) transaction.Commit();
            }
            catch (Exception error)
            {
                if (existingTransaction == null && transaction != null)
                {
                    try { transaction.Rollback(); }
                    catch { }
                }
                else if (typeCreated)
                {
                    // A usable caller transaction can clean up immediately. A doomed one must
                    // be rolled back by its owner, which also rolls back CREATE TYPE.
                    try { connector.ExecuteNonQuery(new RenderedQuery(dropType), transaction); }
                    catch { }
                }
                response.SetError(error);
            }
            finally
            {
                if (existingTransaction == null)
                {
                    try { transaction?.Dispose(); }
                    finally { conn?.Dispose(); }
                }
                (tempQuery.Parameters.GetAll().OfType<TvpParameter>().FirstOrDefault()?.Value as DataTable)?.Dispose();
            }
            return response;
        }
    }
}
