@'
from pathlib import Path
import subprocess
p=Path('src/crems-web/src/pages/maintenance/MaintenanceWorkspace.tsx')
s=subprocess.check_output(['git','show','HEAD:src/crems-web/src/pages/maintenance/MaintenanceWorkspace.tsx']).decode('cp1252')
s=s.replace('type FormEvent','type ChangeEvent, type FormEvent')
s=s.replace("  const formElement = useRef", "  const openedJob = useRef<string | null>(null)\n  const formElement = useRef")
s=s.replace('setDetail(record.data); setForm(jobForm(record.data.job));', "setDetail(record.data); setForm(jobForm(record.data.job));\n      if (openedJob.current !== id) { setTab(record.data.job.releasedAt ? 'History' : closed(record.data.job) ? 'Safety' : 'Details'); openedJob.current = id }")
s=s.replace("setTab('Work')", "setTab('Details')")
s=s.replace("await loadDetail(jobId); onSaved(`${job?.jobNumber} saved.", "await loadDetail(jobId); if (['Completed', 'Cancelled'].includes(nextStatus)) setTab('Safety'); else if (job && closed(job)) setTab('Details'); onSaved(`${job?.jobNumber} saved.")
s=s.replace('notes: safetyNotes,', "notes: safetyNotes.trim() || 'All applicable safety checks passed.',")
s=s.replace('lg: 1000','lg: 820')
a=s.index("          {!job && !['Safety'");b=s.index('\n',a);s=s[:a]+s[b:]
a=s.index('        <Tabs');b=s.index('\n',a)
s=s[:a]+'''        {job && <><Box sx={{ px: 3, py: 1.5, bgcolor: 'background.paper', borderTop: 1, borderColor: 'divider' }}><Typography variant="body2" fontWeight={600}>{job.releasedAt ? 'Finished · Safety check passed' : closed(job) ? 'Next: verify safety before returning to hire' : job.status === 'Open' ? 'Next: plan and start the work' : job.status === 'WaitingForParts' ? 'Waiting for parts · Resume when ready' : 'Next: record repairs, then complete the work'}</Typography><Typography variant="caption" color="text.secondary">Plan → Repair → Safety check → Service history</Typography></B<truncated omitted_approx_tokens=