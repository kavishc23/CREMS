import { Box, Button, Stack, Typography } from '@mui/material'

export function RentalChecklist({items,passed,failed,onChange}:{items:string[];passed:string[];failed:string[];onChange:(passed:string[],failed:string[])=>void}) {
  return <Stack spacing={1}>
    <Stack direction="row" spacing={1} justifyContent="flex-end" role="group" aria-label="Set all checklist results">
      <Button size="small" color="success" variant="outlined" disabled={items.length===0} onClick={()=>onChange([...items],[])}>Pass all</Button>
      <Button size="small" color="error" variant="outlined" disabled={items.length===0} onClick={()=>onChange([],[...items])}>Fail all</Button>
    </Stack>
    {items.map(label=>{
    const status=passed.includes(label)?'Pass':failed.includes(label)?'Fail':'Pending'
    return <Box key={label} sx={{display:'flex',justifyContent:'space-between',alignItems:'center',gap:2,p:1,border:1,borderColor:'divider',borderRadius:1}}>
      <Typography>{label}</Typography>
      <Stack direction="row" spacing={.5} flexShrink={0}>
        <Button size="small" color="success" variant={status==='Pass'?'contained':'outlined'} onClick={()=>onChange([...passed.filter(item=>item!==label),label],failed.filter(item=>item!==label))}>Pass</Button>
        <Button size="small" color="error" variant={status==='Fail'?'contained':'outlined'} onClick={()=>onChange(passed.filter(item=>item!==label),[...failed.filter(item=>item!==label),label])}>Fail</Button>
        {status!=='Pending'&&<Button size="small" onClick={()=>onChange(passed.filter(item=>item!==label),failed.filter(item=>item!==label))}>Clear</Button>}
      </Stack>
    </Box>
  })}</Stack>
}
