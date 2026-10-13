import { useState } from 'react'
import AssessmentOutlined from '@mui/icons-material/AssessmentOutlined'
import DownloadOutlined from '@mui/icons-material/DownloadOutlined'
import { Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography } from '@mui/material'
import { downloadReportCsv, downloadStaffReportPdf, filterReport, reportLabel, reportValue, type StaffReport } from '../utils/reportDocument'

export function ReportButton({ reports, label = 'Generate report', loadReports }: { reports?: StaffReport[]; label?: string; loadReports?: () => Promise<StaffReport[]> }) {
  const [open, setOpen] = useState(false), [loaded, setLoaded] = useState<StaffReport[]>([]), [busy, setBusy] = useState(false), [error, setError] = useState('')
  const [selected, setSelected] = useState(0), [search, setSearch] = useState(''), [from, setFrom] = useState(''), [to, setTo] = useState(''), [page, setPage] = useState(0)
  const options = loadReports ? loaded : reports ?? []; const source = options[selected]
  const invalid = Boolean(from && to && from > to)
  const report = source ? filterReport(source, search, from, to) : null
  const maxRows = Math.max(0, ...(report?.sections.map(section => section.rows.length) ?? []))
  async function show() { setOpen(true); setError(''); setSelected(0); setPage(0); setSearch(''); setFrom(''); setTo(''); if (loadReports) { setBusy(true); setLoaded([]); try { setLoaded(await loadReports()) } catch { setError('This report could not be loaded. Close and try again.') } finally { setBusy(false) } } }
  async function download(pdf: boolean) { if (!report || invalid) return; setBusy(true); setError(''); try { const result = { ...report, coverage: `${report.coverage} Filters: ${from || 'Any start'} to ${to || 'Any end'}; search: ${search || 'All records'}.` }; if (pdf) await downloadStaffReportPdf(result); else downloadReportCsv(result) } catch { setError('The download could not be generated. Please try again.') } finally { setBusy(false) } }
  return <><Button variant="outlined" startIcon={<AssessmentOutlined />} onClick={() => void show()}>{label}</Button><Dialog open={open} onClose={() => !busy && setOpen(false)} fullWidth maxWidth="lg">
    <DialogTitle sx={{ borderBottom: 1, borderColor: 'divider', pb: 2 }}><Typography variant="overline" color="text.secondary">REPORT WORKSPACE</Typography><Typography variant="h5" fontWeight={800}>{source?.title || 'Generate report'}</Typography></DialogTitle>
    <DialogContent sx={{ pt: '24px !important' }}><Stack spacing={2}>
      {error && <Alert severity="error">{error}</Alert>}{busy && <Alert severity="info">Preparing report…</Alert>}
      {source && <><Typography color="text.secondary">{source.description}</Typography><Alert severity="info" variant="outlined">{source.coverage}</Alert>
      <Stack direction={{ xs: 'column', md: 'row' }} gap={2}>
        <TextField select size="small" label="Report" value={selected} onChange={event => { setSelected(Number(event.target.value)); setPage(0) }} sx={{ width: { xs: '100%', md: 240 }, flexShrink: 0 }}>{options.map((item, index) => <MenuItem key={item.title} value={index}>{item.title}</MenuItem>)}</TextField>
        <TextField size="small" label="Search report" value={search} onChange={event => { setSearch(event.target.value); setPage(0) }} sx={{ flex: { md: '1 1 180px' }, minWidth: 0, width: { xs: '100%', md: 'auto' } }} />
        <TextField size="small" type="date" label="From" slotProps={{ inputLabel: { shrink: true } }} value={from} onChange={event => { setFrom(event.target.value); setPage(0) }} sx={{ width: { xs: '100%', md: 190 }, flexShrink: 0 }} />
        <TextField size="small" type="date" label="To" slotProps={{ inputLabel: { shrink: true } }} value={to} onChange={event => { setTo(event.target.value); setPage(0) }} sx={{ width: { xs: '100%', md: 190 }, flexShrink: 0 }} />
      </Stack>
      <Typography variant="caption" color="text.secondary">Date filters apply to the Date column. Undated summary sections remain visible. Downloads include all matching rows.</Typography>{invalid && <Alert severity="error">The end date must be on or after the start date.</Alert>}
      {report?.sections.map(section => <Box key={section.title} sx={{ border: 1, borderColor: 'divider', borderRadius: 2, overflow: 'hidden' }}><Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ p: 2, bgcolor: 'grey.50' }}><Typography fontWeight={750}>{section.title}</Typography><Chip size="small" label={`${section.rows.length} records`} /></Stack><Box sx={{ overflowX: 'auto' }}><Table size="small"><TableHead><TableRow>{section.columns.map(key => <TableCell key={key}>{reportLabel(key)}</TableCell>)}</TableRow></TableHead><TableBody>{section.rows.slice(page * 10, page * 10 + 10).map((row, index) => <TableRow key={index}>{section.columns.map(key => <TableCell key={key} sx={{ verticalAlign: 'top', minWidth: 100, maxWidth: 360, overflowWrap: 'anywhere' }}>{reportValue(row[key])}</TableCell>)}</TableRow>)}{section.rows.length <= page * 10 && <TableRow><TableCell colSpan={section.columns.length}>No records on this page.</TableCell></TableRow>}</TableBody></Table></Box></Box>)}
      <Stack direction="row" gap={1} justifyContent="flex-end" alignItems="center"><Typography variant="caption">Page {page + 1} of {Math.max(1, Math.ceil(maxRows / 10))}</Typography><Button variant="outlined" disabled={!page} onClick={() => setPage(page - 1)}>Previous</Button><Button variant="outlined" disabled={(page + 1) * 10 >= maxRows} onClick={() => setPage(page + 1)}>Next</Button></Stack></>}
    </Stack></DialogContent><DialogActions sx={{ p: 3, borderTop: 1, borderColor: 'divider' }}><Button disabled={busy} onClick={() => setOpen(false)}>Close</Button><Box sx={{ flex: 1 }} /><Button variant="outlined" disabled={busy || !report || invalid} onClick={() => void download(false)}>Export CSV</Button><Button variant="contained" startIcon={<DownloadOutlined />} disabled={busy || !report || invalid} onClick={() => void download(true)}>Download PDF</Button></DialogActions>
  </Dialog></>
}
