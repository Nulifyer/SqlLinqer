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
        private readonly ConcurrentDictionary<string, TvpContractEntry> _tvpContracts = new ConcurrentDictionary<string, TvpContractEntry>(StringComparer.Ordinal);

        internal void InvalidateTvpTypes() => _tvpContracts.Clear();

        private sealed class TvpContractEntry
        {
            internal readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
            internal volatile bool Resolved;
        }

        internal SqlResponse<int> EnsureTvpType(ReusableTvpType type, DbConnection target, DbTransaction callerTransaction, TimeSpan? timeout)
        {
            var key = target.DataSource + "\0" + target.Database + "\0" + type.TypeName + "\0" + type.Hash;
            var entry = _tvpContracts.GetOrAdd(key, _ => new TvpContractEntry());
            if (entry.Resolved) return new SqlResponse<int>(0);
            entry.Gate.Wait();
            try
            {
                if (entry.Resolved) return new SqlResponse<int>(0);
                var query = new RenderedQuery(type.EnsureSql(Options.AutoCleanupTvpTypes)) { CommandRole = "TvpTypeEnsure" };
                var result = callerTransaction == null ? ExecuteTvpRegistryOnConnection(query, target, timeout)
                    : ExecuteTvpRegistry(query, target.Database, timeout);
                if (result.State == ResponseState.Valid) entry.Resolved = true;
                return result;
            }
            finally { entry.Gate.Release(); }
        }

        internal async Task<SqlResponse<int>> EnsureTvpTypeAsync(ReusableTvpType type, DbConnection target, DbTransaction callerTransaction, CancellationToken token, TimeSpan? timeout)
        {
            token.ThrowIfCancellationRequested();
            var key = target.DataSource + "\0" + target.Database + "\0" + type.TypeName + "\0" + type.Hash;
            var entry = _tvpContracts.GetOrAdd(key, _ => new TvpContractEntry());
            if (entry.Resolved) return new SqlResponse<int>(0);
            await entry.Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (entry.Resolved) return new SqlResponse<int>(0);
                var query = new RenderedQuery(type.EnsureSql(Options.AutoCleanupTvpTypes)) { CommandRole = "TvpTypeEnsure" };
                var result = callerTransaction == null
                    ? await ExecuteTvpRegistryOnConnectionAsync(query, target, token, timeout).ConfigureAwait(false)
                    : await ExecuteTvpRegistryAsync(query, target.Database, token, timeout).ConfigureAwait(false);
                if (result.State == ResponseState.Valid) entry.Resolved = true;
                return result;
            }
            finally { entry.Gate.Release(); }
        }

        /// <summary>Lists SqlLinqer-owned reusable TVP types. Named application-owned types are excluded.</summary>
        /// <param name="databaseName">The target database, or null to use the connector's database.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout.</param>
        /// <returns>Persisted type identities for inspection and explicit maintenance cleanup.</returns>
        public SqlResponse<List<SqlTvpTypeInfo>> GetReusableTvpTypes(string databaseName = null, TimeSpan? commandTimeout = null)
        {
            RequireTvpProvider();
            return ExecuteOnConnection(new RenderedQuery(ListTvpSql()), null, false, (command, observation) =>
            {
                ChangeTvpDatabase(command.Connection, databaseName);
                var rows = new List<SqlTvpTypeInfo>();
                using (var reader = ReadCommand(command, observation))
                    while (reader.Read()) rows.Add(ReadTvpType(reader));
                return rows;
            }, commandTimeout: commandTimeout);
        }

        /// <summary>Asynchronously lists SqlLinqer-owned reusable TVP types.</summary>
        /// <param name="databaseName">The target database, or null to use the connector's database.</param>
        /// <param name="cancellationToken">Cancels provider work.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout.</param>
        /// <returns>Persisted type identities for inspection and explicit maintenance cleanup.</returns>
        public Task<SqlResponse<List<SqlTvpTypeInfo>>> GetReusableTvpTypesAsync(string databaseName = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            RequireTvpProvider();
            return ExecuteOnConnectionAsync(new RenderedQuery(ListTvpSql()), null, false, async (command, observation) =>
            {
                ChangeTvpDatabase(command.Connection, databaseName);
                var rows = new List<SqlTvpTypeInfo>();
                using (var reader = await ReadCommandAsync(command, observation, cancellationToken).ConfigureAwait(false))
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) rows.Add(ReadTvpType(reader));
                return rows;
            }, cancellationToken, commandTimeout: commandTimeout);
        }

        /// <summary>
        /// Drops a selected retired reusable TVP type during maintenance. All consumers must be stopped and other connectors restarted before reuse.
        /// Ownership and type ID are rechecked; SQL Server rejects types referenced by procedures. This is not safe online pruning.
        /// </summary>
        /// <param name="type">An identity returned by GetReusableTvpTypes.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout.</param>
        /// <returns>One when dropped, zero when already absent, or an error response.</returns>
        public SqlResponse<int> DropReusableTvpType(SqlTvpTypeInfo type, TimeSpan? commandTimeout = null)
        {
            RequireTvpProvider();
            if (type == null) throw new ArgumentNullException(nameof(type));
            var result = ExecuteTvpRegistry(new RenderedQuery(ReusableTvpType.DropSql(type)) { CommandRole = "TvpTypeDrop" }, type.Database, commandTimeout);
            _tvpContracts.Clear();
            return result;
        }

        /// <summary>Asynchronously drops a selected retired reusable type. Requires the same maintenance quiescence as DropReusableTvpType.</summary>
        /// <param name="type">An identity returned by GetReusableTvpTypes.</param>
        /// <param name="cancellationToken">Cancels provider work.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout.</param>
        /// <returns>One when dropped, zero when already absent, or an error response.</returns>
        public async Task<SqlResponse<int>> DropReusableTvpTypeAsync(SqlTvpTypeInfo type, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
        {
            RequireTvpProvider();
            if (type == null) throw new ArgumentNullException(nameof(type));
            try
            {
                return await ExecuteTvpRegistryAsync(new RenderedQuery(ReusableTvpType.DropSql(type)) { CommandRole = "TvpTypeDrop" },
                    type.Database, cancellationToken, commandTimeout).ConfigureAwait(false);
            }
            finally { _tvpContracts.Clear(); }
        }

        // Without a caller transaction, provision on the open write connection and commit before binding the TVP.
        // This avoids requiring a second pool slot during simultaneous cold requests. Borrowed transactions still use an independent connection.
        private SqlResponse<int> ExecuteTvpRegistryOnConnection(RenderedQuery query, DbConnection connection, TimeSpan? timeout)
        {
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                try
                {
                    var result = ExecuteOnConnection(query, transaction, false, (command, observation) =>
                    {
                        int created = Convert.ToInt32(ScalarCommand(command, observation));
                        transaction.Commit();
                        return created;
                    }, commandTimeout: timeout);
                    if (result.State == ResponseState.Error) { try { transaction.Rollback(); } catch { } }
                    return result;
                }
                catch { try { transaction.Rollback(); } catch { } throw; }
            }
        }

        private async Task<SqlResponse<int>> ExecuteTvpRegistryOnConnectionAsync(RenderedQuery query, DbConnection connection, CancellationToken token, TimeSpan? timeout)
        {
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                try
                {
                    var result = await ExecuteOnConnectionAsync(query, transaction, false, async (command, observation) =>
                    {
                        int created = Convert.ToInt32(await ScalarCommandAsync(command, observation, token).ConfigureAwait(false));
                        token.ThrowIfCancellationRequested();
                        transaction.Commit();
                        return created;
                    }, token, commandTimeout: timeout).ConfigureAwait(false);
                    if (result.State == ResponseState.Error) { try { transaction.Rollback(); } catch { } }
                    return result;
                }
                catch { try { transaction.Rollback(); } catch { } throw; }
            }
        }

        private SqlResponse<int> ExecuteTvpRegistry(RenderedQuery query, string database, TimeSpan? timeout)
            => ExecuteOnConnection(query, null, false, (command, observation) =>
            {
                ChangeTvpDatabase(command.Connection, database);
                using (var transaction = command.Connection.BeginTransaction(IsolationLevel.ReadCommitted))
                {
                    command.Transaction = transaction;
                    try
                    {
                        int result = Convert.ToInt32(ScalarCommand(command, observation));
                        transaction.Commit();
                        return result;
                    }
                    catch { try { transaction.Rollback(); } catch { } throw; }
                }
            }, commandTimeout: timeout);

        private Task<SqlResponse<int>> ExecuteTvpRegistryAsync(RenderedQuery query, string database, CancellationToken token, TimeSpan? timeout)
            => ExecuteOnConnectionAsync(query, null, false, async (command, observation) =>
            {
                ChangeTvpDatabase(command.Connection, database);
                using (var transaction = command.Connection.BeginTransaction(IsolationLevel.ReadCommitted))
                {
                    command.Transaction = transaction;
                    try
                    {
                        int result = Convert.ToInt32(await ScalarCommandAsync(command, observation, token).ConfigureAwait(false));
                        token.ThrowIfCancellationRequested();
                        transaction.Commit();
                        return result;
                    }
                    catch { try { transaction.Rollback(); } catch { } throw; }
                }
            }, token, commandTimeout: timeout);

        private void RequireTvpProvider()
        {
            if (DbFlavor != DbFlavor.SqlServer && DbFlavor != DbFlavor.AzureSql)
                throw new NotSupportedException("Reusable TVP types support SQL Server and Azure SQL only.");
        }
        private static void ChangeTvpDatabase(DbConnection connection, string database)
        {
            if (database != null && !string.Equals(connection.Database, database, StringComparison.Ordinal)) connection.ChangeDatabase(database);
        }
        private static SqlTvpTypeInfo ReadTvpType(DbDataReader reader) => new SqlTvpTypeInfo
        {
            Database = reader.GetString(0),
            Schema = reader.GetString(1),
            Name = reader.GetString(2),
            ContractHash = reader.GetString(3),
            TypeId = reader.GetInt32(4),
            Generation = reader.GetString(5)
        };
        private static string ListTvpSql() => "SELECT DB_NAME(),s.name,t.name,CONVERT(nvarchar(64),p.value),t.user_type_id,CONVERT(nvarchar(36),g.value) FROM sys.table_types t "
            + "JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.extended_properties p ON p.class=6 AND p.major_id=t.user_type_id AND p.minor_id=0 "
            + "JOIN sys.extended_properties g ON g.class=6 AND g.major_id=t.user_type_id AND g.minor_id=0 AND g.name=N'" + ReusableTvpType.GenerationProperty + "' "
            + "WHERE p.name=N'" + ReusableTvpType.OwnershipProperty + "' AND DATALENGTH(CONVERT(nvarchar(max),p.value))=128 AND TRY_CONVERT(uniqueidentifier,g.value) IS NOT NULL "
            + "AND t.name COLLATE Latin1_General_100_BIN2=N'" + ReusableTvpType.Prefix + "'+CONVERT(nvarchar(64),p.value) COLLATE Latin1_General_100_BIN2 ORDER BY s.name,t.name;";
    }
}
