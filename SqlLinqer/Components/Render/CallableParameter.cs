using System;
using System.Data;
using System.Data.Common;
using SqlLinqer.Modeling;

namespace SqlLinqer.Components.Render
{
    internal sealed class CallableParameter : Parameter
    {
        internal readonly ParameterDirection Direction;
        private readonly SqlParameterAttribute _metadata;
        private readonly string _typeName;

        internal CallableParameter(string placeholder, object value, DbType? dbType,
            ParameterDirection direction, SqlParameterAttribute metadata, string defaultSchema = null) : base(placeholder, value, dbType)
        {
            Direction = direction;
            _metadata = metadata;
            _typeName = metadata.TypeName;
            if (_typeName != null && defaultSchema != null && !_typeName.Contains(".")) _typeName = defaultSchema + "." + _typeName;
        }

        internal void Configure(DbParameter parameter)
        {
            parameter.Direction = Direction;
            if (_metadata.ExplicitSqlDbType.HasValue || _typeName != null)
            {
                var property = parameter.GetType().GetProperty("SqlDbType");
                if (property == null || !property.CanWrite)
                    throw new NotSupportedException("Callable parameters require a SQL Server provider with SqlDbType support.");
                property.SetValue(parameter, Enum.ToObject(property.PropertyType,
                    (int)(_typeName != null ? SqlDbType.Structured : _metadata.ExplicitSqlDbType.Value)));
            }
            if (_typeName != null)
            {
                var property = parameter.GetType().GetProperty("TypeName");
                if (property == null || !property.CanWrite)
                    throw new NotSupportedException("The provider does not support SQL Server table type names.");
                property.SetValue(parameter, _typeName);
            }
            if (_metadata.Size != 0) parameter.Size = _metadata.Size;
            if (_metadata.ExplicitPrecision.HasValue) parameter.Precision = _metadata.Precision;
            if (_metadata.ExplicitScale.HasValue) parameter.Scale = _metadata.Scale;
        }
    }
}
