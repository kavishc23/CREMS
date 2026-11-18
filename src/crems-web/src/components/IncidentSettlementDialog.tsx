import { InlineIssueNavigation } from './InlineValidation'
import { FormError } from './FormError'
import { IncidentAttachments, incidentEvidence } from './IncidentAttachments'
import { IncidentInspectionSummary } from './IncidentInspectionSummary'
import { useEffect, useState } from 'react'
import axios from 'axios'
import {
  Alert, Box, Button, Checkbox, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle,
  Divider, FormControlLabel, MenuItem, Stack, TextField, Typography,
} from '@mui/material'
import { api } from '../api/client'
import { downloadInvoicePdf } from '../utils/financialDocumentPdf'
import { roundMoney } from '../utils/rentalPaymentValidation'

type Cost = {
  id: string; description: string; amount: number; decision: 'Pending' | 'Customer' | 'Other'
  responsibility: string; evidence: string; isTaxable: boolean; billed: boolean
}
type Attachment = { name: string; dataUrl: string }
type Settlement = {
  state: {
    revision: number; costs: Cost[]; attachments: Attachment[]; basis: string | null; dueDate: string | null
    outcome: string | null; disputed: boolean; issuedAt: string | null; issuedBy: string | null; closedAt: string | null
    followUpNotes?: string | null
  }
  owner: string | null; ownerId: string | null; canApprove: boolean
  accountChangedSinceIssue: boolean
  assignees: { id: string; name: string; role: string }[]
  followUpAt: string | null; email: string | null; taxInclusive: boolean; taxRate: number
  lines: { description: string; amount: number }[]
  incidents: { evidenceJson?:string|null; inspectionJson?:string|null; outcome?:string|null; incidentNumber: string; status: string; description: string; policeReference: string | null; insuranceClaimNumber: string | null }[]
  total: number; paid: number; balance: number
  bondAmountHeld: number; bondDeductionAmount: number; bondSettledAt: string | null; bondRefundAmount: number
  invoice: { id: string; invoiceNumber: string } | null
}
type Form = {
  followUpDate: string; assignedUserId: string; followUpNotes: string; costs: Cost[]; attachments: Attachment[]
  basis: string; dueDate: string; disputed: boolean; outcome: string
  invoiceCredit: string; invoiceCreditReason: string
  settleBond: boolean; bondCredit: string; bondReason: string; bondRefundMethod: string; bondRefundReference: string
}

const methods = { Cash: 'Cash', Card: 'Card', BankTransfer: 'Bank transfer', MobileMoney: 'Mobile money' }
const money = (amount: number) => `FJD ${amount.toFixed(2)}`
const fijiDate = (value: string | null) => new Intl.DateTimeFormat('en-CA', { timeZone: 'Pacific/Fiji', year: 'numeric', month: '2-digit', day: '2-digit' }).format(value ? new Date(value) : new Date())

function errorMessage(error: unknown) {
  const data = axios.isAxiosError(error) ? error.response?.data : undefined
  return data?.message ?? (data?.errors ? Object.values(data.errors).flat().join(' ') : 'The settlement could not be updated. Try again.')
}

