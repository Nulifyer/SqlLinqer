using System;
using System.Linq;
using System.Linq.Expressions;
using System.Collections.Generic;
using SqlLinqer.Components.Outputs;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Interfaces;
using SqlLinqer.Extensions.MemberExpressionExtensions;
using System.Data;
using SqlLinqer.Components.Insert;
using SqlLinqer.Components.Update;

namespace SqlLinqer.Queries.Typed
{
    /// <summary>
    /// Builds inserts from mapped model values. Ordinary execution batches rows within parameter limits using one write transaction.
    /// SQL Server and Azure SQL cap ordinary commands at 2,100 parameters and 1,000 VALUES rows. Owned batch transactions roll back all batches on failure.
    /// </summary>
    /// <typeparam name="T">The mapped root model type.</typeparam>
    public partial class InsertQuery<T>
        : Core.InsertQuery
        , IQueryTyped
        , IQueryTypedInsert<InsertQuery<T>, T>
        , IQueryTypedOutput<InsertQuery<T>, T>
        , IQueryTypedInsertConflict<InsertQuery<T>, T>
        , IQueryExecutable<int>
        , IAsyncQueryExecutable<int>
        , IQueryExecutableTvp<int>
        , IAsyncQueryExecutableTvp<int>
        , IQueryExecutableWithOutput
        , IAsyncQueryExecutableWithOutput
        , IQueryExecutableWithTypedOutput
        , IAsyncQueryExecutableWithTypedOutput
        where T : class
    {
        /// <summary>
        /// Creates a INSERT builder for the supplied or cached model mapping.
        /// </summary>
        /// <param name="default_schema">The fallback schema for models without an explicit SqlTable schema. This does not bind an execution connector.</param>
        public InsertQuery(string default_schema = null) : this(Table.GetCached<T>(default_schema)) { }

        /// <summary>
        /// Creates a INSERT builder for the supplied or cached model mapping.
        /// </summary>
        /// <param name="table">The root table mapping, whose database objects must already exist.</param>
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
        public InsertQuery<T> Insert<TProperty>(Expression<Func<T, TProperty>> expression, SqlTime value)
        {
            Inserts.SetColumnValue(FindRootColumn(expression) as ReflectedColumn
                ?? throw new FormatException($"Expression '{expression}' does not target a mapped member."), value);
            return this;
        }

        private Column FindRootColumn(Expression expression)
        {
            var path = expression.GetMemberPath();
            var column = Table.FindColumnFromPath(path);
            if (column == null)
                throw new SqlLinqer.Exceptions.SqlPathNotFoundException(Table, path);
            if (column.Table.UUID != Table.UUID)
                throw new FormatException($"Expression '{expression}' does not target a root column. An insert conflict can only reference its own table.");
            return column;
        }

        private InsertConflict ConflictAction => Conflict ?? (Conflict = new InsertConflict());

        /// <inheritdoc/>
        public InsertQuery<T> OnConflict(params Expression<Func<T, object>>[] members)
        {
            if (members == null) throw new ArgumentNullException(nameof(members));
            ConflictAction.SetTarget(members.Select(FindRootColumn).ToList());
            return this;
        }

        /// <inheritdoc/>
        public InsertQuery<T> OnConflictConstraint(string name)
        {
            ConflictAction.SetConstraint(name);
            return this;
        }

        /// <inheritdoc/>
        public InsertQuery<T> OnConflictWhere(Action<SqlLinqer.Components.Where.TableWhereCollection<T>> filter)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            var where = new SqlLinqer.Components.Where.TableWhereCollection<T>(Table, SqlWhereOp.AND);
            filter(where);
            foreach (var column in where.GetColumnReferences())
                if (column.Table.UUID != Table.UUID)
                    throw new FormatException($"Column '{column.ColumnName}' does not belong to the root table. An insert conflict can only reference its own table.");
            ConflictAction.Condition.AddComponent(where);
            return this;
        }

        /// <inheritdoc/>
        public InsertQuery<T> OnConflictWhere<TProperty>(Expression<Func<T, TProperty>> expression, SqlOp op)
        {
            ConflictAction.Condition.AddComponent(new SqlLinqer.Components.Where.WhereColumnInserted(FindRootColumn(expression), op));
            return this;
        }

        /// <inheritdoc/>
        public InsertQuery<T> OnConflictUpdate<TProperty>(Expression<Func<T, TProperty>> expression)
        {
            ConflictAction.Updates.AddUpdate(new ColumnInsertedUpdate(FindRootColumn(expression)));
            return this;
        }

        /// <inheritdoc/>
        public InsertQuery<T> OnConflictUpdate<TProperty>(Expression<Func<T, TProperty>> expression, TProperty value)
        {
            ConflictAction.Updates.AddUpdate(new ColumnValueUpdate(FindRootColumn(expression), value));
            return this;
        }

        /// <inheritdoc/>
        public InsertQuery<T> OnConflictUpdate<TProperty>(Expression<Func<T, TProperty>> expression, SqlTime value)
        {
            ConflictAction.Updates.AddUpdate(new ColumnValueUpdate(FindRootColumn(expression), value));
            return this;
        }

        /// <inheritdoc/>
        public InsertQuery<T> OnConflictUpdate<TProperty>(Expression<Func<T, TProperty>> expression, UpdateOperation operation)
        {
            ConflictAction.Updates.AddUpdate(new ColumnOperationUpdate(FindRootColumn(expression),
                operation ?? throw new ArgumentNullException(nameof(operation))));
            return this;
        }

        /// <inheritdoc/>
        public InsertQuery<T> OnConflictUpdateAuto()
        {
            ConflictAction.UpdateAuto = true;
            return this;
        }

        /// <inheritdoc/>
        public InsertQuery<T> OnConflictDoNothing()
        {
            ConflictAction.DoNothing = true;
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
    }
}
