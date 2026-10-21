namespace SqlLinqer.Tests.Sqlite;

internal static class CheckAssertions
{
    internal static void Expect(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
    internal static T Result<T>(SqlResponse<T> response)
    {
        if (response.State != ResponseState.Valid) throw response.Error;
        return response.Result;
    }
    internal static void Reject(Action action)
    {
        try { action(); } catch (NotSupportedException) { return; }
        throw new Exception("Expected NotSupportedException");
    }
}
