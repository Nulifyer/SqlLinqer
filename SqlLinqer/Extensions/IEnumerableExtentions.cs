using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlLinqer.Extensions.IEnumerableExtentions
{
    /// <summary>
    /// A set of IEnumberable extensions
    /// </summary>
    public static class IEnumerableExtentions
    {
        /// <summary>
        /// Returns a 2D array of items where each sub array is of the set size
        /// </summary>
        /// <param name="source">The current array of items</param>
        /// <param name="size">The size to make each sub array</param>
        public static IEnumerable<IEnumerable<T>> Batch<T>(this IEnumerable<T> source, int size)
        {
            if (size < 1) size = 1;
            var list = source.ToList();
            for (int i = 0; i < list.Count(); i += size)
            {
                yield return list.GetRange(i, Math.Min(size, list.Count() - i));
            }
        }
        /// <summary>
        /// Returns distinct elements from a sequence based on a specified key.
        /// </summary>
        /// <typeparam name="T">The type of elements in the source sequence.</typeparam>
        /// <typeparam name="TKey">The type of the key used for distinct comparison.</typeparam>
        /// <param name="source">The source sequence.</param>
        /// <param name="keySelector">A function to extract the key for each element.</param>
        /// <returns>An <see cref="IEnumerable{T}"/> that contains distinct elements based on the specified key.</returns>
        /// <remarks>
        /// This extension method uses a hash set to keep track of keys seen during enumeration
        /// and yields only elements with distinct keys in the order they appear in the source sequence.
        /// </remarks>
        /// <example>
        /// <code>
        /// var persons = new List&lt;Person&gt;
        /// {
        ///     new Person { Id = 1, Name = "Alice" },
        ///     new Person { Id = 2, Name = "Bob" },
        ///     new Person { Id = 1, Name = "Another Alice" },
        /// };
        ///
        /// var distinctPersons = persons.DistinctBy(p => p.Id);
        ///
        /// foreach (var person in distinctPersons)
        /// {
        ///     Console.WriteLine($"{person.Id} - {person.Name}");
        /// }
        /// </code>
        /// </example>
        /// <seealso cref="HashSet{T}"/>
        public static IEnumerable<T> DistinctBy<T, TKey>(this IEnumerable<T> source, Func<T, TKey> keySelector)
        {
            HashSet<TKey> seenKeys = new HashSet<TKey>();
            foreach (T element in source)
            {
                TKey key = keySelector(element);
                if (seenKeys.Add(key))
                {
                    yield return element;
                }
            }
        }
    }
}