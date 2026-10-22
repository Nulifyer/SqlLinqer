using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace SqlLinqer.Components.Caching
{
    internal sealed class MemoryCache<TKey, TValue> : IDisposable
    {
        private static readonly Func<long> DefaultTimestamp = CreateTimestamp();
        private readonly ConcurrentDictionary<TKey, CacheItem> _cacheDictionary = new ConcurrentDictionary<TKey, CacheItem>();
        private readonly Timer _cleanupTimer;
        private readonly bool _keepAliveWhenUsed;
        private readonly Func<long> _timestampMilliseconds;
        private readonly object _creationGate = new object();

        internal int Count => _cacheDictionary.Count;

        internal MemoryCache(TimeSpan cleanupInterval, bool keepAliveWhenUsed = false, Func<long> timestampMilliseconds = null)
        {
            _keepAliveWhenUsed = keepAliveWhenUsed;
            _timestampMilliseconds = timestampMilliseconds ?? DefaultTimestamp;
            _cleanupTimer = new Timer(CleanupExpiredItems, null, cleanupInterval, cleanupInterval);
        }

        internal void Add(TKey key, TValue value, TimeSpan ttl)
        {
            _cacheDictionary[key] = new CacheItem(value, ttl, _timestampMilliseconds());
        }

        internal TValue GetOrAdd(TKey key, Func<TKey, TValue> factory, TimeSpan ttl)
        {
            if (TryGetValue(key, out var value)) return value;
            // Misses are uncommon. Serialize construction so callers share one
            // mapping identity, including immediately after an entry expires.
            lock (_creationGate)
            {
                if (TryGetValue(key, out value)) return value;
                value = factory(key);
                Add(key, value, ttl);
                return value;
            }
        }

        internal bool TryGetValue(TKey key, out TValue value)
        {
            if (_cacheDictionary.TryGetValue(key, out var item) && item.TryGet(_timestampMilliseconds, _keepAliveWhenUsed, out value))
                return true;
            value = default(TValue);
            return false;
        }

        private void CleanupExpiredItems(object state) => RemoveExpired();

        internal void RemoveExpired()
        {
            long now = _timestampMilliseconds();
            foreach (var kvp in _cacheDictionary)
            {
                // Renewal and expiry checks share the entry lock. Conditional
                // removal cannot delete a replacement installed by another call.
                lock (kvp.Value)
                {
                    if (kvp.Value.HasExpired(now))
                        ((ICollection<KeyValuePair<TKey, CacheItem>>)_cacheDictionary).Remove(kvp);
                }
            }
        }

        public void Dispose() => _cleanupTimer.Dispose();

        private static Func<long> CreateTimestamp()
        {
            // TickCount64 avoids high-resolution clock overhead on modern
            // runtimes. Resolve once because netstandard2.0 does not expose it.
            // Older runtimes retain a monotonic Stopwatch fallback. Both clocks
            // return milliseconds and neither allocates on a cache hit.
            var getter = typeof(Environment).GetProperty("TickCount64")?.GetMethod;
            return getter == null ? new Func<long>(StopwatchMilliseconds)
                : (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), getter);
        }

        private static long StopwatchMilliseconds()
            => (long)(Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency));

        private sealed class CacheItem
        {
            private readonly long _ttlMilliseconds;
            private readonly TValue _value;
            private long _lastAccessMilliseconds;

            internal CacheItem(TValue value, TimeSpan ttl, long now)
            {
                _value = value;
                _lastAccessMilliseconds = now;
                double milliseconds = Math.Ceiling(ttl.TotalMilliseconds);
                _ttlMilliseconds = milliseconds >= long.MaxValue ? long.MaxValue : Math.Max(0, (long)milliseconds);
            }

            internal bool HasExpired(long now) => unchecked(now - _lastAccessMilliseconds) >= _ttlMilliseconds;

            internal bool TryGet(Func<long> timestampMilliseconds, bool keepAlive, out TValue value)
            {
                lock (this)
                {
                    long now = timestampMilliseconds();
                    if (!HasExpired(now))
                    {
                        if (keepAlive) _lastAccessMilliseconds = now;
                        value = _value;
                        return true;
                    }
                    value = default(TValue);
                    return false;
                }
            }
        }
    }
}
