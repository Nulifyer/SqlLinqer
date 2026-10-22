using System;
using System.Runtime.ExceptionServices;

namespace SqlLinqer
{
    /// <summary>Provides optional exception-based access to successful query responses.</summary>
    public static class SqlResponseExtensions
    {
        /// <summary>Returns the same successful response, preserving its concrete type and metadata. Otherwise rethrows its recorded error.</summary>
        /// <typeparam name="TResponse">The concrete response type.</typeparam>
        /// <param name="response">The response to check.</param>
        /// <returns>The original successful response.</returns>
        /// <exception cref="ArgumentNullException">The response is null.</exception>
        /// <exception cref="InvalidOperationException">An invalid response has no recorded error.</exception>
        public static TResponse EnsureSuccess<TResponse>(this TResponse response) where TResponse : ISqlResponse
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            if (response.State == ResponseState.Valid) return response;
            ExceptionDispatchInfo.Capture(response.Error ?? new InvalidOperationException("The query failed without a recorded error.")).Throw();
            return response;
        }

        /// <summary>Returns the successful result, including null, zero, or an empty list. Otherwise rethrows the recorded error.</summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="response">The response to check.</param>
        /// <returns>The successful result.</returns>
        public static T GetResultOrThrow<T>(this SqlResponse<T> response) => response.EnsureSuccess().Result;
    }
}
