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
    /// <summary>An invocation of a typed SQL Server or Azure SQL procedure.</summary>
    public sealed class CallableInvocation<TCallable, TArgs, TResult>
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

        /// <summary>Renders a parameterized EXEC command for SQL Server or Azure SQL.</summary>
        public RenderedQuery Render(DbFlavor flavor) => Build(flavor, null).Query;

        /// <summary>Renders using the connector's flavor and default schema.</summary>
        public RenderedQuery Render(BaseConnector connector)
        {
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            return Build(connector.DbFlavor, connector.Options.DefaultSchema).Query;
        }

        /// <summary>Executes using the default connector. Caller-owned transactions remain caller-owned.</summary>
        public SqlResponse<TResult> Execute(DbTransaction transaction = null) => Execute(Default.Connector, transaction);

        /// <summary>Executes and buffers typed rows and output values before returning.</summary>
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
                        CallableValue.Convert(parameter.Value, output.Value.GetFieldOrPropValueType()));
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
