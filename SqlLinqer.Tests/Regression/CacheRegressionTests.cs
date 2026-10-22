using SqlLinqer.Components.Caching;
using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Tests.Regression;

internal static class CacheRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("cache distinguishes a missing key from a cached null", () =>
        {
            using var cache = new MemoryCache<string, object>(Timeout.InfiniteTimeSpan);
            Check(!cache.TryGetValue("missing", out var missing) && missing == null, "Missing key returned a value");
            cache.Add("null", null, TimeSpan.FromMinutes(1));
            Check(cache.TryGetValue("null", out var value) && value == null, "Cached null became a miss");
        });

        test("absolute cache reads do not renew expiry", () =>
        {
            var clock = new Clock();
            using var cache = new MemoryCache<string, int>(Timeout.InfiniteTimeSpan, timestampMilliseconds: clock.Read);
            cache.Add("key", 7, TimeSpan.FromSeconds(10));
            clock.Advance(9);
            Check(cache.TryGetValue("key", out int value) && value == 7, "Entry expired early");
            clock.Advance(1);
            Check(!cache.TryGetValue("key", out value) && value == 0, "Absolute expiry was renewed");
        });

        test("sliding cache reads renew only the accessed entry", () =>
        {
            var clock = new Clock();
            using var cache = new MemoryCache<string, int>(Timeout.InfiniteTimeSpan, true, clock.Read);
            cache.Add("used", 7, TimeSpan.FromSeconds(10));
            cache.Add("idle", 8, TimeSpan.FromSeconds(10));
            clock.Advance(9);
            Check(cache.TryGetValue("used", out _), "Entry expired early");
            clock.Advance(9);
            cache.RemoveExpired();
            Check(cache.Count == 1, "Cleanup retained the idle entry");
            Check(cache.TryGetValue("used", out _), "Cleanup removed a renewed entry");
            Check(!cache.TryGetValue("idle", out _), "Idle entry was renewed");
            clock.Advance(10);
            Check(!cache.TryGetValue("used", out _), "Entry survived its idle interval");
        });

        test("cache does not revive expired or zero-lifetime entries", () =>
        {
            var clock = new Clock();
            using var cache = new MemoryCache<string, int>(Timeout.InfiniteTimeSpan, true, clock.Read);
            cache.Add("expired", 7, TimeSpan.FromSeconds(1));
            cache.Add("zero", 8, TimeSpan.Zero);
            Check(!cache.TryGetValue("zero", out _), "Zero TTL survived");
            clock.Advance(1);
            Check(!cache.TryGetValue("expired", out _), "Expired sliding entry was revived");
        });

        test("expired cleanup cannot remove a concurrent replacement", () =>
        {
            var clock = new Clock();
            using var cache = new MemoryCache<CallbackKey, string>(Timeout.InfiniteTimeSpan, true, clock.Read);
            var key = new CallbackKey();
            cache.Add(key, "old", TimeSpan.FromSeconds(1));
            clock.Advance(2);
            // The dictionary hashes the key after the sweep finds the old entry
            // expired. Replace it at that exact point, before conditional removal.
            key.OnHash = () => cache.Add(key, "replacement", TimeSpan.FromSeconds(10));
            cache.RemoveExpired();
            Check(key.OnHash == null, "Cleanup did not exercise the replacement race");
            Check(cache.TryGetValue(key, out var value) && value == "replacement", "Sweep removed the new entry");
        });

        test("a delayed cache hit cannot overwrite a concurrent replacement", () =>
        {
            var clock = new Clock();
            using var cache = new MemoryCache<string, string>(Timeout.InfiniteTimeSpan, true, clock.Read);
            cache.Add("key", "old", TimeSpan.FromSeconds(10));
            clock.OnRead = () => cache.Add("key", "replacement", TimeSpan.FromSeconds(10));
            Check(cache.TryGetValue("key", out _), "Racing read failed");
            Check(cache.TryGetValue("key", out var value) && value == "replacement", "Old renewal replaced the new value");
        });

        test("concurrent cache misses share one constructed value", () =>
        {
            var clock = new Clock();
            using var cache = new MemoryCache<string, object>(Timeout.InfiniteTimeSpan, true, clock.Read);
            int constructed = 0;
            Func<string, object> factory = _ => { Interlocked.Increment(ref constructed); return new object(); };
            var values = new object[128];
            Parallel.For(0, values.Length, i => values[i] = cache.GetOrAdd("key", factory, TimeSpan.FromSeconds(1)));
            Check(constructed == 1 && values.All(value => ReferenceEquals(value, values[0])), "Cold misses returned different mappings");
            clock.Advance(1);
            object previous = values[0];
            Parallel.For(0, values.Length, i => values[i] = cache.GetOrAdd("key", factory, TimeSpan.FromSeconds(1)));
            Check(constructed == 2 && !ReferenceEquals(previous, values[0])
                && values.All(value => ReferenceEquals(value, values[0])), "Expired misses returned different mappings");
        });

        test("a failed cache factory can be retried", () =>
        {
            using var cache = new MemoryCache<string, object>(Timeout.InfiniteTimeSpan);
            try { cache.GetOrAdd("key", _ => throw new InvalidOperationException("factory"), TimeSpan.FromMinutes(1)); }
            catch (InvalidOperationException) { }
            var expected = new object();
            Check(ReferenceEquals(expected, cache.GetOrAdd("key", _ => expected, TimeSpan.FromMinutes(1))), "Failure poisoned the key");
        });

        test("metadata cache hits allocate no managed memory", () =>
        {
            // Warm the real public entry point and its sliding cache. The two
            // schemas must keep independent identities without allocating keys.
            var first = Table.GetCached<CacheRow>("cache_one");
            var second = Table.GetCached<CacheRow>("cache_two");
            for (int i = 0; i < 2000; i++) Table.GetCached<CacheRow>("cache_one");
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) Table.GetCached<CacheRow>("cache_one");
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated == 0, $"Warm cache allocated {allocated} bytes");
            Check(!ReferenceEquals(first, second) && ReferenceEquals(first, Table.GetCached<CacheRow>("cache_one")), "Schema cache identities differ");
        });

        test("concurrent table cache misses preserve mapping identity", () =>
        {
            var tables = new Table[128];
            Parallel.For(0, tables.Length, i => tables[i] = Table.GetCached<CacheRow>("cache_parallel"));
            Check(tables.All(table => ReferenceEquals(table, tables[0])), "Concurrent queries received different root mappings");
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class Clock
    {
        private long _now;
        internal Action OnRead;
        internal long Read()
        {
            var action = Interlocked.Exchange(ref OnRead, null);
            action?.Invoke();
            return Interlocked.Read(ref _now);
        }
        internal void Advance(int seconds) => Interlocked.Add(ref _now, seconds * 1000L);
    }

    private sealed class CallbackKey
    {
        internal Action OnHash;
        public override int GetHashCode()
        {
            var action = Interlocked.Exchange(ref OnHash, null);
            action?.Invoke();
            return 7;
        }
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
    }

    private sealed class CacheRow { public int Id { get; set; } }
}
