using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SqlLinqer.Extensions.TypeExtensions;

namespace SqlLinqer.Modeling
{
    /// <summary>Registers collection factories for multi-callable result tables. Register custom types during application startup.</summary>
    public static class SqlResultCollections
    {
        private static readonly ConcurrentDictionary<Type, Func<IList, object>> Factories = new ConcurrentDictionary<Type, Func<IList, object>>();

        /// <summary>Registers or replaces the factory for an exact collection type. In-flight calls retain their captured factory.</summary>
        /// <typeparam name="TRow">The table row type.</typeparam>
        /// <typeparam name="TCollection">The declared collection type, implementing IEnumerable of the row type.</typeparam>
        /// <param name="factory">Converts buffered rows in database order into a non-null collection. Exceptions fail the entire call.</param>
        public static void Register<TRow, TCollection>(Func<IReadOnlyList<TRow>, TCollection> factory) where TCollection : IEnumerable<TRow>
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (GetRowType(typeof(TCollection)) != typeof(TRow))
                throw new ArgumentException("The factory row type must exactly match the declared collection row type.", nameof(factory));
            Factories[typeof(TCollection)] = rows => factory(((List<TRow>)rows).AsReadOnly());
        }

        internal static Type GetRowType(Type collection)
        {
            var rowType = collection.GetItemTypeFromIEnumerable();
            if (rowType == null)
                throw new InvalidOperationException($"Result table '{collection}' must be a collection with one unambiguous row type.");
            return rowType;
        }

        internal static Func<IList, object> Resolve<TRow>(Type collection)
        {
            if (Factories.TryGetValue(collection, out var factory)) return factory;
            if (collection == typeof(TRow[])) return rows => ((List<TRow>)rows).ToArray();
            if (collection == typeof(List<TRow>) || collection == typeof(IList<TRow>) || collection == typeof(ICollection<TRow>))
                return rows => rows;
            if (collection == typeof(IEnumerable<TRow>) || collection == typeof(IReadOnlyList<TRow>)
                || collection == typeof(IReadOnlyCollection<TRow>) || collection == typeof(ReadOnlyCollection<TRow>))
                return rows => ((List<TRow>)rows).AsReadOnly();
            if (collection == typeof(Collection<TRow>)) return rows => new Collection<TRow>((List<TRow>)rows);
            if (collection == typeof(HashSet<TRow>) || collection == typeof(ISet<TRow>)) return rows => new HashSet<TRow>((List<TRow>)rows);
            if (collection == typeof(Queue<TRow>)) return rows => new Queue<TRow>((List<TRow>)rows);
            if (collection == typeof(Stack<TRow>)) return rows => new Stack<TRow>((List<TRow>)rows);
            if (collection == typeof(LinkedList<TRow>)) return rows => new LinkedList<TRow>((List<TRow>)rows);
            throw new InvalidOperationException($"Result collection '{collection}' is not supported. Register a factory with SqlResultCollections.Register.");
        }
    }
}
