import argparse,hashlib,json,os,pathlib,secrets,shutil,subprocess,sys,tempfile,time
root=pathlib.Path(__file__).resolve().parents[2]
project=pathlib.Path(__file__).resolve().parent
p=argparse.ArgumentParser()
p.add_argument("--baseline-core",type=pathlib.Path)
p.add_argument("--output",type=pathlib.Path,default=root/"TestResults/sqlserver-native-vs-json.json")
p.add_argument("--profile-directory",type=pathlib.Path)
p.add_argument("--profile-seconds",type=int,default=12)
args=p.parse_args()
if args.profile_directory and args.baseline_core:p.error("Profiling uses the current core; omit --baseline-core")
if not 1<=args.profile_seconds<=120:p.error("profile-seconds must be in 1..120")
output=args.output.resolve()
if args.profile_directory:
 args.profile_directory=args.profile_directory.resolve()
 output=args.profile_directory/"unused.json"
output.parent.mkdir(parents=True,exist_ok=True)
subprocess.run(["dotnet","build",str(project),"-c","Release","-v","quiet"],check=True)
candidate=project/"bin/Release/net10.0"
versions=[("candidate",candidate)]
if args.baseline_core:
 baseline=root/"TestResults/sqlserver-core-baseline"
 shutil.copytree(candidate,baseline,dirs_exist_ok=True)
 shutil.copy2(args.baseline_core,baseline/"SQLLinqer.dll")
 # A released netstandard2.0 assembly needs its declared async-interface
 # support assembly even when the current netstandard2.1 core does not.
 support=args.baseline_core.parent/"Microsoft.Bcl.AsyncInterfaces.dll"
 if support.exists():
  shutil.copy2(support,baseline/support.name)
  dep=baseline/"SqlLinqer.Streaming.SqlServer.Benchmarks.deps.json"
  metadata=json.loads(dep.read_text());key="Microsoft.Bcl.AsyncInterfaces/10.0.13"
  metadata["libraries"][key]={"type":"reference","serviceable":False,"sha512":""}
  metadata["targets"][metadata["runtimeTarget"]["name"]][key]={"runtime":{support.name:{}}}
  dep.write_text(json.dumps(metadata))
 versions.insert(0,("baseline",baseline))
hashes={v:hashlib.sha256((d/"SQLLinqer.dll").read_bytes()).hexdigest() for v,d in versions}
name="sqllinqer-production-probe-"+secrets.token_hex(5)
password="Aa1!"+secrets.token_hex(20)
if not (root/"TestResults/sqlserver-official-runtime/dotnet").exists():
 raise RuntimeError("Prepare the official .NET 10.0.13 runtime at TestResults/sqlserver-official-runtime; see README.md")
with tempfile.TemporaryDirectory(prefix="sqllinqer-probe-")as directory:
 env=pathlib.Path(directory)/"server.env";env.write_text("ACCEPT_EULA=Y\nMSSQL_SA_PASSWORD="+password+"\nMSSQL_MEMORY_LIMIT_MB=2048\n");env.chmod(0o600)
 subprocess.run(["docker","run","--detach","--name",name,"--env-file",str(env),"--memory","4g","--volume",str(root)+":"+str(root)+":ro","--volume",str(root/"TestResults/sqlserver-official-runtime")+":/dotnet:ro","--volume",str(output.parent)+":/results:rw","mcr.microsoft.com/mssql/server:2022-latest"],check=True,capture_output=True)
 try:
  deadline=time.monotonic()+120
  while True:
   logs=subprocess.check_output(["docker","logs",name],stderr=subprocess.STDOUT,text=True)
   if "SQL Server is now ready for client connections" in logs:break
   if time.monotonic()>deadline:raise TimeoutError("SQL Server startup")
   time.sleep(1)
  data=json.loads(subprocess.check_output(["docker","inspect",name],text=True))[0]
  port="1433"
  settings="Server=127.0.0.1,"+port+";Database=master;User Id=sa;Password="+password+";Encrypt=True;TrustServerCertificate=True;Connect Timeout=30;Pooling=False"
  if args.profile_directory:
   sys.path.insert(0,str(root/"SqlLinqer.Streaming.Benchmarks/Profiling"))
   from capture import capture,convert
   tool=root/"TestResults/profiling/tools/dotnet-trace"
   if not tool.exists():raise RuntimeError("Install the local dotnet-trace tool; see Profiling/README.md")
   prefix=["docker","exec","--user",str(os.getuid()),"--env","DOTNET_ROOT=/dotnet"]
   result=capture(prefix+["-i","--env","SQLLINQER_PROBE_SQLSERVER="+settings,name,"/dotnet/dotnet",str(candidate/"SqlLinqer.Streaming.SqlServer.Benchmarks.dll"),"unused","--profile",str(args.profile_seconds)],
       prefix+[name,str(tool)],args.profile_directory,"sqlserver",args.profile_seconds,"/results/sqlserver.nettrace")
   convert(tool,args.profile_directory,"sqlserver")
   (args.profile_directory/"sqlserver.manifest.json").write_text(json.dumps(dict(result,coreSha256=hashes["candidate"],fixture="2000 indexed roots; warmed async native stream; connection/transaction opened outside capture; compilation excluded",metric="EventPipe sampled managed thread time, not precise CPU time"),indent=2)+"\n")
   print("sqlserver",result["operations"],"profiled streams")
   sys.exit(0)
  all_samples=[];all_plans=[];metadata=None
  for iteration in range(7 if args.baseline_core else 1):
   ordered=versions if iteration%2==0 else list(reversed(versions))
   for version,directory in ordered:
    part="part-"+version+".json" if args.baseline_core else output.name
    subprocess.run(["docker","exec","--user",str(os.getuid()),"--env","DOTNET_ROOT=/dotnet","--env","SQLLINQER_PROBE_SQLSERVER="+settings,name,"/dotnet/dotnet",str(directory/"SqlLinqer.Streaming.SqlServer.Benchmarks.dll"),"/results/"+part,str(1 if args.baseline_core else 7)],check=True)
    if args.baseline_core:
     metadata=json.loads((output.parent/part).read_text())
     all_samples.extend(dict(s,version=version,run=iteration) for s in metadata["samples"])
     if iteration==0:all_plans.extend(dict(plan,version=version) for plan in metadata["plans"])
     (output.parent/part).unlink()
  if args.baseline_core:
   metadata.update(samples=all_samples,plans=all_plans,coreSha256=hashes,
       method="Seven alternating isolated processes per core version; identical harness, warmups, disposable fixture, connection and transaction excluded from timing.")
   output.write_text(json.dumps(metadata,indent=2)+"\n")
 finally:subprocess.run(["docker","rm","--force","--volumes",name],check=True,capture_output=True)
