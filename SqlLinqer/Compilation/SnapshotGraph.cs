using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Select;
using SqlLinqer.Components.Where;
using SqlLinqer.Queries.Core.Abstract;

namespace SqlLinqer.Compilation
{
    // Compatibility adapter for the existing public, mutable query graph. Field plans
    // are discovered once per runtime type, including user-derived query nodes. Identity
    // tracking preserves shared CTEs, column references, and model relationship cycles.
    // The snapshot owns its graph; every compilation receives a separate working graph.
    internal sealed class SnapshotGraph
    {
        private static readonly Func<object, object> ShallowCopy = CreateShallowCopy();
        private static readonly ConcurrentDictionary<Type, Action<SnapshotGraph, object, object>> Fields = new ConcurrentDictionary<Type, Action<SnapshotGraph, object, object>>();
        private static readonly ConcurrentDictionary<Type, Func<object, object>> CollectionFactories = new ConcurrentDictionary<Type, Func<object, object>>();
        private static readonly Func<Type, Action<SnapshotGraph, object, object>> FieldCopierFactory = CreateFieldCopier;
        private static readonly Func<Type, Func<object, object>> CollectionFactory = CreateCollectionFactory;
        private readonly Dictionary<object, object> copies = new Dictionary<object, object>(ReferenceComparer.Instance);

        internal static T Capture<T>(T value) where T : class => (T)new SnapshotGraph().Copy(value);

        // Callable preparation creates fresh bindings. Reuse private generated tables and snapshot their mutable cells;
        // borrowed tables and scalar arrays still receive independent copies through the same identity-tracking graph.
        internal static void CaptureCallableInputs(SqlLinqer.Components.Render.RenderedQuery query)
        {
            var graph = new SnapshotGraph();
            foreach (var table in query.OwnedTables)
            {
                graph.copies.Add(table, table);
                graph.CopyTableCells(table, table);
            }
            query.Parameters = (SqlLinqer.Components.Render.ParameterCollection)graph.Copy(query.Parameters);
            foreach (var parameter in query.Parameters.GetAll())
                if (parameter.Value is DataTable table) query.OwnTable(table);
        }

        private void CopyTableCells(DataTable source, DataTable destination)
        {
            // DataTable.Copy retains reference-valued cells.
            for (int row = 0; row < destination.Rows.Count; row++)
                for (int column = 0; column < destination.Columns.Count; column++)
                {
                    var cell = source.Rows[row][column];
                    var copied = Copy(cell);
                    if (ReferenceEquals(cell, copied)) continue;
                    var definition = destination.Columns[column];
                    bool readOnly = definition.ReadOnly;
                    if (readOnly) definition.ReadOnly = false;
                    try { destination.Rows[row][column] = copied; }
                    finally { if (readOnly) definition.ReadOnly = true; }
                }
        }

        private object Copy(object value)
        {
            if (value == null) return null;
            var type = value.GetType();
            if (type.IsValueType || value is string || value is Type || value is MemberInfo || value is Delegate || value is ModelIdentityScope)
                return value;
            if (copies.TryGetValue(value, out var existing)) return existing;

            if (value is DataTable table)
            {
                var result = table.Copy();
                copies.Add(value, result);
                CopyTableCells(table, result);
                return result;
            }
            if (value is Array array)
            {
                var result = (Array)array.Clone();
                copies.Add(value, result);
                if (!type.GetElementType().IsValueType)
                {
                    if (array.Rank == 1)
                        for (int i = array.GetLowerBound(0); i <= array.GetUpperBound(0); i++)
                            result.SetValue(Copy(array.GetValue(i)), i);
                    else CopyArray(array, result, new int[array.Rank], 0);
                }
                return result;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var result = (IDictionary)CollectionFactories.GetOrAdd(type, CollectionFactory)(value);
                copies.Add(value, result);
                foreach (DictionaryEntry item in (IDictionary)value)
                    result.Add(Copy(item.Key), Copy(item.Value));
                return result;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var result = (IList)CollectionFactories.GetOrAdd(type, CollectionFactory)(value);
                copies.Add(value, result);
                foreach (var item in (IList)value) result.Add(Copy(item));
                return result;
            }
            if (value is IEnumerable<Column> columns && !(value is ColumnCollection))
            {
                var result = columns.Select(c => (Column)Copy(c)).ToArray();
                copies.Add(value, result);
                return result;
            }

