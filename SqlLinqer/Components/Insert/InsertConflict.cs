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
    // What an insert does with a row whose unique key already exists: update chosen columns, or skip the row.
    internal sealed class InsertConflict
    {
        internal const string TargetAlias = "target", SourceAlias = "source";

        internal readonly List<Column> Target = new List<Column>();
        private readonly List<Assignment> _assignments = new List<Assignment>();
        internal bool UpdateAuto;
        internal bool DoNothing;
        // A unique constraint named as the conflict target instead of key columns. PostgreSQL only.
        internal string Constraint;
        // Predicates on the existing row that a conflict update must satisfy. Null updates every conflicting row.
        private WhereCollection _condition;
        // Comparisons of the existing row's value with the value this insert supplied for the same column.
        private readonly List<Comparison> _comparisons = new List<Comparison>();

        private sealed class Assignment
        {
            internal Column Column;
            internal bool FromInserted;
            internal object Value;
            internal UpdateOperation Operation;
        }

        private sealed class Comparison
        {
            internal Column Column;
            internal SqlOp Operator;
        }

        internal void SetTarget(IEnumerable<Column> columns)
        {
            Target.Clear();
            Target.AddRange(columns);
            Constraint = null;
        }

        internal void SetConstraint(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A constraint name is required.", nameof(name));
            Target.Clear();
            Constraint = name;
        }

        internal void Where(WhereCollection filter)
        {
            if (_condition == null) _condition = new WhereCollection(SqlWhereOp.AND);
            _condition.AddComponent(filter);
        }

        internal void WhereInserted(Column column, SqlOp op) => _comparisons.Add(new Comparison { Column = column, Operator = op });

        private bool Conditional => _condition != null || _comparisons.Count > 0;

        internal void UpdateFromInserted(Column column) => Set(new Assignment { Column = column, FromInserted = true });
        internal void UpdateValue(Column column, object value) => Set(new Assignment { Column = column, Value = value });
        internal void UpdateOperation(Column column, UpdateOperation operation)
            => Set(new Assignment { Column = column, Operation = operation ?? throw new ArgumentNullException(nameof(operation)) });

        private void Set(Assignment assignment)
        {
            _assignments.RemoveAll(existing => existing.Column.UUID == assignment.Column.UUID);
            _assignments.Add(assignment);
        }

        // Automatic assignments are resolved here so InsertAuto and OnConflictUpdateAuto can be called in either order.
        private List<Assignment> Resolve(IReadOnlyCollection<ReflectedColumn> inserted)
        {
            if (DoNothing)
            {
                if (_assignments.Count > 0 || UpdateAuto)
                    throw new InvalidOperationException("An insert cannot both update and skip a conflicting row. Use OnConflictUpdate or OnConflictDoNothing, not both.");
                if (Conditional)
                    throw new InvalidOperationException("OnConflictWhere limits a conflict update. It does not apply to OnConflictDoNothing.");
                return _assignments;
            }
            var resolved = new List<Assignment>(_assignments);
            if (UpdateAuto)
                foreach (var column in inserted)
                    if (column.AutoOptions.Update && !column.PrimaryKey
                        && Target.All(target => target.UUID != column.UUID)
                        && resolved.All(existing => existing.Column.UUID != column.UUID))
                        resolved.Add(new Assignment { Column = column, FromInserted = true });
            if (resolved.Count == 0)
                throw new InvalidOperationException("OnConflict needs an action. Add OnConflictUpdate, OnConflictUpdateAuto, or OnConflictDoNothing.");
            foreach (var assignment in resolved)
                if (assignment.FromInserted && inserted.All(column => column.UUID != assignment.Column.UUID))
                    throw new InvalidOperationException($"Column '{assignment.Column.ColumnName}' is not inserted, so a conflict cannot update it from the inserted value. Assign a value or operation instead.");
            return resolved;
        }

        private static string InsertedValue(Column column, DbFlavor flavor)
        {
            string name = column.ColumnName.DbWrap(flavor);
            if (flavor == DbFlavor.SqlServer || flavor == DbFlavor.AzureSql) return $"{SourceAlias.DbWrap(flavor)}.{name}";
            return flavor == DbFlavor.MySql ? $"VALUES({name})" : $"EXCLUDED.{name}";
        }

        // The condition a conflicting row must satisfy to be updated, or null. Existing values render through the table alias.
        private string RenderCondition(SqlLinqer.Queries.Core.UpdateQuery owner, IReadOnlyCollection<ReflectedColumn> inserted, ParameterCollection parameters, DbFlavor flavor)
        {
            var parts = new List<string>();
            string filter = _condition?.Render(owner, parameters, flavor, include_where: false);
            if (!string.IsNullOrEmpty(filter)) parts.Add($"({filter})");
            foreach (var comparison in _comparisons)
            {
                if (inserted.All(column => column.UUID != comparison.Column.UUID))
                    throw new InvalidOperationException($"Column '{comparison.Column.ColumnName}' is not inserted, so a conflict cannot compare it with the inserted value.");
                parts.Add($"{comparison.Column.Render(flavor)} {comparison.Operator.GetOpString(flavor)} {InsertedValue(comparison.Column, flavor)}");
            }
            return parts.Count == 0 ? null : string.Join(" AND ", parts);
        }

        private string RenderAssignments(SqlLinqer.Queries.Core.InsertQuery query, IReadOnlyCollection<ReflectedColumn> inserted, ParameterCollection parameters, DbFlavor flavor,
            out string condition)
        {
            bool merge = flavor == DbFlavor.SqlServer || flavor == DbFlavor.AzureSql;
            // Operations and conditions render the existing row through the table alias, as they do in an UPDATE.
            var owner = new SqlLinqer.Queries.Core.UpdateQuery(query.Table);
            var resolved = Resolve(inserted);
            condition = null;
            // MySQL has no conditional conflict update, so each assignment keeps the existing value unless the condition holds.
            bool perAssignment = flavor == DbFlavor.MySql && Conditional && !DoNothing;
            if (perAssignment) resolved = OrderForMySql(resolved);
            var rendered = new List<string>();
            foreach (var assignment in resolved)
            {
                string name = assignment.Column.ColumnName.DbWrap(flavor);
                string target = merge ? $"{TargetAlias.DbWrap(flavor)}.{name}" : name;
                string value;
                if (assignment.Operation != null)
                {
                    var statement = new ColumnOperationUpdate(assignment.Column, assignment.Operation);
                    string operation = assignment.Operation.Render(owner, statement, parameters, flavor);
                    if (!perAssignment) { rendered.Add(operation); continue; }
                    target = statement.RenderTarget(flavor);
                    if (!operation.StartsWith(target + " = ", StringComparison.Ordinal))
                        throw new NotSupportedException($"The operation on '{assignment.Column.ColumnName}' cannot be made conditional on MySQL, because it does not render as an assignment to the column.");
                    value = operation.Substring(target.Length + 3);
                }
                else if (!assignment.FromInserted) value = parameters.AddParameter(assignment.Value);
                else value = InsertedValue(assignment.Column, flavor);
                rendered.Add(perAssignment
                    ? $"{target} = IF({RenderCondition(owner, inserted, parameters, flavor)}, {value}, {assignment.Column.Render(flavor)})"
                    : $"{target} = {value}");
            }
            if (!perAssignment && !DoNothing) condition = RenderCondition(owner, inserted, parameters, flavor);
            return string.Join(",", rendered);
        }

        // MySQL assigns left to right, and a later assignment reads the values earlier ones wrote. A column that the
        // condition reads must therefore be assigned last, which is possible for one such column only.
        private List<Assignment> OrderForMySql(List<Assignment> assignments)
        {
            var read = new HashSet<string>(_comparisons.Select(comparison => comparison.Column.UUID));
            if (_condition != null)
                foreach (var column in _condition.GetColumnReferences()) read.Add(column.UUID);
            var last = assignments.Where(assignment => read.Contains(assignment.Column.UUID)).ToList();
            if (last.Count > 1)
                throw new NotSupportedException("On MySQL, a conditional conflict update can assign at most one of the columns its condition reads: "
                    + string.Join(", ", last.Select(assignment => assignment.Column.ColumnName)) + ". Assign one of them, or use hand-written SQL.");
            return assignments.Where(assignment => !read.Contains(assignment.Column.UUID)).Concat(last).ToList();
        }

        // The conflict action binds its values once per statement, so each batch must leave room for them.
        internal int CountParameters(SqlLinqer.Queries.Core.InsertQuery query, IReadOnlyCollection<ReflectedColumn> inserted, DbFlavor flavor)
        {
            var parameters = new ParameterCollection(flavor);
            RenderAssignments(query, inserted, parameters, flavor, out _);
            return parameters.Count;
        }

        private string RenderTarget(DbFlavor flavor)
            => string.Join(",", Target.Select(column => column.ColumnName.DbWrap(flavor)));

        // PostgreSQL, SQLite, and DuckDB: the clause that follows VALUES.
        internal string RenderOnConflict(SqlLinqer.Queries.Core.InsertQuery query, IReadOnlyCollection<ReflectedColumn> inserted, ParameterCollection parameters, DbFlavor flavor)
        {
            string assignments = RenderAssignments(query, inserted, parameters, flavor, out string condition);
            if (Constraint != null && flavor != DbFlavor.PostgreSql)
                throw new NotSupportedException($"{flavor} cannot name a constraint as a conflict target. Pass the unique key members to OnConflict.");
            string target = Constraint != null ? $" ON CONSTRAINT {Constraint.DbWrap(flavor)}"
                : Target.Count == 0 ? null : $" ({RenderTarget(flavor)})";
            if (DoNothing) return $"ON CONFLICT{target} DO NOTHING";
            if (target == null)
                throw new InvalidOperationException("Updating a conflicting row requires a conflict target. Pass the unique key members to OnConflict.");
            return $"ON CONFLICT{target} DO UPDATE SET {assignments}{(condition == null ? null : " WHERE " + condition)}";
        }

        // MySQL applies whichever unique key collides, so the target is not rendered.
        internal string RenderOnDuplicateKey(SqlLinqer.Queries.Core.InsertQuery query, IReadOnlyCollection<ReflectedColumn> inserted, ParameterCollection parameters, DbFlavor flavor)
        {
            string assignments = RenderAssignments(query, inserted, parameters, flavor, out _);
            if (!DoNothing) return $"ON DUPLICATE KEY UPDATE {assignments}";
            // A self-assignment skips the row without hiding unrelated errors the way INSERT IGNORE does.
            string name = (Target.Count > 0 ? Target[0] : inserted.First()).ColumnName.DbWrap(flavor);
            return $"ON DUPLICATE KEY UPDATE {name} = {name}";
        }

        // SQL Server and Azure SQL have no conflict clause. HOLDLOCK keeps the match and the write atomic.
        internal string RenderMerge(SqlLinqer.Queries.Core.InsertQuery query, IReadOnlyCollection<ReflectedColumn> inserted, string rows, string outputs,
            ParameterCollection parameters, DbFlavor flavor)
        {
            if (Constraint != null)
                throw new NotSupportedException($"{flavor} cannot name a constraint as a conflict target. Pass the unique key members to OnConflict.");
            if (Target.Count == 0)
                throw new InvalidOperationException("SQL Server upserts require a conflict target. Pass the unique key members to OnConflict.");
            foreach (var column in Target)
                if (inserted.All(candidate => candidate.UUID != column.UUID))
                    throw new InvalidOperationException($"Conflict target column '{column.ColumnName}' must be one of the inserted columns.");
            string assignments = RenderAssignments(query, inserted, parameters, flavor, out string condition);
            string target = TargetAlias.DbWrap(flavor), source = SourceAlias.DbWrap(flavor);
            string columns = string.Join(",", inserted.Select(column => column.ColumnName.DbWrap(flavor)));
            string sourceColumns = string.Join(",", inserted.Select(column => $"{source}.{column.ColumnName.DbWrap(flavor)}"));
            string match = string.Join(" AND ", Target.Select(column => $"{target}.{column.ColumnName.DbWrap(flavor)} = {source}.{column.ColumnName.DbWrap(flavor)}"));
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
