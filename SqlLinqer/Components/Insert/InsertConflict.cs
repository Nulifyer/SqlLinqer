using System;
using System.Collections.Generic;
using System.Linq;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Update;
using SqlLinqer.Components.Where;
using SqlLinqer.Extensions.StringExtensions;

namespace SqlLinqer.Components.Insert
{
    /// <summary>
    /// What an insert does with a row whose unique key already exists: update chosen columns, or skip the row.
    /// The assignments are the same statements an UPDATE uses, and the condition is an ordinary predicate group.
    /// </summary>
    public sealed class InsertConflict
    {
        internal const string TargetAlias = "target", SourceAlias = "source";

        private readonly List<Column> _target = new List<Column>();

        /// <summary>The key columns that identify a conflicting row. Empty when a constraint is named or no target is set.</summary>
        public IReadOnlyList<Column> Target => _target;
        /// <summary>A unique constraint named as the conflict target instead of key columns. PostgreSQL only.</summary>
        public string Constraint { get; private set; }
        /// <summary>The assignments applied to a conflicting row.</summary>
        public UpdatesCollection Updates { get; } = new UpdatesCollection();
        /// <summary>The predicates a conflicting row must satisfy to be updated. An empty group updates every conflicting row.</summary>
        public WhereCollection Condition { get; } = new WhereCollection(SqlWhereOp.AND);
        /// <summary>True to also assign every inserted column that permits automatic updates from its inserted value.</summary>
        public bool UpdateAuto { get; set; }
        /// <summary>True to leave a conflicting row unchanged and skip the new row.</summary>
        public bool DoNothing { get; set; }

        /// <summary>Names the key columns that identify a conflicting row. This clears a named constraint.</summary>
        /// <param name="columns">Root columns that together form a primary or unique key.</param>
        public void SetTarget(IEnumerable<Column> columns)
        {
            _target.Clear();
            _target.AddRange(columns);
            Constraint = null;
        }

        /// <summary>Names a unique constraint as the conflict target. This clears the key columns.</summary>
        /// <param name="name">The constraint name as the database stores it.</param>
        public void SetConstraint(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A constraint name is required.", nameof(name));
            _target.Clear();
            Constraint = name;
        }

        private static IEnumerable<WhereColumnInserted> InsertedComparisons(WhereCollection group)
        {
            foreach (var component in group.GetAll())
            {
                if (component is WhereColumnInserted comparison) yield return comparison;
                else if (component is WhereCollection nested)
                    foreach (var inner in InsertedComparisons(nested)) yield return inner;
            }
        }

        // Automatic assignments are resolved here so InsertAuto and OnConflictUpdateAuto can be called in either order.
        private List<UpdateStatement> Resolve(IReadOnlyCollection<ReflectedColumn> inserted)
        {
            var resolved = Updates.GetAll().ToList();
            if (DoNothing)
            {
                if (resolved.Count > 0 || UpdateAuto)
                    throw new InvalidOperationException("An insert cannot both update and skip a conflicting row. Use OnConflictUpdate or OnConflictDoNothing, not both.");
                if (Condition.Count > 0)
                    throw new InvalidOperationException("OnConflictWhere limits a conflict update. It does not apply to OnConflictDoNothing.");
                return resolved;
            }
            if (UpdateAuto)
                foreach (var column in inserted)
                    if (column.AutoOptions.Update && !column.PrimaryKey
                        && _target.All(target => target.UUID != column.UUID)
                        && resolved.All(existing => existing.UpdateColumn.UUID != column.UUID))
                        resolved.Add(new ColumnInsertedUpdate(column));
            if (resolved.Count == 0)
                throw new InvalidOperationException("OnConflict needs an action. Add OnConflictUpdate, OnConflictUpdateAuto, or OnConflictDoNothing.");
            foreach (var statement in resolved)
                if (statement is ColumnInsertedUpdate && inserted.All(column => column.UUID != statement.UpdateColumn.UUID))
                    throw new InvalidOperationException($"Column '{statement.UpdateColumn.ColumnName}' is not inserted, so a conflict cannot update it from the inserted value. Assign a value or operation instead.");
            foreach (var comparison in InsertedComparisons(Condition))
                if (inserted.All(column => column.UUID != comparison.Column.UUID))
                    throw new InvalidOperationException($"Column '{comparison.Column.ColumnName}' is not inserted, so a conflict cannot compare it with the inserted value.");
            return resolved;
        }

        // The condition a conflicting row must satisfy to be updated, or null. Existing values render through the table alias.
        private string RenderCondition(ParameterCollection parameters, DbFlavor flavor)
        {
            string condition = Condition.Count == 0 ? null : Condition.Render(null, parameters, flavor, include_where: false);
            return string.IsNullOrEmpty(condition) ? null : condition;
        }

        private string RenderAssignments(IReadOnlyCollection<ReflectedColumn> inserted, ParameterCollection parameters, DbFlavor flavor, out string condition)
        {
            var resolved = Resolve(inserted);
            condition = null;
            if (DoNothing) return null;
            // MySQL has no conditional conflict update, so each assignment keeps the existing value unless the condition holds.
            if (flavor == DbFlavor.MySql && Condition.Count > 0)
                return string.Join(",", OrderForMySql(resolved).Select(statement =>
                    $"{statement.RenderTarget(flavor)} = IF({RenderCondition(parameters, flavor)}, {statement.RenderValue(null, parameters, flavor)}, {statement.UpdateColumn.Render(flavor)})"));
            string assignments = string.Join(",", resolved.Select(statement => statement.Render(null, parameters, flavor)));
            condition = RenderCondition(parameters, flavor);
            return assignments;
        }

