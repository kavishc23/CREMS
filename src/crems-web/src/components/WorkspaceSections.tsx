import { Box, Button, Stack } from '@mui/material'

export function WorkspaceSections({ labels, active, onChange, disabled = [] }: { labels: string[]; active: number; onChange: (index: number) => void; disabled?: number[] }) {
  return <Stack component="nav" aria-label="Form sections" direction={{ xs: 'row', md: 'column' }} spacing={1} sx={{ width: { md: 230 }, flexShrink: 0, p: 2, bgcolor: '#f3f2ed', borderRight: { md: 1 }, borderColor: 'divider', overflowX: 'auto' }}>
    {labels.map((label, index) => <Button key={label} disabled={disabled.includes(index)} onClick={() => onChange(index)} aria-current={active === index ? 'step' : undefined} sx={{ justifyContent: 'flex-start', textAlign: 'left', minWidth: { xs: 'max-content', md: 0 }, py: 1.5, px: 2, gap: 1.25, color: 'text.primary', bgcolor: active === index ? 'secondary.main' : 'transparent', fontWeight: active === index ? 800 : 500, '&:hover': { bgcolor: active === index ? 'secondary.main' : 'action.hover' } }}><Box component="span" sx={{ width: 25, height: 25, flexShrink: 0, border: 1, borderColor: 'divider', borderRadius: '50%', display: 'grid', placeItems: 'center' }}>{index + 1}</Box>{label}</Button>)}
  </Stack>
}
