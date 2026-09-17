import type { ReactNode } from 'react'
import { Box, Stack, Typography } from '@mui/material'

type PageHeaderProps = {
  icon?: ReactNode
  eyebrow?: string
  title: string
  subtitle?: string
  actions?: ReactNode
  dense?: boolean
}

// Standard staff-page header: icon tile + title/subtitle on the left, actions on the right.
// Used across every staff workspace so headers read as one consistent product, not per-page one-offs.
export function PageHeader({ icon, eyebrow, title, subtitle, actions, dense }: PageHeaderProps) {
  return (
    <Stack
      direction={{ xs: 'column', md: 'row' }}
      justifyContent="space-between"
      alignItems={{ xs: 'flex-start', md: 'center' }}
      gap={2}
      sx={{
        mb: dense ? 2.5 : 3.5,
        px: { xs: 2.25, sm: 3 }, py: { xs: 2.25, sm: 2.75 },
        bgcolor: 'background.paper', border: 1, borderColor: 'divider',
        borderRadius: 1, position: 'relative', overflow: 'hidden',
        boxShadow: '0 8px 28px rgba(21,20,15,.045)',
        '&:before': { content: '""', position: 'absolute', inset: '0 auto 0 0', width: 5, bgcolor: 'secondary.main' },
        '&:after': { content: '""', position: 'absolute', width: 110, height: 110, right: -45, top: -65, border: '22px solid', borderColor: 'rgba(255,237,0,.15)', borderRadius: '50%', pointerEvents: 'none' },
      }}
    >
      <Stack direction="row" gap={1.75} alignItems="flex-start">
        {icon && (
          <Box
            sx={{
              width: 42, height: 42, borderRadius: 1, flexShrink: 0,
              display: 'grid', placeItems: 'center',
              bgcolor: '#15140f', color: '#ffed00',
              border: '1px solid #15140f',
              '& svg': { fontSize: 22 },
            }}
          >
            {icon}
          </Box>
        )}
        <Box>
          {eyebrow && (
            <Typography variant="overline" color="text.secondary" sx={{ display: 'block', lineHeight: 1.4 }}>
              {eyebrow}
            </Typography>
          )}
          <Typography variant="h4" fontWeight={800} lineHeight={1.15}>{title}</Typography>
          {subtitle && <Typography color="text.secondary" mt={.5} maxWidth={640}>{subtitle}</Typography>}
        </Box>
      </Stack>
      {actions && <Stack direction="row" gap={1.25} alignItems="center" flexWrap="wrap">{actions}</Stack>}
    </Stack>
  )
}
