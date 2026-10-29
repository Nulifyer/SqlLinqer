using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Compilation;
using SqlLinqer.Components.Render;
using SqlLinqer.Modeling;

namespace SqlLinqer.Connections
{
    public abstract partial class BaseConnector
    {
        private readonly Guid _tvpNodeId = Guid.NewGuid();
        private readonly ConcurrentDictionary<string, long> _nextTvpCleanup = new ConcurrentDictionary<string, long>();

        private static int TvpSeconds(TimeSpan value, bool allowZero, string name)
        {
            if (value < TimeSpan.Zero || (!allowZero && value == TimeSpan.Zero) || value > TimeSpan.FromDays(365))
                throw new ArgumentOutOfRangeException(name, "TVP durations must be within zero through 365 days; lease and interval must be positive.");
            return (int)Math.Ceiling(value.TotalSeconds);
        }

        private string LeaseRenewal(ReusableTvpType type)
            => TvpLeaseSql.Renew(type, _tvpNodeId, TvpSeconds(Options.TvpLeaseDuration, false, nameof(Options.TvpLeaseDuration)),
                TvpSeconds(Options.TvpTypeRetention, true, nameof(Options.TvpTypeRetention)));

        private bool CleanupDue(DbConnection connection, string schema)
        {
            if (!Options.AutoCleanupTvpTypes) return false;
            int seconds = TvpSeconds(Options.TvpCleanupInterval, false, nameof(Options.TvpCleanupInterval));
            string key = connection.DataSource + "\0" + connection.Database + "\0" + schema;
            long now = DateTime.UtcNow.Ticks, next = now + TimeSpan.FromSeconds(seconds).Ticks;
            long previous = _nextTvpCleanup.GetOrAdd(key, 0);
            return previous <= now && _nextTvpCleanup.TryUpdate(key, next, previous);
        }

        internal DbTransaction BeginReusableTvpExecution(ReusableTvpType type, DbConnection target, DbTransaction caller, TimeSpan? timeout)
        {
            if (!type.Managed)
            {
                RequireTvpSuccess(EnsureTvpType(type, target, caller, timeout));
                return caller ?? target.BeginTransaction(IsolationLevel.ReadCommitted);
            }
            var renewal = new RenderedQuery(LeaseRenewal(type)) { CommandRole = "TvpLeaseRenew" };
            // A caller's transaction cannot commit lease registration. Use an independent connection in that case.
            using (var management = caller == null ? null : CreateConnection())
            {
                var connection = management ?? target;
                if (management != null) { connection.Open(); ChangeTvpDatabase(connection, target.Database); }
                if (CleanupDue(connection, type.Schema))
                {
                    // Opportunistic maintenance must not turn a valid write into a failure. Inner commands still report errors to the observer.
                    try { CleanupTvpOnConnection(connection, type.Schema, timeout); } catch { }
                }
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    RequireTvpSuccess(EnsureTvpType(type, connection, null, timeout));
                    if (RequireTvpSuccess(ExecuteTvpRegistryOnConnection(renewal, connection, timeout)) == 0)
                    { InvalidateTvpTypes(); continue; }
                    var transaction = caller ?? target.BeginTransaction(IsolationLevel.ReadCommitted);
                    try
                    {
                        var guard = new RenderedQuery(TvpLeaseSql.Guard(type)) { CommandRole = "TvpExecutionGuard" };
                        if (RequireTvpSuccess(ExecuteOnConnection(guard, transaction, false,
                            (command, observation) => Convert.ToInt32(ScalarCommand(command, observation)), commandTimeout: timeout)) == 1)
                            return transaction;
                    }
                    catch { if (caller == null) transaction.Dispose(); throw; }
                    if (caller == null) transaction.Dispose();
                    InvalidateTvpTypes();
                }
                throw new InvalidOperationException("The reusable TVP was repeatedly removed before execution. No rows were sent.");
            }
        }

        internal async Task<DbTransaction> BeginReusableTvpExecutionAsync(ReusableTvpType type, DbConnection target, DbTransaction caller, CancellationToken token, TimeSpan? timeout)
        {
            if (!type.Managed)
            {
                RequireTvpSuccess(await EnsureTvpTypeAsync(type, target, caller, token, timeout).ConfigureAwait(false));
                token.ThrowIfCancellationRequested();
                return caller ?? target.BeginTransaction(IsolationLevel.ReadCommitted);
            }
            var renewal = new RenderedQuery(LeaseRenewal(type)) { CommandRole = "TvpLeaseRenew" };
            using (var management = caller == null ? null : CreateConnection())
            {
                var connection = management ?? target;
                if (management != null) { await connection.OpenAsync(token).ConfigureAwait(false); ChangeTvpDatabase(connection, target.Database); }
                if (CleanupDue(connection, type.Schema))
                {
                    try { await CleanupTvpOnConnectionAsync(connection, type.Schema, token, timeout).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch { }
                }
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    RequireTvpSuccess(await EnsureTvpTypeAsync(type, connection, null, token, timeout).ConfigureAwait(false));
                    if (RequireTvpSuccess(await ExecuteTvpRegistryOnConnectionAsync(renewal, connection, token, timeout).ConfigureAwait(false)) == 0)
                    { InvalidateTvpTypes(); continue; }
                    token.ThrowIfCancellationRequested();
                    var transaction = caller ?? target.BeginTransaction(IsolationLevel.ReadCommitted);
                    try
                    {
                        var guard = new RenderedQuery(TvpLeaseSql.Guard(type)) { CommandRole = "TvpExecutionGuard" };
                        if (RequireTvpSuccess(await ExecuteOnConnectionAsync(guard, transaction, false,
                            async (command, observation) => Convert.ToInt32(await ScalarCommandAsync(command, observation, token).ConfigureAwait(false)),
                            token, commandTimeout: timeout).ConfigureAwait(false)) == 1)
                            return transaction;
                    }
                    catch { if (caller == null) transaction.Dispose(); throw; }
                    if (caller == null) transaction.Dispose();
                    InvalidateTvpTypes();
                }
                throw new InvalidOperationException("The reusable TVP was repeatedly removed before execution. No rows were sent.");
            }
        }

