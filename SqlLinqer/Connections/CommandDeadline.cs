using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

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
        private readonly CancellationToken cancellationToken;
        private readonly CancellationTokenRegistration registration;

        internal CommandDeadline(DbCommand command, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (timeout < TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
                throw new ArgumentOutOfRangeException(nameof(timeout), "DuckDB command timeout must be zero or between zero and 4294967294 milliseconds.");
            this.command = command;
            this.cancellationToken = cancellationToken;
            timer = new Timer(Cancel, null, timeout > TimeSpan.Zero ? timeout : Timeout.InfiniteTimeSpan, TimeSpan.FromMilliseconds(20));
            registration = cancellationToken.Register(() =>
            {
                lock (gate)
                {
                    if (!active) return;
                    try { command.Cancel(); }
                    catch (Exception error) { cancellationError = error; }
                    timer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(20));
                }
            });
        }

        private void Cancel(object state)
        {
            lock (gate)
            {
                if (!active) return;
                if (!cancellationToken.IsCancellationRequested) expired = true;
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
                if (cancellationToken.IsCancellationRequested)
                {
                    (result as IDisposable)?.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                }
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
                cancellationToken.ThrowIfCancellationRequested();
                if (expired && !(error is TimeoutException))
                    throw new TimeoutException("The DuckDB command exceeded ConnectorOptions.CommandTimeout.", error);
                throw;
            }
        }

        internal async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
        {
            try
            {
                T result = await action().ConfigureAwait(false);
                Dispose();
                if (cancellationToken.IsCancellationRequested)
                {
                    (result as IDisposable)?.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                }
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
                cancellationToken.ThrowIfCancellationRequested();
                if (expired && !(error is TimeoutException))
                    throw new TimeoutException("The DuckDB command exceeded ConnectorOptions.CommandTimeout.", error);
                throw;
            }
        }

        public void Dispose()
        {
            lock (gate)
                active = false;
            registration.Dispose();
            timer?.Dispose();
        }
    }
}
