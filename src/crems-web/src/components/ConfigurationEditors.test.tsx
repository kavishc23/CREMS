// @vitest-environment jsdom
import { useState } from 'react'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { ConfigurationFields, RequiredDocumentsEditor, MaintenanceIntervalEditor } from './ConfigurationEditors'
import { ConfiguredCustomerFields } from './ConfiguredCustomerFields'
import { MaintenanceSchedule, InspectionReadings } from './ServiceRequirements'
import { DivisionControls, DivisionBranding } from './DivisionControls'
import { MaintenanceTaxFields } from '../pages/MaintenanceTaxFields'
import type { TaxSettings } from '../pages/maintenanceTax'
import { newService } from '../config/organizationSetup'
afterEach(cleanup)
async function select(label: string, name: string | RegExp) { fireEvent.mouseDown(screen.getByRole('combobox', { name: label })); fireEvent.click(await screen.findByRole('option', { name })) }
function JsonEditor({ kind, initial }: { kind: string; initial: string }) { const [value, setValue] = useState(initial); return <><output>{value}</output>{kind === 'fields' ? <ConfigurationFields label="Settings" description="Training settings" value={value} onChange={setValue} /> : kind === 'documents' ? <RequiredDocumentsEditor value={value} onChange={setValue} /> : <MaintenanceIntervalEditor value={value} onChange={setValue} />}</> }
it('edits primitive settings, preserves complex data, adds and removes named values', () => {
 render(<JsonEditor kind="fields" initial='{"enabled":true,"limit":3,"note":"Text","nested":{"a":1}}' />)
 fireEvent.click(screen.getByLabelText('Enabled')); fireEvent.change(screen.getByDisplayValue('3'), { target: { value: '5' } }); fireEvent.change(screen.getByDisplayValue('Text'), { target: { value: 'Updated' } })
 fireEvent.change(screen.getByLabelText('Setting name'), { target: { value: 'NewKey' } }); fireEvent.change(screen.getByLabelText('Value'), { target: { value: ' New value ' } }); fireEvent.click(screen.getByRole('button', { name: 'Add' }))
 expect(JSON.parse(screen.getByRole('status').textContent!)).toEqual({ enabled: false, limit: 5, note: 'Updated', nested: { a: 1 }, NewKey: 'New value' })
 fireEvent.click(screen.getAllByRole('button', { name: 'Remove' })[0]); expect(JSON.parse(screen.getByRole('status').textContent!)).not.toHaveProperty('enabled')
})
it.each(['broken', 'null', '[]'])('handles invalid object settings %s', initial => { render(<JsonEditor kind="fields" initial={initial} />); expect(screen.getByLabelText('Setting name')).toBeTruthy() })
it('adds unique required documents, ignores non-string entries and deletes a requirement', () => {
 render(<JsonEditor kind="documents" initial='["Licence",123]' />)
 fireEvent.change(screen.getByLabelText('Document name'), { target: { value: ' licence ' } }); expect((screen.getByRole('button', { name: 'Add' }) as HTMLButtonElement).disabled).toBe(true)
 fireEvent.change(screen.getByLabelText('Document name'), { target: { value: '  Training ID  ' } }); fireEvent.click(screen.getByRole('button', { name: 'Add' }))
 expect(JSON.parse(screen.getByRole('status').textContent!)).toEqual(['Licence','Training ID'])
 fireEvent.click(screen.getAllByTestId('CancelIcon')[0]); expect(JSON.parse(screen.getByRole('status').textContent!)).toEqual(['Training ID'])
})
it.each(['broken', '{}'])('handles invalid required document lists %s', initial => { render(<JsonEditor kind="documents" initial={initial} />); expect(screen.getByText('No extra documents required.')).toBeTruthy() })
it('sets and clears maintenance intervals while preserving unrelated settings', () => {
 render(<JsonEditor kind="interval" initial='{"custom":true}' />)
 for (const [label, value] of [['Calendar service interval (days)','90'],['Meter service interval','500']]) fireEvent.change(screen.getByLabelText(label), { target: { value } })
 expect(JSON.parse(screen.getByRole('status').textContent!)).toEqual({ custom: true, serviceIntervalDays: 90, meterInterval: 500 })
 for (const label of ['Calendar service interval (days)','Meter service interval']) fireEvent.change(screen.getByLabelText(label), { target: { value: '' } })
 expect(JSON.parse(screen.getByRole('status').textContent!)).toEqual({ custom: true })
})
it('renders and edits all supported customer answer types', async () => {
 const change = vi.fn(); render(<ConfiguredCustomerFields questions={['Text','LongText','Number','Integer','Date','Choice','Boolean'].map(type => ({ code: type, label: type, type, required: false, options: ['Option A'], help: type === 'Text' ? 'Help' : undefined }))} answers={{ Text: 'Existing' }} onChange={change} />)
 for (const type of ['Text','LongText','Number','Integer','Date']) fireEvent.change(screen.getByLabelText(type), { target: { value: type === 'Date' ? '2026-11-16' : ['Number','Integer'].includes(type) ? '2' : 'Answer' } })
 await select('Choice','Option A'); await select('Boolean','Yes')
 expect(change).toHaveBeenLastCalledWith({ Text: 'Existing', Boolean: 'true' })
 expect(screen.getByLabelText('Integer').getAttribute('step')).toBe('1')
})
function Requirements() { const [value, setValue] = useState(newService().service); return <><output>{JSON.stringify(value)}</output><MaintenanceSchedule value={value} onChange={patch => setValue(current => ({ ...current, ...patch }))} /><InspectionReadings value={value} onChange={patch => setValue(current => ({ ...current, ...patch }))} /></> }
it('switches maintenance modes, preserves usage requirements and edits units', async () => {
 render(<Requirements />)
 await select('When should routine maintenance be due?', 'By calendar or usage — whichever comes first')
 fireEvent.change(screen.getByLabelText(/Every \(days\)/), { target: { value: '120' } }); fireEvent.change(screen.getByLabelText(/Every \(usage units\)/), { target: { value: '' } })
 fireEvent.change(screen.getAllByLabelText(/Usage unit/)[0], { target: { value: 'hours' } })
 expect(screen.getByText(/A usage counter is enabled/)).toBeTruthy()
 await select('When should routine maintenance be due?', 'By calendar — after a number of days')
 await select('Additional readings at handover and return','Fuel / battery level')
 await select('Additional readings at handover and return','Usage counter and fuel / battery level')
 fireEvent.change(screen.getByLabelText(/Usage unit/), { target: { value: '' } })
 await select('When should routine maintenance be due?','On demand — staff create work when needed')
 await select('Additional readings at handover and return','None — use the condition checklists')
 const data = JSON.parse(screen.getByRole('status').textContent!); expect(JSON.parse(data.maintenanceRulesJson)).toEqual({}); expect(JSON.parse(data.inspectionRequirementsJson)).toEqual(expect.objectContaining({ tracksMeter: false, tracksFuel: false }))
})
function Branding() { const [value, setValue] = useState<{ capabilities: number | string; isActive: boolean; isPublic: boolean }>({ capabilities: 1, isActive: true, isPublic: true }), [brand, setBrand] = useState('{}'); return <><output>{JSON.stringify({ ...value, brand })}</output><DivisionControls value={value} onChange={patch => setValue(current => ({ ...current, ...patch }))} compact /><DivisionBranding value={brand} onChange={setBrand} compact /></> }
it('changes operational scope, publication and customer branding', async () => {
 render(<Branding />); fireEvent.click(screen.getByLabelText('Division active')); fireEvent.click(screen.getByLabelText('List on customer website'))
 fireEvent.mouseDown(screen.getByRole('combobox',{name:'Operational scope'})); fireEvent.click(screen.getAllByRole('option')[1]); fireEvent.keyDown(screen.getByRole('listbox'),{key:'Escape'})
 for (const [label,value] of [['Customer display name','Training'],['Accent colour','#123456'],['Logo URL','https://example.test/logo.png']]) fireEvent.change(screen.getByLabelText(label),{target:{value}})
 const data=JSON.parse(screen.getByRole('status').textContent!);expect(data.isActive).toBe(false);expect(data.isPublic).toBe(false);expect(JSON.parse(data.brand).displayName).toBe('Training')
})
function Tax() { const [value,setValue]=useState<TaxSettings>({mode:'Manual',rate:'0',categories:0,reason:''}),[manual,setManual]=useState('0');return <><output>{JSON.stringify({value,manual})}</output><MaintenanceTaxFields value={value} onChange={setValue} manualTax={manual} onManualTax={setManual} defaultRate={15} issuedStockCost={10} tax={5} taxable={100}/></> }
it('edits manual tax and switches to automatic inclusive tax with category flags',async()=>{
 render(<Tax/>);fireEvent.change(screen.getByLabelText('Tax (FJD)'),{target:{value:'20'}});fireEvent.change(screen.getByLabelText('Tax override reason'),{target:{value:'Invoice tax'}})
 await select('Tax treatment','Tax-inclusive — tax already included');expect((screen.getByLabelText(/Tax rate/) as HTMLInputElement).value).toBe('15')
 fireEvent.change(screen.getByLabelText(/Tax rate/),{target:{value:'10'}});fireEvent.click(screen.getByRole('button',{name:'Use division rate (15%)'}));fireEvent.click(screen.getAllByRole('checkbox')[0]);fireEvent.click(screen.getAllByRole('checkbox')[0]);await select('Tax treatment','No tax')
 expect(screen.queryByLabelText(/Tax rate/)).toBeNull()
})
