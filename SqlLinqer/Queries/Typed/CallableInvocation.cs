using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.MemberInfoExtensions;
using SqlLinqer.Modeling;

namespace SqlLinqer.Queries.Typed
{
    /// <summary>
    /// A typed procedure call with captured input member values and buffered result-set, output-parameter, and return-code mapping.
    /// Missing or extra tabular result sets return mapping errors. A nonzero procedure return code does not automatically fail the response.
    /// </summary>
    /// <typeparam name="TCallable">The procedure definition carrying SqlProcedure.</typeparam>
    /// <typeparam name="TArgs">The input model.</typeparam>
    /// <typeparam name="TResult">The writable result model with a public parameterless constructor.</typeparam>
    public sealed partial class CallableInvocation<TCallable, TArgs, TResult>
        where TCallable : SqlLinqerCallable<TCallable, TArgs, TResult>
        where TArgs : class
        where TResult : class, new()
    {
        private static readonly Lazy<CallableDefinition> Definition = new Lazy<CallableDefinition>(() =>
            new CallableDefinition(typeof(TCallable), typeof(TArgs), typeof(TResult)));
        private readonly object[] _values;
        private readonly bool[] _specified;

        internal CallableInvocation(TArgs args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            var parameters = Definition.Value.Parameters;
            _values = new object[parameters.Length];
            _specified = new bool[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                object value = parameter.Input?.GetFieldOrPropValue(args);
                _specified[i] = !(value is ISqlArgument optional) || optional.IsSpecified;
                if (value is ISqlArgument argument) value = argument.UntypedValue;
                if (!_specified[i] && parameter.Output != null)
                    throw new InvalidOperationException($"Input/output parameter '{parameter.Name}' cannot be omitted.");
                if (_specified[i] && parameter.Metadata.TypeName != null && value == null)
                    throw new ArgumentException($"Table-valued argument '{parameter.Name}' cannot be null. Supply an empty DataTable.", nameof(args));
                _values[i] = value is Enum ? Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType())) : value;
            }
        }

        /// <summary>
        /// Renders a parameterized EXEC command with typed input, output, and return-code binding metadata, without opening a connection.
        /// Use Execute or ExecuteAsync to map result sets and outputs.
        /// </summary>
        /// <param name="flavor">SQL Server or Azure SQL. Other dialects are unsupported.</param>
        /// <returns>The SQL call and its provider bindings.</returns>
        public RenderedQuery Render(DbFlavor flavor) => Build(flavor, null).Query;

        /// <summary>
        /// Renders a parameterized EXEC command with typed input, output, and return-code binding metadata, without opening a connection.
        /// Use Execute or ExecuteAsync to map result sets and outputs.
        /// </summary>
        /// <param name="connector">The connector supplying the SQL dialect and fallback procedure schema.</param>
        /// <returns>The SQL call and its provider bindings.</returns>
        public RenderedQuery Render(BaseConnector connector)
        {
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            return Build(connector.DbFlavor, connector.Options.DefaultSchema).Query;
        }

        /// <summary>
        /// Executes the procedure and buffers every declared result set before reading outputs and return code.
        /// Uses the current default connector. Owned transactions commit after all results and outputs load; failures attempt rollback.
        /// </summary>
        /// <param name="transaction">An optional caller-owned transaction with an active connection.</param>
        /// <returns>The populated result model and execution state. Empty result sets become empty collections.</returns>
        public SqlResponse<TResult> Execute(DbTransaction transaction = null) => Execute(Default.Connector, transaction);

        /// <summary>
        /// Executes the procedure and buffers every declared result set before reading outputs and return code.
        /// Owned transactions commit after all results and outputs load; failures attempt rollback.
        /// </summary>
        /// <param name="connector">The SQL Server or Azure SQL connector, including its fallback procedure schema.</param>
        /// <param name="transaction">An optional caller-owned transaction with an active connection.</param>
        /// <returns>The populated result model and execution state. Empty result sets become empty collections.</returns>
        public SqlResponse<TResult> Execute(BaseConnector connector, DbTransaction transaction = null)
        {
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            var plan = Build(connector.DbFlavor, connector.Options.DefaultSchema);
            return connector.ExecuteCallable(plan.Query, transaction, command =>
            {
                var result = new TResult();
                int setIndex = 0;
                using (var reader = command.ExecuteReader())
                {
                    do
                    {
                        // Update counts are not tabular result sets and do not consume a declared index.
                        if (reader.FieldCount == 0) continue;
                        if (setIndex >= Definition.Value.ResultSets.Length)
                            throw new InvalidOperationException("The procedure returned an undeclared result set.");
                        var set = Definition.Value.ResultSets[setIndex++];
                        set.Member.SetFieldOrPropValue(result, set.Read(reader));
                    } while (reader.NextResult());
                }
                if (setIndex != Definition.Value.ResultSets.Length)
                    throw new InvalidOperationException($"The procedure returned {setIndex} result sets; expected {Definition.Value.ResultSets.Length}.");
                // SQL Server populates output parameters only after the reader closes.
                foreach (var output in plan.Outputs)
                {
                    var parameter = (DbParameter)command.Parameters[output.Key];
                    output.Value.SetFieldOrPropValue(result,
                        TabularValue.Convert(parameter.Value, output.Value.GetFieldOrPropValueType()));
                }
                return result;
            });
        }

        private Plan Build(DbFlavor flavor, string defaultSchema)
        {
            if (flavor != DbFlavor.SqlServer && flavor != DbFlavor.AzureSql)
                throw new NotSupportedException("Typed procedure callables currently support SQL Server and Azure SQL only.");
            var definition = Definition.Value;
            var plan = new Plan();
            string returnPrefix = "";
            if (definition.ReturnValue != null)
            {
                var id = plan.Query.Parameters.AddCallableParameter(null, DbType.Int32,
                    ParameterDirection.Output, new SqlParameterAttribute());
                plan.Outputs.Add(id, definition.ReturnValue);
                returnPrefix = id + " = ";
            }
            var assignments = new List<string>();
            for (int i = 0; i < definition.Parameters.Length; i++)
            {
                if (!_specified[i]) continue;
                var binding = definition.Parameters[i];
                var direction = binding.Output == null ? ParameterDirection.Input
                    : binding.Input == null ? ParameterDirection.Output : ParameterDirection.InputOutput;
                var id = plan.Query.Parameters.AddCallableParameter(_values[i], binding.DbType, direction, binding.Metadata);
                assignments.Add("@" + binding.Name + " = " + id + (binding.Output != null ? " OUTPUT" : ""));
                if (binding.Output != null) plan.Outputs.Add(id, binding.Output);
            }
            var schema = definition.Procedure.Schema ?? defaultSchema;
            if (schema != null && string.IsNullOrWhiteSpace(schema))
                throw new InvalidOperationException("The default schema must not be empty.");
            plan.Query.Text = "EXEC " + returnPrefix + (schema == null ? "" : Quote(schema) + ".")
                + Quote(definition.Procedure.Name) + (assignments.Count == 0 ? "" : " " + string.Join(", ", assignments));
            return plan;
        }

        private static string Quote(string name) => "[" + name.Replace("]", "]]") + "]";

        private sealed class Plan
        {
            internal readonly RenderedQuery Query = new RenderedQuery();
            internal readonly Dictionary<string, System.Reflection.MemberInfo> Outputs = new Dictionary<string, System.Reflection.MemberInfo>();
        }
    }
}
