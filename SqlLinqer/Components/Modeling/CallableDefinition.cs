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
    // Definition objects contain metadata only. Values and provider parameter handles belong to an invocation.
    internal sealed class CallableDefinition
    {
        internal readonly SqlProcedure Procedure;
        internal readonly CallableBinding[] Parameters;
        internal readonly CallableResultSet[] ResultSets;
        internal readonly MemberInfo ReturnValue;
        internal readonly string TotalResultsParameter;

        internal CallableDefinition(Type callable, Type args, Type result, SqlProcedure procedure = null, string totalResultsParameter = null)
        {
            Procedure = procedure ?? (SqlProcedure)Attribute.GetCustomAttribute(callable, typeof(SqlProcedure), false)
                ?? throw new InvalidOperationException($"Callable '{callable}' needs a SqlProcedure attribute.");
            if (string.IsNullOrWhiteSpace(Procedure.Name) || Procedure.Schema != null && string.IsNullOrWhiteSpace(Procedure.Schema))
                throw new InvalidOperationException("Procedure name and schema must not be empty.");

            var bindings = new Dictionary<string, CallableBinding>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in Members(args))
            {
                member.RequireReadable();
                var metadata = member.GetCustomAttribute<SqlParameterAttribute>() ?? new SqlParameterAttribute();
                if (metadata is SqlOutputParameter)
                    throw new InvalidOperationException($"Output parameter '{member.Name}' belongs on the result model.");
                var name = ParameterName(metadata.Name ?? member.Name);
                if (bindings.ContainsKey(name)) throw new InvalidOperationException($"Duplicate input parameter '{name}'.");
                bindings.Add(name, new CallableBinding(name, member, metadata));
            }

            var sets = new List<CallableResultSet>();
            foreach (var member in Members(result))
            {
                member.RequireWritable();
                var output = member.GetCustomAttribute<SqlOutputParameter>();
                var set = member.GetCustomAttribute<SqlResultSet>();
                bool returnValue = member.GetCustomAttribute<SqlReturnValue>() != null;
                if ((output != null ? 1 : 0) + (set != null ? 1 : 0) + (returnValue ? 1 : 0) != 1)
                    throw new InvalidOperationException($"Result member '{member.Name}' needs exactly one SqlResultSet, SqlOutputParameter, or SqlReturnValue attribute. Use SqlIgnore for other members.");
                if (output != null)
                {
                    var name = ParameterName(output.Name ?? member.Name);
                    if (!bindings.TryGetValue(name, out var binding))
                    {
                        binding = new CallableBinding(name, null, output);
                        bindings.Add(name, binding);
                    }
                    binding.AddOutput(member, output);
                }
                else if (set != null)
                    sets.Add(new CallableResultSet(member, set.Index));
                else
                {
                    if (ReturnValue != null || member.GetFieldOrPropValueType() != typeof(int))
                        throw new InvalidOperationException("A callable may have one SqlReturnValue member of type Int32.");
                    ReturnValue = member;
                }
            }
            Parameters = bindings.Values.OrderBy(x => x.Metadata.Order).ThenBy(x => x.Name, StringComparer.Ordinal).ToArray();
            foreach (var parameter in Parameters) parameter.Validate();
            if (totalResultsParameter != null)
            {
                TotalResultsParameter = ParameterName(totalResultsParameter);
                if (bindings.ContainsKey(TotalResultsParameter))
                    throw new InvalidOperationException("The total results output parameter must not also be declared on the input model.");
            }
            ResultSets = sets.OrderBy(x => x.Index).ToArray();
            for (int i = 0; i < ResultSets.Length; i++)
                if (ResultSets[i].Index != i)
                    throw new InvalidOperationException("Result-set indexes must be unique and contiguous, starting at zero.");
        }

        internal static IEnumerable<MemberInfo> Members(Type type)
            => type.GetInstanceFieldsAndPropertyMembers().Where(m => m.GetCustomAttribute<SqlIgnore>() == null);

        private static string ParameterName(string name)
        {
            if (name.StartsWith("@", StringComparison.Ordinal)) name = name.Substring(1);
            if (name.Length == 0 || name.Length > 127 || !(char.IsLetter(name[0]) || name[0] == '_')
                || name.Any(c => !(char.IsLetterOrDigit(c) || c == '_')))
                throw new InvalidOperationException($"Invalid SQL parameter name '{name}'.");
            return name;
        }
    }

    internal sealed class CallableBinding
    {
        internal readonly string Name;
        internal readonly MemberInfo Input;
        internal MemberInfo Output { get; private set; }
        internal SqlParameterAttribute Metadata { get; private set; }
        internal Type ValueType => Unwrap((Input ?? Output).GetFieldOrPropValueType());
        internal DbType? DbType { get; private set; }

        internal CallableBinding(string name, MemberInfo input, SqlParameterAttribute metadata)
        { Name = name; Input = input; Metadata = metadata; }

        internal static Type Unwrap(Type type)
            => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SqlArgument<>) ? type.GetGenericArguments()[0] : type;

        internal void AddOutput(MemberInfo output, SqlOutputParameter metadata)
        {
            if (Output != null) throw new InvalidOperationException($"Duplicate output parameter '{Name}'.");
            if (output.GetFieldOrPropValueType() != Unwrap(output.GetFieldOrPropValueType()))
                throw new InvalidOperationException("SqlArgument<T> is only supported on input members.");
            if (Input != null && ValueType != output.GetFieldOrPropValueType())
                throw new InvalidOperationException($"Input and output CLR types differ for '{Name}'.");
            if (Input != null)
            {
                if (Metadata.ExplicitDbType.HasValue && metadata.ExplicitDbType.HasValue && Metadata.ExplicitDbType != metadata.ExplicitDbType
                    || Metadata.ExplicitSqlDbType.HasValue && metadata.ExplicitSqlDbType.HasValue && Metadata.ExplicitSqlDbType != metadata.ExplicitSqlDbType
                    || Metadata.Size != 0 && metadata.Size != 0 && Metadata.Size != metadata.Size
                    || Metadata.ExplicitPrecision.HasValue && metadata.ExplicitPrecision.HasValue && Metadata.ExplicitPrecision != metadata.ExplicitPrecision
                    || Metadata.ExplicitScale.HasValue && metadata.ExplicitScale.HasValue && Metadata.ExplicitScale != metadata.ExplicitScale)
                    throw new InvalidOperationException($"Input and output metadata differ for '{Name}'.");
                metadata.ExplicitDbType = metadata.ExplicitDbType ?? Metadata.ExplicitDbType;
                metadata.ExplicitSqlDbType = metadata.ExplicitSqlDbType ?? Metadata.ExplicitSqlDbType;
                metadata.ExplicitPrecision = metadata.ExplicitPrecision ?? Metadata.ExplicitPrecision;
                metadata.ExplicitScale = metadata.ExplicitScale ?? Metadata.ExplicitScale;
                if (metadata.Size == 0) metadata.Size = Metadata.Size;
                metadata.TypeName = metadata.TypeName ?? Metadata.TypeName;
            }
            Metadata = metadata;
            Output = output;
        }

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
                if (Output != null || string.IsNullOrWhiteSpace(Metadata.TypeName)
                    || Metadata.ExplicitSqlDbType.HasValue && Metadata.SqlDbType != SqlDbType.Structured)
                    throw new InvalidOperationException($"DataTable argument '{Name}' requires TypeName and must be input-only.");
                return;
            }
            if (Metadata.TypeName != null || Metadata.ExplicitSqlDbType == SqlDbType.Structured)
                throw new InvalidOperationException($"Structured argument '{Name}' must be a DataTable.");
            var type = Nullable.GetUnderlyingType(ValueType) ?? ValueType;
            DbType = Metadata.ExplicitDbType ?? (Metadata.ExplicitSqlDbType.HasValue ? (DbType?)null
                : (type.IsEnum ? Enum.GetUnderlyingType(type) : type).GetDbType());
            if (type == typeof(char) && Metadata.Size == 0) Metadata.Size = 1;
            bool variableLength = type == typeof(string) || type == typeof(byte[])
                || DbType == System.Data.DbType.String || DbType == System.Data.DbType.AnsiString
                || DbType == System.Data.DbType.StringFixedLength || DbType == System.Data.DbType.AnsiStringFixedLength
                || DbType == System.Data.DbType.Binary
                || Metadata.ExplicitSqlDbType == SqlDbType.NVarChar || Metadata.ExplicitSqlDbType == SqlDbType.VarChar
                || Metadata.ExplicitSqlDbType == SqlDbType.NChar || Metadata.ExplicitSqlDbType == SqlDbType.Char
                || Metadata.ExplicitSqlDbType == SqlDbType.VarBinary || Metadata.ExplicitSqlDbType == SqlDbType.Binary;
            if (Output != null && Metadata.Size == 0 && variableLength)
                throw new InvalidOperationException($"Variable-length output '{Name}' requires Size.");
            bool temporal = DbType == System.Data.DbType.DateTime2 || DbType == System.Data.DbType.DateTimeOffset
                || DbType == System.Data.DbType.Time || Metadata.ExplicitSqlDbType == SqlDbType.DateTime2
                || Metadata.ExplicitSqlDbType == SqlDbType.DateTimeOffset || Metadata.ExplicitSqlDbType == SqlDbType.Time;
            if (temporal && Metadata.Scale > 7)
                throw new InvalidOperationException($"Temporal parameter '{Name}' requires a scale between zero and seven.");
        }
    }
}
