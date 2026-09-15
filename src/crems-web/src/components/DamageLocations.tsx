import { Box, Button, Stack, Typography } from '@mui/material'
const zones=['Front left','Front','Front right','Left side','Interior / controls','Right side','Rear left','Rear','Rear right']
export function DamageLocations({value,onChange}:{value:string[];onChange:(value:string[])=>void}){
  return <Stack gap={1}><Typography fontWeight={750}>Damage locations</Typography><Typography variant="body2" color="text.secondary">Mark affected areas and describe the damage below. Leave clear when no damage is present.</Typography>
    <Box sx={{display:'grid',gridTemplateColumns:'repeat(3,1fr)',gap:1}}>{zones.map(zone=><Button key={zone} aria-pressed={value.includes(zone)} variant={value.includes(zone)?'contained':'outlined'} onClick={()=>onChange(value.includes(zone)?value.filter(x=>x!==zone):[...value,zone])}>{zone}</Button>)}</Box>
  </Stack>
}
