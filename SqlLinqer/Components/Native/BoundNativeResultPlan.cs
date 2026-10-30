using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlLinqer.Components.Native
{
    // Per-execution CLR bindings reference compiled layout without cloning or
    // mutating it. Commands and mutable reader state never enter this metadata.
    internal sealed class BoundNativeResultPlan
    {
        private readonly NativeResultPlan layout;
        internal readonly BoundNativeResultNode Root;
        internal readonly BoundNativeResultNode[] Nodes;
        internal int Depth => layout.Depth;
        internal int ControlCount => layout.ControlCount;
        internal int FieldCount => layout.FieldCount;
        internal bool IsGraph => layout.IsGraph;

        internal BoundNativeResultPlan(NativeResultPlan layout, Type type)
        {
            this.layout = layout;
            var nodes = new List<BoundNativeResultNode>();
            BoundNativeResultNode Bind(NativeResultNode source, Type target, BoundNativeResultNode parent)
            {
                var model = target == null ? null : new NativeModelBinding(target,
                    source.Scalars.Select(s => s.Alias).Concat(source.Children.Select(c => c.Alias)));
                var node = new BoundNativeResultNode(source, model, parent);
                nodes.Add(node);
                foreach (var child in source.Children)
                {
                    var childType = model?.Types[source.Scalars.Length + child.EdgeIndex];
                    var collection = childType != null && child.IsCollection ? NativeCollectionBinding.Get(childType) : null;
                    var bound = Bind(child, collection?.ElementType ?? childType, node);
                    bound.Collection = collection;
                    node.Children.Add(bound);
                }
                return node;
            }
            Root = Bind(layout.Root, type, null);
            Nodes = nodes.OrderBy(n => n.Id).ToArray();
        }
    }

    internal sealed class BoundNativeResultNode
    {
        internal readonly NativeResultNode Definition;
        internal readonly NativeModelBinding Model;
        internal readonly BoundNativeResultNode Parent;
        internal readonly List<BoundNativeResultNode> Children = new List<BoundNativeResultNode>();
        internal NativeCollectionBinding Collection;
        internal int Id => Definition.Id;
        internal int Depth => Definition.Depth;
        internal int EdgeIndex => Definition.EdgeIndex;
        internal bool IsCollection => Definition.IsCollection;
        internal int HiddenCount => Definition.HiddenCount;
        internal NativeResultScalar[] Scalars => Definition.Scalars;
        internal IReadOnlyList<NativeInputBinding> Inputs => Definition.Inputs;

        internal BoundNativeResultNode(NativeResultNode definition, NativeModelBinding model, BoundNativeResultNode parent)
        { Definition = definition; Model = model; Parent = parent; }
    }
}
