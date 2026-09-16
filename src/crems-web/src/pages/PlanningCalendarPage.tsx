import { useEffect, useMemo, useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  CircularProgress,
  Divider,
  FormControl,
  InputLabel,
  MenuItem,
  Select,
  Stack,
  Typography,
} from '@mui/material'
import { AssetProfileDialog } from '../components/AssetProfileDialog'
import { api } from '../api/client'

type Asset = { id: string; assetNumber: string; name: string; status: string; branchId: string; divisionId: string | null }
type Event = { assetId: string; startAt: string; endAt: string; type: string; reference: string }
type Calendar = {
  assets: Asset[]
  events: Event[]
  maintenance: Event[]
  transfers: Event[]
  personnel: { personnelId: string; fullName: string; startAt: string; endAt: string; type: string; reference: string }[]
  closures: { id: string; date: string; name: string; isClosed: boolean }[]
}

type DayStatus = { label: string; short: string; color: string; textColor: string; busy: boolean }

const DAY_IN_MS = 24 * 60 * 60 * 1000
const STATUS_PALETTE = {
  available: { label: 'Available', short: 'A', color: '#CFEFDC', textColor: '#14532D', busy: false },
  booked: { label: 'Booked', short: 'B', color: '#CDE7FF', textColor: '#1E3A8A', busy: true },
  maintenance: { label: 'Maintenance', short: 'M', color: '#FFF4D6', textColor: '#8A4B00', busy: true },
  transfer: { label: 'Transfer', short: 'T', color: '#F3E8FF', textColor: '#5B21B6', busy: true },
  closed: { label: 'Closed', short: 'C', color: '#F3F4F6', textColor: '#374151', busy: true },
} as const

function startOfDay(date: Date) {
  const copy = new Date(date)
  copy.setHours(0, 0, 0, 0)
  return copy
}

function addDays(date: Date, days: number) {
  const copy = startOfDay(date)
  copy.setTime(copy.getTime() + days * DAY_IN_MS)
  return copy
}

