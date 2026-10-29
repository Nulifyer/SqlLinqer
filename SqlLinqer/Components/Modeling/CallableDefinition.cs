using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using SqlLinqer.Extensions.MemberInfoExtensions;
using SqlLinqer.Extensions.TypeExtensions;
using SqlLinqer.Modeling;

namespace SqlLinqer.Components.Modeling
{
    internal sealed class CallableDefinition
    {
        internal readonly SqlProcedure Procedure;
        internal readonly CallableParameterBinding[] Parameters;

        internal CallableDefinition(SqlProcedure procedure, Type input)
        {
            Procedure = procedure;
            var bindings = new Dictionary<string, CallableParameterBinding>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in input.GetInstanceFieldsAndPropertyMembers().Where(m => m.GetCustomAttribute<SqlIgnore>() == null))
            {
                member.RequireReadable();
                var metadata = member.GetCustomAttribute<SqlParameterAttribute>() ?? new SqlParameterAttribute();
                var name = ParameterName(metadata.Name ?? member.Name);
                if (bindings.ContainsKey(name)) throw new InvalidOperationException($"Duplicate input parameter '{name}'.");
                var binding = new CallableParameterBinding(name, member, metadata);
                binding.Validate();
                bindings.Add(name, binding);
            }
            if (bindings.ContainsKey("TotalResults") || bindings.ContainsKey("total_count"))
                throw new InvalidOperationException("Standard total results parameters belong to the procedure response, not the input model.");
            Parameters = bindings.Values.OrderBy(x => x.Metadata.Order).ThenBy(x => x.Name, StringComparer.Ordinal).ToArray();
        }

        private static string ParameterName(string name)
        {
            if (name.StartsWith("@", StringComparison.Ordinal)) name = name.Substring(1);
            if (name.Length == 0 || name.Length > 127 || !(char.IsLetter(name[0]) || name[0] == '_')
                || name.Any(c => !(char.IsLetterOrDigit(c) || c == '_')))
                throw new InvalidOperationException($"Invalid SQL parameter name '{name}'.");
            return name;
        }
    }

    internal sealed class CallableParameterBinding
    {
        internal readonly string Name;
        internal readonly MemberInfo Input;
        internal readonly SqlParameterAttribute Metadata;
        internal Type ValueType => Unwrap(Input.GetFieldOrPropValueType());
        internal DbType? DbType { get; private set; }
        internal CallableTableInput TableInput { get; private set; }

        internal CallableParameterBinding(string name, MemberInfo input, SqlParameterAttribute metadata)
        { Name = name; Input = input; Metadata = metadata; }

        private static Type Unwrap(Type type)
            => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SqlArgument<>) ? type.GetGenericArguments()[0] : type;

        internal void Validate()
        {
            if (Metadata.Size < -1 || Metadata.Precision > 38 || Metadata.Scale > 38
                || Metadata.ExplicitPrecision.HasValue && Metadata.Scale > Metadata.Precision)
                throw new InvalidOperationException($"Invalid size, precision, or scale for '{Name}'.");
            if (Metadata.ExplicitDbType.HasValue && !Enum.IsDefined(typeof(DbType), Metadata.DbType)
                || Metadata.ExplicitSqlDbType.HasValue && !Enum.IsDefined(typeof(SqlDbType), Metadata.SqlDbType))
                throw new InvalidOperationException($"Invalid database type for '{Name}'.");
            if (ValueType == typeof(DataTable))
            {
                if (string.IsNullOrWhiteSpace(Metadata.TypeName)
                    || Metadata.ExplicitSqlDbType.HasValue && Metadata.SqlDbType != SqlDbType.Structured)
                    throw new InvalidOperationException($"DataTable argument '{Name}' requires TypeName and must be input-only.");
                return;
            }
            if (Metadata.TypeName != null || Metadata.ExplicitSqlDbType == SqlDbType.Structured)
            {
                if (string.IsNullOrWhiteSpace(Metadata.TypeName)
                    || Metadata.ExplicitSqlDbType.HasValue && Metadata.SqlDbType != SqlDbType.Structured)
                    throw new InvalidOperationException($"Table-valued argument '{Name}' requires TypeName and Structured SQL type metadata.");
                TableInput = new CallableTableInput(ValueType);
                return;
            }
            var type = Nullable.GetUnderlyingType(ValueType) ?? ValueType;
            DbType = Metadata.ExplicitDbType ?? (Metadata.ExplicitSqlDbType.HasValue ? (DbType?)null
                : (type.IsEnum ? Enum.GetUnderlyingType(type) : type).GetDbType());
            if (type == typeof(char) && Metadata.Size == 0) Metadata.Size = 1;
            bool temporal = DbType == System.Data.DbType.DateTime2 || DbType == System.Data.DbType.DateTimeOffset
                || DbType == System.Data.DbType.Time || Metadata.ExplicitSqlDbType == SqlDbType.DateTime2
                || Metadata.ExplicitSqlDbType == SqlDbType.DateTimeOffset || Metadata.ExplicitSqlDbType == SqlDbType.Time;
            if (temporal && Metadata.Scale > 7)
                throw new InvalidOperationException($"Temporal parameter '{Name}' requires a scale between zero and seven.");
        }
    }
}
