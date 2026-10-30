using System;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.IO;
using SqlLinqer.Exceptions;

namespace SqlLinqer.Diagnostics
{
    internal sealed class DiagnosticFailure
    {
        internal string ErrorType, WrapperType, Kind, ProviderCode;
        private static readonly DiagnosticFailure Empty = new DiagnosticFailure();
        internal static DiagnosticFailure Capture(Exception error)
        {
            if (error == null) return Empty;
            string wrapper = error.GetType().FullName;
            while ((error is SqlResponseException || error is TargetInvocationException) && error.InnerException != null)
                error = error.InnerException;
            var result = new DiagnosticFailure
            {
                ErrorType = error.GetType().FullName,
                WrapperType = wrapper,
                Kind = error is TimeoutException ? "Timeout" : error is OperationCanceledException ? "Cancelled"
                    : error is DbException ? "Database" : (error is FormatException || error is InvalidCastException || error is OverflowException || error is InvalidDataException) ? "Mapping" : "Execution"
            };
            if (error is DbException database)
            {
                result.ProviderCode = database.ErrorCode.ToString(CultureInfo.InvariantCulture);
                foreach (string name in new[] { "SqlState", "SqliteErrorCode", "Number" })
                {
                    try
                    {
                        var property = error.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                        object value = property?.GetValue(error);
                        if (value is string code) { result.ProviderCode = code; break; }
                        if (value is int number) { result.ProviderCode = number.ToString(CultureInfo.InvariantCulture); break; }
                    }
                    catch { /* Provider metadata must never alter execution. */ }
                }
            }
            for (var cause = error; cause != null; cause = cause.InnerException)
                if (cause is TimeoutException) { result.Kind = "Timeout"; break; }
            return result;
        }
    }
}
