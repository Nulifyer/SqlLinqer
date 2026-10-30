using System;
using System.Collections.Generic;
using SqlLinqer.Components.Render;

namespace SqlLinqer.Components.Native
{
    internal sealed class NativeResultPlan
    {
        internal NativeResultNode Root;
        internal NativeResultNode[] Nodes;
        internal int PayloadCount, Depth;
        internal bool IsGraph;
        internal bool LocalQueries;
        internal int ControlCount => IsGraph ? 3 + Depth : 0;
        internal int FieldCount => ControlCount + PayloadCount;

        internal BoundNativeResultPlan Bind(Type type) => new BoundNativeResultPlan(this, type);
    }

    internal sealed class NativeResultNode
    {
        internal int Id, Depth, EdgeIndex;
        internal string Alias;
        internal bool IsCollection;
        internal NativeResultNode Parent;
        internal readonly List<NativeResultNode> Children = new List<NativeResultNode>();
        internal NativeResultScalar[] Scalars;
        internal RenderedQuery LocalQuery;
        internal readonly List<NativeInputBinding> Inputs = new List<NativeInputBinding>();
        internal int HiddenCount;
    }

    internal sealed class NativeInputBinding
    {
        internal string Parameter;
        internal int Node, HiddenIndex;
    }

    internal sealed class NativeResultScalar
    {
        internal string Alias;
        internal int Ordinal;
    }
}