            // Provider-specific scalar values and runtime dependencies remain opaque.
            // They must be immutable for the lifetime of a snapshot, as documented.
            if (!Fields.TryGetValue(type, out var copyFields))
            {
                if (!IsQueryNode(type)) return value;
                copyFields = Fields.GetOrAdd(type, FieldCopierFactory);
            }

            var clone = ShallowCopy(value);
            copies.Add(value, clone);
            copyFields(this, value, clone);
            if (clone is BaseQuery query) query.IsCompilationCopy = true;
            if (clone is SqlLinqer.Components.Insert.InsertRowCollection inserts) inserts.CaptureValues(Copy);
            return clone;
        }

        private void CopyArray(Array source, Array destination, int[] indices, int dimension)
        {
            for (int i = source.GetLowerBound(dimension); i <= source.GetUpperBound(dimension); i++)
            {
                indices[dimension] = i;
                if (dimension + 1 == source.Rank) destination.SetValue(Copy(source.GetValue(indices)), indices);
                else CopyArray(source, destination, indices, dimension + 1);
            }
        }

        private static FieldInfo[] GetFields(Type type)
        {
            var fields = new List<FieldInfo>();
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
                fields.AddRange(current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(f => !f.FieldType.IsValueType && f.FieldType != typeof(string)));
            return fields.ToArray();
        }

        private static bool IsQueryNode(Type type)
        {
            if (typeof(BaseQuery).IsAssignableFrom(type) || typeof(SelectStatement).IsAssignableFrom(type)
                || typeof(IWhereComponent).IsAssignableFrom(type)
                || typeof(SqlLinqer.Components.Ordering.IOrderByStatement).IsAssignableFrom(type)
                || typeof(SqlLinqer.Components.Outputs.IOutputStatement).IsAssignableFrom(type)) return true;
            for (var owner = type; owner != null; owner = owner.BaseType)
                if (owner.Assembly == typeof(BaseQuery).Assembly
                    && (owner.Namespace.StartsWith("SqlLinqer.Components.", StringComparison.Ordinal)
                        || owner.Namespace.StartsWith("SqlLinqer.Modeling", StringComparison.Ordinal))) return true;
            return false;
        }

        private static Action<SnapshotGraph, object, object> CreateFieldCopier(Type type)
        {
            var graph = Expression.Parameter(typeof(SnapshotGraph));
            var source = Expression.Parameter(typeof(object));
            var destination = Expression.Parameter(typeof(object));
            var copy = typeof(SnapshotGraph).GetMethod(nameof(Copy), BindingFlags.NonPublic | BindingFlags.Instance);
            var setValue = typeof(FieldInfo).GetMethod(nameof(FieldInfo.SetValue), new[] { typeof(object), typeof(object) });
            var assignments = new List<Expression>();
            foreach (var field in GetFields(type))
            {
                var input = Expression.Field(Expression.Convert(source, field.DeclaringType), field);
                var output = Expression.Call(graph, copy, Expression.Convert(input, typeof(object)));
                // Readonly backing fields still need reflection to preserve the
                // existing snapshot contract; other fields use direct access.
                assignments.Add(field.IsInitOnly
                    ? (Expression)Expression.Call(Expression.Constant(field), setValue, destination, output)
                    : Expression.Assign(Expression.Field(Expression.Convert(destination, field.DeclaringType), field), Expression.Convert(output, field.FieldType)));
            }
            assignments.Add(Expression.Empty());
            return Expression.Lambda<Action<SnapshotGraph, object, object>>(Expression.Block(assignments), graph, source, destination).Compile();
        }

        private static Func<object, object> CreateCollectionFactory(Type type)
        {
            var source = Expression.Parameter(typeof(object));
            var typed = Expression.Convert(source, type);
            var count = Expression.Property(typed, "Count");
            NewExpression create;
            if (type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var comparer = Expression.Property(typed, "Comparer");
                create = Expression.New(type.GetConstructor(new[] { typeof(int), comparer.Type }), count, comparer);
            }
            else create = Expression.New(type.GetConstructor(new[] { typeof(int) }), count);
            return Expression.Lambda<Func<object, object>>(Expression.Convert(create, typeof(object)), source).Compile();
        }

        private static Func<object, object> CreateShallowCopy()
        {
            var value = Expression.Parameter(typeof(object));
            var method = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
            return Expression.Lambda<Func<object, object>>(Expression.Call(value, method), value).Compile();
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
