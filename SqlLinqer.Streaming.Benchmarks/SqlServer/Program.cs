using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using SqlLinqer;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using NativeProbe.Control;

try { if (args.FirstOrDefault() == "--stress") await NestedStress.Run(args.Skip(1).ToArray()); else await Run(args); return 0; }
catch(Exception error) { Console.Error.WriteLine(error); return 1; }

static async Task Run(string[] args)
{
var settings = Environment.GetEnvironmentVariable("SQLLINQER_PROBE_SQLSERVER") ?? throw new Exception("Missing disposable SQL Server settings");
var builder = new SqlConnectionStringBuilder(settings); string database = "native_probe_" + Guid.NewGuid().ToString("N");
using var admin = new SqlConnection(settings); await admin.OpenAsync();
using(var cmd=admin.CreateCommand()){cmd.CommandText="CREATE DATABASE ["+database+"]";await cmd.ExecuteNonQueryAsync();}
builder.InitialCatalog=database; string connectionString=builder.ConnectionString;
try
{
    using var setup=new SqlConnection(connectionString);await setup.OpenAsync();
    using(var cmd=setup.CreateCommand())
    {
        cmd.CommandText="""
        CREATE TABLE native_roots(Id int PRIMARY KEY,Name nvarchar(100),ProfileId int);
        CREATE TABLE native_profiles(Id int PRIMARY KEY,Note nvarchar(100));
        CREATE TABLE native_orders(Id int PRIMARY KEY,ParentId int,Title nvarchar(100));
        CREATE TABLE native_items(Id int PRIMARY KEY,ParentId int,Label nvarchar(100));
        CREATE TABLE native_tags(Id int,ParentId int);
        CREATE INDEX ix_order_parent ON native_orders(ParentId,Id);
        CREATE INDEX ix_item_parent ON native_items(ParentId,Id);
        CREATE INDEX ix_tag_parent ON native_tags(ParentId,Id);
        INSERT native_profiles VALUES(1,NULL);
        WITH n(i) AS(SELECT 1 UNION ALL SELECT i+1 FROM n WHERE i<2000)
        INSERT native_roots SELECT i,N'root 雪 '+CONVERT(nvarchar(10),i),1 FROM n OPTION(MAXRECURSION 0);
        INSERT native_orders SELECT Id*2-1,Id,'one' FROM native_roots UNION ALL SELECT Id*2,Id,'two' FROM native_roots;
        INSERT native_items SELECT Id*2-1,Id,'first' FROM native_orders UNION ALL SELECT Id*2,Id,NULL FROM native_orders;
        INSERT native_tags SELECT Id*2,Id FROM native_roots UNION ALL SELECT Id*2,Id FROM native_roots;
        """;
        await cmd.ExecuteNonQueryAsync();
    }
    var connector=new ConnectionFactoryConnector(()=>new SqlConnection(connectionString),DbFlavor.SqlServer);
    // Bind once so this benchmark can load the released JSON core as well as
    // the current native core. This version adapter exists only in the harness.
    var executeMethod=typeof(BaseConnector).GetMethods().Single(m=>m.IsGenericMethodDefinition &&
        (m.Name=="ExecuteReaderModelAsyncStream" || m.Name=="ExecuteReaderJsonAsyncStream") && m.GetParameters().Length==4);
    var execute=(Func<SqlLinqer.Components.Render.RenderedQuery,DbTransaction,CancellationToken,TimeSpan?,IAsyncEnumerable<Root>>)
        executeMethod.MakeGenericMethod(typeof(Root)).CreateDelegate(typeof(Func<SqlLinqer.Components.Render.RenderedQuery,DbTransaction,CancellationToken,TimeSpan?,IAsyncEnumerable<Root>>),connector);
    string libraryTransport=executeMethod.Name.Contains("Json")?"released-library-json":"production-native";
    var native=new SelectQuery<Root>().SelectRootColumns().Select(x=>x.Profile).Select(x=>x.Orders).Select(x=>x.Orders.First().Items).Select(x=>x.Tags).OrderByAuto().Render(DbFlavor.SqlServer);
    const string json="""
        SELECT r.Id,r.Name,r.ProfileId,
        JSON_QUERY((SELECT p.Id,p.Note FROM native_profiles p WHERE p.Id=r.ProfileId FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER)) AS Profile,
        JSON_QUERY((SELECT o.Id,o.ParentId,o.Title,
          JSON_QUERY((SELECT i.Id,i.ParentId,i.Label FROM native_items i WHERE i.ParentId=o.Id ORDER BY i.Id FOR JSON PATH,INCLUDE_NULL_VALUES)) AS Items
          FROM native_orders o WHERE o.ParentId=r.Id ORDER BY o.Id FOR JSON PATH,INCLUDE_NULL_VALUES)) AS Orders,
        JSON_QUERY((SELECT t.Id,t.ParentId FROM native_tags t WHERE t.ParentId=r.Id ORDER BY t.Id FOR JSON PATH,INCLUDE_NULL_VALUES)) AS Tags
        FROM native_roots r ORDER BY r.Id FOR JSON PATH,INCLUDE_NULL_VALUES
        """;
    async IAsyncEnumerable<Root> Json(SqlConnection c,SqlTransaction tx,bool first)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=json;bool complete=false;
        using var reader=await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        try
        {
            if(!await reader.ReadAsync()){complete=true;yield break;}
            using var stream=new JsonFragmentStream(reader);
            await foreach(var row in JsonSerializer.DeserializeAsyncEnumerable<Root>(stream)) yield return row!;
            complete=true;
        }
        finally {if(!complete)cmd.Cancel();}
    }
    async Task<object> Measure(bool useNative,bool first,int run)
    {
        using var c=new SqlConnection(connectionString){StatisticsEnabled=true};await c.OpenAsync();
        using var tx=(SqlTransaction)await c.BeginTransactionAsync();c.ResetStatistics();
        var rows=useNative?execute(native,tx,default,null):Json(c,tx,first);
        long allocated=GC.GetTotalAllocatedBytes(true),start=Stopwatch.GetTimestamp();double firstMs=0;int roots=0;long digest=0;
        await foreach(var row in rows)
        {
            if(roots==0)firstMs=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if(row.Orders.Count!=2||row.Orders.Any(o=>o.Items.Count!=2)||row.Tags.Count!=2||row.Profile?.Id!=1||row.Profile.Note!=null)
                throw new Exception("Incomplete root");
            if(run<0)
            {
                if(row.Name!="root 雪 "+row.Id||row.ProfileId!=1)throw new Exception("Wrong scalar value");
                for(int oi=0;oi<2;oi++)
                {
                    var order=row.Orders[oi];
                    if(order.Id!=row.Id*2-1+oi||order.ParentId!=row.Id||order.Title!=(oi==0?"one":"two"))throw new Exception("Wrong order");
                    for(int ii=0;ii<2;ii++)
                    {
                        var item=order.Items[ii];
                        if(item.Id!=order.Id*2-1+ii||item.ParentId!=order.Id||item.Label!=(ii==0?"first":null))throw new Exception("Wrong item");
                    }
                }
                if(row.Tags.Any(t=>t.Id!=row.Id*2||t.ParentId!=row.Id))throw new Exception("Wrong duplicate tag");
            }
            digest+=row.Id;roots++;if(first)break;
        }
        double elapsed=Stopwatch.GetElapsedTime(start).TotalMilliseconds;allocated=GC.GetTotalAllocatedBytes(true)-allocated;
        var statistics=c.RetrieveStatistics();
        long bytes=Convert.ToInt64(statistics["BytesReceived"]),sent=Convert.ToInt64(statistics["BytesSent"]);
        if(roots!=(first?1:2000)||digest!=(first?1:2001000))throw new Exception("Wrong roots");
        await tx.RollbackAsync();
        return new{run,transport=useNative?libraryTransport:"framework-json-control",mode=first?"first-and-cancel":"full-stream",roots,firstMs,elapsedMs=elapsed,allocatedBytes=allocated,providerBytesReceived=bytes,providerBytesSent=sent,sqlCharacters=(useNative?native.Text:json).Length};
    }
    foreach(bool t in new[]{true,false})foreach(bool first in new[]{false,true})await Measure(t,first,-1);
    if(args.Length>1 && args[1]=="--profile")
    {
        using var c=new SqlConnection(connectionString);await c.OpenAsync();
        using var tx=(SqlTransaction)await c.BeginTransactionAsync();
        await SqlLinqer.Tests.Performance.ProfilingSession.Run("sqlserver",int.Parse(args[2]),async()=>
        {
            int count=0;long checksum=0;
            await foreach(var row in execute(native,tx,default,null))
            {
                if(row.Orders.Count!=2||row.Orders.Any(o=>o.Items.Count!=2)||row.Tags.Count!=2||row.Profile?.Id!=1||row.Profile.Note!=null)
                    throw new Exception("Incomplete profiled native graph");
                count++;checksum+=row.Id;
            }
            if(count!=2000||checksum!=2001000)throw new Exception("Wrong profiled root count/checksum");
            return checksum;
        });
        await tx.RollbackAsync();
        return;
    }
    var samples=new List<object>();
    int runs=args.Length>1?int.Parse(args[1]):7;
    for(int run=0;run<runs;run++)foreach(bool first in run%2==0?new[]{false,true}:new[]{true,false})
      foreach(bool t in run%2==0?new[]{true,false}:new[]{false,true})
      {GC.Collect();GC.WaitForPendingFinalizers();samples.Add(await Measure(t,first,run));}
    var plans=new List<object>();
    foreach(var pair in new[]{(libraryTransport,native.Text),("framework-json-control",json)})
    {
        using var cmd=setup.CreateCommand();cmd.CommandText="SET STATISTICS XML ON;"+pair.Item2+";SET STATISTICS XML OFF;";
        using var r=await cmd.ExecuteReaderAsync();string? xml=null;
        do {while(await r.ReadAsync())if(r.FieldCount==1&&r.GetName(0).Contains("Showplan"))xml=r.GetString(0);}while(await r.NextResultAsync());
        if(xml==null)throw new Exception("Missing actual plan");
        var doc=XDocument.Parse(xml);var ops=doc.Descendants().Where(x=>x.Name.LocalName=="RelOp").GroupBy(x=>(string?)x.Attribute("PhysicalOp")).ToDictionary(g=>g.Key!,g=>g.Count());
        plans.Add(new{transport=pair.Item1,operators=ops,xml});
    }
    File.WriteAllText(args[0],JsonSerializer.Serialize(new{samples,plans,byteMetric="SqlClient BytesReceived/BytesSent: SqlClient counters, not a TCP/TLS capture",fixture="2000 indexed roots, two orders with two items each, duplicate tags, present NULL-note profiles",runtime=Environment.Version.ToString(),serverVersion=setup.ServerVersion},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"{samples.Count} samples validated; actual plans captured");
}
finally
{
    SqlConnection.ClearAllPools();
    using var cmd=admin.CreateCommand();cmd.CommandText="ALTER DATABASE ["+database+"] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;DROP DATABASE ["+database+"]";await cmd.ExecuteNonQueryAsync();
}

}

