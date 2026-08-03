import { Box, Card, CardContent, Typography } from '@mui/material'

export function PlaceholderPage({ title }: { title: string }) {
  return (
    <Box sx={{ p: { xs: 2, md: 4 }, maxWidth: 1440, mx: 'auto' }}>
      <Typography variant="h4" fontWeight={700} mb={3}>{title}</Typography>
      <Card variant="outlined">
        <CardContent sx={{ py: 6, textAlign: 'center' }}>
          <Typography color="text.secondary">This module will be implemented after client requirements are confirmed.</Typography>
        </CardContent>
      </Card>
    </Box>
  )
}

