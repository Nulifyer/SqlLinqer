using System.Threading;
using System.Threading.Tasks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Reflection;
using SqlLinqer.Extensions.MemberInfoExtensions;

namespace SqlLinqer.Components.Modeling
{
    internal sealed class CallableResultSet
    {
        internal readonly MemberInfo Member;
        internal readonly int Index;
        private readonly Type _listType;
        private readonly bool _array;
        private readonly TabularRowType _row;

        internal CallableResultSet(MemberInfo member, int index)
        {
            Member = member;
            Index = index;
            var type = member.GetFieldOrPropValueType();
            _array = type.IsArray && type.GetArrayRank() == 1;
            var item = _array ? type.GetElementType() : type.IsGenericType && type.GetGenericArguments().Length == 1
                ? type.GetGenericArguments()[0] : null;
            if (item == null || !(_array || type.IsAssignableFrom(typeof(List<>).MakeGenericType(item))))
                throw new InvalidOperationException($"Result set '{member.Name}' must accept a List<T> or be a one-dimensional array.");
            _listType = typeof(List<>).MakeGenericType(item);
            _row = new TabularRowType(item);
        }

        internal object Read(DbDataReader reader)
        {
            var rows = (IList)Activator.CreateInstance(_listType);
            var ordinals = _row.GetOrdinals(reader);
            while (reader.Read()) rows.Add(_row.Read(reader, ordinals));
            if (!_array) return rows;
            var array = Array.CreateInstance(_row.Type, rows.Count);
            rows.CopyTo(array, 0);
            return array;
        }
        internal async Task<object> ReadAsync(DbDataReader reader, CancellationToken cancellationToken)
        {
            var rows = (IList)Activator.CreateInstance(_listType);
            var ordinals = _row.GetOrdinals(reader);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                rows.Add(_row.Read(reader, ordinals));
            }
            if (!_array) return rows;
            var array = Array.CreateInstance(_row.Type, rows.Count);
            rows.CopyTo(array, 0);
            return array;
        }

    }

}
