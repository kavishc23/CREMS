from pathlib import Path
from collections import defaultdict
from datetime import datetime
from zoneinfo import ZoneInfo
import xml.etree.ElementTree as ET
import json,html,shutil,math,csv,re
R=Path(__file__).resolve().parents[1];O=R/'output/coverage';B=O/'backend'
src=max(B.glob('*/*.cobertura.xml'),key=lambda x:x.stat().st_mtime);shutil.copy2(src,B/'coverage.cobertura.xml')
r=ET.parse(src).getroot();f=json.loads((O/'frontend/coverage-summary.json').read_text());ft=f['total'];trx=ET.parse(B/'backend-tests.trx').getroot();ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};counts=trx.find('.//t:Counters',ns).attrib
back={'lines':{'covered':int(r.get('lines-covered')),'total':int(r.get('lines-valid'))},'branches':{'covered':int(r.get('branches-covered')),'total':int(r.get('branches-valid'))}}
for m in back.values():m['pct']=100*m['covered']/m['total'] if m['total'] else 0
front={k:ft[k] for k in ['lines','statements','functions','branches']}
combined={k:{'covered':back[k]['covered']+front[k]['covered'],'total':back[k]['total']+front[k]['total']} for k in ['lines','branches']}
for m in combined.values():m['pct']=100*m['covered']/m['total']
files=defaultdict(lambda:{'covered':0,'total':0})
for c in r.findall('.//class'):
 name=str(Path(c.get('filename')).relative_to(R));lines=c.findall('./lines/line');files[name]['total']+=len(lines);files[name]['covered']+=sum(int(x.get('hits'))>0 for x in lines)
for k,v in f.items():
 if k=='total':continue
 files[str(Path(k).relative_to(R))]={'covered':v['lines']['covered'],'total':v['lines']['total']}
groups=defaultdict(lambda:{'covered':0,'total':0})
for name,v in files.items():
 parts=Path(name).parts;group='/'.join(parts[:3]) if len(parts)>3 else '/'.join(parts[:2])
 for k in ['covered','total']:groups[group][k]+=v[k]
for dic in [files,groups]:
 for v in dic.values():v['pct']=100*v['covered']/v['total'] if v['total'] else None;v['uncovered']=v['total']-v['covered']
frontend_results=json.loads((O/'frontend-tests.json').read_text());front_passed=frontend_results['numPassedTests'];front_failed=frontend_results['numFailedTests'];front_skipped=frontend_results['numPendingTests'];front_files=len(frontend_results['testResults'])
failed=int(counts['failed']);passed=int(counts['passed']);skipped=int(counts['total'])-int(counts['executed']);date=datetime.now(ZoneInfo('Pacific/Fiji')).strftime('%d %B %Y %H:%M %Z')
skips=[x.get('testName') for x in trx.findall('.//t:UnitTestResult',ns) if x.get('outcome')=='NotExecuted']
report={'target_line_coverage_pct':95,'target_reached':back['lines']['pct']>=95 and front['lines']['pct']>=95,'generated_at_fiji':date,'scope':'Backend unit/component tests and isolated SQL integration tests; frontend unit/component tests. Browser end-to-end tests were not run.','tests':{'backend':{'total':int(counts['total']),'passed':passed,'failed':failed,'skipped':skipped},'frontend':{'passed':front_passed,'failed':front_failed,'skipped':front_skipped,'files':front_files},'skipped_backend_tests':skips},'coverage':{'backend':back,'frontend':front,'combined_indicative':combined},'files':files,'modules':groups}
notes_path=O/'verification-notes.json'
verification_notes=[]
if notes_path.exists():
    notes=json.loads(notes_path.read_text())
    if notes.get('backend_coverage_evidence_name')==src.name:verification_notes=notes.get('notes',[])
if failed or front_failed:verification_notes.append(f'This run contains {failed+front_failed} failed test(s); review the individual outcomes before treating it as a successful validation.')
report['verification_notes']=verification_notes
# Keep Istanbul's standard high watermark; never recolor a gap as success.
green_threshold=80
coverage_audit=[]
for layer,metrics in [('Backend',back),('Frontend',front)]:
    for metric,value in metrics.items():
        coverage_audit.append({'scope':layer,'metric':metric,'covered':value['covered'],'total':value['total'],'pct':value['pct'],'green':value['total']>0 and value['pct']>=green_threshold})
