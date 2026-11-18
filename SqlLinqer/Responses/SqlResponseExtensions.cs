using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

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

        /// <summary>Awaits the response and returns it when successful, preserving its concrete type and metadata. Otherwise rethrows its recorded error.</summary>
        /// <typeparam name="TResponse">The concrete response type.</typeparam>
        /// <param name="response">The pending response from an asynchronous terminal.</param>
        /// <returns>The original successful response.</returns>
        /// <exception cref="ArgumentNullException">The task is null.</exception>
        public static async Task<TResponse> EnsureSuccessAsync<TResponse>(this Task<TResponse> response) where TResponse : ISqlResponse
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            return (await response.ConfigureAwait(false)).EnsureSuccess();
        }

        /// <summary>Awaits the response and returns its successful result, including null, zero, or an empty list. Otherwise rethrows the recorded error.</summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="response">The pending response from an asynchronous terminal.</param>
        /// <returns>The successful result.</returns>
        public static async Task<T> GetResultOrThrowAsync<T>(this Task<SqlResponse<T>> response)
            => (await response.EnsureSuccessAsync().ConfigureAwait(false)).Result;

        /// <summary>Awaits a select response and returns its rows. Otherwise rethrows the recorded error. Use EnsureSuccessAsync to keep the total count.</summary>
        /// <typeparam name="T">The row type.</typeparam>
        /// <param name="response">The pending response from an asynchronous terminal.</param>
        /// <returns>The selected rows.</returns>
        public static async Task<List<T>> GetResultOrThrowAsync<T>(this Task<SqlSelectResponse<T>> response)
            => (await response.EnsureSuccessAsync().ConfigureAwait(false)).Result;

        /// <summary>Awaits a primary-key write response and returns its result. Otherwise rethrows the recorded error. Use EnsureSuccessAsync to keep the touched keys.</summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <typeparam name="TPrimaryKey">The primary-key type.</typeparam>
        /// <param name="response">The pending response from an asynchronous terminal.</param>
        /// <returns>The successful result.</returns>
        public static async Task<T> GetResultOrThrowAsync<T, TPrimaryKey>(this Task<SqlPrimaryKeysResponse<T, TPrimaryKey>> response)
            => (await response.EnsureSuccessAsync().ConfigureAwait(false)).Result;

        /// <summary>Awaits a stored-procedure response and returns its rows. Otherwise rethrows the recorded error. Use EnsureSuccessAsync to keep its metadata.</summary>
        /// <typeparam name="T">The row type.</typeparam>
        /// <param name="response">The pending response from an asynchronous terminal.</param>
        /// <returns>The returned rows.</returns>
        public static async Task<List<T>> GetResultOrThrowAsync<T>(this Task<SqlCallableResponse<T>> response)
            => (await response.EnsureSuccessAsync().ConfigureAwait(false)).Result;

        /// <summary>Awaits a multi-result stored-procedure response and returns its result tables. Otherwise rethrows the recorded error. Use EnsureSuccessAsync to keep its metadata.</summary>
        /// <typeparam name="T">The tuple of result collections.</typeparam>
        /// <param name="response">The pending response from an asynchronous terminal.</param>
        /// <returns>The returned result tables.</returns>
        public static async Task<T> GetResultOrThrowAsync<T>(this Task<SqlMultiCallableResponse<T>> response)
            => (await response.EnsureSuccessAsync().ConfigureAwait(false)).Result;
    }
}