        private static T RequireTvpSuccess<T>(SqlResponse<T> response)
        {
            if (response.State == ResponseState.Error) throw response.Error;
            return response.Result;
        }

        /// <summary>Removes up to 100 expired managed TVP types in one schema. Active transactions, live leases, and procedure dependencies prevent deletion. All consumers must use the lease-aware execution protocol.</summary>
        /// <param name="schema">The registry schema, or null to use ReusableTvpSchema, DefaultSchema, or dbo.</param>
        /// <param name="databaseName">The database to inspect, or null to use the connector default.</param>
        /// <param name="commandTimeout">Overrides the command timeout.</param>
        /// <returns>The number of removed types, or an error response. Each removal commits independently.</returns>
        public SqlResponse<int> CleanupExpiredTvpTypes(string schema = null, string databaseName = null, TimeSpan? commandTimeout = null)
        {
            RequireTvpProvider();
            schema = ResolveTvpLeaseSchema(schema);
            return ExecuteOnConnection(new RenderedQuery("-- Cleanup expired reusable TVPs") { CommandRole = "TvpCleanup" }, null, false,
                (command, observation) => { ChangeTvpDatabase(command.Connection, databaseName); return CleanupTvpOnConnection(command.Connection, schema, commandTimeout); },
                commandTimeout: commandTimeout);
        }

        /// <summary>Asynchronously removes expired managed TVP types. Uses the same bounded, lease-aware protocol as CleanupExpiredTvpTypes.</summary>
        /// <param name="schema">The registry schema, or null to use ReusableTvpSchema, DefaultSchema, or dbo.</param>
        /// <param name="databaseName">The database to inspect, or null to use the connector default.</param>
        /// <param name="cancellationToken">Cancels provider work. Completed removals stay committed.</param>
        /// <param name="commandTimeout">Overrides the command timeout.</param>
        /// <returns>The number of removed types, or an error response. Each removal commits independently.</returns>
        public Task<SqlResponse<int>> CleanupExpiredTvpTypesAsync(string schema = null, string databaseName = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            RequireTvpProvider();
            schema = ResolveTvpLeaseSchema(schema);
            return ExecuteOnConnectionAsync(new RenderedQuery("-- Cleanup expired reusable TVPs") { CommandRole = "TvpCleanup" }, null, false,
                async (command, observation) => { ChangeTvpDatabase(command.Connection, databaseName); return await CleanupTvpOnConnectionAsync(command.Connection, schema, cancellationToken, commandTimeout).ConfigureAwait(false); },
                cancellationToken, commandTimeout: commandTimeout);
        }

        private string ResolveTvpLeaseSchema(string schema)
        {
            schema = schema ?? Options.ReusableTvpSchema ?? Options.DefaultSchema ?? "dbo";
            ReusableTvpType.ValidateIdentifier(schema, nameof(schema));
            return schema;
        }

        private int CleanupTvpOnConnection(DbConnection connection, string schema, TimeSpan? timeout)
        {
            List<SqlTvpTypeInfo> candidates;
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                candidates = RequireTvpSuccess(ExecuteOnConnection(new RenderedQuery(TvpLeaseSql.Candidates(schema, ListTvpSql())) { CommandRole = "TvpCleanupCandidates" },
                    transaction, false, (command, observation) =>
                    {
                        var rows = new List<SqlTvpTypeInfo>();
                        using (var reader = ReadCommand(command, observation)) while (reader.Read()) rows.Add(ReadTvpType(reader));
                        return rows;
                    }, commandTimeout: timeout));
                transaction.Commit();
            }
            int removed = 0;
            foreach (var candidate in candidates)
                removed += RequireTvpSuccess(ExecuteTvpRegistryOnConnection(new RenderedQuery(ReusableTvpType.DropSql(candidate, true)) { CommandRole = "TvpTypeExpire" }, connection, timeout));
            return removed;
        }

        private async Task<int> CleanupTvpOnConnectionAsync(DbConnection connection, string schema, CancellationToken token, TimeSpan? timeout)
        {
            List<SqlTvpTypeInfo> candidates;
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                candidates = RequireTvpSuccess(await ExecuteOnConnectionAsync(new RenderedQuery(TvpLeaseSql.Candidates(schema, ListTvpSql())) { CommandRole = "TvpCleanupCandidates" },
                    transaction, false, async (command, observation) =>
                    {
                        var rows = new List<SqlTvpTypeInfo>();
                        using (var reader = await ReadCommandAsync(command, observation, token).ConfigureAwait(false))
                            while (await reader.ReadAsync(token).ConfigureAwait(false)) rows.Add(ReadTvpType(reader));
                        return rows;
                    }, token, commandTimeout: timeout).ConfigureAwait(false));
                transaction.Commit();
            }
            int removed = 0;
            foreach (var candidate in candidates)
                removed += RequireTvpSuccess(await ExecuteTvpRegistryOnConnectionAsync(new RenderedQuery(ReusableTvpType.DropSql(candidate, true)) { CommandRole = "TvpTypeExpire" }, connection, token, timeout).ConfigureAwait(false));
            return removed;
        }
    }
}