backend_file_metrics=defaultdict(lambda:{'lines':{'covered':0,'total':0},'branches':{'covered':0,'total':0}})
for cls in r.findall('.//class'):
    name=str(Path(cls.get('filename')).relative_to(R))
    metrics=backend_file_metrics[name]
    for line in cls.findall('./lines/line'):
        metrics['lines']['total']+=1;metrics['lines']['covered']+=int(line.get('hits'))>0
        condition=re.search(r'\((\d+)/(\d+)\)',line.get('condition-coverage',''))
        if condition:
            metrics['branches']['covered']+=int(condition[1]);metrics['branches']['total']+=int(condition[2])
for name,metrics in backend_file_metrics.items():
    for metric,value in metrics.items():
        pct=100*value['covered']/value['total'] if value['total'] else None
        coverage_audit.append({'scope':name,'metric':metric,'covered':value['covered'],'total':value['total'],'pct':pct,'green':pct is not None and pct>=green_threshold})
for name,metrics in f.items():
    if name=='total':continue
    for metric in ['statements','branches','functions','lines']:
        value=metrics[metric]
        coverage_audit.append({'scope':str(Path(name).relative_to(R)),'metric':metric,'covered':value['covered'],'total':value['total'],'pct':value['pct'],'green':value['total']>0 and value['pct']>=green_threshold})
report['green_threshold_pct']=green_threshold
report['all_layer_metrics_green']=all(row['green'] for row in coverage_audit if row['scope'] in ['Backend','Frontend'])
report['coverage_audit']=coverage_audit
frontend_directories={}
for label,prefix in [('src/components','components/'),('src/pages','pages/'),('src/pages/maintenance','pages/maintenance/')]:
    values={metric:{'covered':0,'total':0} for metric in ['statements','branches','functions','lines']}
    for filename,metrics in f.items():
        if filename=='total':continue
        relative=str(Path(filename).relative_to(R/'src/crems-web/src'))
        if not relative.startswith(prefix) or label=='src/pages' and relative.startswith('pages/maintenance/'):continue
        for metric,value in values.items():
            value['covered']+=metrics[metric]['covered'];value['total']+=metrics[metric]['total']
    for value in values.values():value['pct']=100*value['covered']/value['total'] if value['total'] else None
    frontend_directories[label]=values
report['requested_frontend_directories']=frontend_directories
report['requested_directories_all_green']=all(value['pct'] is not None and value['pct']>=green_threshold for metrics in frontend_directories.values() for value in metrics.values())
frontend_file_audit=[]
for name,metrics in f.items():
    if name=='total':continue
    measured=[metrics[m] for m in ['statements','branches','functions','lines'] if metrics[m]['total']>0]
    frontend_file_audit.append({'file':str(Path(name).relative_to(R)),'all_metrics_green':bool(measured) and all(v['pct']>=green_threshold for v in measured)})
report['frontend_file_green_summary']={'green':sum(row['all_metrics_green'] for row in frontend_file_audit),'total':len(frontend_file_audit),'files':frontend_file_audit}
with (O/'coverage-metric-audit.csv').open('w',newline='') as stream:
    writer=csv.DictWriter(stream,fieldnames=['scope','metric','covered','total','pct','green']);writer.writeheader();writer.writerows(coverage_audit)
test_cases=[]
for result in trx.findall('.//t:UnitTestResult',ns):
    duration=result.get('duration','00:00:00').split(':')
    milliseconds=round((float(duration[0])*3600+float(duration[1])*60+float(duration[2]))*1000) if len(duration)==3 else None
    test_cases.append({'suite':'Backend','name':result.get('testName',''),'outcome':result.get('outcome','Unknown'),'duration_ms':milliseconds})
for file in frontend_results['testResults']:
    for case in file.get('assertionResults',[]):
        test_cases.append({'suite':'Frontend','name':case.get('fullName',case.get('title','')),'outcome':case.get('status','unknown'),'duration_ms':case.get('duration')})
