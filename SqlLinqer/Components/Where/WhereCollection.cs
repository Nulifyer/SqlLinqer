using System;
using System.Linq;
using System.Text;
using SqlLinqer.Components.Generic;
using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Extensions.EnumExtensions;
using System.Collections.Generic;
using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Components.Where
{
    public class WhereCollection : ComponentCollectionBase<IWhereComponent>, IWhereComponent
    {
        protected internal readonly string UUID;
        protected internal readonly SqlWhereOp WhereOp;

        /// <summary>
        /// Sort order
        /// </summary>
        protected internal int Order => 99999;

        public WhereCollection(SqlWhereOp where_op) : base()
        {
            UUID = SqlLinqer.Extensions.StringExtensions.StringExtensions.GetRandomHashString();
            WhereOp = where_op;
        }

        protected internal new void AddComponentFirst(IWhereComponent component)
            => base.AddComponentFirst(component);
        public new void AddComponent(IWhereComponent component)
            => base.AddComponent(component);
        public new void AddComponents(IEnumerable<IWhereComponent> components)
            => base.AddComponents(components);
        internal new void Clear()
            => base.Clear();
        internal new IEnumerable<IWhereComponent> GetAll()
        {
            return base.GetAll();
        }

        internal void ImportFrom(WhereCollection from_collection)
        {
            foreach (var com in from_collection.GetAll())
            {
                AddComponent(com);
            }
        }

        private WhereCollection Simplify()
        {
            var newCollection = new WhereCollection(WhereOp);
            var old_components = GetAll();
            foreach (var com in old_components)
            {
                var current_com = com;
                bool addToNew = true;

                if (current_com is WhereCollection sub_collection)
                    current_com = sub_collection.Simplify();

                foreach (var new_com in newCollection.GetAll())
                {
                    if (current_com.TryCombineInto(newCollection, new_com))
                    {
                        addToNew = false;
                        break;
                    }
                    else if (new_com.TryCombineInto(newCollection, current_com))
                    {
                        addToNew = true;
                        newCollection.Components.Remove(new_com);
                        break;
                    }
                }

                if (
                    addToNew && !current_com.TryCombineInto(newCollection, newCollection)
                    && !(current_com is WhereCollection sub_collection_2 && sub_collection_2.Count == 0)
                )
                {
                    newCollection.AddComponent(current_com);
                }
            }

            return newCollection;
        }
        internal WhereCollection FinalizeStatements(WhereQuery query)
        {
            var newCollection = new WhereCollection(WhereOp);

            foreach (var com in GetAll())
            {
                switch (com)
                {
                    case WhereCollection sub_group:
                        var new_group = sub_group.FinalizeStatements(query);
                        if (new_group.Count > 0)
                            newCollection.AddComponent(new_group);
                        break;
                    case WhereStatement statement:
                        var new_stm = statement.Finalize(query);
                        if (new_stm != null)
                            newCollection.AddComponent(new_stm);
                        break;
                    default:
                        newCollection.AddComponent(com);
                        break;
                }
            }

            if (newCollection.Count == 1 && newCollection.Components.First() is WhereCollection only_sub_collection)
                return only_sub_collection;

            return newCollection;
        }
        
        protected internal WhereCollection Invert()
        {
            var invertedCollection = new WhereCollection(WhereOp == SqlWhereOp.AND ? SqlWhereOp.OR : SqlWhereOp.AND);
            foreach (var com in GetAll())
            {
                switch (com)
                {
                    case WhereCollection subCollection:
                        invertedCollection.AddComponent(subCollection.Invert());
                        break;
                    case WhereStatement statement:
                        invertedCollection.AddComponent(statement.Invert());
                        break;
                    default:
                        throw new NotSupportedException($"{nameof(WhereCollection)}.{nameof(Invert)} does not support type '{com.GetType().Name}'.");
                }
            }
            return invertedCollection;
        }

        internal WhereCollection Finalize(WhereQuery query)
        {
            var finalized = this
                .Simplify()
                .FinalizeStatements(query)
                .Simplify();

            finalized.Components = finalized.GetAll()
                .OrderBy(x =>
                {
                    switch (x)
                    {
                        case WhereCollection sub_group: return sub_group.Order;
                        case WhereStatement statement: return statement.Order;
                        default: return 0;
                    }
                })
                .ToList();

            return finalized;
        }

        protected internal string Render(ParameterCollection parameters, DbFlavor flavor)
        {
            return Render(null, parameters, flavor);
        }
        protected internal string Render(WhereQuery query, ParameterCollection parameters, DbFlavor flavor, bool include_where = true)
        {
            if (Count == 0)
                return null;

            var builder = new StringBuilder();
            var finalized = Finalize(query);

            if (finalized.Count == 0)
                return null;

            Render(finalized, query, parameters, flavor, builder);

            if (query != null && include_where)
                builder.Insert(0, "WHERE ");

            return builder.ToString();
        }

        private static void Render(WhereCollection collection, WhereQuery query, ParameterCollection parameters, DbFlavor flavor, StringBuilder builder)
        {
            bool first = true;
            foreach (var com in collection.GetAll())
            {
                if (!first)
                    builder.Append($" {collection.WhereOp.GetDescription()} ");

                switch (com)
                {
                    case WhereCollection sub_collection:
                        builder.Append("(");
                        Render(sub_collection, query, parameters, flavor, builder);
                        builder.Append(")");
                        break;
                    case WhereStatement statement:
                        statement.Render(query, parameters, flavor, builder);
                        break;
                    default:
                        throw new NotSupportedException($"{com.GetType().Name} is not supported as a {nameof(IWhereComponent)}");
                }

                if (first)
                    first = false;
            }
        }

        public bool TryCombineInto(WhereCollection current_collection, IWhereComponent parent_component)
        {
            // parent is a where with
            if (parent_component is WhereWithJoinBack parent_where_with)
            {
                var components = this.GetAll();
                if (
                    // all components in group are where withs
                    components.All(x => x is WhereWithJoinBack)
                    // all where withs in group can be combined into parent
                    && components.OfType<WhereWithJoinBack>().All(x => x.SubQuery.Table.IsSameOrChildOf(parent_where_with.SubQuery.Table))
                )
                {
                    var wrap_collection = new WhereCollection(current_collection.WhereOp);
                    // add each sub query where group to the wrap
                    foreach (var com in components.OfType<WhereWithJoinBack>())
                    {
                        wrap_collection.AddComponent(com.SubQuery.RootWhere);
                    }
                    // add self to wrap
                    wrap_collection.AddComponent(parent_where_with.SubQuery.RootWhere);
                    // wrap is new root
                    parent_where_with.SubQuery.RootWhere = wrap_collection;
                    return true;
                }
            }
            else if (
                // parent is also a collection
                parent_component is WhereCollection parent_collection
                // parent com and the wrapping collection all share the same OP
                && parent_collection.WhereOp == this.WhereOp
                && current_collection.WhereOp == this.WhereOp
            )
            {
                parent_collection.AddComponents(this.GetAll());
                return true;
            }
            return false;
        }
    
        public IEnumerable<Column> GetColumnReferences()
        {
            foreach (var com in this.Components)
            {
                foreach (var col in com.GetColumnReferences())
                {
                    yield return col;
                }
            }
        }
    }
}