using System.Collections.Generic;

namespace SqlLinqer.Extensions.DictionaryExtensions
{
    /// <summary>
    /// A set of Dictionary extensions
    /// </summary>
    public static class DictionaryExtensions
    {
        /// <summary>
        /// Try to get the value from the dictionary and return 'default' if not found
        /// </summary>
        /// <param name="dic">The current dictionary</param>
        /// <param name="key">The key to get</param>
        public static TValue TryGetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dic, TKey key)
        {
            TValue val;
            if (dic.TryGetValue(key, out val))
                return val;
            return default;
        }
    }
}