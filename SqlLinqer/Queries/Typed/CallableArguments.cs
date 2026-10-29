using System;
using System.Collections.Concurrent;
using SqlLinqer.Compilation;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.MemberInfoExtensions;
using SqlLinqer.Modeling;

namespace SqlLinqer.Queries.Typed
{
    internal sealed class CallableArguments<TInput> where TInput : class
    {
        private static readonly ConcurrentDictionary<(string Name, string Schema), CallableDefinition> Definitions
            = new ConcurrentDictionary<(string Name, string Schema), CallableDefinition>();
        private readonly CallableDefinition _definition;
        private readonly object[] _values;
        private readonly bool[] _specified;

        internal CallableArguments(SqlProcedure procedure, TInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var key = (procedure.Name, procedure.Schema);
            if (!Definitions.TryGetValue(key, out _definition))
                _definition = Definitions.GetOrAdd(key, CreateDefinition);
            var parameters = _definition.Parameters;
            _values = new object[parameters.Length];
            _specified = new bool[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var binding = parameters[i];
                object value = binding.Input.GetFieldOrPropValue(input);
                if (value is ISqlArgument argument)
                {
                    _specified[i] = argument.IsSpecified;
                    value = argument.UntypedValue;
                }
                else _specified[i] = value != null;
                if (_specified[i] && binding.Metadata.TypeName != null && value == null)
                    throw new ArgumentException($"Table-valued argument '{binding.Name}' cannot be null. Supply an empty collection or DataTable.", nameof(input));
                _values[i] = value is Enum ? Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType())) : value;
            }
        }

        private static CallableDefinition CreateDefinition((string Name, string Schema) key)
            => new CallableDefinition(new SqlProcedure(key.Name) { Schema = key.Schema }, typeof(TInput));

        internal PreparedCallable Prepare(BaseConnector connector, bool includeTotalResults = true)
        {
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            return Prepare(connector.DbFlavor, connector.Options.DefaultSchema, includeTotalResults);
        }

        internal PreparedCallable Prepare(DbFlavor flavor, string schema, bool includeTotalResults = true)
            => CallablePreparation.Prepare(_definition, _values, _specified, flavor, schema, includeTotalResults);
    }
}