export function IncidentSettlementDialog({ bookingId, bookingNumber, onClose, onUpdated }: {
  bookingId: string; bookingNumber: string; onClose: () => void; onUpdated: () => void
}) {
  const url = `/rentals/${bookingId}/incident-settlement`
  const [data, setData] = useState<Settlement>()
  const [form, setForm] = useState<Form>()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [attempted, setAttempted] = useState<'save' | 'issue' | 'close' | 'follow-up' | null>(null)
  const [notice, setNotice] = useState('')
  const [payment, setPayment] = useState({ amount: '', method: 'BankTransfer', receiptNumber: '' })
  const [credit, setCredit] = useState<{ total: string; reason: string } | null>(null)

  function receive(value: Settlement) {
    setData(value)
    setForm({
      followUpDate: fijiDate(value.followUpAt), assignedUserId: value.ownerId ?? '', followUpNotes: value.state.followUpNotes ?? '', costs: value.state.costs,
      attachments: value.state.attachments, basis: value.state.basis ?? '', dueDate: value.state.dueDate ?? '',
      disputed: value.state.disputed, outcome: value.state.outcome ?? '', settleBond: false,
      invoiceCredit: '', invoiceCreditReason: '',
      bondCredit: String(value.bondDeductionAmount), bondReason: '', bondRefundMethod: 'BankTransfer', bondRefundReference: '',
    })
  }

  useEffect(() => {
    const controller = new AbortController()
    api.get<Settlement>(url, { signal: controller.signal }).then(result => receive(result.data))
      .catch(reason => { if (!controller.signal.aborted) setError(errorMessage(reason)) })
    return () => controller.abort()
  }, [url])

  function edit(values: Partial<Form>) {
    setForm(current => current && { ...current, ...values })
  }

  function editCost(id: string, values: Partial<Cost>) {
    setForm(current => current && { ...current, costs: current.costs.map(cost => cost.id === id ? { ...cost, ...values } : cost) })
  }

  async function refreshAccount() {
    const { data: updated } = await api.get<Settlement>(url)
    setData(current => current && { ...updated, state: current.state })
  }

  async function attach(files: FileList | null) {
    if (!files?.length || !form) return
    const selected = Array.from(files)
    if (form.attachments.length + selected.length > 5 || selected.some(file => file.size > 2 * 1024 * 1024)) {
      setError('Attach up to five files, at most 2 MB each.')
      return
    }
    setError('')
    setBusy(true)
    try {
      const attachments = await Promise.all(selected.map(file => new Promise<Attachment>((resolve, reject) => {
        const reader = new FileReader()
        reader.onload = () => resolve({ name: file.name, dataUrl: String(reader.result) })
        reader.onerror = reject
        reader.readAsDataURL(file)
      })))
      edit({ attachments: [...form.attachments, ...attachments] })
    } catch {
      setError('An evidence file could not be read.')
    } finally {
      setBusy(false)
    }
  }

  async function save(action: 'save' | 'issue' | 'close') {
    if (!data || !form || busy) return
    setAttempted(action)
    setError('')
    if (settlementIssues(action).some(issue => issue.message)) return
    setBusy(true)
    setNotice('')
    try {
      const { data: result } = await api.put<{ invoiceNumber: string | null; emailQueued: boolean }>(url, {
        ...form, assignedUserId: form.assignedUserId === data.ownerId ? null : form.assignedUserId || null,
        dueDate: paymentDue ? form.dueDate || null : null, followUpDate: form.followUpDate || null, revision: data.state.revision,
        issueInvoice: action === 'issue', closeCase: action === 'close', settleBond: action === 'issue' && form.settleBond,
        bondCredit: Number(form.bondCredit), invoiceCredit: action === 'issue' ? Number(form.invoiceCredit) : 0,
      })
      onUpdated()
      receive((await api.get<Settlement>(url)).data)
      setAttempted(null)
      setNotice(action === 'issue' ? `Invoice ${result.invoiceNumber} issued. ${result.emailQueued ? 'Customer email queued.' : 'Download the invoice to deliver it to the customer.'}` :
        action === 'close' ? 'Settlement case closed.' : 'Case review saved.')
    } catch (reason) {
      setError(errorMessage(reason))
    } finally {
      setBusy(false)
    }
  }

  async function saveFollowUp() {
    if (!data || !form || busy) return
    setBusy(true)
    setError('')
    setNotice('')
    try {
      await api.patch(`${url}/follow-up`, { revision: data.state.revision, followUpDate: form.followUpDate, notes: form.followUpNotes })
      receive((await api.get<Settlement>(url)).data)
      onUpdated()
      setNotice('Follow-up saved.')
    } catch (reason) {
      setError(errorMessage(reason))
    } finally {
      setBusy(false)
    }
  }

  async function recordPayment() {
    setBusy(true)
    setNotice('')
    setError('')
    try {
      await api.post(`/rentals/${bookingId}/lifecycle/payments`, { ...payment, amount: Number(payment.amount), type: 'RentalCharge' })
      setPayment(current => ({ ...current, amount: '', receiptNumber: '' }))
      onUpdated()
      await refreshAccount()
      setNotice('Payment recorded.')
    } catch (reason) {
      setError(errorMessage(reason))
    } finally {
      setBusy(false)
    }
  }

  async function download(id: string) {
    try {
      const { data: document } = await api.get<Parameters<typeof downloadInvoicePdf>[0]>(`/finance/invoices/${id}/document`)
      await downloadInvoicePdf({ ...document, id: undefined })
    } catch (reason) {
      setError(errorMessage(reason))
    }
  }

  async function applyCredit() {
    if (!credit || !data?.invoice || busy) return
    setBusy(true)
    setNotice('')
    setError('')
    try {
      await api.post(`/finance/invoices/${data.invoice.id}/credit-notes`, { total: Number(credit.total), reason: credit.reason.trim() })
      setCredit(null)
      onUpdated()
      await refreshAccount()
      setNotice('Credit note applied. Issue the revised settlement to notify the customer.')
    } catch (reason) {
      setError(errorMessage(reason))
    } finally {
      setBusy(false)
    }
  }

  const closed = Boolean(data?.state.closedAt)
  const pending = form?.costs.some(cost => cost.decision === 'Pending' || cost.decision === 'Customer' && !cost.billed)
  const approved = form?.costs.filter(cost => cost.decision === 'Customer' && !cost.billed) ?? []
  const taxableCents = approved.reduce((sum, cost) => sum + (cost.isTaxable ? Math.round(cost.amount * 100) : 0), 0)
  const addedTax = data && !data.taxInclusive ? Math.round(taxableCents * Math.round(data.taxRate * 100) / 10_000) / 100 : 0
  const addedCharges = approved.reduce((sum, cost) => sum + cost.amount, 0)
  const paymentDue = Boolean(data && form && roundMoney(data.balance + addedCharges + addedTax -
    Number(form.invoiceCredit) - (form.settleBond ? Number(form.bondCredit) : 0)) > 0)
  const today = fijiDate(null)
  const canIssue = data && form && (!data.state.issuedAt || data.accountChangedSinceIssue || form.settleBond ||
    approved.length > 0 || Boolean(paymentDue && form.dueDate && form.dueDate !== data.state.dueDate))
  function settlementIssues(action: 'save' | 'issue' | 'close' | 'follow-up') {
    if (!form) return []
    const credit = Number(form.invoiceCredit)
    const creditLimit = Math.max(0, roundMoney((data?.balance ?? 0) + addedCharges + addedTax))
    const reviewingCredit = action === 'issue' && !data?.state.issuedAt
    return [
      { target: 'settlement-owner', message: action !== 'follow-up' && action !== 'close' && !form.assignedUserId ? 'Assign a staff member to this case.' : '' },
      { target: 'settlement-follow-up-date', message: action !== 'close' && !form.followUpDate ? 'Choose a follow-up date.' : '' },
      { target: 'settlement-follow-up-notes', message: action === 'follow-up' && !form.followUpNotes.trim() ? 'Enter the investigation or follow-up notes.' : '' },
      { target: 'settlement-closure-outcome', message: action === 'close' && !form.outcome.trim() ? 'Record the closure outcome.' : '' },
      { target: 'settlement-basis', message: action === 'issue' && form.basis.trim().length < 10 ? 'Describe the agreement or settlement basis in at least 10 characters.' : '' },
      { target: 'settlement-due-date', message: action === 'issue' && paymentDue && form.dueDate < today ? 'Choose a payment due date of today or later.' : '' },
      { target: 'settlement-invoice-credit', message: reviewingCredit && (!Number.isFinite(credit) || credit < 0 || credit > creditLimit || credit !== roundMoney(credit)) ? 'Enter an invoice credit from FJD 0.00 to ' + money(creditLimit) + ' using up to two decimal places.' : '' },
      { target: 'settlement-credit-authority', message: reviewingCredit && credit > 0 && !form.invoiceCreditReason.trim() ? 'Record who authorized the credit and why.' : '' },
    ]
  }
  function requestFollowUp() {
    setAttempted('follow-up')
    setError('')
    if (settlementIssues('follow-up').some(issue => issue.message)) return
    void saveFollowUp()
  }
  return <Dialog open onClose={busy ? undefined : onClose} fullWidth maxWidth="md">
    <DialogTitle>Incident settlement · {bookingNumber}</DialogTitle>
    <DialogContent dividers>
      <Stack spacing={2}>
        {error && <FormError>{error}</FormError>}
        {notice && <Alert severity="success">{notice}</Alert>}
        {!data && !error && <CircularProgress size={28} />}
        {data && form && <>
          <Typography fontWeight={700}>{closed ? 'Case closed' : form.disputed ? 'Disputed' : data.state.issuedAt ? 'Settlement issued' : 'Settlement pending'}</Typography>
          <Typography>Case owner {data.owner ?? 'Unassigned'}{data.state.issuedBy && ` · Last issued by ${data.state.issuedBy}`}</Typography>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            {data.canApprove && !closed && <TextField select fullWidth id="settlement-owner" label="Assign to" value={form.assignedUserId} onChange={event => edit({ assignedUserId: event.target.value })} disabled={busy}>
              {!data.ownerId && <MenuItem value="" disabled>Select a case owner</MenuItem>}
              {data.ownerId && !data.assignees.some(user => user.id === data.ownerId) && <MenuItem value={data.ownerId}>{data.owner ?? 'Current case owner'}</MenuItem>}
              {data.assignees.filter((user, index, users) => users.findIndex(item => item.id === user.id) === index).map(user =>
                <MenuItem key={user.id} value={user.id}>{user.name} · {user.role.replace(/([a-z])([A-Z])/g, '$1 $2')}</MenuItem>)}
            </TextField>}
            <TextField id="settlement-follow-up-date" label="Follow-up date" type="date" value={form.followUpDate} onChange={event => edit({ followUpDate: event.target.value })} slotProps={{ inputLabel: { shrink: true } }} disabled={closed || busy} />
          </Stack>
          {data.incidents.map(incident => <Box key={incident.incidentNumber}>
            <Typography fontWeight={700}>{incident.incidentNumber} · {incident.status}</Typography>
            <Typography variant="body2">{incident.description}</Typography><IncidentAttachments value={incidentEvidence(incident.evidenceJson)}/><IncidentInspectionSummary json={incident.inspectionJson} outcome={incident.outcome}/>
            {(incident.policeReference || incident.insuranceClaimNumber) && <Typography variant="body2">
              {incident.policeReference && `Police reference ${incident.policeReference} `}{incident.insuranceClaimNumber && `Insurance claim ${incident.insuranceClaimNumber}`}
            </Typography>}
          </Box>)}
          {!data.canApprove && <TextField fullWidth multiline id="settlement-follow-up-notes" label="Investigation and follow-up notes" value={form.followUpNotes}
            onChange={event => edit({ followUpNotes: event.target.value })} disabled={busy || closed} slotProps={{ htmlInput: { maxLength: 1000 } }} />}
          {data.canApprove && data.state.followUpNotes && <Box>
            <Typography fontWeight={700}>Investigation and follow-up notes</Typography>
            <Typography sx={{ whiteSpace: 'pre-wrap' }}>{data.state.followUpNotes}</Typography>
          </Box>}
          {data.canApprove && <>
          <Divider />
          <Typography variant="h6">Hire account</Typography>
          {data.lines.map((line, index) => <Typography key={index} variant="body2">{line.description} · {money(line.amount)}</Typography>)}
          <Typography fontWeight={700}>Total {money(data.total)} · Paid or applied {money(data.paid)} · Balance {money(data.balance)}</Typography>
          {data.invoice && <Button sx={{ alignSelf: 'flex-start' }} onClick={() => void download(data.invoice!.id)}>Download {data.invoice.invoiceNumber}</Button>}
          <TextField fullWidth multiline id="settlement-basis" label="Agreement clause or agreed settlement" value={form.basis} onChange={event => edit({ basis: event.target.value })} disabled={closed || busy} slotProps={{ htmlInput: { maxLength: 1000 } }} />
          {paymentDue && <TextField required id="settlement-due-date" label="Payment due date" type="date" value={form.dueDate} onChange={event => edit({ dueDate: event.target.value })}
            slotProps={{ inputLabel: { shrink: true }, htmlInput: { min: today } }} disabled={closed || busy} />}
          {!closed && !data.state.issuedAt && <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
            <TextField id="settlement-invoice-credit" label="Agreed invoice credit (FJD)" type="number" value={form.invoiceCredit} onChange={event => edit({ invoiceCredit: event.target.value })} slotProps={{ htmlInput: { min: 0, step: '0.01' } }} />
            {Number(form.invoiceCredit) > 0 && <TextField id="settlement-credit-authority" fullWidth label="Reason and authority for credit" value={form.invoiceCreditReason} onChange={event => edit({ invoiceCreditReason: event.target.value })} slotProps={{ htmlInput: { maxLength: 500 } }} />}
          </Stack>}
          <Divider />
          <Typography variant="h6">Costs and liability</Typography>
          {form.costs.map(cost => <Stack key={cost.id} spacing={1.5} component="fieldset" sx={{ m: 0, p: 2, border: 1, borderColor: 'divider', borderRadius: 1, minWidth: 0 }} disabled={closed || cost.billed || busy}>
            <Typography component="legend">{cost.billed ? 'Invoiced charge' : 'Incident cost'}</Typography>
            <TextField label="Cost description" value={cost.description} onChange={event => editCost(cost.id, { description: event.target.value })} slotProps={{ htmlInput: { maxLength: 240 } }} />
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
              <TextField label={data.taxInclusive ? 'Amount incl. VAT (FJD)' : 'Amount before VAT (FJD)'} type="number" value={cost.amount || ''} onChange={event => editCost(cost.id, { amount: Number(event.target.value) })} slotProps={{ htmlInput: { min: 0, step: '0.01' } }} />
              <TextField select fullWidth label="Liability decision" value={cost.decision} onChange={event => editCost(cost.id, { decision: event.target.value as Cost['decision'] })} disabled={closed || cost.billed || busy}>
                <MenuItem value="Pending">Pending investigation</MenuItem><MenuItem value="Customer">Charge customer</MenuItem><MenuItem value="Other">No customer charge</MenuItem>
              </TextField>
            </Stack>
            <TextField label="Responsible party" value={cost.responsibility} onChange={event => editCost(cost.id, { responsibility: event.target.value })} slotProps={{ htmlInput: { maxLength: 150 } }} />
            <TextField label="Evidence and reason for decision" multiline value={cost.evidence} onChange={event => editCost(cost.id, { evidence: event.target.value })} slotProps={{ htmlInput: { maxLength: 1000 } }} />
            <FormControlLabel control={<Checkbox checked={cost.isTaxable} onChange={event => editCost(cost.id, { isTaxable: event.target.checked })} />} label="VAT applies" />
            {!closed && !data.state.costs.some(saved => saved.id === cost.id) && <Button sx={{ alignSelf: 'flex-start' }} onClick={() => edit({ costs: form.costs.filter(item => item.id !== cost.id) })}>Remove cost</Button>}
          </Stack>)}
          {!closed && <Stack direction="row" flexWrap="wrap" gap={1}>
            <Button variant="outlined" disabled={busy || form.costs.length >= 50} onClick={() => edit({ costs: [...form.costs, {
              id: crypto.randomUUID(), description: '', amount: 0, decision: 'Pending', responsibility: '', evidence: '', isTaxable: false, billed: false,
            }] })}>Add incident cost</Button>
            <Button component="label" variant="outlined" disabled={busy || form.attachments.length >= 5}>Attach evidence
              <input hidden multiple type="file" accept="image/jpeg,image/png,image/webp,application/pdf" onChange={event => {
                void attach(event.target.files)
                event.target.value = ''
              }} />
            </Button>
          </Stack>}
          {form.attachments.map((file, index) => <Stack key={index} direction="row" spacing={1} alignItems="center">
            <Button href={file.dataUrl} download={file.name}>{file.name}</Button>
            {!closed && index >= data.state.attachments.length && <Button onClick={() => edit({ attachments: form.attachments.filter((_, position) => index !== position) })}>Remove</Button>}
          </Stack>)}
          <Divider />
          <Typography variant="h6">Refundable bond</Typography>
          <Typography>{money(data.bondAmountHeld)} collected{data.bondSettledAt && ` · ${money(data.bondDeductionAmount)} applied · ${money(data.bondRefundAmount)} refunded`}</Typography>
          {!closed && !data.bondSettledAt && data.bondAmountHeld > 0 && <>
            <FormControlLabel label="Settle the bond when issuing the invoice" control={<Checkbox checked={form.settleBond} onChange={event => edit({ settleBond: event.target.checked })} />} />
            {form.settleBond && <>
              <TextField label="Bond credit to apply (FJD)" type="number" value={form.bondCredit} onChange={event => edit({ bondCredit: event.target.value })} slotProps={{ htmlInput: { min: 0, max: data.bondAmountHeld, step: '0.01' } }} />
              <TextField label="Reason for bond credit" value={form.bondReason} onChange={event => edit({ bondReason: event.target.value })} slotProps={{ htmlInput: { maxLength: 500 } }} />
              {Number(form.bondCredit) < data.bondAmountHeld && <>
                <Alert severity="info">Refund {money(data.bondAmountHeld - Number(form.bondCredit))} and record its transaction reference before issuing.</Alert>
                <TextField select label="Refund method" value={form.bondRefundMethod} onChange={event => edit({ bondRefundMethod: event.target.value })}>
                  {Object.entries(methods).map(([value, label]) => <MenuItem key={value} value={value}>{label}</MenuItem>)}
                </TextField>
                <TextField label="Refund transaction reference" value={form.bondRefundReference} onChange={event => edit({ bondRefundReference: event.target.value })} slotProps={{ htmlInput: { maxLength: 60 } }} />
              </>}
            </>}
          </>}
          <Divider />
          <Typography variant="h6">Follow-up and outcome</Typography>
          <FormControlLabel label="Customer disputes this settlement" control={<Checkbox disabled={closed || busy} checked={form.disputed} onChange={event => edit({ disputed: event.target.checked })} />} />
          <TextField id="settlement-closure-outcome" label="Follow-up notes or closure outcome" multiline value={form.outcome} onChange={event => edit({ outcome: event.target.value })} disabled={closed || busy} slotProps={{ htmlInput: { maxLength: 1000 } }} />
          {!closed && data.state.issuedAt && data.balance > 0 && <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
            <TextField label="Payment received (FJD)" type="number" value={payment.amount} onChange={event => setPayment({ ...payment, amount: event.target.value })} slotProps={{ htmlInput: { min: '0.01', max: data.balance, step: '0.01' } }} />
            <TextField select label="Payment method" value={payment.method} onChange={event => setPayment({ ...payment, method: event.target.value })} sx={{ minWidth: 145 }}>
              {Object.entries(methods).map(([value, label]) => <MenuItem key={value} value={value}>{label}</MenuItem>)}
            </TextField>
            <TextField label="Receipt reference" value={payment.receiptNumber} onChange={event => setPayment({ ...payment, receiptNumber: event.target.value })} slotProps={{ htmlInput: { maxLength: 60 } }} />
            <Button disabled={busy || !(Number(payment.amount) > 0 && Number(payment.amount) <= data.balance) || !payment.receiptNumber.trim()} onClick={() => void recordPayment()}>Record payment</Button>
          </Stack>}
          {!closed && data.state.issuedAt && data.balance > 0 && (credit ? <Stack spacing={1.5}>
            <TextField label="Authorized credit amount (FJD)" type="number" value={credit.total} onChange={event => setCredit({ ...credit, total: event.target.value })} slotProps={{ htmlInput: { min: '0.01', max: data.balance, step: '0.01' } }} />
            <TextField label="Reason and authority for credit" value={credit.reason} onChange={event => setCredit({ ...credit, reason: event.target.value })} slotProps={{ htmlInput: { maxLength: 500 } }} />
            <Stack direction="row" spacing={1}>
              <Button disabled={busy} onClick={() => setCredit(null)}>Cancel credit</Button>
              <Button disabled={busy || !credit.reason.trim() || !(Number(credit.total) > 0 && Number(credit.total) <= data.balance)} onClick={() => void applyCredit()}>Apply credit note</Button>
            </Stack>
          </Stack> : <Button sx={{ alignSelf: 'flex-start' }} onClick={() => setCredit({ total: '', reason: '' })}>Add credit note</Button>)}
          {!closed && data.email && <Typography variant="body2">Issuing the settlement emails the itemized account to {data.email}.</Typography>}
          </>}
        </>}
      </Stack>
    </DialogContent>
    <DialogActions sx={{ flexWrap: 'wrap' }}>
      <InlineIssueNavigation attempted={attempted!==null} issues={attempted?settlementIssues(attempted):[]}/>
      <Button onClick={onClose} disabled={busy}>Done</Button>
      {!closed && data && form && (data.canApprove ? <>
        <Button disabled={busy} onClick={() => void save('save')}>Save review</Button>
        <Button disabled={busy || !data.state.issuedAt || data.balance > 0 || pending || form.disputed || data.bondAmountHeld > 0 && !data.bondSettledAt} onClick={() => void save('close')}>Close case</Button>
        {canIssue && <Button variant="contained" disabled={busy} onClick={() => void save('issue')}>
          {data.state.issuedAt ? 'Issue revised settlement' : 'Issue settlement'}
        </Button>}
      </> : <Button variant="contained" disabled={busy} onClick={requestFollowUp}>Save follow-up</Button>)}
    </DialogActions>
  </Dialog>
}
