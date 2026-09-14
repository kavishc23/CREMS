import { Box, ButtonBase, Typography } from '@mui/material'
import ArrowForwardOutlined from '@mui/icons-material/ArrowForwardOutlined'

type QueueItem = { label: string; count: number; detail: string; urgent?: boolean; onOpen: () => void }

export function WorkQueueSummary({ items, loading }: { items: QueueItem[]; loading: boolean }) {
  return <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2,minmax(0,1fr))', lg: `repeat(${items.length}, minmax(0,1fr))` }, gap: 1.5, mb: 3 }}>
    {items.map(item => <ButtonBase key={item.label} onClick={item.onOpen} aria-label={`Open ${item.label}`} sx={{ textAlign: 'left', justifyContent: 'space-between', p: 2.5, gap: 2, borderRadius: 1.5, border: '1px solid', borderColor: item.urgent && item.count > 0 ? '#e8c6bf' : 'divider', bgcolor: item.urgent && item.count > 0 ? '#fff8f5' : 'background.paper', '&:hover': { bgcolor: '#faf9e9' }, '&.Mui-focusVisible': { outline: '2px solid #807400', outlineOffset: 2 } }}>
      <Box><Typography variant="overline" color="text.secondary">{item.label}</Typography><Typography variant="h4" sx={{ my: .5, color: item.urgent && item.count > 0 ? 'error.main' : 'text.primary' }}>{loading ? '—' : item.count}</Typography><Typography variant="caption" color="text.secondary">{item.detail}</Typography></Box>
      <ArrowForwardOutlined sx={{ color: '#82765c', fontSize: 20 }} />
    </ButtonBase>)}
  </Box>
}
