using System;
using System.Data.Common;

namespace SqlLinqer
{
    /// <summary>
    /// Exception that also contains the sql command that caused the exception
    /// </summary>
    public sealed class SQLResponseException : Exception
    {
        private DbCommand _command;

        /// <summary>
        /// sql command text that caused the exception
        /// </summary>
        public string SQLCommandText
        {
            get => _command.CommandText;
        }

        /// <summary>
        /// sql command text that caused the exception
        /// </summary>
        public string SQLCommandTextWithParams
        {
            get => _command.CommandTextWithParamValues();
        }

        /// <summary>
        /// Create a new <see cref="SQLResponseException"/> that contains the exception thrown 
        /// and the sql command text that cause the exception
        /// </summary>
        /// <param name="sqlCommand">The command that cause the exception</param>
        /// <param name="innerException">The original exception</param>
        public SQLResponseException(DbCommand sqlCommand, Exception innerException)
            : base(innerException.Message, innerException)
        {
            _command = sqlCommand;
        }
    }
}
