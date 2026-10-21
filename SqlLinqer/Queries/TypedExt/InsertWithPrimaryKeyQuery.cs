using System;
using System.Linq;
using System.Data.Common;
using System.Linq.Expressions;
using System.Collections.Generic;
using SqlLinqer.Connections;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Interfaces;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Components.Outputs;

namespace SqlLinqer.Queries.TypedExt
{
    /// <summary>
    /// Builds inserts that return affected-row counts and inserted primary keys. Database-generated keys are omitted from automatic inputs.
    /// MySQL modified-row output is unsupported.
    /// </summary>
    /// <typeparam name="T">The root model with a SqlPrimaryKey member.</typeparam>
    /// <typeparam name="TPk">The primary key value type.</typeparam>
    public partial class InsertWithPrimaryKeyQuery<T, TPk>
        : InsertQuery<T>
        , IQueryTyped
        , IQueryTypedInsert<InsertWithPrimaryKeyQuery<T, TPk>, T>
        , IQueryTypedOutput<InsertWithPrimaryKeyQuery<T, TPk>, T>
        , IQueryExecutableWithPrimaryKeys<int, TPk>
        , IAsyncQueryExecutableWithPrimaryKeys<int, TPk>
        , IQueryExecutableTvpWithPrimaryKeys<int, TPk>
        , IAsyncQueryExecutableTvpWithPrimaryKeys<int, TPk>
        where T : class
    {
        /// <summary>
        /// Creates a insert builder that captures affected primary keys. A primary-key mapping is required.
        /// </summary>
        /// <param name="default_schema">The fallback schema for the cached model mapping.</param>
        public InsertWithPrimaryKeyQuery(string default_schema = null) : this(Table.GetCached<T>(default_schema)) { }
        /// <summary>
        /// Creates a insert builder that captures affected primary keys. A primary-key mapping is required.
        /// </summary>
        /// <param name="table">The root mapping. It must define a primary key.</param>
        public InsertWithPrimaryKeyQuery(Table table) : base(table)
        {
            if (table.PrimaryKey == null)
                throw new System.FormatException($"Table '{table.Name}' has no primary key.");
            Outputs.AddOutput(new ColumnOutput(table.PrimaryKey, "Inserted"));
        }

        /// <inheritdoc/>
        public new InsertWithPrimaryKeyQuery<T, TPk> InsertAuto(T obj)
        {
            base.InsertAuto(obj);
            return this;
        }
        /// <inheritdoc/>
        public new InsertWithPrimaryKeyQuery<T, TPk> InsertAuto(IEnumerable<T> objs)
        {
            base.InsertAuto(objs);
            return this;
        }

        /// <inheritdoc/>
        public new InsertWithPrimaryKeyQuery<T, TPk> Output<TProperty>(Expression<Func<T, TProperty>> expression)
        {
            base.Output<TProperty>(expression);
            return this;
        }

        /// <inheritdoc/>
        public new SqlPrimaryKeysResponse<int, TPk> Execute(DbTransaction transaction = null)
        {
            return Execute(ExecutionConnector, transaction);
        }
        /// <inheritdoc/>
        public new SqlPrimaryKeysResponse<int, TPk> Execute(BaseConnector connector, DbTransaction transaction = null)
        {
            var response = new SqlPrimaryKeysResponse<int, TPk>();
            var primaryKeys = new List<object>();

            var baseRes = base.ExecuteAndCapturePrimaryKeys(connector, transaction, primaryKeys);

            switch (baseRes.State)
            {
                case ResponseState.Valid:
                    response.SetResult(baseRes.Result, primaryKeys.Select(x => connector.DbFlavor == DbFlavor.Sqlite
                        ? Components.Render.SqliteValue.ConvertTo<TPk>(x) : connector.DbFlavor == DbFlavor.DuckDb
                        ? DbValueConversion.ConvertTo<TPk>(x) : (TPk)x).ToList());
                    break;
                case ResponseState.Error:
                    response.SetError(baseRes.Error);
                    break;
                default:
                    throw new System.NotSupportedException($"The {nameof(ResponseState)} {baseRes.State} is not supported");
            }

            return response;
        }

        /// <inheritdoc/>
        public new SqlPrimaryKeysResponse<int, TPk> ExecuteTvp(DbTransaction transaction = null)
        {
            return ExecuteTvp(ExecutionConnector, transaction);
        }
        /// <inheritdoc/>
        public new SqlPrimaryKeysResponse<int, TPk> ExecuteTvp(BaseConnector connector, DbTransaction transaction = null)
        {
            var response = new SqlPrimaryKeysResponse<int, TPk>();
            var primaryKeys = new List<object>();

            var baseRes = base.ExecuteTvpAndCapturePrimaryKeys(connector, transaction, primaryKeys);

            switch (baseRes.State)
            {
                case ResponseState.Valid:
                    response.SetResult(baseRes.Result, primaryKeys.Select(x => connector.DbFlavor == DbFlavor.Sqlite
                        ? Components.Render.SqliteValue.ConvertTo<TPk>(x) : connector.DbFlavor == DbFlavor.DuckDb
                        ? DbValueConversion.ConvertTo<TPk>(x) : (TPk)x).ToList());
                    break;
                case ResponseState.Error:
                    response.SetError(baseRes.Error);
                    break;
                default:
                    throw new System.NotSupportedException($"The {nameof(ResponseState)} {baseRes.State} is not supported");
            }

            return response;
        }
    }
}
