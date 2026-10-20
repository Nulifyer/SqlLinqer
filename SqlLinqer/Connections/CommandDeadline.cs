using System;
using System.Data.Common;
using System.Threading;

namespace SqlLinqer.Connections
{
    // DuckDB Cancel interrupts the connection. Serialize cancellation with completion
    // so no callback can reach the connection after the command's execution scope.
    internal sealed class CommandDeadline : IDisposable
    {
        private readonly object gate = new object();
        private readonly DbCommand command;
        private readonly Timer timer;
        private bool active = true;
        private bool expired;
        private Exception cancellationError;

        internal CommandDeadline(DbCommand command, TimeSpan timeout)
        {
            if (timeout < TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
                throw new ArgumentOutOfRangeException(nameof(timeout), "DuckDB command timeout must be zero or between zero and 4294967294 milliseconds.");
            this.command = command;
            if (timeout > TimeSpan.Zero)
            {
                // Repeat after expiry: an interrupt racing command startup may be cleared
                // by the engine before execution. Every callback stays inside this scope.
                timer = new Timer(Cancel, null, timeout, TimeSpan.FromMilliseconds(20));
            }
        }

        private void Cancel(object state)
        {
            lock (gate)
            {
                if (!active) return;
                expired = true;
                try { command.Cancel(); }
                catch (Exception error) { cancellationError = error; }
            }
        }

        internal T Execute<T>(Func<T> action)
        {
            try
            {
                T result = action();
                Dispose();
                if (expired)
                {
                    (result as IDisposable)?.Dispose();
                    throw new TimeoutException("The DuckDB command exceeded ConnectorOptions.CommandTimeout.", cancellationError);
                }
                return result;
            }
            catch (Exception error)
            {
                Dispose();
                if (expired && !(error is TimeoutException))
                    throw new TimeoutException("The DuckDB command exceeded ConnectorOptions.CommandTimeout.", error);
                throw;
            }
        }

        public void Dispose()
        {
            lock (gate)
                active = false;
            timer?.Dispose();
        }
    }
}
