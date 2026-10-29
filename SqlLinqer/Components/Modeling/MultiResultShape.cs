using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using SqlLinqer.Modeling;

namespace SqlLinqer.Components.Modeling
{
    // Cache immutable reflection and constructor plans; bind replaceable collection factories per invocation.
    internal sealed class MultiResultShape<TResults>
    {
        private static readonly Lazy<Layout> CachedLayout = new Lazy<Layout>(() => new Layout());
        private readonly Layout _layout;
        internal readonly ResultTable[] Tables;

        internal MultiResultShape()
        {
            _layout = CachedLayout.Value;
            Tables = _layout.Bind();
        }

        internal TResults Create(object[] tables) => _layout.Create(tables);

        private sealed class Layout
        {
            private readonly Type[] _collections;
            private readonly Func<ResultTable>[] _bind;
            internal readonly Func<object[], TResults> Create;

            internal Layout()
            {
                var collections = new List<Type>();
                var bind = new List<Func<ResultTable>>();
                var values = Expression.Parameter(typeof(object[]), "tables");
                var tuple = BuildTuple(typeof(TResults), values, collections, bind);
                if (collections.Count < 2) throw new InvalidOperationException("Multi-callable results require a value tuple containing at least two result collections.");
                _collections = collections.ToArray();
                _bind = bind.ToArray();
                Create = Expression.Lambda<Func<object[], TResults>>(tuple, values).Compile();
            }

            internal ResultTable[] Bind()
            {
                var tables = new ResultTable[_bind.Length];
                var captured = new Dictionary<Type, ResultTable>();
                for (int i = 0; i < tables.Length; i++)
                {
                    var collection = _collections[i];
                    if (!captured.TryGetValue(collection, out var table))
                        captured.Add(collection, table = _bind[i]());
                    tables[i] = table;
                }
                return tables;
            }

            private static Expression BuildTuple(Type type, ParameterExpression values, List<Type> collections, List<Func<ResultTable>> bind)
            {
                if (!type.IsValueType || !type.IsGenericType || type.Namespace != "System" || !type.Name.StartsWith("ValueTuple`", StringComparison.Ordinal))
                    throw new InvalidOperationException("Multi-callable results must be a value tuple of result collections.");
                var types = type.GetGenericArguments();
                var arguments = new Expression[types.Length];
                for (int i = 0; i < types.Length; i++)
                {
                    if (i == 7) { arguments[i] = BuildTuple(types[i], values, collections, bind); continue; }
                    var collection = types[i];
                    var rowType = SqlResultCollections.GetRowType(collection);
                    var row = TabularRowType.GetCached(rowType);
                    var constructor = typeof(ResultTable<>).MakeGenericType(rowType).GetConstructor(new[] { typeof(TabularRowType), typeof(Type) });
                    bind.Add(Expression.Lambda<Func<ResultTable>>(Expression.New(constructor,
                        Expression.Constant(row), Expression.Constant(collection, typeof(Type)))).Compile());
                    arguments[i] = Expression.Convert(Expression.ArrayIndex(values, Expression.Constant(collections.Count)), collection);
                    collections.Add(collection);
                }
                return Expression.New(type.GetConstructor(types), arguments);
            }
        }
    }

    internal abstract class ResultTable
    {
        internal readonly TabularRowType Row;
        protected ResultTable(TabularRowType row) { Row = row; }
        internal abstract IList CreateBuffer();
        internal abstract object Materialize(IList rows);
    }

    internal sealed class ResultTable<TRow> : ResultTable
    {
        private readonly Func<IList, object> _materialize;
        public ResultTable(TabularRowType row, Type collection) : base(row) { _materialize = SqlResultCollections.Resolve<TRow>(collection); }
        internal override IList CreateBuffer() => new List<TRow>();
        internal override object Materialize(IList rows)
            => _materialize(rows) ?? throw new InvalidOperationException("A result collection factory returned null.");
    }
}
