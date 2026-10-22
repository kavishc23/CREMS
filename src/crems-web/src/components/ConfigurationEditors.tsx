import { useState } from 'react'
import AddOutlined from '@mui/icons-material/AddOutlined'
import { Alert, Box, Button, Checkbox, Chip, FormControlLabel, Stack, TextField, Typography } from '@mui/material'

function readObject(json:string):Record<string,unknown>{try{const value=JSON.parse(json);return value&&typeof value==='object'&&!Array.isArray(value)?value:{}}catch{return {}}}
function readStrings(json:string):string[]{try{const value=JSON.parse(json);return Array.isArray(value)?value.filter(x=>typeof x==='string'):[]}catch{return []}}

export function ConfigurationFields({label,description,value,onChange}:{label:string;description:string;value:string;onChange:(value:string)=>void}){
  const [key,setKey]=useState(''),[initial,setInitial]=useState('')
  const entries=readObject(value)
  function update(name:string,next:unknown){onChange(JSON.stringify({...entries,[name]:next}))}
  function remove(name:string){const next={...entries};delete next[name];onChange(JSON.stringify(next))}
  return <Box><Typography variant="subtitle2" fontWeight={750}>{label}</Typography><Typography variant="body2" color="text.secondary" mb={1}>{description}</Typography><Stack gap={1}>{Object.entries(entries).map(([name,item])=><Stack key={name} direction={{xs:'column',sm:'row'}} alignItems={{sm:'center'}} gap={1}><Typography sx={{minWidth:150}} variant="body2" fontWeight={650}>{name.replace(/([a-z])([A-Z])/g,'$1 $2')}</Typography>{typeof item==='boolean'?<FormControlLabel control={<Checkbox checked={item} onChange={e=>update(name,e.target.checked)}/>} label="Enabled"/>:typeof item==='number'?<TextField size="small" type="number" value={item} onChange={e=>update(name,Number(e.target.value))} fullWidth/>:typeof item==='string'?<TextField size="small" value={item} onChange={e=>update(name,e.target.value)} fullWidth/>:<Chip label="Complex value preserved" size="small"/>}<Button size="small" color="error" onClick={()=>remove(name)}>Remove</Button></Stack>)}<Stack direction={{xs:'column',sm:'row'}} gap={1}><TextField size="small" label="Setting name" value={key} onChange={e=>setKey(e.target.value)} fullWidth/><TextField size="small" label="Value" value={initial} onChange={e=>setInitial(e.target.value)} fullWidth/><Button startIcon={<AddOutlined/>} disabled={!key.trim()||key in entries} onClick={()=>{update(key.trim(),initial.trim());setKey('');setInitial('')}}>Add</Button></Stack></Stack></Box>
}

export function RequiredDocumentsEditor({value,onChange}:{value:string;onChange:(value:string)=>void}){
  const [next,setNext]=useState('')
  const items=readStrings(value)
  return <Box><Typography variant="subtitle2" fontWeight={750}>Required customer documents</Typography><Typography variant="body2" color="text.secondary" mb={1}>Name the documents customers must supply for this service.</Typography><Stack direction="row" flexWrap="wrap" gap={.75} mb={1}>{items.map(item=><Chip key={item} label={item} onDelete={()=>onChange(JSON.stringify(items.filter(x=>x!==item)))}/>)}{!items.length&&<Typography variant="body2" color="text.secondary">No extra documents required.</Typography>}</Stack><Stack direction="row" gap={1}><TextField size="small" label="Document name" value={next} onChange={e=>setNext(e.target.value)} fullWidth/><Button startIcon={<AddOutlined/>} disabled={!next.trim()||items.some(x=>x.toLowerCase()===next.trim().toLowerCase())} onClick={()=>{onChange(JSON.stringify([...items,next.trim()]));setNext('')}}>Add</Button></Stack></Box>
}

export function MaintenanceIntervalEditor({value,onChange}:{value:string;onChange:(value:string)=>void}){
  const settings=readObject(value),interval=typeof settings.meterInterval==='number'?settings.meterInterval:''
  return <Box><Typography variant="subtitle2" fontWeight={750}>Preventive maintenance</Typography><Typography variant="body2" color="text.secondary" mb={1}>Automatically create a service job when the meter reaches this interval. Leave empty to disable.</Typography><Stack gap={1.5}><TextField size="small" type="number" label="Calendar service interval (days)" inputProps={{min:1,max:3650}} value={typeof settings.serviceIntervalDays==='number'?settings.serviceIntervalDays:''} onChange={e=>{const next={...settings};if(e.target.value)next.serviceIntervalDays=Number(e.target.value);else delete next.serviceIntervalDays;onChange(JSON.stringify(next))}} fullWidth/><TextField size="small" type="number" label="Meter service interval" inputProps={{min:1,max:100000000}} value={interval} onChange={e=>{const next={...settings};if(e.target.value)next.meterInterval=Number(e.target.value);else delete next.meterInterval;onChange(JSON.stringify(next))}} fullWidth/></Stack>{Object.keys(settings).some(x=>x!=='meterInterval'&&x!=='serviceIntervalDays')&&<Alert severity="info" sx={{mt:1}}>Other existing maintenance settings are preserved. The configured meter and calendar intervals trigger automatic jobs.</Alert>}</Box>
}
