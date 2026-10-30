using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using SqlLinqer.Connections;

namespace SqlLinqer.Components.Native
{
    // SQLite has no correlated FROM/LATERAL. Prepared child cursors run on the
    // root's embedded connection and snapshot. No expanded result is sorted or
    // buffered; only the current root and its selected descendants are retained.
    internal sealed class LocalNativeResultMapper<T> : IDisposable where T : class
    {
        private readonly BoundNativeResultPlan plan;
        private readonly Func<NativeResultNode, DbCommand> create;
        private readonly DbCommand[] commands;
        private readonly object[][] hidden;
        private readonly Action<DbCommand> cancel;
        private readonly ExecutionObservation observation;
        private readonly NativeMaterializer materializer;

        internal LocalNativeResultMapper(NativeResultPlan plan, Func<NativeResultNode, DbCommand> create, Action<DbCommand> cancel, ExecutionObservation observation)
        {
            this.plan = plan.Bind(typeof(T)); this.create = create; this.cancel = cancel;
            this.observation = observation;
            materializer = new NativeMaterializer();
            commands = new DbCommand[plan.Nodes.Length]; hidden = new object[plan.Nodes.Length][];
        }

        private DbCommand Command(BoundNativeResultNode node)
        {
            var command = commands[node.Id];
            if (command == null) commands[node.Id] = command = create(node.Definition);
            foreach (var input in node.Inputs)
                command.Parameters[input.Parameter].Value = hidden[input.Node][input.HiddenIndex] ?? DBNull.Value;
            return command;
        }

        private NativeObjectState Values(BoundNativeResultNode node, DbDataReader reader)
        {
            if (reader.FieldCount != node.Scalars.Length + node.HiddenCount)
                throw new InvalidDataException("Native local SELECT returned an unexpected projection.");
            var state = new NativeObjectState(node) { Header = true };
            for (int i = 0; i < node.Scalars.Length; i++)
            {
                var scalar = node.Scalars[i];
                if (node.Model?.Types[i] != null)
                {
                    object value = NativeValueReader.Read(reader, i);
                    state.SetScalar(i, value);
                }
            }
            var keys = hidden[node.Id] ?? (hidden[node.Id] = new object[node.HiddenCount]);
            for (int i = 0; i < keys.Length; i++) keys[i] = NativeValueReader.Read(reader, node.Scalars.Length + i);

            return state;
        }

        private NativeObjectState ReadObject(BoundNativeResultNode node, DbDataReader reader)
        {
            var values = Values(node, reader);
            foreach (var child in node.Children)
            {
                var command = Command(child);
                using (var result = ReadChild(command))
                    while (result.Read()) values.ChildSlots(child.EdgeIndex).Add(ReadObject(child, result));
            }
            return values;
        }

        private async Task<NativeObjectState> ReadObjectAsync(BoundNativeResultNode node, DbDataReader reader, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var values = Values(node, reader);
            foreach (var child in node.Children)
            {
                var command = Command(child);
                using (var deadline = new CommandDeadline(command, TimeSpan.Zero, token, cancel, "SQLite"))
                using (var result = await ReadChildAsync(command, token).ConfigureAwait(false))
                    while (await result.ReadAsync(token).ConfigureAwait(false))
                        values.ChildSlots(child.EdgeIndex).Add(await ReadObjectAsync(child, result, token).ConfigureAwait(false));
            }
            return values;
        }

        private DbDataReader ReadChild(DbCommand command)
        {
            long started = observation == null ? 0 : Stopwatch.GetTimestamp();
            try { return command.ExecuteReader(); }
            finally { observation?.CommandFinished(started); }
        }
        private async Task<DbDataReader> ReadChildAsync(DbCommand command, CancellationToken token)
        {
            long started = observation == null ? 0 : Stopwatch.GetTimestamp();
            try { return await command.ExecuteReaderAsync(token).ConfigureAwait(false); }
            finally { observation?.CommandFinished(started); }
        }

        internal List<T> Read(DbDataReader reader)
        {
            var result = new List<T>();
            while (reader.Read())
                if (materializer.Accept(ReadObject(plan.Root, reader)) is T row) result.Add(row);
            return result;
        }

        internal async Task<List<T>> ReadAsync(DbDataReader reader, CancellationToken token)
        {
            var result = new List<T>();
            await foreach (var row in ReadAsyncStream(reader, token).ConfigureAwait(false)) result.Add(row);
            return result;
        }

        internal async IAsyncEnumerable<T> ReadAsyncStream(DbDataReader reader, [EnumeratorCancellation] CancellationToken token)
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                if (materializer.Accept(await ReadObjectAsync(plan.Root, reader, token).ConfigureAwait(false)) is T row) yield return row;
        }

        public void Dispose()
        {
            // Dispose every command even if a provider throws during disposal.
            Exception failure = null;
            foreach (var command in commands)
                try { command?.Dispose(); } catch (Exception error) { failure = failure ?? error; }
            if (failure != null) throw failure;
        }
    }
}