        // MySQL assigns left to right, and a later assignment reads the values earlier ones wrote. A column that the
        // condition reads must therefore be assigned last, which is possible for one such column only.
        private List<UpdateStatement> OrderForMySql(List<UpdateStatement> assignments)
        {
            var read = new HashSet<string>(Condition.GetColumnReferences().Select(column => column.UUID));
            var last = assignments.Where(statement => read.Contains(statement.UpdateColumn.UUID)).ToList();
            if (last.Count > 1)
                throw new NotSupportedException("On MySQL, a conditional conflict update can assign at most one of the columns its condition reads: "
                    + string.Join(", ", last.Select(statement => statement.UpdateColumn.ColumnName)) + ". Assign one of them, or use hand-written SQL.");
            return assignments.Where(statement => !read.Contains(statement.UpdateColumn.UUID)).Concat(last).ToList();
        }

        // The conflict action binds its values once per statement, so each batch must leave room for them.
        internal int CountParameters(IReadOnlyCollection<ReflectedColumn> inserted, DbFlavor flavor)
        {
            var parameters = new ParameterCollection(flavor);
            RenderAssignments(inserted, parameters, flavor, out _);
            return parameters.Count;
        }

        private string RenderTarget(DbFlavor flavor)
            => string.Join(",", _target.Select(column => column.ColumnName.DbWrap(flavor)));

        // PostgreSQL, SQLite, and DuckDB: the clause that follows VALUES.
        internal string RenderOnConflict(IReadOnlyCollection<ReflectedColumn> inserted, ParameterCollection parameters, DbFlavor flavor)
        {
            string assignments = RenderAssignments(inserted, parameters, flavor, out string condition);
            if (Constraint != null && flavor != DbFlavor.PostgreSql)
                throw new NotSupportedException($"{flavor} cannot name a constraint as a conflict target. Pass the unique key members to OnConflict.");
            string target = Constraint != null ? $" ON CONSTRAINT {Constraint.DbWrap(flavor)}"
                : _target.Count == 0 ? null : $" ({RenderTarget(flavor)})";
            if (DoNothing) return $"ON CONFLICT{target} DO NOTHING";
            if (target == null)
                throw new InvalidOperationException("Updating a conflicting row requires a conflict target. Pass the unique key members to OnConflict.");
            return $"ON CONFLICT{target} DO UPDATE SET {assignments}{(condition == null ? null : " WHERE " + condition)}";
        }

        // MySQL applies whichever unique key collides, so the target is not rendered.
        internal string RenderOnDuplicateKey(IReadOnlyCollection<ReflectedColumn> inserted, ParameterCollection parameters, DbFlavor flavor)
        {
            string assignments = RenderAssignments(inserted, parameters, flavor, out _);
            if (!DoNothing) return $"ON DUPLICATE KEY UPDATE {assignments}";
            // A self-assignment skips the row without hiding unrelated errors the way INSERT IGNORE does.
            string name = (_target.Count > 0 ? _target[0] : inserted.First()).ColumnName.DbWrap(flavor);
            return $"ON DUPLICATE KEY UPDATE {name} = {name}";
        }

        // SQL Server and Azure SQL have no conflict clause. HOLDLOCK keeps the match and the write atomic.
        internal string RenderMerge(SqlLinqer.Queries.Core.InsertQuery query, IReadOnlyCollection<ReflectedColumn> inserted, string rows, string outputs,
            ParameterCollection parameters, DbFlavor flavor)
        {
            if (Constraint != null)
                throw new NotSupportedException($"{flavor} cannot name a constraint as a conflict target. Pass the unique key members to OnConflict.");
            if (_target.Count == 0)
                throw new InvalidOperationException("SQL Server upserts require a conflict target. Pass the unique key members to OnConflict.");
            foreach (var column in _target)
                if (inserted.All(candidate => candidate.UUID != column.UUID))
                    throw new InvalidOperationException($"Conflict target column '{column.ColumnName}' must be one of the inserted columns.");
            string assignments = RenderAssignments(inserted, parameters, flavor, out string condition);
            string target = TargetAlias.DbWrap(flavor), source = SourceAlias.DbWrap(flavor);
            string columns = string.Join(",", inserted.Select(column => column.ColumnName.DbWrap(flavor)));
            string sourceColumns = string.Join(",", inserted.Select(column => $"{source}.{column.ColumnName.DbWrap(flavor)}"));
            string match = string.Join(" AND ", _target.Select(column => $"{target}.{column.ColumnName.DbWrap(flavor)} = {source}.{column.ColumnName.DbWrap(flavor)}"));
            return string.Join(" ", new[]
            {
                $"MERGE INTO {query.Table.RenderFullName(flavor)} WITH (HOLDLOCK) AS {target}",
                $"USING (VALUES {rows}) AS {source} ({columns})",
                $"ON {match}",
                DoNothing ? null : $"WHEN MATCHED{(condition == null ? null : " AND " + condition)} THEN UPDATE SET {assignments}",
                $"WHEN NOT MATCHED THEN INSERT ({columns}) VALUES ({sourceColumns})",
                outputs,
            }.Where(part => part != null)) + ";";
        }
    }
}
