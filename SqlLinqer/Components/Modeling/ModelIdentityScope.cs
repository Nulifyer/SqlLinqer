using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;

namespace SqlLinqer.Components.Modeling
{
    // Identities are opaque tokens, not hashes. Each model graph owns its lookup
    // scope. Snapshots share this thread-safe metadata cache so a logical path
    // keeps its identity even when first discovered in a detached compilation.
    internal sealed class ModelIdentityScope
    {
        private static long next;
        private ConcurrentDictionary<(string Parent, string Member, Type Source, string Name), string> relationships;
        private ConcurrentDictionary<(string Table, string Column), string> columns;
        private static readonly Func<(string, string, Type, string), string> NewRelationship = _ => Next("$t");
        private static readonly Func<(string, string), string> NewColumn = _ => Next("$c");

        internal static string Next(string prefix)
        {
            long value = Interlocked.Increment(ref next);
            if (value <= 0) throw new InvalidOperationException("Model identity space is exhausted.");
            return prefix + value.ToString("x", CultureInfo.InvariantCulture);
        }

        internal string Relationship(string parent, string member, Type source, string name)
        {
            var map = Volatile.Read(ref relationships);
            if (map == null)
            {
                var created = new ConcurrentDictionary<(string, string, Type, string), string>(1, 8);
                map = Interlocked.CompareExchange(ref relationships, created, null) ?? created;
            }
            return map.GetOrAdd((parent, member, source, name), NewRelationship);
        }

        internal string Column(string table, string column)
        {
            var map = Volatile.Read(ref columns);
            if (map == null)
            {
                var created = new ConcurrentDictionary<(string, string), string>(1, 8);
                map = Interlocked.CompareExchange(ref columns, created, null) ?? created;
            }
            return map.GetOrAdd((table, column ?? string.Empty), NewColumn);
        }
    }
}
