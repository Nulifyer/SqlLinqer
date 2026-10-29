using System;
using System.Collections.Generic;
using System.Data;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Modeling;

namespace SqlLinqer.Compilation
{
    internal static class CallablePreparation
    {
        internal static PreparedCallable Prepare(CallableDefinition definition, object[] values, bool[] specified, DbFlavor flavor, string defaultSchema, bool includeTotalResults = true)
        {
            if (flavor != DbFlavor.SqlServer && flavor != DbFlavor.AzureSql)
                throw new NotSupportedException("Stored procedure callables support SQL Server and Azure SQL only.");
            var schema = definition.Procedure.Schema ?? defaultSchema;
            if (schema != null && string.IsNullOrWhiteSpace(schema)) throw new InvalidOperationException("The default schema must not be empty.");
            var plan = new PreparedCallable();
            plan.ReturnCode = plan.Query.Parameters.AddCallableParameter(null, DbType.Int32, ParameterDirection.Output, new SqlParameterAttribute());
            try
            {
                var assignments = new List<string>();
                for (int i = 0; i < definition.Parameters.Length; i++)
                {
                    if (!specified[i]) continue;
                    var binding = definition.Parameters[i];
                    var value = values[i];
                    if (binding.TableInput != null)
                    {
                        if (value == null) throw new ArgumentException($"Table-valued argument '{binding.Name}' cannot be SQL NULL. Supply an empty collection.");
                        var table = binding.TableInput.Materialize(value);
                        plan.Query.OwnTable(table);
                        value = table;
                    }
                    var id = plan.Query.Parameters.AddCallableParameter(value, binding.DbType, ParameterDirection.Input, binding.Metadata, schema);
                    assignments.Add("@" + binding.Name + " = " + id);
                }
                if (includeTotalResults)
                    plan.TotalResults = plan.Query.Parameters.AddCallableParameter(null, DbType.Int64, ParameterDirection.Output, new SqlParameterAttribute());
                var procedure = (schema == null ? "" : Quote(schema) + ".") + Quote(definition.Procedure.Name);
                var call = "EXEC " + plan.ReturnCode + " = " + procedure + (assignments.Count == 0 ? "" : " " + string.Join(", ", assignments));
                plan.Query.Text = includeTotalResults ? WithStandardTotals(call, procedure, plan.TotalResults, assignments.Count == 0) : call + ";";
                return plan;
            }
            catch { plan.Dispose(); throw; }
        }

        // Discover standard outputs in the same SQL batch, without a second connection or metadata round trip.
        // Exact names also work in databases whose identifiers use a case-sensitive collation.
        private static string WithStandardTotals(string call, string procedure, string total, bool noInputs)
        {
            var objectId = "OBJECT_ID(N'" + procedure.Replace("'", "''") + "')";
            var separator = noInputs ? " " : ", ";
            string Branch(string name)
            {
                var predicate = "object_id = " + objectId + " AND name COLLATE Latin1_General_100_BIN2 = N'" + name + "' AND is_output = 1";
                return "IF EXISTS (SELECT 1 FROM sys.parameters WHERE " + predicate + ") BEGIN\n"
                    + "IF EXISTS (SELECT 1 FROM sys.parameters WHERE " + predicate + " AND system_type_id NOT IN (48, 52, 56, 127)) BEGIN; "
                    + "THROW 50000, 'The standard total results output must use an integer SQL type.', 1; END;\n"
                    + call + separator + name + " = " + total + " OUTPUT;\nEND";
            }
            return Branch("@TotalResults") + "\nELSE " + Branch("@total_count") + "\nELSE " + call + ";";
        }

        private static string Quote(string name) => "[" + name.Replace("]", "]]") + "]";

    }

    internal sealed class PreparedCallable : IDisposable
    {
        internal RenderedQuery Query = new RenderedQuery();
        internal string TotalResults;
        internal string ReturnCode;
        internal void CaptureInputs() => SnapshotGraph.CaptureCallableInputs(Query);

        public void Dispose() => Query.Dispose();
    }
}
