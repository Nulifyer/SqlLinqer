using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SqlLinqer.Components.Caching
{
    internal class MemoryCache<TKey, TValue>
    {
        private readonly ConcurrentDictionary<TKey, CacheItem<TValue>> _cacheDictionary;
        private readonly Timer _cleanupTimer;
        private readonly bool _keepAliveWhenUsed;

        public MemoryCache(TimeSpan cleanupInterval, bool keep_alive_when_used = false)
        {
            _keepAliveWhenUsed = keep_alive_when_used;
            _cacheDictionary = new ConcurrentDictionary<TKey, CacheItem<TValue>>();
            _cleanupTimer = new Timer(CleanupExpiredItems, null, cleanupInterval, cleanupInterval);
        }

        public void Add(TKey key, TValue value, TimeSpan ttl)
        {
            var newItem = new CacheItem<TValue>(value, ttl);
            _cacheDictionary[key] = newItem;
        }
        public TValue Get(TKey key)
        {
            if (_cacheDictionary.TryGetValue(key, out CacheItem<TValue> cacheItem) && !cacheItem.HasExpired)
            {
                if (_keepAliveWhenUsed)
                {
                    var renewed = cacheItem.GetRenewed();
                    _cacheDictionary.AddOrUpdate(key, renewed, (k, v) => renewed);
                }
                return cacheItem.Value;
            }
            return default(TValue);
        }
        public bool TryGetValue(TKey key, out TValue value)
        {
            if (_cacheDictionary.TryGetValue(key, out CacheItem<TValue> cacheItem) && !cacheItem.HasExpired)
            {
                if (_keepAliveWhenUsed)
                {
                    var renewed = cacheItem.GetRenewed();
                    _cacheDictionary.AddOrUpdate(key, renewed, (k, v) => renewed);
                }
                value = cacheItem.Value;
                return true;
            }
            value = default(TValue);
            return false;
        }
        private void CleanupExpiredItems(object state)
        {
            foreach (var kvp in _cacheDictionary)
            {
                if (kvp.Value.HasExpired)
                {
                    _cacheDictionary.TryRemove(kvp.Key, out _);
                }
            }
        }

        private class CacheItem<T>
        {
            private readonly TimeSpan _ttl;
            public readonly T Value;
            public readonly DateTimeOffset Expiration;

            public bool HasExpired => DateTimeOffset.Now >= Expiration;

            public CacheItem(T value, TimeSpan ttl)
            {
                Value = value;
                Expiration = DateTimeOffset.Now.Add(ttl);
                _ttl = ttl;
            }

            public CacheItem<T> GetRenewed()
            {
                return new CacheItem<T>(Value, _ttl);
            }
        }
    }
}