using System;
using System.Collections.Generic;
using System.IO;
using SqlLinqer.Connections;

namespace SqlLinqer.Components.Native
{
    internal sealed class NativeObjectState
    {
        internal readonly BoundNativeResultNode Node;
        internal readonly object[] Values;
        private List<NativeObjectState>[] children;
        internal bool Header;
        internal NativeObjectState(BoundNativeResultNode node)
        {
            Node = node; Values = node.Model == null ? null : new object[node.Scalars.Length + node.Children.Count];
        }

        internal List<NativeObjectState> ChildRows(int index) => children?[index];
        internal List<NativeObjectState> ChildSlots(int index)
        {
            if (children == null) children = new List<NativeObjectState>[Node.Children.Count];
            return children[index] ?? (children[index] = new List<NativeObjectState>());
        }

        internal void SetScalar(int index, object value)
        {
            var target = Node.Model?.Types[index];
            if (target != null) Values[index] = DbValueConversion.ConvertTo(value, target);
        }
    }

    // Relational occurrence reconstruction and CLR construction share this module
    // for positional streams and SQLite's prepared local cursors.
    internal sealed class NativeMaterializer
    {
        internal object Accept(NativeObjectState state) => Build(state);

        private static object Build(NativeObjectState state)
        {
            if (state == null || !state.Header) throw new InvalidDataException("Native result is missing an object header.");
            var node = state.Node;
            for (int i = 0; i < node.Children.Count; i++)
            {
                var child = node.Children[i];
                var rows = state.ChildRows(i);
                int rowCount = rows?.Count ?? 0;
                int capacity = rowCount;
                var values = node.Model != null && child.IsCollection && rowCount > 0 ? new List<object>(capacity) : null;
                object single = null; int count = 0;
                for (int row = 0; row < rowCount; row++)
                {
                    var value = Build(rows[row]);
                    // Release staging state as the final model takes ownership.
                    rows[row] = null;
                    if (child.IsCollection) values?.Add(value);
                    else
                    {
                        if (count != 0) throw new InvalidDataException("Native object relationship has multiple occurrences.");
                        single = value;
                    }
                    count++;
                }
                if (node.Model != null)
                {
                    if (child.Collection != null) state.Values[node.Scalars.Length + i] = child.Collection.Build((IList<object>)values ?? Array.Empty<object>());
                    else state.Values[node.Scalars.Length + i] = values == null ? single : values.Count == 0 ? null : values[0];
                }
            }
            return node.Model?.Construct(state.Values);
        }
    }
}
