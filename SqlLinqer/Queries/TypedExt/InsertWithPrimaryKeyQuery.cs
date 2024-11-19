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
    /// A INSERT query. Based around a type with a primary key.
    /// </summary>
    /// <typeparam name="T">The type serving as the root data model</typeparam>
    /// <typeparam name="TPk">The type of the primary key</typeparam>
    public class InsertWithPrimaryKeyQuery<T, TPk>
        : InsertQuery<T>
        , IQueryTyped
        , IQueryTypedInsert<InsertWithPrimaryKeyQuery<T, TPk>, T>
        , IQueryTypedOutput<InsertWithPrimaryKeyQuery<T, TPk>, T>
        , IQueryExecutableWithPrimaryKeys<int, TPk>
        , IQueryExecutableTvpWithPrimaryKeys<int, TPk>
        where T : class
    {
        /// <summary>
        /// A INSERT query. Based around a type with a primary key.
        /// </summary>
        public InsertWithPrimaryKeyQuery(string default_schema = null) : this(Table.GetCached<T>(default_schema)) { }
        /// <summary>
        /// A INSERT query. Based around a type with a primary key.
        /// </summary>
        /// <param name="table">The root table of the query</param>
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
            return Execute(SqlLinqer.Default.Connector, transaction);
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
                    response.SetResult(baseRes.Result, primaryKeys.Cast<TPk>().ToList());
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
            return ExecuteTvp(SqlLinqer.Default.Connector, transaction);
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
                    response.SetResult(baseRes.Result, primaryKeys.Cast<TPk>().ToList());
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