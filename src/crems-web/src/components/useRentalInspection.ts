import { useEffect, useState } from 'react'

import { api } from '../api/client'

type Template = { id:string; name:string; stage:string; checklistJson:string }
type Context = { templates:Template[]; assetCurrentMeterReading:number|null; preHire:null|{conditionNotes:string|null;damageNotes:string|null;evidenceJson:string;meterReading:number|null;fuelLevelPercent:number|null} }
export function useRentalInspection(bookingId:string|undefined, stage:'PreHire'|'PostHire'){
  const [state,setState]=useState<{id?:string;data?:Context;error?:string}>({})
  useEffect(()=>{
    if(!bookingId)return
    let active=true
    api.get<Context>(`/rentals/${bookingId}/inspection-context`).then(response=>{
      if(active)setState({id:bookingId,data:response.data})
    }).catch(()=>{if(active)setState({id:bookingId,error:'Inspection templates and starting evidence could not be loaded. Close and reopen this workflow to retry.'})})
    return()=>{active=false}
  },[bookingId])
  const current=state.id===bookingId?state:undefined
  const template=current?.data?.templates?.find(x=>x.stage===stage)
  let items:string[]|undefined
  let templateError:string|undefined
  if(template){
    try{
      const sections=JSON.parse(template.checklistJson) as {section:string;items:{label:string}[]}[]
      items=sections.flatMap(section=>section.items.map(item=>`${section.section}: ${item.label}`))
      if(!items.length||items.some(item=>item.includes('undefined')))throw new Error('Invalid checklist')
    }catch{templateError='The configured inspection checklist is invalid. Correct the category template before continuing.'}
  }
  return {items,templateName:template?.name,preHire:current?.data?.preHire,assetCurrentMeterReading:current?.data?.assetCurrentMeterReading,error:current?.error||templateError,loading:Boolean(bookingId&&!current)}
}
