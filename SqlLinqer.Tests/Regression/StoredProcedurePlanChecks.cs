using System.Data;
using System.Reflection;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;

namespace SqlLinqer.Tests.Regression;

internal static partial class StoredProcedureRegressionTests
{
    private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void RunCallablePlanChecks(Action<string, Action> test)
    {
        test("multi callables reuse immutable plans while binding collections independently per call", () =>
        {
            // Identity assertions protect the cache without machine-dependent timing thresholds.
            object Shape()
            {
                var query = typeof(MultiUsers).BaseType.GetMethod("CreateQuery", InstancePrivate).Invoke(new MultiUsers(), null);
                return query.GetType().GetField("_shape", InstancePrivate).GetValue(query);
            }
            var first = Shape(); var second = Shape();
            Check(!ReferenceEquals(first, second), "Invocation state was shared");
            Check(ReferenceEquals(first.GetType().GetField("_layout", InstancePrivate).GetValue(first),
                second.GetType().GetField("_layout", InstancePrivate).GetValue(second)), "Immutable tuple planning was repeated");
            var firstTables = (Array)first.GetType().GetField("Tables", InstancePrivate).GetValue(first);
            var secondTables = (Array)second.GetType().GetField("Tables", InstancePrivate).GetValue(second);
            Check(!ReferenceEquals(firstTables.GetValue(0), secondTables.GetValue(0)), "Per-call collection bindings were cached globally");
        });
        test("cached layouts support later registration and keep repeated collection factories stable during a call", () =>
        {
            var procedure = new LateRegisteredTables();
            Throws<InvalidOperationException>(() => procedure.Render(DbFlavor.SqlServer));
            SqlResultCollections.Register<int, LateRegisteredNumbers>(rows =>
            {
                SqlResultCollections.Register<int, LateRegisteredNumbers>(next => new LateRegisteredNumbers(next.Select(x => x + 100)));
                return new LateRegisteredNumbers(rows);
            });
            var connector = new FakeConnector(MultiDatabase(Numbers(1), Numbers(2)), DbFlavor.SqlServer);
            var first = procedure.Execute(connector).GetResultOrThrow();
            Check(first.First.Single() == 1 && first.Second.Single() == 2, "Registration changed the remaining tables in an active call");
            var second = procedure.Execute(connector).GetResultOrThrow();
            Check(second.First.Single() == 101 && second.Second.Single() == 102, "A cached plan retained an obsolete collection factory");
        });
        foreach (bool disposalFailure in new[] { false, true })
            test($"render disposal cleans generated TVPs exactly once and preserves borrowed tables, error={disposalFailure}", () =>
            {
                using var borrowed = Numbers(3);
                bool borrowedDisposed = false; borrowed.Disposed += (_, _) => borrowedDisposed = true;
                using var rendered = new MixedOwnershipCall().Render(new MixedOwnershipInput
                {
                    First = new[] { new ObjectTvpRow { Id = 1 } },
                    Second = new[] { new ObjectTvpRow { Id = 2 } },
                    Borrowed = borrowed
                }, DbFlavor.SqlServer);
                var generated = rendered.Parameters.GetAll().Select(p => p.Value).OfType<DataTable>().Where(t => !ReferenceEquals(t, borrowed)).ToArray();
                Check(generated.Length == 2, "Mixed render did not bind its generated tables");
                int disposed = 0;
                foreach (var table in generated) table.Disposed += (_, _) => disposed++;
                if (disposalFailure) generated[0].Disposed += (_, _) => throw new InvalidOperationException("Dispose event failed");
                if (disposalFailure) Throws<InvalidOperationException>(rendered.Dispose); else rendered.Dispose();
                rendered.Dispose();
                Check(disposed == 2 && !borrowedDisposed && (int)borrowed.Rows[0][0] == 3, "Render disposal leaked owned tables or disposed a borrowed table");
            });
        test("async TVP capture keeps one generated table and copies mutable cells and borrowed tables", () =>
        {
            var rows = Enumerable.Range(0, 1024).Select(i => new ObjectTvpRow { Id = i, Payload = new byte[] { 1, 2, 3 } }).ToArray();
            using var borrowed = Numbers(3);
            var args = new MixedOwnershipInput { First = rows, Second = Array.Empty<ObjectTvpRow>(), Borrowed = borrowed };
            var argumentsType = typeof(SqlLinqerCallable<>).Assembly.GetType("SqlLinqer.Queries.Typed.CallableArguments`1").MakeGenericType(typeof(MixedOwnershipInput));
            var arguments = Activator.CreateInstance(argumentsType, InstancePrivate, null, new object[] { new SqlProcedure("MixedOwnership"), args }, null);
            var prepare = argumentsType.GetMethod("Prepare", InstancePrivate, null, new[] { typeof(DbFlavor), typeof(string), typeof(bool) }, null);
            using var prepared = (IDisposable)prepare.Invoke(arguments, new object[] { DbFlavor.SqlServer, null, true });
            var preparedType = prepared.GetType();
            var query = (RenderedQuery)preparedType.GetField("Query", InstancePrivate).GetValue(prepared);
            var originalTables = query.Parameters.GetAll().Select(p => p.Value).OfType<DataTable>().ToArray();
            var originalGenerated = originalTables.Where(t => !ReferenceEquals(t, borrowed)).ToArray();
            preparedType.GetMethod("CaptureInputs", InstancePrivate).Invoke(prepared, null);
            var captured = (RenderedQuery)preparedType.GetField("Query", InstancePrivate).GetValue(prepared);
            var capturedTables = captured.Parameters.GetAll().Select(p => p.Value).OfType<DataTable>().ToArray();
            Check(originalGenerated.All(t => capturedTables.Any(c => ReferenceEquals(c, t))), "Async capture allocated a second generated table");
            Check(!capturedTables.Any(t => ReferenceEquals(t, borrowed)), "Async capture retained a borrowed table");
            var tableWithRows = originalGenerated.Single(t => t.Rows.Count == 1024);
            Check(!ReferenceEquals(tableWithRows.Rows[0]["Payload"], rows[0].Payload), "Reusing the owned table left a mutable binary cell borrowed");
            rows[0].Payload[0] = 99;
            Check(((byte[])tableWithRows.Rows[0]["Payload"])[0] == 1, "Captured cells changed with the source");
        });
    }

    private sealed class LateRegisteredNumbers(IEnumerable<int> rows) : List<int>(rows) { }
    [SqlProcedure("LateRegisteredTables")]
    private sealed class LateRegisteredTables : SqlLinqerMultiCallable<(LateRegisteredNumbers First, LateRegisteredNumbers Second)> { }
    private sealed class MixedOwnershipInput
    {
        [SqlParameter(TypeName = "dbo.ObjectRows")] public ObjectTvpRow[] First { get; set; }
        [SqlParameter(TypeName = "dbo.ObjectRows")] public ObjectTvpRow[] Second { get; set; }
        [SqlParameter(TypeName = "dbo.Ids")] public DataTable Borrowed { get; set; }
    }
    [SqlProcedure("MixedOwnership")]
    private sealed class MixedOwnershipCall : SqlLinqerCallable<MixedOwnershipInput, int> { }
}
