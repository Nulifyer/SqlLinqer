using System;
using System.Collections;
using System.Linq;
using System.Data;
using SqlLinqer.Components.Insert;
using SqlLinqer.Extensions.MemberInfoExtensions;
using SqlLinqer.Extensions.TypeExtensions;

namespace SqlLinqer.Components.Modeling
{
    // Reuses InsertAuto's column selection and TVP payload writer for existing procedure table types.
    internal sealed class CallableTableInput
    {
        private readonly Type _rowType;
        private readonly bool _collection;
        private readonly ReflectedColumn[] _columns;

        internal CallableTableInput(Type type)
        {
            var element = type.GetItemTypeFromIEnumerable();
            _collection = element != null;
            _rowType = element ?? type;
            if (Connections.DbValueConversion.IsScalarType(_rowType) || _rowType.IsInterface || _rowType.IsAbstract
                || typeof(IEnumerable).IsAssignableFrom(_rowType))
                throw new InvalidOperationException($"Table-valued input '{type}' requires mapped row objects.");
            _columns = Table.GetCached(_rowType).Columns.GetAll<ReflectedColumn>().Where(c => c.AutoOptions.Insert).ToArray();
            if (_columns.Length == 0) throw new InvalidOperationException($"Table-valued row '{_rowType}' has no insertable mapped columns.");
            if (_columns.Select(c => c.ColumnName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _columns.Length)
                throw new InvalidOperationException($"Table-valued row '{_rowType}' has duplicate mapped columns.");
            foreach (var column in _columns)
            {
                column.CodeMember.RequireReadable();
                if (column.ValueType == typeof(object) || !Connections.DbValueConversion.IsScalarType(column.DeclaredValueType))
                    throw new InvalidOperationException($"Table-valued column '{column.ColumnName}' requires a scalar value.");
            }
        }

        internal DataTable Materialize(object value)
        {
            var rows = new InsertRowCollection();
            foreach (var column in _columns) rows.AddColumn(column);
            if (_collection)
                foreach (var row in (IEnumerable)value) Add(row);
            else Add(value);
            return rows.RenderAsDataTable();

            void Add(object row)
            {
                if (row == null || !_rowType.IsInstanceOfType(row))
                    throw new ArgumentException($"Table-valued input requires non-null '{_rowType}' rows.", nameof(value));
                rows.AddRow(row);
            }
        }
    }
}
