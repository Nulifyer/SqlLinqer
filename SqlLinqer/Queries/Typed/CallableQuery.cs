using System;
using System.Collections.Concurrent;
using System.Data;
using SqlLinqer.Compilation;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.MemberInfoExtensions;
using SqlLinqer.Modeling;

namespace SqlLinqer.Queries.Typed
{
    internal sealed partial class CallableQuery<TInput, TOutput> where TInput : class
    {
        private static readonly ConcurrentDictionary<(string Name, string Schema), CallableDefinition> Definitions
            = new ConcurrentDictionary<(string Name, string Schema), CallableDefinition>();
        private readonly CallableDefinition _definition;
        private readonly TabularRowType _row;
        private readonly object[] _values;
        private readonly bool[] _specified;

        internal CallableQuery(SqlProcedure procedure, TInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var key = (procedure.Name, procedure.Schema);
            if (!Definitions.TryGetValue(key, out _definition))
                _definition = Definitions.GetOrAdd(key, CreateDefinition);
            _row = TabularRowType.GetCached(typeof(TOutput));
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
                    throw new ArgumentException($"Table-valued argument '{binding.Name}' cannot be null. Supply an empty DataTable.", nameof(input));
                _values[i] = value is Enum ? Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType())) : value;
            }
        }

        private static CallableDefinition CreateDefinition((string Name, string Schema) key)
            => new CallableDefinition(new SqlProcedure(key.Name) { Schema = key.Schema }, typeof(TInput));

        internal RenderedQuery Render(DbFlavor flavor) => CallablePreparation.Prepare(_definition, _values, _specified, flavor, null).Query;
        internal RenderedQuery Render(BaseConnector connector)
            => Prepare(connector).Query;

        private PreparedCallable Prepare(BaseConnector connector)
        {
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            return CallablePreparation.Prepare(_definition, _values, _specified, connector.DbFlavor, connector.Options.DefaultSchema);
        }
    }
}