[SqlTable("native_roots")] public sealed class Root
{
    [SqlPrimaryKey,SqlAutoOrderBy]public int Id{get;set;}public string? Name{get;set;}public int? ProfileId{get;set;}
    [SqlOneToOne(nameof(ProfileId))]public Profile? Profile{get;set;}
    [SqlOneToMany(nameof(Order.ParentId))]public List<Order> Orders{get;set;}=[];
    [SqlOneToMany(nameof(Tag.ParentId))]public List<Tag> Tags{get;set;}=[];
}
[SqlTable("native_profiles")]public sealed class Profile{[SqlPrimaryKey,SqlAutoOrderBy]public int Id{get;set;}public string? Note{get;set;}}
[SqlTable("native_orders")]public sealed class Order
{
    [SqlPrimaryKey,SqlAutoOrderBy]public int Id{get;set;}public int ParentId{get;set;}public string? Title{get;set;}
    [SqlOneToMany(nameof(Item.ParentId))]public List<Item> Items{get;set;}=[];
}
[SqlTable("native_items")]public sealed class Item{[SqlPrimaryKey,SqlAutoOrderBy]public int Id{get;set;}public int ParentId{get;set;}public string? Label{get;set;}}
[SqlTable("native_tags")]public sealed class Tag{[SqlAutoOrderBy]public int Id{get;set;}public int ParentId{get;set;}}
