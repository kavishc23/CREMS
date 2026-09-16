import { useMemo, useState } from 'react'
import axios from 'axios'
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  FormControl,
  Grid,
  InputLabel,
  MenuItem,
  Select,
  Tab,
  Tabs,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { api } from '../api/client'

type Approval = {
  id: string
  canDecide?: boolean
  requestNumber: string
  status: string
  type: string
  entityType: string
  amount: number
  reason: string
  createdAt: string
  currentStage: number
  totalStages: number
  decisionNote: string | null
  decidedAt: string | null
  decidedByName: string | null
  stageDecisions: { stageNumber: number; stageName: string; assignedRole: string | null; status: string; decisionNote: string | null; decidedAt: string | null; decidedByName: string | null }[]
}

function formatRole(value: string | null) {
  if (!value) return 'Not assigned'
  return value.replace(/([a-z])([A-Z])/g, '$1 $2')
}

function formatAuditDate(value: string | null) {
  if (!value) return 'Awaiting decision'
  return new Intl.DateTimeFormat('en-FJ', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
}

export function ApprovalWorkspace({ approvals, reload }: { approvals: Approval[]; reload: () => Promise<void> }) {
  const [saving, setSaving] = useState('')
  const [error, setError] = useState('')
  const [dialog, setDialog] = useState<{ item: Approval; approved: boolean } | null>(null)
  const [decisionNote, setDecisionNote] = useState('')
  const [statusFilter, setStatusFilter] = useState('All')

  const orderedApprovals = useMemo(
    () => [...approvals].sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()),
    [approvals],
  )
  const counts = useMemo(() => ({
    all: approvals.length,
    pending: approvals.filter((item) => item.status === 'Pending').length,
    approved: approvals.filter((item) => item.status === 'Approved').length,
    rejected: approvals.filter((item) => item.status === 'Rejected').length,
  }), [approvals])
  const visibleApprovals = statusFilter === 'All'
    ? orderedApprovals
    : orderedApprovals.filter((item) => item.status === statusFilter)

  async function decide(item: Approval, approved: boolean) {
    const note = decisionNote.trim() || (approved ? 'Approved after manager review.' : 'Rejected after manager review.')
    setSaving(item.id)
    setError('')
    try {
      await api.post(`/corporate-operations/approvals/${item.id}/decision`, { approved, note })
      setDialog(null)
      setDecisionNote('')
      await reload()
    } catch (e) {
      const data = axios.isAxiosError(e) ? e.response?.data : undefined
      setError(data?.message ?? 'Only an authorised manager can decide this request.')
    } finally {
      setSaving('')
    }
  }

  return (
    <>
      <Dialog open={Boolean(dialog)} onClose={() => setDialog(null)} maxWidth="sm" fullWidth>
        <DialogTitle>
          {dialog?.approved ? 'Approve approval request' : 'Reject approval request'}
        </DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <Typography variant="body2" color="text.secondary">
              {dialog?.item.requestNumber} · {dialog?.item.type}
            </Typography>
            <TextField
              autoFocus
              multiline
              minRows={4}
              fullWidth
              label={dialog?.approved ? 'Approval note' : 'Rejection reason'}
              value={decisionNote}
              onChange={(event) => setDecisionNote(event.target.value)}
              placeholder={dialog?.approved ? 'Approved after branch review and risk check.' : 'Please explain why this request is being rejected.'}
            />
          </Stack>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2.5 }}>
          <Button onClick={() => setDialog(null)} disabled={saving === dialog?.item.id}>Cancel</Button>
          <Button
            color={dialog?.approved ? 'success' : 'error'}
            variant="contained"
            disabled={saving === dialog?.item.id}
            onClick={() => {
              if (!dialog) return
              void decide(dialog.item, dialog.approved)
            }}
          >
            {dialog
              ? dialog.approved
                ? (saving === dialog.item.id ? 'Approving…' : 'Approve')
                : (saving === dialog.item.id ? 'Rejecting…' : 'Reject')
              : 'Confirm'}
          </Button>
        </DialogActions>
      </Dialog>

      <Stack spacing={2.5}>
        {error && <Alert severity="error">{error}</Alert>}

        <Card sx={{ borderRadius: 3, color: '#fff', background: 'linear-gradient(120deg, #123047 0%, #1e5260 58%, #28746b 100%)' }}>
          <CardContent sx={{ p: { xs: 2.5, md: 3 } }}>
            <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" gap={2}>
              <Box>
                <Typography variant="overline" sx={{ letterSpacing: 1.2, color: 'rgba(255,255,255,.72)' }}>CONTROLLED WORKFLOW</Typography>
                <Typography variant="h5" fontWeight={850}>Approval queue</Typography>
                <Typography sx={{ mt: .5, color: 'rgba(255,255,255,.78)' }}>
                  Review decisions by status and keep every manager action documented.
                </Typography>
              </Box>
              <Chip label={`${counts.pending} need action`} sx={{ alignSelf: { xs: 'flex-start', md: 'center' }, color: '#123047', backgroundColor: '#d7f2e5', fontWeight: 800 }} />
            </Stack>
          </CardContent>
        </Card>

        <Grid container spacing={1.5}>
          {[
            { label: 'Total requests', value: counts.all, color: 'secondary.main' },
            { label: 'Pending action', value: counts.pending, color: 'warning.main' },
            { label: 'Approved', value: counts.approved, color: 'success.main' },
            { label: 'Rejected', value: counts.rejected, color: 'error.main' },
          ].map((metric) => (
            <Grid key={metric.label} size={{ xs: 6, md: 3 }}>
              <Card variant="outlined" sx={{ height: '100%', borderTop: 3, borderTopColor: metric.color, borderRadius: 2 }}>
                <CardContent sx={{ p: 2 }}>
                  <Typography variant="caption" color="text.secondary">{metric.label}</Typography>
                  <Typography variant="h4" fontWeight={850} sx={{ mt: .25 }}>{metric.value}</Typography>
                </CardContent>
              </Card>
            </Grid>
          ))}
        </Grid>

        <Card variant="outlined" sx={{ borderRadius: 2 }}>
          <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ sm: 'center' }} gap={1} sx={{ px: 1.5, py: 1 }}>
            <Tabs value={statusFilter} onChange={(_, value: string) => setStatusFilter(value)} variant="scrollable" scrollButtons="auto">
              <Tab value="All" label={`All (${counts.all})`} />
              <Tab value="Pending" label={`Pending (${counts.pending})`} />
              <Tab value="Approved" label={`Approved (${counts.approved})`} />
              <Tab value="Rejected" label={`Rejected (${counts.rejected})`} />
            </Tabs>
            <FormControl size="small" sx={{ minWidth: 150, mr: 1 }}>
              <InputLabel id="approval-status-label">Show status</InputLabel>
              <Select labelId="approval-status-label" value={statusFilter} label="Show status" onChange={(event) => setStatusFilter(event.target.value)}>
                <MenuItem value="All">All statuses</MenuItem>
                <MenuItem value="Pending">Pending</MenuItem>
                <MenuItem value="Approved">Approved</MenuItem>
                <MenuItem value="Rejected">Rejected</MenuItem>
              </Select>
            </FormControl>
          </Stack>
        </Card>

        <Grid container spacing={2.5}>
          {visibleApprovals.map((item) => {
            const stage = item.stageDecisions?.find((step) => step.stageNumber === item.currentStage)
            const isPending = item.status === 'Pending'
            const canDecide = isPending && item.canDecide === true

            return (
              <Grid key={item.id} size={{ xs: 12, md: 6 }}>
                <Card variant="outlined" sx={{ height: '100%', borderRadius: 3, borderColor: 'divider', background: 'linear-gradient(180deg, rgba(255,255,255,0.98), rgba(248,250,252,0.95))' }}>
                  <CardContent sx={{ p: 2.5 }}>
                    <Stack spacing={2}>
                      <Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={2}>
                        <Box>
                          <Typography variant="overline" color="text.secondary" sx={{ letterSpacing: 1.1 }}>{item.requestNumber}</Typography>
                          <Typography variant="h6" fontWeight={800} sx={{ lineHeight: 1.25 }}>{item.type}</Typography>
                        </Box>
                        <Chip
                          label={item.status}
                          color={item.status === 'Approved' ? 'success' : item.status === 'Rejected' ? 'error' : 'warning'}
                          variant={item.status === 'Pending' ? 'filled' : 'outlined'}
                          size="small"
                        />
                      </Stack>

                      <Stack direction="row" justifyContent="space-between" alignItems="center" spacing={1}>
                        <Typography variant="body2" color="text.secondary">Amount</Typography>
                        <Typography variant="h6" fontWeight={800}>FJD {item.amount.toFixed(2)}</Typography>
                      </Stack>

                      <Divider />

                      <Box>
                        <Typography variant="body2" color="text.secondary" sx={{ mb: 0.75 }}>Reason</Typography>
                        <Typography variant="body2" sx={{ lineHeight: 1.6 }}>{item.reason}</Typography>
                      </Box>

                      <Box sx={{ p: 1.5, borderRadius: 2, backgroundColor: '#f8fafc', border: '1px solid #e5e7eb' }}>
                        <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1}>
                          <Typography variant="caption" color="text.secondary">Current stage</Typography>
                          <Chip label={`Phase ${item.currentStage || 1} / ${item.totalStages || 1}`} size="small" variant="outlined" />
                        </Stack>
                        <Typography variant="subtitle2" fontWeight={800} sx={{ mt: 1 }}>{stage?.stageName || 'Manager approval'}</Typography>
                        <Typography variant="caption" color="text.secondary">
                          Assigned to {formatRole(stage?.assignedRole ?? null)}
                        </Typography>
                      </Box>

                      <Box>
                        <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 1 }}>
                          <Typography variant="body2" fontWeight={800}>Audit timeline</Typography>
                          <Typography variant="caption" color="text.secondary">{item.stageDecisions?.length || 0} stage{item.stageDecisions?.length === 1 ? '' : 's'}</Typography>
                        </Stack>
                        <Stack spacing={1.25} sx={{ pl: 1.5, borderLeft: '2px solid', borderColor: 'divider' }}>
                          {(item.stageDecisions || []).map((step) => {
                            const decided = step.status !== 'Pending'
                            const rejected = step.status === 'Rejected'
                            return (
                              <Box key={step.stageNumber} sx={{ position: 'relative' }}>
                                <Box sx={{ position: 'absolute', left: -21, top: 4, width: 10, height: 10, borderRadius: '50%', bgcolor: rejected ? 'error.main' : decided ? 'success.main' : 'warning.main', border: '2px solid', borderColor: 'background.paper' }} />
                                <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" gap={.5}>
                                  <Box>
                                    <Typography variant="body2" fontWeight={750}>{step.stageName}</Typography>
                                    <Typography variant="caption" color="text.secondary">
                                      {decided ? `${step.status} by ${step.decidedByName || 'Staff member'}` : `Assigned to ${formatRole(step.assignedRole)}`}
                                    </Typography>
                                  </Box>
                                  <Typography variant="caption" color="text.secondary" sx={{ whiteSpace: 'nowrap' }}>{formatAuditDate(step.decidedAt)}</Typography>
                                </Stack>
                                {step.decisionNote && <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: .35, fontStyle: 'italic' }}>&ldquo;{step.decisionNote}&rdquo;</Typography>}
                              </Box>
                            )
                          })}
                          {(!item.stageDecisions || item.stageDecisions.length === 0) && <Typography variant="caption" color="text.secondary">No stage history has been recorded.</Typography>}
                        </Stack>
                        {item.decidedAt && <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>Final decision by {item.decidedByName || 'Staff member'} · {formatAuditDate(item.decidedAt)}</Typography>}
                      </Box>

                      {canDecide && (
                        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.25}>
                          <Button
                            color="error"
                            variant="outlined"
                            fullWidth
                            disabled={saving === item.id}
                            onClick={() => {
                              setDecisionNote('')
                              setDialog({ item, approved: false })
                            }}
                          >
                            Reject
                          </Button>
                          <Button
                            variant="contained"
                            fullWidth
                            disabled={saving === item.id}
                            onClick={() => {
                              setDecisionNote('')
                              setDialog({ item, approved: true })
                            }}
                          >
                            Approve
                          </Button>
                        </Stack>
                      )}
                    </Stack>
                  </CardContent>
                </Card>
              </Grid>
            )
          })}
        </Grid>

        {visibleApprovals.length === 0 && (
          <Alert severity={statusFilter === 'Pending' ? 'success' : 'info'}>
            {statusFilter === 'Pending' ? 'No approval decisions are waiting for action.' : `No ${statusFilter.toLowerCase()} approval requests to show.`}
          </Alert>
        )}
      </Stack>
    </>
  )
}
