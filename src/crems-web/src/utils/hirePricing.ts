export const hireUnits=['Hour','Day','Week','Month','Unit','Trip','Kilometre','Tonne','SquareMetre','Fixed'] as const
export function measuredUnit(unit:string) { return ['Unit','Trip','Kilometre','Tonne','SquareMetre'].includes(unit) }
export function hireQuantity(unit:string,start:string,end:string,measured=1) {
  const a=new Date(start),b=new Date(end)
  if(!Number.isFinite(a.getTime())||!Number.isFinite(b.getTime())||b<=a)return 0
  if(measuredUnit(unit))return measured>0?measured:0
  if(unit==='Fixed')return 1
  if(unit==='Month'){
    let months=(b.getUTCFullYear()-a.getUTCFullYear())*12+b.getUTCMonth()-a.getUTCMonth()
    const anniversary=new Date(a);anniversary.setUTCDate(1);anniversary.setUTCMonth(a.getUTCMonth()+months)
    anniversary.setUTCDate(Math.min(a.getUTCDate(),new Date(Date.UTC(anniversary.getUTCFullYear(),anniversary.getUTCMonth()+1,0)).getUTCDate()))
    if(anniversary<b)months++
    return Math.max(1,months)
  }
  const hours=(b.getTime()-a.getTime())/3600000
  return Math.max(1,Math.ceil(hours/(unit==='Hour'?1:unit==='Week'?168:24)))
}
