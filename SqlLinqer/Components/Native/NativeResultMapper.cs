using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Connections;

namespace SqlLinqer.Components.Native
{
    internal sealed class NativeResultMapper<T> where T : class
    {
        private readonly NativeResultPlan plan;
        private readonly DbFlavor flavor;
        internal NativeResultMapper(NativeResultPlan plan, DbFlavor flavor) { this.plan = plan; this.flavor = flavor; }
        internal List<T> Read(DbDataReader reader)
        {
            var cursor = new Cursor(Bind(reader), reader, flavor); var rows = new List<T>();
            while (reader.Read()) if (cursor.Accept() is T row) rows.Add(row);
            cursor.Complete(); return rows;
        }
        internal async Task<List<T>> ReadAsync(DbDataReader reader, CancellationToken token)
        {
            var rows = new List<T>();
            await foreach (var row in ReadAsyncStream(reader, token).ConfigureAwait(false)) rows.Add(row);
            return rows;
        }
        internal async IAsyncEnumerable<T> ReadAsyncStream(DbDataReader reader, [EnumeratorCancellation] CancellationToken token)
        {
            var cursor = new Cursor(Bind(reader), reader, flavor);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                if (cursor.Accept() is T row) yield return row;
            }
            token.ThrowIfCancellationRequested(); cursor.Complete();
        }

        private BoundNativeResultPlan Bind(DbDataReader reader)
        {
            if (plan?.IsGraph == true) return plan.Bind(typeof(T));
            var fields = Enumerable.Range(0, reader.FieldCount)
                .Where(i => plan == null || plan.Root.Scalars.Any(s => string.Equals(s.Alias, reader.GetName(i), StringComparison.OrdinalIgnoreCase)))
                .Select(i => new NativeResultScalar { Alias = reader.GetName(i), Ordinal = i }).ToArray();
            var root = new NativeResultNode { Scalars = fields };
            return new NativeResultPlan { Root = root, Nodes = new[] { root }, PayloadCount = reader.FieldCount }.Bind(typeof(T));
        }

        private sealed class Cursor
        {
            private readonly BoundNativeResultPlan plan;
            private readonly DbDataReader reader;
            private readonly bool[] utc;
            private NativeObjectState root;
            private long occurrence, lastOccurrence, received, expected;
            private readonly int[] positions;
            private readonly object[] flatValues, flatArguments;
            private readonly NativeMaterializer materializer;
            internal Cursor(BoundNativeResultPlan plan, DbDataReader reader, DbFlavor flavor)
            {
                this.plan = plan; this.reader = reader; materializer = new NativeMaterializer();
                if (reader.FieldCount != plan.FieldCount)
                    throw new InvalidDataException($"Native SELECT expected {plan.FieldCount} fields, received {reader.FieldCount}.");
                utc = new bool[plan.FieldCount]; positions = new int[plan.Depth];
                if (!plan.IsGraph && plan.Root.Model.Model.Parameters.Length != 0)
                {
                    flatValues = new object[plan.Root.Scalars.Length];
                    flatArguments = new object[plan.Root.Model.Model.Parameters.Length];
                }
                if (flavor == DbFlavor.DuckDb)
                    for (int i = plan.ControlCount; i < utc.Length; i++)
                        utc[i] = string.Equals(reader.GetDataTypeName(i), "TIMESTAMPTZ", StringComparison.OrdinalIgnoreCase);
            }
            internal object Accept()
            {
                if (!plan.IsGraph)
                {
                    return plan.Root.Model.ReadFlat(reader, plan.Root.Scalars, utc, flatValues, flatArguments);
                }
                // These fields are emitted by NativeSelectCompiler, rather than
                // selected user values: ROW_NUMBER/count are 64-bit integers and
                // the shape discriminator is an integer literal. Typed reads
                // avoid boxing every control field on every expanded record.
                long current = reader.GetInt64(0);
                int id = reader.GetInt32(1);
                long count = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                for (int i = 0; i < positions.Length; i++) positions[i] = checked((int)reader.GetInt64(3 + i));
                if (id < 0 || id >= plan.Nodes.Length) throw new InvalidDataException("Unknown native result shape.");
                if (root == null)
                {
                    if (current <= lastOccurrence) throw new InvalidDataException("Native root occurrences are not ordered.");
                    root = new NativeObjectState(plan.Root); occurrence = current; received = expected = 0;
                }
                if (current != occurrence) throw new InvalidDataException("Native root ended before its completion count.");
                var node = plan.Nodes[id];
                for (int i = node.Depth; i < positions.Length; i++)
                    if (positions[i] != 0) throw new InvalidDataException("Native record has an invalid occurrence path.");
                if (id == 0)
                {
                    if (count < 1) throw new InvalidDataException("Native root has no completion count.");
                    expected = count;
                }
                else if (count != 0) throw new InvalidDataException("Native child supplied a root completion count.");
                ReadHeader(Find(node)); received++;
                if (expected != 0 && received > expected) throw new InvalidDataException("Native root has too many records.");
                if (expected == 0 || received != expected) return null;
                object result = materializer.Accept(root); lastOccurrence = occurrence; root = null; return result;
            }
            private NativeObjectState Find(BoundNativeResultNode node)
            {
                if (node.Parent == null) return root;
                var parent = Find(node.Parent); int position = positions[node.Depth - 1];
                if (position < 1 || !node.IsCollection && position != 1)
                    throw new InvalidDataException("Invalid native collection position.");
                var slots = parent.ChildSlots(node.EdgeIndex);
                while (slots.Count < position) slots.Add(null);
                return slots[position - 1] ?? (slots[position - 1] = new NativeObjectState(node));
            }
            private void ReadHeader(NativeObjectState state)
            {
                if (state.Header) throw new InvalidDataException("Duplicate native object header.");
                state.Header = true;
                for (int i = 0; i < state.Node.Scalars.Length; i++)
                {
                    var scalar = state.Node.Scalars[i];
                    if (state.Node.Model?.Types[i] == null) continue;
                    int ordinal = plan.ControlCount + scalar.Ordinal;
                    object value = NativeValueReader.Read(reader, ordinal, utc[ordinal]);
                    state.SetScalar(i, value);
                }

            }
            internal void Complete()
            {
                if (root != null) throw new InvalidDataException("Native result ended inside a root object.");
            }
        }

    }
}
