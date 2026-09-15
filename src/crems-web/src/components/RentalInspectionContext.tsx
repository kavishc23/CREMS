import { Alert, Box, Stack, Typography } from '@mui/material'
export function EvidenceComparison({evidenceJson,photos}:{evidenceJson:string|undefined;photos:string[]}){
  let previous:string[]=[]
  try{
    const value=JSON.parse(evidenceJson||'{}')
    const candidates=Array.isArray(value)?value:value.photos
    if(Array.isArray(candidates))previous=candidates.filter((x:unknown):x is string=>typeof x==='string'&&x.startsWith('data:image/'))
  }catch{ /* Legacy inspection without photos. */ }
  return <Stack direction={{xs:'column',sm:'row'}} gap={2}>
    {[{title:'Pre-hire photos',items:previous},{title:'Return photos',items:photos}].map(group=><Box key={group.title} flex={1}>
      <Typography fontWeight={750} mb={1}>{group.title}</Typography>
      {!group.items.length?<Alert severity="info">No photos recorded.</Alert>:<Stack gap={1}>{group.items.map((photo,index)=><Box component="img" key={index} src={photo} alt={`${group.title} ${index+1}`} sx={{width:'100%',maxHeight:260,objectFit:'contain'}}/>)}</Stack>}
    </Box>)}
  </Stack>
}