test_cases.sort(key=lambda case:(case['suite'],case['name']))
report['test_cases']=test_cases
report['measurement_sources']={'backend_cobertura':str(src.relative_to(R)),'backend_trx':'output/coverage/backend/backend-tests.trx','frontend_results':'output/coverage/frontend-tests.json','frontend_summary':'output/coverage/frontend/coverage-summary.json'}
backend_times=trx.find('t:Times',ns)
report['run_times']={'backend':backend_times.attrib if backend_times is not None else {},'frontend_start_epoch_ms':frontend_results.get('startTime')}
with (O/'test-case-results.csv').open('w',newline='') as stream:
    writer=csv.DictWriter(stream,fieldnames=['suite','name','outcome','duration_ms']);writer.writeheader();writer.writerows(test_cases)
(O/'test-coverage-report.json').write_text(json.dumps(report,indent=2))
fmt=lambda v:'—' if v is None else f'{v:.2f}%'
esc=html.escape
rows=''
for label,v in [('Backend',back),('Frontend',front),('Combined indicative',combined)]:rows+=f'<tr><th>{label}</th><td>{fmt(v["lines"]["pct"])}</td><td>{v["lines"]["covered"]:,} / {v["lines"]["total"]:,}</td><td>{fmt(v["branches"]["pct"])}</td></tr>'
def tab(items):
 return '<table><thead><tr><th>Source area</th><th>Line coverage</th><th>Covered / total</th><th>Uncovered</th></tr></thead><tbody>'+''.join(f'<tr><td>{esc(k)}</td><td>{fmt(v["pct"])}</td><td>{v["covered"]:,} / {v["total"]:,}</td><td>{v["uncovered"]:,}</td></tr>' for k,v in items)+'</tbody></table>'
