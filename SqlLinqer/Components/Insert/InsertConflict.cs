using System;
using System.Collections.Generic;
using System.Linq;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Update;
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

        private sealed class Assignment
        {
            internal Column Column;
            internal bool FromInserted;
            internal object Value;
            internal UpdateOperation Operation;
        }

        internal void SetTarget(IEnumerable<Column> columns)
        {
            Target.Clear();
            Target.AddRange(columns);
        }

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

        private string RenderAssignments(SqlLinqer.Queries.Core.InsertQuery query, IReadOnlyCollection<ReflectedColumn> inserted, ParameterCollection parameters, DbFlavor flavor)
        {
            bool merge = flavor == DbFlavor.SqlServer || flavor == DbFlavor.AzureSql;
            // Operations render the existing row through the table alias, as they do in an UPDATE.
            SqlLinqer.Queries.Core.UpdateQuery owner = null;
            var rendered = new List<string>();
            foreach (var assignment in Resolve(inserted))
            {
                string name = assignment.Column.ColumnName.DbWrap(flavor);
                string target = merge ? $"{TargetAlias.DbWrap(flavor)}.{name}" : name;
                if (assignment.Operation != null)
                {
                    if (owner == null) owner = new SqlLinqer.Queries.Core.UpdateQuery(query.Table);
                    rendered.Add(assignment.Operation.Render(owner, new ColumnOperationUpdate(assignment.Column, assignment.Operation), parameters, flavor));
                }
                else if (!assignment.FromInserted)
                    rendered.Add($"{target} = {parameters.AddParameter(assignment.Value)}");
                else if (merge)
                    rendered.Add($"{target} = {SourceAlias.DbWrap(flavor)}.{name}");
                else if (flavor == DbFlavor.MySql)
                    rendered.Add($"{target} = VALUES({name})");
                else
                    rendered.Add($"{target} = EXCLUDED.{name}");
            }
            return string.Join(",", rendered);
        }

        // The conflict action binds its values once per statement, so each batch must leave room for them.
        internal int CountParameters(SqlLinqer.Queries.Core.InsertQuery query, IReadOnlyCollection<ReflectedColumn> inserted, DbFlavor flavor)
        {
            var parameters = new ParameterCollection(flavor);
            RenderAssignments(query, inserted, parameters, flavor);
            return parameters.Count;
        }

        private string RenderTarget(DbFlavor flavor)
            => string.Join(",", Target.Select(column => column.ColumnName.DbWrap(flavor)));

        // PostgreSQL, SQLite, and DuckDB: the clause that follows VALUES.
        internal string RenderOnConflict(SqlLinqer.Queries.Core.InsertQuery query, IReadOnlyCollection<ReflectedColumn> inserted, ParameterCollection parameters, DbFlavor flavor)
        {
            string assignments = RenderAssignments(query, inserted, parameters, flavor);
            string target = Target.Count == 0 ? null : $" ({RenderTarget(flavor)})";
            if (DoNothing) return $"ON CONFLICT{target} DO NOTHING";
            if (target == null)
                throw new InvalidOperationException("Updating a conflicting row requires a conflict target. Pass the unique key members to OnConflict.");
            return $"ON CONFLICT{target} DO UPDATE SET {assignments}";
        }

        // MySQL applies whichever unique key collides, so the target is not rendered.
        internal string RenderOnDuplicateKey(SqlLinqer.Queries.Core.InsertQuery query, IReadOnlyCollection<ReflectedColumn> inserted, ParameterCollection parameters, DbFlavor flavor)
        {
            string assignments = RenderAssignments(query, inserted, parameters, flavor);
            if (!DoNothing) return $"ON DUPLICATE KEY UPDATE {assignments}";
            // A self-assignment skips the row without hiding unrelated errors the way INSERT IGNORE does.
            string name = (Target.Count > 0 ? Target[0] : inserted.First()).ColumnName.DbWrap(flavor);
            return $"ON DUPLICATE KEY UPDATE {name} = {name}";
        }

        // SQL Server and Azure SQL have no conflict clause. HOLDLOCK keeps the match and the write atomic.
        internal string RenderMerge(SqlLinqer.Queries.Core.InsertQuery query, IReadOnlyCollection<ReflectedColumn> inserted, string rows, string outputs,
            ParameterCollection parameters, DbFlavor flavor)
        {
            if (Target.Count == 0)
                throw new InvalidOperationException("SQL Server upserts require a conflict target. Pass the unique key members to OnConflict.");
            foreach (var column in Target)
                if (inserted.All(candidate => candidate.UUID != column.UUID))
                    throw new InvalidOperationException($"Conflict target column '{column.ColumnName}' must be one of the inserted columns.");
            string assignments = RenderAssignments(query, inserted, parameters, flavor);
            string target = TargetAlias.DbWrap(flavor), source = SourceAlias.DbWrap(flavor);
            string columns = string.Join(",", inserted.Select(column => column.ColumnName.DbWrap(flavor)));
            string sourceColumns = string.Join(",", inserted.Select(column => $"{source}.{column.ColumnName.DbWrap(flavor)}"));
            string match = string.Join(" AND ", Target.Select(column => $"{target}.{column.ColumnName.DbWrap(flavor)} = {source}.{column.ColumnName.DbWrap(flavor)}"));
            return string.Join(" ", new[]
            {
                $"MERGE INTO {query.Table.RenderFullName(flavor)} WITH (HOLDLOCK) AS {target}",
                $"USING (VALUES {rows}) AS {source} ({columns})",
                $"ON {match}",
                DoNothing ? null : $"WHEN MATCHED THEN UPDATE SET {assignments}",
                $"WHEN NOT MATCHED THEN INSERT ({columns}) VALUES ({sourceColumns})",
                outputs,
            }.Where(part => part != null)) + ";";
        }
    }
}