function toLocalDateKey(date: Date) {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

function formatDate(date: Date, style: 'short' | 'day' = 'short') {
  return date.toLocaleDateString('en-FJ', style === 'short'
    ? { month: 'short', day: 'numeric' }
    : { weekday: 'short', month: 'short', day: 'numeric' })
}

function formatDisplayDate(date: Date) {
  const day = String(date.getDate()).padStart(2, '0')
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const year = date.getFullYear()
  return `${day}/${month}/${year}`
}

function parseDisplayDate(value: string) {
  const cleaned = value.trim()
  const match = cleaned.match(/^(\d{1,2})\/(\d{1,2})\/(\d{4})$/)

  if (!match) return null

  const [, day, month, year] = match
  const parsedDay = Number(day)
  const parsedMonth = Number(month)
  const parsedYear = Number(year)

  if (!parsedDay || !parsedMonth || parsedYear < 1900) return null

  const date = new Date(parsedYear, parsedMonth - 1, parsedDay)
  if (
    date.getFullYear() !== parsedYear ||
    date.getMonth() !== parsedMonth - 1 ||
    date.getDate() !== parsedDay
  ) {
    return null
  }

  return date
}

function overlapsDate(date: Date, startAt: string, endAt: string) {
  const start = new Date(startAt)
  const end = new Date(endAt)
  const dayStart = startOfDay(date)
  const dayEnd = new Date(dayStart)
  dayEnd.setHours(23, 59, 59, 999)

  return start < dayEnd && end > dayStart
}

function getAssetStatusForDate(assetId: string, date: Date, events: Event[]): DayStatus {
  const matches = events.filter((event) => event.assetId === assetId && overlapsDate(date, event.startAt, event.endAt))

  if (!matches.length) {
    return STATUS_PALETTE.available
  }

  switch (matches[0].type.toLowerCase()) {
    case 'maintenance':
      return STATUS_PALETTE.maintenance
    case 'transfer':
      return STATUS_PALETTE.transfer
    case 'personnel':
      return { ...STATUS_PALETTE.booked, label: 'Personnel', short: 'P' }
    default:
      return STATUS_PALETTE.booked
  }
}

export function PlanningCalendarPage() {
  const [data, setData] = useState<Calendar | null>(null)
  const [error, setError] = useState('')
  const [anchorDate, setAnchorDate] = useState(() => startOfDay(new Date()))
  const [rangeStartDate, setRangeStartDate] = useState(() => startOfDay(new Date()))
  const [rangeEndDate, setRangeEndDate] = useState(() => addDays(startOfDay(new Date()), 13))
  const [rangeDays, setRangeDays] = useState(14)
  const [selectedAssetId, setSelectedAssetId] = useState('all')
  const [branchFilter, setBranchFilter] = useState('all')
  const [divisionFilter, setDivisionFilter] = useState('all')
  const [availableOnly, setAvailableOnly] = useState(false)
  const [detailAssetId, setDetailAssetId] = useState<string | null>(null)
  const [branches, setBranches] = useState<{ id: string; name: string }[]>([])
  const [divisions, setDivisions] = useState<{ id: string; name: string }[]>([])

  useEffect(() => {
    const from = addDays(rangeStartDate, -7)
    const to = addDays(rangeEndDate, 7)

    void api.get<Calendar>('/planning/calendar', {
      params: {
        from: from.toISOString(),
        to: to.toISOString(),
      },
    })
      .then((response) => setData(response.data))
      .catch(() => setError('Unable to load availability planning.'))
  }, [rangeEndDate, rangeStartDate])

  useEffect(() => {
    void Promise.all([
      api.get<{ id: string; name: string }[]>('/branches'),
      api.get<{ id: string; name: string }[]>('/divisions'),
    ])
      .then(([branchResponse, divisionResponse]) => {
        setBranches(branchResponse.data)
        setDivisions(divisionResponse.data)
      })
      .catch(() => {
        setBranches([])
        setDivisions([])
      })
  }, [])

  const visibleDates = useMemo(() => {
    const diffDays = Math.max(0, Math.round((rangeEndDate.getTime() - rangeStartDate.getTime()) / DAY_IN_MS) + 1)
    return Array.from({ length: diffDays }, (_, index) => addDays(rangeStartDate, index))
  }, [rangeEndDate, rangeStartDate])

  const allEvents = useMemo(() => {
    if (!data) return [] as Event[]
    return [...data.events, ...data.maintenance, ...data.transfers]
  }, [data])

  const closureMap = useMemo(() => {
    if (!data) return new Map<string, string>()
    return new Map(data.closures.map((closure) => [closure.date, closure.name]))
  }, [data])

  const todayKey = toLocalDateKey(new Date())

  const visibleAssets = useMemo(() => {
    if (!data) return []

    return data.assets.filter((asset) => {
      if (selectedAssetId !== 'all' && asset.id !== selectedAssetId) return false
      if (branchFilter !== 'all' && asset.branchId !== branchFilter) return false
      if (divisionFilter !== 'all' && asset.divisionId !== divisionFilter) return false
      if (!availableOnly) return true

      return !visibleDates.some((date) => getAssetStatusForDate(asset.id, date, allEvents).busy)
    })
  }, [allEvents, branchFilter, data, divisionFilter, selectedAssetId, availableOnly, visibleDates])

  const availabilitySummary = useMemo(() => {
    const summary = { available: 0, booked: 0, maintenance: 0, transfer: 0 }

    if (!data) return summary

    for (const asset of visibleAssets) {
      for (const date of visibleDates) {
        const status = getAssetStatusForDate(asset.id, date, allEvents)

        if (status.label === 'Available') summary.available += 1
        else if (status.label === 'Booked' || status.label === 'Personnel') summary.booked += 1
        else if (status.label === 'Maintenance') summary.maintenance += 1
        else if (status.label === 'Transfer') summary.transfer += 1
      }
    }

    return summary
  }, [allEvents, visibleAssets, visibleDates, data])

  if (error) {
    return <Alert severity="error">{error}</Alert>
  }

  if (!data) {
    return (
      <Box minHeight={350} display="grid" sx={{ placeItems: 'center' }}>
        <CircularProgress />
      </Box>
    )
  }

  return (
    <Box sx={{ p: { xs: 2, sm: 3, lg: 4 } }}>
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ xs: 'flex-start', md: 'center' }} gap={2} mb={3}>
        <Box>
          <Typography variant="h4" fontWeight={850}>Asset availability calendar</Typography>
          <Typography color="text.secondary">Track bookings, maintenance, transfers, and closures across the fleet.</Typography>
        </Box>

        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
          <Button
            variant="outlined"
            size="small"
            onClick={() => {
              const nextStart = addDays(rangeStartDate, -rangeDays)
              const nextEnd = addDays(nextStart, rangeDays - 1)
              setRangeStartDate(nextStart)
              setRangeEndDate(nextEnd)
              setAnchorDate(nextStart)
            }}
          >
            Previous
          </Button>
          <Button
            variant="outlined"
            size="small"
            onClick={() => {
              const today = startOfDay(new Date())
              setRangeStartDate(today)
              setRangeEndDate(addDays(today, rangeDays - 1))
              setAnchorDate(today)
            }}
          >
            Today
          </Button>
          <Button
            variant="outlined"
            size="small"
            onClick={() => {
              const nextStart = addDays(rangeStartDate, rangeDays)
              const nextEnd = addDays(nextStart, rangeDays - 1)
              setRangeStartDate(nextStart)
              setRangeEndDate(nextEnd)
              setAnchorDate(nextStart)
            }}
          >
            Next
          </Button>
        </Stack>
      </Stack>

      <Stack direction={{ xs: 'column', md: 'row' }} alignItems={{ xs: 'stretch', md: 'center' }} justifyContent="space-between" gap={2} mb={2}>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} alignItems={{ xs: 'stretch', sm: 'center' }} flexWrap="wrap">
          <FormControl size="small" sx={{ minWidth: 180 }}>
            <InputLabel id="asset-filter-label">Asset</InputLabel>
            <Select
              labelId="asset-filter-label"
              label="Asset"
              value={selectedAssetId}
              onChange={(event) => setSelectedAssetId(event.target.value)}
            >
              <MenuItem value="all">All assets</MenuItem>
              {data.assets.map((asset) => (
                <MenuItem key={asset.id} value={asset.id}>{asset.assetNumber}</MenuItem>
              ))}
            </Select>
          </FormControl>

          <FormControl size="small" sx={{ minWidth: 180 }}>
            <InputLabel id="branch-filter-label">Branch</InputLabel>
            <Select
              labelId="branch-filter-label"
              label="Branch"
              value={branchFilter}
              onChange={(event) => setBranchFilter(event.target.value)}
            >
              <MenuItem value="all">All branches</MenuItem>
              {branches.map((branch) => (
                <MenuItem key={branch.id} value={branch.id}>{branch.name}</MenuItem>
              ))}
            </Select>
          </FormControl>

          <FormControl size="small" sx={{ minWidth: 180 }}>
            <InputLabel id="division-filter-label">Division</InputLabel>
            <Select
              labelId="division-filter-label"
              label="Division"
              value={divisionFilter}
              onChange={(event) => setDivisionFilter(event.target.value)}
            >
              <MenuItem value="all">All divisions</MenuItem>
              {divisions.map((division) => (
                <MenuItem key={division.id} value={division.id}>{division.name}</MenuItem>
              ))}
            </Select>
          </FormControl>

          <FormControl size="small" sx={{ minWidth: 140 }}>
            <InputLabel id="range-filter-label">Range</InputLabel>
            <Select
              labelId="range-filter-label"
              label="Range"
              value={rangeDays}
              onChange={(event) => {
                const nextRangeDays = Number(event.target.value)
                const nextStart = startOfDay(new Date(rangeStartDate))
                const nextEnd = addDays(nextStart, nextRangeDays - 1)
                setRangeDays(nextRangeDays)
                setRangeStartDate(nextStart)
                setRangeEndDate(nextEnd)
                setAnchorDate(nextStart)
              }}
            >
              <MenuItem value={7}>7 days</MenuItem>
              <MenuItem value={14}>14 days</MenuItem>
              <MenuItem value={21}>21 days</MenuItem>
              <MenuItem value={30}>30 days</MenuItem>
            </Select>
          </FormControl>

          <Box sx={{ display: 'flex', gap: 1, alignItems: 'center', minWidth: 300 }}>
            <Box sx={{ display: 'flex', flexDirection: 'column', minWidth: 120 }}>
              <Typography variant="caption" color="text.secondary">Start</Typography>
              <input
                type="text"
                value={formatDisplayDate(rangeStartDate)}
                onChange={(event) => {
                  const nextStart = parseDisplayDate(event.target.value)
                  if (!nextStart) return
                  const updatedStart = startOfDay(nextStart)
                  const updatedEnd = rangeEndDate < updatedStart ? updatedStart : rangeEndDate
                  setRangeStartDate(updatedStart)
                  setRangeEndDate(updatedEnd)
                  setAnchorDate(updatedStart)
                  setRangeDays(Math.max(1, Math.round((updatedEnd.getTime() - updatedStart.getTime()) / DAY_IN_MS) + 1))
                }}
                placeholder="DD/MM/YYYY"
                inputMode="numeric"
                style={{
                  border: '1px solid #d1d5db',
                  borderRadius: 10,
                  padding: '10px 12px',
                  background: '#fff',
                  fontSize: 16,
                  fontWeight: 500,
                  color: '#374151',
                  minHeight: 42,
                  boxShadow: 'inset 0 1px 2px rgba(15, 23, 42, 0.04)',
                }}
              />
            </Box>
            <Box sx={{ display: 'flex', flexDirection: 'column', minWidth: 120 }}>
              <Typography variant="caption" color="text.secondary">End</Typography>
              <input
                type="text"
                value={formatDisplayDate(rangeEndDate)}
                onChange={(event) => {
                  const nextEnd = parseDisplayDate(event.target.value)
                  if (!nextEnd) return
                  const updatedEnd = startOfDay(nextEnd)
                  const updatedStart = rangeStartDate > updatedEnd ? updatedEnd : rangeStartDate
                  setRangeEndDate(updatedEnd)
                  setRangeStartDate(updatedStart)
                  setAnchorDate(updatedStart)
                  setRangeDays(Math.max(1, Math.round((updatedEnd.getTime() - updatedStart.getTime()) / DAY_IN_MS) + 1))
                }}
                placeholder="DD/MM/YYYY"
                inputMode="numeric"
                style={{
                  border: '1px solid #d1d5db',
                  borderRadius: 10,
                  padding: '10px 12px',
                  background: '#fff',
                  fontSize: 16,
                  fontWeight: 500,
                  color: '#374151',
                  minHeight: 42,
                  boxShadow: 'inset 0 1px 2px rgba(15, 23, 42, 0.04)',
                }}
              />
            </Box>
          </Box>
        </Stack>

        <Button
          variant={availableOnly ? 'contained' : 'outlined'}
          color="success"
          size="small"
          onClick={() => setAvailableOnly((current) => !current)}
        >
          {availableOnly ? 'Showing available only' : 'Show available only'}
        </Button>
      </Stack>

      <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} mb={2} alignItems={{ xs: 'flex-start', md: 'center' }}>
        {Object.values(STATUS_PALETTE).map((status) => (
          <Stack key={status.label} direction="row" spacing={0.75} alignItems="center" sx={{ px: 0.75, py: 0.5, borderRadius: 999, bgcolor: '#f8fafc', border: '1px solid #e5e7eb' }}>
            <Box sx={{ width: 12, height: 12, borderRadius: '50%', bgcolor: status.color, border: '1px solid rgba(0,0,0,0.08)' }} />
            <Typography variant="caption" sx={{ color: status.textColor, fontWeight: 700 }}>{status.label}</Typography>
          </Stack>
        ))}
      </Stack>

      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} mb={2}>
        <Card variant="outlined" sx={{ flex: 1, bgcolor: '#fffdf4', borderColor: '#f7d972' }}>
          <CardContent sx={{ py: 1.5 }}>
            <Typography variant="caption" color="text.secondary">Current period</Typography>
            <Typography variant="h6" fontWeight={800}>{formatDate(visibleDates[0], 'short')} - {formatDate(visibleDates[visibleDates.length - 1], 'short')}</Typography>
          </CardContent>
        </Card>
        <Card variant="outlined" sx={{ flex: 1 }}>
          <CardContent sx={{ py: 1.5 }}>
            <Typography variant="caption" color="text.secondary">Fleet coverage</Typography>
            <Typography variant="h6" fontWeight={800}>{visibleAssets.length} assets shown</Typography>
          </CardContent>
        </Card>
        <Card variant="outlined" sx={{ flex: 1 }}>
          <CardContent sx={{ py: 1.5 }}>
            <Typography variant="caption" color="text.secondary">Planned closures</Typography>
            <Typography variant="h6" fontWeight={800}>{data.closures.length} items</Typography>
          </CardContent>
        </Card>
      </Stack>

      <Card variant="outlined" sx={{ borderWidth: 1.5, borderColor: '#d4d4d8' }}>
        <CardContent sx={{ p: 0 }}>
          <Box sx={{ overflowX: 'auto' }}>
            <Box sx={{ minWidth: 900, p: 1.5 }}>
              <Box
                sx={{
                  display: 'grid',
                  gridTemplateColumns: `220px repeat(${visibleDates.length}, minmax(64px, 1fr))`,
                  gap: 0.75,
                  alignItems: 'stretch',
                }}
              >
                <Box sx={{ fontWeight: 900, py: 1.25, px: 1, borderRadius: 1, bgcolor: '#f8fafc', color: '#111827' }}>Asset</Box>
                {visibleDates.map((date) => {
                  const dateKey = toLocalDateKey(date)
                  const isToday = dateKey === todayKey
                  return (
                    <Box
                      key={dateKey}
                      sx={{
                        textAlign: 'center',
                        py: 1.25,
                        fontWeight: 800,
                        fontSize: 11,
                        borderRadius: 1,
                        bgcolor: isToday ? '#fff7d6' : '#f8fafc',
                        color: isToday ? '#7a4b00' : '#111827',
                        border: isToday ? '1px solid #f5c451' : 'none',
                      }}
                    >
                      {formatDate(date, 'short')}
                    </Box>
                  )
                })}

                {visibleAssets.length === 0 ? (
                  <Box sx={{ gridColumn: '1 / -1', py: 4, textAlign: 'center', color: 'text.secondary' }}>
                    No assets match the current availability filter.
                  </Box>
                ) : (
                  visibleAssets.map((asset) => (
                    <Box key={asset.id} sx={{ display: 'contents' }}>
                      <Box sx={{ py: 1.5, pr: 1, borderTop: '1px solid', borderColor: '#e5e7eb', bgcolor: '#ffffff', borderLeft: '4px solid #ffed00' }}>
                        <Typography variant="body2" fontWeight={800}>{asset.assetNumber}</Typography>
                        <Typography variant="caption" color="text.secondary">{asset.name}</Typography>
                      </Box>

                      {visibleDates.map((date) => {
                        const dateKey = toLocalDateKey(date)
                        const closureName = closureMap.get(dateKey)
                        const status = getAssetStatusForDate(asset.id, date, allEvents)
                        const cellColor = closureName ? '#f5f5f5' : status.color
                        const textColor = closureName ? '#616161' : status.textColor
                        const cellText = closureName ? 'Closed' : status.short
                        const cellLabel = closureName ? closureName : status.label
                        const isToday = dateKey === todayKey

                        return (
                          <Box
                            key={`${asset.id}-${dateKey}`}
                            onClick={() => setDetailAssetId(asset.id)}
                            sx={{
                              minHeight: 52,
                              borderRadius: 1,
                              border: '1px solid',
                              borderColor: closureName ? '#d1d5db' : isToday ? '#f5c451' : '#dfe7ef',
                              bgcolor: cellColor,
                              color: textColor,
                              display: 'flex',
                              alignItems: 'center',
                              justifyContent: 'center',
                              fontSize: 12,
                              fontWeight: 800,
                              textAlign: 'center',
                              px: 0.25,
                              py: 0.5,
                              cursor: 'pointer',
                              transition: 'transform 0.15s ease, box-shadow 0.15s ease',
                              '&:hover': {
                                boxShadow: '0 0 0 2px rgba(19, 100, 255, 0.18)',
                                transform: 'translateY(-1px)',
                              },
                              boxShadow: isToday ? 'inset 0 0 0 1px rgba(122,75,0,0.08)' : closureName ? 'inset 0 0 0 1px rgba(0,0,0,0.04)' : 'none',
                              title: `${asset.assetNumber} · ${formatDate(date, 'day')} · ${cellLabel}`,
                            }}
                          >
                            {cellText}
                          </Box>
                        )
                      })}
                    </Box>
                  ))
                )}
              </Box>
            </Box>
          </Box>
        </CardContent>
      </Card>

      <Divider sx={{ my: 3 }} />

      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
        <Card variant="outlined" sx={{ flex: 1 }}>
          <CardContent>
            <Typography variant="h6" fontWeight={800} mb={1}>Upcoming bookings</Typography>
            {data.events.length === 0 ? (
              <Typography color="text.secondary">No booking events in the selected range.</Typography>
            ) : (
              <Stack spacing={1.25}>
                {data.events.slice(0, 5).map((event, index) => {
                  const asset = data.assets.find((item) => item.id === event.assetId)
                  return (
                    <Box
                      key={`${event.assetId}-${event.startAt}-${index}`}
                      sx={{ p: 1.25, borderRadius: 1, bgcolor: '#f7f7f7', cursor: 'pointer' }}
                      onClick={() => setDetailAssetId(event.assetId)}
                    >
                      <Typography variant="body2" fontWeight={800}>{asset ? `${asset.assetNumber} · ${asset.name}` : event.reference}</Typography>
                      <Typography variant="caption" color="text.secondary" display="block">
                        {event.type} · {new Date(event.startAt).toLocaleDateString('en-FJ')} – {new Date(event.endAt).toLocaleDateString('en-FJ')}
                      </Typography>
                    </Box>
                  )
                })}
              </Stack>
            )}
          </CardContent>
        </Card>

        <Card variant="outlined" sx={{ flex: 1 }}>
          <CardContent>
            <Typography variant="h6" fontWeight={800} mb={1}>Branch closures</Typography>
            {data.closures.length === 0 ? (
              <Typography color="text.secondary">No branch closures in the selected range.</Typography>
            ) : (
              <Stack spacing={1.25}>
                {data.closures.slice(0, 5).map((closure) => (
                  <Box key={closure.id} sx={{ p: 1.25, borderRadius: 1, bgcolor: '#fafafa' }}>
                    <Typography variant="body2" fontWeight={700}>{closure.name}</Typography>
                    <Typography variant="caption" color="text.secondary">
                      {new Date(closure.date).toLocaleDateString('en-FJ')} · {closure.isClosed ? 'Closed' : 'Open'}
                    </Typography>
                  </Box>
                ))}
              </Stack>
            )}
          </CardContent>
        </Card>
      </Stack>

      <AssetProfileDialog assetId={detailAssetId} onClose={() => setDetailAssetId(null)} />
    </Box>
  )
}