modules=tab(sorted(groups.items()));gaps=tab(sorted(files.items(),key=lambda kv:kv[1]['uncovered'],reverse=True)[:15]);full=tab(sorted(files.items()))
audit_rows=''.join(f'<tr><th>{esc(row["scope"])}</th><td>{esc(row["metric"])}</td><td>{fmt(row["pct"])}</td><td>{"Green" if row["green"] else "Below green"}</td></tr>' for row in coverage_audit if row['scope'] in ['Backend','Frontend'])
directory_rows=''.join('<tr><th>'+esc(name)+'</th>'+''.join('<td>'+fmt(metrics[metric]['pct'])+'</td>' for metric in ['statements','branches','functions','lines'])+'</tr>' for name,metrics in frontend_directories.items())
case_rows=''.join('<tr><td>'+esc(case['suite'])+'</td><td>'+esc(case['name'])+'</td><td>'+esc(case['outcome'])+'</td><td>'+str(case['duration_ms'] if case['duration_ms'] is not None else '—')+'</td></tr>' for case in test_cases)
body=f'''<header><p class="eyebrow">CREMS · Engineering verification</p><h1>System test coverage report</h1><p>{date}</p></header>
<p class="lead"><strong>{passed+front_passed} tests passed; {failed+front_failed} failed</strong>. The indicative combined line coverage is <strong>{combined['lines']['pct']:.2f}%</strong>. This confirms the current tests pass; it does not establish complete functional coverage.</p>
<h2>Verification status</h2><p>The target is 95% line coverage in each layer. Current target status: <strong>{'Reached' if report['target_reached'] else 'Not reached'}</strong>.</p>{''.join('<p>'+esc(note)+'</p>' for note in verification_notes)}
<h2>Test results</h2><table><thead><tr><th>Suite</th><th>Passed</th><th>Failed</th><th>Skipped</th></tr></thead><tbody><tr><th>Backend xUnit</th><td>{passed}</td><td>{failed}</td><td>{skipped}</td></tr><tr><th>Frontend Vitest · {front_files} files</th><td>{front_passed}</td><td>{front_failed}</td><td>{front_skipped}</td></tr><tr><th>Total</th><td>{passed+front_passed}</td><td>{failed+front_failed}</td><td>{skipped+front_skipped}</td></tr></tbody></table>
<h2>Remaining work against 95%</h2><p>Backend requires at least {max(0,math.ceil(back['lines']['total']*.95)-back['lines']['covered']):,} more covered line entries. Frontend requires at least {max(0,math.ceil(front['lines']['total']*.95)-front['lines']['covered']):,} more covered lines. These are estimates against the current measured source totals, not estimates of test count or completion time.</p>
<h2>Measured code coverage</h2><table><thead><tr><th>Layer</th><th>Lines</th><th>Covered / measured lines</th><th>Branches</th></tr></thead><tbody>{rows}</tbody></table>
<p>Frontend statement coverage: {front['statements']['pct']:.2f}%. Frontend function coverage: {front['functions']['pct']:.2f}%. Equivalent backend statement/function metrics are not published by this collector.</p>
<h2>Requested frontend directories</h2><p>These rows match the coverage listing: pages/maintenance is shown separately from the immediate src/pages files. Each percentage must reach 80% to be green. All requested directory metrics green: <strong>{'Yes' if report['requested_directories_all_green'] else 'No'}</strong>.</p><table><thead><tr><th>Directory</th><th>Statements</th><th>Branches</th><th>Functions</th><th>Lines</th></tr></thead><tbody>{directory_rows}</tbody></table>
<h2>Green coverage audit</h2><p>Green requires at least {green_threshold}% in each measured metric, using Istanbul’s standard high watermark. All layer metrics green: <strong>{'Yes' if report['all_layer_metrics_green'] else 'No'}</strong>. The separate 95% line target remains in force. An unavailable backend statement/function metric is not a passing result.</p><table><thead><tr><th>Layer</th><th>Metric</th><th>Coverage</th><th>Status</th></tr></thead><tbody>{audit_rows}</tbody></table><p><a href="coverage-metric-audit.csv">Download the layer and source file metric audit</a>. Rows with no measured entries are not counted as green. Frontend files with every measured metric green: <strong>{report['frontend_file_green_summary']['green']} / {report['frontend_file_green_summary']['total']}</strong>.</p>
<h2>How to interpret the figures</h2><p>Line coverage measures executable line entries reached at least once. Branch coverage measures the decision paths reached. Passing tests alone do not prove correct behaviour for untested paths. The combined figure weights the covered counts by measured counts across both collectors; it is not an average of the two percentages. C# and JavaScript instrumentation count code differently, so the per-layer figures are the primary measures. Source lines shared by multiple C# classes can appear in multiple collector entries.</p>
<h2>Scope and exclusions</h2><p>The backend run includes the existing xUnit unit and component/API tests, many using EF Core InMemory. Microsoft Code Coverage measures CREMS.Api.dll. Generated obj/bin sources and EF migrations are excluded. The frontend run uses Vitest V8 across src/**/*.ts and src/**/*.tsx, excluding test files and type declarations. Neither browser/manual testing nor database schema coverage is included. New assertions exercise authentication, configuration, staff/customer access, booking decisions, pickup/return workflows, inspection controls, draft persistence, planning, reports, finance, assets, maintenance, background reminders and PDF generation. The 95% line coverage target has not yet been reached.</p>
<p>Skipped backend tests in this run: {skipped}.</p><ul>{''.join('<li>'+esc(x)+'</li>' for x in skips)}</ul><p>The SQL-enabled run exercises maintenance, organization setup, rental settings and fresh-database initialization using disposable databases. An additional disposable HTTP test host exercises application startup, documentation, routing, authorization and SQL notification read/retry behavior. These are integration tests, reported alongside unit/component tests. No production or training database records were changed by this test run.</p>
<h2>Coverage by source area</h2>{modules}
<h2>Largest gaps by uncovered line count</h2>{gaps}<p>Prioritize tests around pickup/return transitions, odometer validation, maintenance release, hire/bond settlement, approval decisions and authorization. For the frontend, test validation and state changes in the actual pages/components in addition to utility functions. Add browser end-to-end checks separately for complete customer and staff journeys.</p>
<h2>Reproduce the measurement</h2><p>From the repository root, after dependencies are installed:</p><pre>./scripts/test-coverage.sh</pre><p>Backend settings: tests/CREMS.Api.Tests/coverage.runsettings. The script refreshes raw coverage/test outputs and this consolidated report. Enable the documented SQL test flags to include database integration tests.</p>
<h2>Evidence files</h2><ul><li><a href="frontend/index.html">Frontend interactive coverage report</a></li><li><a href="backend/coverage.cobertura.xml">Backend Cobertura coverage</a></li><li><a href="backend/backend-tests.trx">Backend test results TRX</a></li><li><a href="frontend/coverage-summary.json">Frontend coverage JSON</a></li><li><a href="test-coverage-report.json">Consolidated metrics and test cases JSON</a></li><li><a href="test-case-results.csv">Individual test results CSV</a></li><li><a href="frontend-tests.json">Frontend test results JSON</a></li></ul>
<details><summary>Executed test cases and outcomes ({len(test_cases)})</summary><table class="test-cases"><thead><tr><th>Suite</th><th>Test case</th><th>Outcome</th><th>Duration (ms)</th></tr></thead><tbody>{case_rows}</tbody></table></details>
<details><summary>Complete file coverage breakdown</summary>{full}</details>
'''
css='''body{font:16px/1.55 system-ui,sans-serif;color:#162131;background:#fff;max-width:1120px;margin:40px auto;padding:0 28px}h1{font-size:38px;line-height:1.15;color:#10223b}h2{margin-top:36px;font-size:23px;color:#10223b}.eyebrow{letter-spacing:.12em;text-transform:uppercase;font-size:12px;color:#52657a}.lead{font-size:21px}table{border-collapse:collapse;width:100%;margin:18px 0;font-size:14px}th,td{border:1px solid #d9e0e8;padding:11px 13px;text-align:left;vertical-align:top;overflow-wrap:anywhere}thead th{background:#142d4e;color:white}tbody tr:nth-child(even){background:#f3f6fa}td:not(:first-child){white-space:nowrap}.test-cases td:nth-child(2){white-space:normal;overflow-wrap:anywhere}a{color:#1456a1}pre{background:#f2f5f9;padding:16px;overflow:auto}details{margin-top:30px}summary{font-size:20px;font-weight:650;cursor:pointer}@media print{body{margin:0;max-width:none;font-size:10pt}h1{font-size:24pt}table{font-size:8pt}tr{break-inside:avoid}details{display:none}a{color:inherit}}'''
(O/'CREMS_Test_Coverage_Report.html').write_text('<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>CREMS System Test Coverage Report</title><style>'+css+'</style><main>'+body+'</main></html>')
md=f'''# CREMS System Test Coverage Report\n\nMeasured {date}.\n\n| Layer | Tests passed | Failed | Skipped | Line coverage | Branch coverage |\n| --- | ---: | ---: | ---: | ---: | ---: |\n| Backend | {passed} | {failed} | {skipped} | {back['lines']['pct']:.2f}% | {back['branches']['pct']:.2f}% |\n| Frontend | {front_passed} | {front_failed} | {front_skipped} | {front['lines']['pct']:.2f}% | {front['branches']['pct']:.2f}% |\n\nIndicative combined line coverage: **{combined['lines']['pct']:.2f}%**, calculated from {combined['lines']['covered']:,} covered / {combined['lines']['total']:,} measured line entries. {passed+front_passed} tests passed; {failed+front_failed} failed. {skipped} backend tests were skipped. Browser end-to-end tests were not run. Additional behavior tests were added. The 95% target has not yet been reached.\n\nBackend application coverage excludes build-generated sources and EF migrations. The frontend includes all source TS/TSX except tests and type declarations. Combined coverage is a count-weighted cross-collector indicator; use per-layer figures as primary measures.\n\nRun `./scripts/test-coverage.sh` from the repository root to refresh measurements. See [the full report](CREMS_Test_Coverage_Report.html) for source-area/file results and evidence links.\n'''
md+='\n[Individual test results CSV](test-case-results.csv).\n'
md+='\n## Requested frontend directories\n\n| Directory | Statements | Branches | Functions | Lines |\n| --- | ---: | ---: | ---: | ---: |\n'
for directory,metrics in frontend_directories.items():
    md+='| '+directory+' | '+' | '.join(fmt(metrics[metric]['pct']) for metric in ['statements','branches','functions','lines'])+' |\n'
md+='\nAll requested directory metrics at or above the unchanged 80% green watermark: **'+('Yes' if report['requested_directories_all_green'] else 'No')+'**. Individual files may still have lower coverage.\n'
if verification_notes:md+='\nVerification notes:\n\n'+''.join('- '+note+'\n' for note in verification_notes)
(O/'CREMS_Test_Coverage_Report.md').write_text(md)
print(json.dumps({'tests':report['tests'],'coverage':report['coverage']},indent=2))
