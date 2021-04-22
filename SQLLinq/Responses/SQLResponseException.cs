using System;

namespace SqlLinqer
{
    /// <summary>
    /// Exception that also contains the sql command that caused the exception
    /// </summary>
    public sealed class SQLResponseException : Exception
    {
        /// <summary>
        /// sql command text that caused the exception
        /// </summary>
        public string SQLCommandText { get; private set; }

        /// <summary>
        /// Create a new <see cref="SQLResponseException"/> that contains the exception thrown 
        /// and the sql command text that cause the exception
        /// </summary>
        /// <param name="sqlCommandText">The command text that cause the exception</param>
        /// <param name="innerException">The original exception</param>
        public SQLResponseException(string sqlCommandText, Exception innerException)
            : base(innerException.Message, innerException)
        {
            SQLCommandText = sqlCommandText;
        }
    }
}
