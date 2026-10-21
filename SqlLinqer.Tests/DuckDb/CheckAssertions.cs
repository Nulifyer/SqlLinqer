namespace SqlLinqer.Tests.DuckDb;

internal static class CheckAssertions
{
    internal static void Expect(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static T Result<T>(SqlResponse<T> response)
    {
        if (response.State != ResponseState.Valid) throw new Exception(response.Error?.ToString());
        return response.Result;
    }
    internal static void Reject(Action action)
    {
        try { action(); } catch (NotSupportedException) { return; }
        throw new Exception("Expected NotSupportedException");
    }
}
