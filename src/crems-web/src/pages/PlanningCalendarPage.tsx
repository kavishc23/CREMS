import { useEffect, useMemo, useState } from 'react'
import EventAvailableOutlined from '@mui/icons-material/EventAvailableOutlined'
import SearchOutlined from '@mui/icons-material/SearchOutlined'
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  CircularProgress,
  Divider,
  FormControl,
  InputLabel,
  InputAdornment,
  MenuItem,
  Select,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { AssetProfileDialog } from '../components/AssetProfileDialog'
import { PageHeader } from '../components/PageHeader'
import { api } from '../api/client'

type Asset = { id: string; assetNumber: string; name: string; status: string; branchId: string; divisionId: string | null }
type Event = { assetId: string; startAt: string; endAt: string; type: string; reference: string }
type Calendar = {
  assets: Asset[]
  events: Event[]
  maintenance: Event[]
  transfers: Event[]
  personnel: { personnelId: string; fullName: string; startAt: string; endAt: string; type: string; reference: string }[]
  closures: { id: string; branchId: string; date: string; name: string; isClosed: boolean }[]
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
  copy.setDate(copy.getDate() + days)
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

function overlapsDate(date: Date, startAt: string, endAt: string) {
  const start = new Date(startAt)
  const end = new Date(endAt)
  const dayStart = startOfDay(date)
  const dayEnd = new Date(dayStart)
  dayEnd.setHours(23, 59, 59, 999)

  return start < dayEnd && end > dayStart
}

function getAssetStatusForDate(asset: Asset, date: Date, events: Event[]): DayStatus {
  const matches = events.filter((event) => event.assetId === asset.id && overlapsDate(date, event.startAt, event.endAt))

  if (!matches.length) {
    if (['Reserved', 'Rented'].includes(asset.status)) return STATUS_PALETTE.booked
    if (asset.status === 'Inspection') return { ...STATUS_PALETTE.maintenance, label: 'Inspection', short: 'I' }
    if (asset.status === 'Maintenance') return STATUS_PALETTE.maintenance
    if (['OutOfService', 'Retired'].includes(asset.status)) return STATUS_PALETTE.closed
    return STATUS_PALETTE.available
  }

  if (matches.some(event => event.type.toLowerCase() === 'maintenance')) return STATUS_PALETTE.maintenance
  if (matches.some(event => event.type.toLowerCase() === 'transfer')) return STATUS_PALETTE.transfer
  return STATUS_PALETTE.booked
}

export function PlanningCalendarPage() {
  const [data, setData] = useState<Calendar | null>(null)
  const [error, setError] = useState('')
  const [rangeStartDate, setRangeStartDate] = useState(() => startOfDay(new Date()))
  const [rangeEndDate, setRangeEndDate] = useState(() => addDays(startOfDay(new Date()), 13))
  const [rangeDays, setRangeDays] = useState(14)
  const [selectedAssetId, setSelectedAssetId] = useState('all')
  const [assetSearch, setAssetSearch] = useState('')
  const [branchFilter, setBranchFilter] = useState('all')
  const [divisionFilter, setDivisionFilter] = useState('all')
  const [availableOnly, setAvailableOnly] = useState(false)
  const [detailAssetId, setDetailAssetId] = useState<string | null>(null)
  const [branches, setBranches] = useState<{ id: string; name: string }[]>([])
  const [divisions, setDivisions] = useState<{ id: string; name: string }[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    const from = addDays(rangeStartDate, -7)
    const to = addDays(rangeEndDate, 7)

    setLoading(true); setError('')
    void api.get<Calendar>('/planning/calendar', {
      params: {
        from: from.toISOString(),
        to: to.toISOString(),
      },
    })
      .then((response) => setData(response.data))
      .catch(() => setError('Unable to load availability planning.'))
      .finally(() => setLoading(false))
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

  const visibleClosures = useMemo(() => {
    if (!data) return []
    const firstKey = toLocalDateKey(rangeStartDate)
    const lastKey = toLocalDateKey(rangeEndDate)
    return data.closures.filter(closure => closure.date >= firstKey && closure.date <= lastKey)
  }, [data, rangeEndDate, rangeStartDate])

  const closureMap = useMemo(() => {
    if (!data) return new Map<string, string>()
    return new Map(visibleClosures.filter(closure => closure.isClosed).map((closure) => [`${closure.branchId}:${closure.date}`, closure.name]))
  }, [data, visibleClosures])

  const visibleBookingEvents = useMemo(() => {
    if (!data) return []
    const rangeEndExclusive = addDays(rangeEndDate, 1)
    return data.events.filter(event => new Date(event.startAt) < rangeEndExclusive && new Date(event.endAt) > rangeStartDate)
      .sort((left, right) => new Date(left.startAt).getTime() - new Date(right.startAt).getTime())
  }, [data, rangeEndDate, rangeStartDate])

  const todayKey = toLocalDateKey(new Date())

  const visibleAssets = useMemo(() => {
    if (!data) return []

    return data.assets.filter((asset) => {
      const search = assetSearch.trim().toLowerCase()
      if (search && !`${asset.assetNumber} ${asset.name}`.toLowerCase().includes(search)) return false
      if (selectedAssetId !== 'all' && asset.id !== selectedAssetId) return false
      if (branchFilter !== 'all' && asset.branchId !== branchFilter) return false
      if (divisionFilter !== 'all' && asset.divisionId !== divisionFilter) return false
      if (!availableOnly) return true

      return !visibleDates.some((date) => closureMap.has(`${asset.branchId}:${toLocalDateKey(date)}`) || getAssetStatusForDate(asset, date, allEvents).busy)
    })
  }, [allEvents, assetSearch, branchFilter, closureMap, data, divisionFilter, selectedAssetId, availableOnly, visibleDates])

  const availabilitySummary = useMemo(() => {
    const summary = { available: 0, booked: 0, maintenance: 0, transfer: 0 }

    if (!data) return summary

    for (const asset of visibleAssets) {
      for (const date of visibleDates) {
        const status = closureMap.has(`${asset.branchId}:${toLocalDateKey(date)}`) ? STATUS_PALETTE.closed : getAssetStatusForDate(asset, date, allEvents)

        if (status.label === 'Available') summary.available += 1
        else if (status.label === 'Booked' || status.label === 'Personnel') summary.booked += 1
        else if (status.label === 'Maintenance') summary.maintenance += 1
        else if (status.label === 'Transfer') summary.transfer += 1
      }
    }

    return summary
  }, [allEvents, closureMap, visibleAssets, visibleDates, data])

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
      <PageHeader icon={<EventAvailableOutlined />} title="Asset availability calendar" subtitle="Track bookings, maintenance, transfers, and closures across the fleet." actions={
        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
          {loading && <CircularProgress size={18} thickness={5} />}
          <Button
            variant="outlined"
            size="small"
            onClick={() => {
              const nextStart = addDays(rangeStartDate, -rangeDays)
              const nextEnd = addDays(nextStart, rangeDays - 1)
              setRangeStartDate(nextStart)
              setRangeEndDate(nextEnd)
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
            }}
          >
            Next
          </Button>
        </Stack>
      } />

      <Card variant="outlined" sx={{ mb: 2.5, borderTop: '3px solid', borderTopColor: 'secondary.main' }}><CardContent sx={{ p: { xs: 2, md: 2.5 } }}><Box sx={{
        display: 'grid', gap: 1.5, alignItems: 'center',
        gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))', md: 'repeat(4, minmax(0, 1fr))', xl: 'minmax(220px, 1.3fr) repeat(3, minmax(150px, 1fr)) minmax(110px, .7fr) repeat(2, minmax(145px, .85fr)) auto' },
      }}>
          <TextField
            label="Search vehicle or asset"
            placeholder="Name or asset number"
            value={assetSearch}
            onChange={(event) => setAssetSearch(event.target.value)}
            fullWidth
            InputProps={{ startAdornment: <InputAdornment position="start"><SearchOutlined fontSize="small" /></InputAdornment> }}
          />
          <FormControl size="small" fullWidth>
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

          <FormControl size="small" fullWidth>
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

          <FormControl size="small" fullWidth>
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

          <FormControl size="small" fullWidth>
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
              }}
            >
              <MenuItem value={7}>7 days</MenuItem>
              <MenuItem value={14}>14 days</MenuItem>
              <MenuItem value={21}>21 days</MenuItem>
              <MenuItem value={30}>30 days</MenuItem>
            </Select>
          </FormControl>

          <TextField fullWidth type="date" label="Start" value={toLocalDateKey(rangeStartDate)} InputLabelProps={{ shrink: true }} onChange={(event) => {
            const updatedStart = startOfDay(new Date(`${event.target.value}T00:00:00`))
            if (Number.isNaN(updatedStart.getTime())) return
            setRangeStartDate(updatedStart)
            setRangeEndDate(addDays(updatedStart, rangeDays - 1))
          }} />
          <TextField fullWidth type="date" label="End" value={toLocalDateKey(rangeEndDate)} InputLabelProps={{ shrink: true }} inputProps={{ min: toLocalDateKey(rangeStartDate) }} onChange={(event) => {
            const updatedEnd = startOfDay(new Date(`${event.target.value}T00:00:00`))
            if (Number.isNaN(updatedEnd.getTime()) || updatedEnd < rangeStartDate) return
            setRangeEndDate(updatedEnd)
            setRangeDays(Math.max(1, Math.round((updatedEnd.getTime() - rangeStartDate.getTime()) / DAY_IN_MS) + 1))
          }} />
        <Button
          variant={availableOnly ? 'contained' : 'outlined'}
          color="success"
          size="small"
          onClick={() => setAvailableOnly((current) => !current)}
          sx={{ minHeight: 40, whiteSpace: 'nowrap', justifySelf: { xs: 'stretch', xl: 'end' } }}
        >
          {availableOnly ? 'Showing available only' : 'Show available only'}
        </Button>
      </Box></CardContent></Card>

      <Stack direction="row" spacing={1} mb={2.5} alignItems="center" flexWrap="wrap">
        {Object.values(STATUS_PALETTE).map((status) => (
          <Stack key={status.label} direction="row" spacing={0.75} alignItems="center" sx={{ px: 1.25, py: 0.65, borderRadius: 1, bgcolor: 'background.paper', border: 1, borderColor: 'divider' }}>
            <Box sx={{ width: 9, height: 9, borderRadius: .5, bgcolor: status.color, border: '1px solid rgba(0,0,0,0.08)' }} />
            <Typography variant="caption" sx={{ color: status.textColor, fontWeight: 700 }}>{status.label}</Typography>
          </Stack>
        ))}
      </Stack>

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} mb={2.5}>
        <Card variant="outlined" sx={{ flex: 1, bgcolor: '#15140f', color: 'white' }}>
          <CardContent sx={{ py: 1.5 }}>
            <Typography variant="overline" sx={{ color: '#ffed00' }}>Current period</Typography>
            <Typography variant="h6" fontWeight={800}>{formatDate(visibleDates[0], 'short')} - {formatDate(visibleDates[visibleDates.length - 1], 'short')}</Typography>
          </CardContent>
        </Card>
        <Card variant="outlined" sx={{ flex: 1 }}>
          <CardContent sx={{ py: 1.5 }}>
            <Typography variant="overline" color="text.secondary">Fleet coverage</Typography>
            <Typography variant="h6" fontWeight={800}>{visibleAssets.length} assets shown</Typography>
          </CardContent>
        </Card>
        <Card variant="outlined" sx={{ flex: 1 }}>
          <CardContent sx={{ py: 1.5 }}>
            <Typography variant="overline" color="text.secondary">Available asset-days</Typography>
            <Typography variant="h6" fontWeight={800}>{availabilitySummary.available} of {visibleAssets.length * visibleDates.length}</Typography>
          </CardContent>
        </Card>
        <Card variant="outlined" sx={{ flex: 1 }}>
          <CardContent sx={{ py: 1.5 }}>
            <Typography variant="overline" color="text.secondary">Planned closures</Typography>
            <Typography variant="h6" fontWeight={800}>{visibleClosures.filter(closure => closure.isClosed).length} items</Typography>
          </CardContent>
        </Card>
      </Stack>

      <Card variant="outlined" sx={{ borderColor: 'divider', boxShadow: '0 12px 30px rgba(21,20,15,.06)' }}>
        <CardContent sx={{ p: 0 }}>
          <Box className="crems-scroll" sx={{ overflowX: 'auto' }}>
            <Box sx={{ minWidth: 900, p: 1.5 }}>
              <Box
                sx={{
                  display: 'grid',
                  gridTemplateColumns: `220px repeat(${visibleDates.length}, minmax(64px, 1fr))`,
                  gap: 0.75,
                  alignItems: 'stretch',
                }}
              >
                <Box sx={{ fontWeight: 900, py: 1.25, px: 1.5, borderRadius: 1, bgcolor: '#15140f', color: '#fff', position: 'sticky', left: 0, zIndex: 4 }}>Asset</Box>
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
                        bgcolor: isToday ? '#ffed00' : '#f3f2ed',
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
                      <Box onClick={() => setDetailAssetId(asset.id)} sx={{ py: 1.5, px: 1.25, borderTop: '1px solid', borderColor: 'divider', bgcolor: '#ffffff', borderLeft: '4px solid #ffed00', position: 'sticky', left: 0, zIndex: 3, cursor: 'pointer', '&:hover': { bgcolor: '#fffdeb' } }}>
                        <Typography variant="body2" fontWeight={800}>{asset.assetNumber}</Typography>
                        <Typography variant="caption" color="text.secondary">{asset.name}</Typography>
                      </Box>

                      {visibleDates.map((date) => {
                        const dateKey = toLocalDateKey(date)
                        const closureName = closureMap.get(`${asset.branchId}:${dateKey}`)
                        const status = getAssetStatusForDate(asset, date, allEvents)
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
                                boxShadow: '0 0 0 2px rgba(201,171,0,.35)',
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
            {visibleBookingEvents.length === 0 ? (
              <Typography color="text.secondary">No booking events in the selected range.</Typography>
            ) : (
              <Stack spacing={1.25}>
                {visibleBookingEvents.slice(0, 5).map((event, index) => {
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
            {visibleClosures.length === 0 ? (
              <Typography color="text.secondary">No branch closures in the selected range.</Typography>
            ) : (
              <Stack spacing={1.25}>
                {visibleClosures.slice(0, 5).map((closure) => (
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
