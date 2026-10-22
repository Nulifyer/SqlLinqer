#!/usr/bin/env python3
"""Create a self-contained interactive HTML report from a completed measurement run."""
import argparse
import html
import json
import pathlib


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=pathlib.Path)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    args = parser.parse_args()
    report = json.loads(args.input.read_text())
    if report.get("status") != "complete" or report.get("purpose") != "measurement":
        parser.error("Use a completed measurement run, not a correctness or partial report")
    data = json.dumps(report["summaries"], ensure_ascii=False).replace("</", "<\\/")
    options = report["options"]
    metadata = html.escape(f"{report['host']['processor']} · {report['sdk']} SDK · SQLite {report['samples'][0]['sqliteVersion']} · "
                           f"{options['warmup']} warmups and {options['operations']} operations per worker · "
                           f"{options['iterations']} processes per case · {report['startedUtc'][:10]}")
    page = TEMPLATE.replace("__DATA__", data).replace("__METADATA__", metadata)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(page)
    print(args.output)


TEMPLATE = r'''<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>SQLite library comparisons</title>
<style>
:root { color-scheme: light dark; }
body { margin:0; font:14px/1.5 var(--font-sans,system-ui,sans-serif); color:var(--foreground,#273244); background:transparent; }
h2 { font-size:23px; margin:0 0 8px; letter-spacing:-.4px; } p { margin:8px 0 18px; }
.muted { color:var(--muted-foreground,#68758a); font-size:12px; }
.controls { display:flex; gap:14px; flex-wrap:wrap; margin:20px 0; }
label { display:grid; gap:5px; font-size:12px; }
select { font:inherit; padding:7px 9px; border:1px solid var(--border,#b9c2cf); border-radius:6px;
  color:inherit; background:var(--card,#fff); }
.row { display:grid; grid-template-columns:110px minmax(60px,1fr) 90px; align-items:center; gap:8px; margin:13px 0; }
.row strong { font-weight:550; font-size:12px; } .row output { text-align:right; font-variant-numeric:tabular-nums; font-size:12px; }
.track { background:var(--secondary,#eaf0f5); height:17px; border-radius:3px; overflow:hidden; }
.bar { height:100%; background:var(--muted-foreground,#7c899b); border-radius:3px; }
.own .bar { background:var(--accent,#3267d5); } .own strong { font-weight:700; }
.tablewrap { overflow-x:auto; margin-top:25px; } table { border-collapse:collapse; width:100%; font-size:12px; }
th,td { border-bottom:1px solid var(--border,#d5dce5); padding:9px 6px; text-align:right; white-space:nowrap; font-variant-numeric:tabular-nums; }
th:first-child,td:first-child { text-align:left; padding-left:0; } th { font-weight:550; }
.limits { margin-top:20px; padding:16px; border-radius:var(--radius,8px); background:var(--secondary,#edf2f7); font-size:12px; }
.limits p { margin:0; } .limits p + p { margin-top:8px; }
@media(prefers-color-scheme:dark) { body { color:var(--foreground,#e4e9f1); } select { background:var(--card,#202836); }
  .track,.limits { background:var(--secondary,#252d39); } }
</style></head><body>
<h2>SQLite reads, measured across five libraries</h2>
<p>Equivalent results, warmed operations, and both EF Core loading strategies. Lower values mean less time or fewer managed allocations.</p>
<p class="muted">__METADATA__</p>
<div class="controls">
<label>Fixture customers<select id="rows" aria-label="Fixture customers"></select></label>
<label>Workload<select id="workload" aria-label="Workload">
<option value="flat">Flat buffered read</option><option value="graph" selected>Two nested collections</option>
<option value="page">Graph page + total</option><option value="stream">Flat async stream</option></select></label>
<label>Chart metric<select id="metric" aria-label="Chart metric"><option value="medianMs">Time per operation</option>
<option value="medianAllocatedBytes">Managed allocations</option></select></label>
</div>
<p id="description" class="muted" aria-live="polite"></p>
<div id="chart" role="img" aria-label="Median comparison bars; exact values follow in the table"></div>
<div class="tablewrap"><table><thead><tr><th>Library</th><th>Median ms/op<br><span class="muted">min–max of sample means</span></th>
<th>KiB/op</th><th>Commands/op</th></tr></thead><tbody id="numbers"></tbody></table></div>
<div class="limits"><p><strong>Local SQLite baseline.</strong> No remote server, network latency, server CPU, or transferred-byte measurements.
Low child fanout, indexed relationships, warm caches, connection pooling disabled, and queries without EF change tracking.</p>
<p>Dapper batches graph result sets in one command. SqlLinqer uses database JSON. SqlKata streaming composes its compiler with Dapper.
One command may contain several statements. Allocations exclude native memory. Both EF variants use the same flat query.</p></div>
<script>
const summaries=__DATA__;
const labels={sqllinqer:'SqlLinqer',dapper:'Dapper',sqlkata:'SqlKata + Dapper',linq2db:'LINQ to DB','ef-single':'EF single','ef-split':'EF split'};
const order=Object.keys(labels);
const rows=document.getElementById('rows'),workload=document.getElementById('workload'),metric=document.getElementById('metric');
for(const size of [...new Set(summaries.map(x=>x.fixtureRows))].sort((a,b)=>a-b)) rows.add(new Option(size.toLocaleString(),size));
for(const option of [...workload.options]) if(!summaries.some(x=>x.workload===option.value)) option.remove();
rows.value=Math.max(...summaries.map(x=>x.fixtureRows));
function render(){
 const group=summaries.filter(x=>x.fixtureRows===Number(rows.value)&&x.workload===workload.value).sort((a,b)=>order.indexOf(a.library)-order.indexOf(b.library));
 const max=Math.max(...group.map(x=>x[metric.value]),1e-9)*1.05;
 const allocation=metric.value==='medianAllocatedBytes';
 document.getElementById('description').textContent=group.length?`${group[0].resultRows.toLocaleString()} returned roots · medians across ${group[0].samples} independent worker processes per case`: 'No samples for this selection.';
 document.getElementById('chart').innerHTML=group.map(x=>`<div class="row ${x.library==='sqllinqer'?'own':''}"><strong>${labels[x.library]}</strong><div class="track"><div class="bar" style="width:${100*x[metric.value]/max}%"></div></div><output>${allocation?(x.medianAllocatedBytes/1024).toFixed(1)+' KiB':x.medianMs.toFixed(3)+' ms'}</output></div>`).join('');
 document.getElementById('numbers').innerHTML=group.map(x=>`<tr><td>${labels[x.library]}</td><td>${x.medianMs.toFixed(3)}<br><span class="muted">${x.minMs.toFixed(3)}–${x.maxMs.toFixed(3)}</span></td><td>${(x.medianAllocatedBytes/1024).toFixed(1)}</td><td>${x.commandsPerOperation}</td></tr>`).join('');
}
for(const control of [rows,workload,metric]) control.addEventListener('change',render);
render();
</script></body></html>
'''


if __name__ == "__main__":
    main()
