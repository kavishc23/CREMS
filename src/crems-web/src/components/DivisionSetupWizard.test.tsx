// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { DivisionSetupWizard } from './DivisionSetupWizard'
import { newDocument, templateService, type SetupDocument } from '../config/organizationSetup'
import { api } from '../api/client'
vi.mock('../api/client', () => ({ api: { get: vi.fn(), post: vi.fn() } }))
let documentData: SetupDocument
beforeEach(() => {
 vi.resetAllMocks(); HTMLElement.prototype.scrollTo = vi.fn()
 documentData = newDocument(); documentData.division.name = 'Training Motors'; documentData.division.code = 'TRAIN'; documentData.services = [templateService(0)]; documentData.services[0].categories = documentData.services[0].categories.slice(0, 1); documentData.services[0].categories[0].attributes = []
 vi.mocked(api.get).mockImplementation(async url => ({ data: url === '/branches' ? [{ id: 'suva', name: 'Suva', isActive: true, isPublic: true }] : url === '/administration/settings' ? [{ key: 'rentals.vatRate', value: '15' }] : structuredClone(documentData) }) as never)
 vi.mocked(api.post).mockResolvedValue({})
})
afterEach(cleanup)
const click = (name: string | RegExp) => fireEvent.click(screen.getByRole('button', { name }))
const change = (label: string | RegExp, value: string) => fireEvent.change(screen.getAllByLabelText(label).at(-1)!, { target: { value } })
async function select(label: string, name: string | RegExp) { fireEvent.mouseDown(screen.getAllByRole('combobox', { name: label }).at(-1)!); fireEvent.click(await screen.findByRole('option', { name })) }
async function open() { const saved = vi.fn(), close = vi.fn(); render(<DivisionSetupWizard open divisionId="division-1" onClose={close} onSaved={saved} />); await screen.findByLabelText(/Division name/); return { saved, close } }
it('saves a complete operating setup with financial defaults and terms', async () => {
 const { saved } = await open()
 change(/Division name/, 'Training Fleet'); change(/Division code/, 'fleet'); change('Description', 'Fleet training'); change('Contact email', 'fleet@example.test'); change('Contact phone', '12345')
 click(/^Customer presentation \(optional\)/); change('Customer display name', 'Fleet rentals'); change('Accent colour', '#123456'); change('Logo URL', 'https://example.test/logo.png')
 click('Review'); click(/^Financial defaults and terms/); change('Refundable bond (FJD)', '500'); change('VAT (%)', '15'); change('Default hire terms', 'Return clean and refuelled.')
 click('Save configuration'); await waitFor(() => expect(saved).toHaveBeenCalledOnce())
 expect(api.post).toHaveBeenCalledWith('/organization/setup', expect.objectContaining({ division: expect.objectContaining({ name: 'Training Fleet', code: 'FLEET', defaultCurrency: 'FJD', defaultTaxRate: 15, defaultBondAmount: 500, defaultRentalTerms: 'Return clean and refuelled.' }) }), { timeout: 60000 })
})
it('edits service request routing, required documents and customer questions', async () => {
 await open(); click('Customer booking options')
 await select('What can customers do?', 'Get quote only'); await select('Pricing unit', 'Week'); change('Service description (optional)', 'Training vehicle service')
 fireEvent.click(screen.getByLabelText('Service normally operates at the customer site'))
 fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Operating branches' })); fireEvent.click(screen.getByRole('option', { name: 'Suva' })); fireEvent.keyDown(screen.getByRole('listbox'), { key: 'Escape' })
 click(/^Shared division booking policy/); await select('Booking policy', 'Staff prepare a quotation for every request'); change('Minimum duration (days)', '2'); change('Maximum duration (days)', '30'); change('Quotation valid for (days)', '10'); await select('Driver/operator included', 'Optional')
 click(/^Required documents and customer questions/); change('Document name', 'Training ID'); click('Add')
 click('Add customer question'); change(/Question label/, 'Preferred contact'); change('Question code', 'CONTACT'); await select('Answer type', 'Choice'); change('Choices (one per line)', 'Email\nPhone'); change('Help text', 'Choose one'); fireEvent.click(screen.getByLabelText('Customer must answer'))
 await select('Question applies to', 'All services in this division'); click('Review'); click('Save configuration')
 await waitFor(() => expect(api.post).toHaveBeenCalled())
 const payload = vi.mocked(api.post).mock.calls[0][1] as SetupDocument
 expect(payload.flow.customerFields?.[0]).toEqual(expect.objectContaining({ label: 'Preferred contact', code: 'CONTACT', options: ['Email','Phone'], required: false, serviceCode: null }))
 expect(payload.services[0].service).toEqual(expect.objectContaining({ requiresQuote: true, defaultHireUnit: 'Week', requiresDelivery: true }))
 expect(payload.services[0].branchIds).toContain('suva')
})
it('configures calendar and usage maintenance intervals', async () => {
 await open(); click('Maintenance'); await select('When should routine maintenance be due?', 'By calendar or usage — whichever comes first')
 change(/Every \(days\)/, '120'); change(/Every \(usage units\)/, '10000'); change(/Usage unit/, 'km'); click('Review'); click('Save configuration')
 await waitFor(() => expect(api.post).toHaveBeenCalled())
 const payload = vi.mocked(api.post).mock.calls[0][1] as SetupDocument
 expect(JSON.parse(payload.services[0].service.maintenanceRulesJson)).toEqual(expect.objectContaining({ meterInterval: 10000, serviceIntervalDays: 120 }))
})
it('edits category fields and both handover and return checklists', async () => {
 await open(); click('Asset fields & checks')
 change(/Category name/, 'Training vehicles'); change(/Category code/, 'TRAIN_CAR'); await select('Default asset type', 'Passenger Vehicle')
 fireEvent.click(screen.getByRole('tab', { name: /Fields/ })); click('Add field'); change('Field name', 'Seat colour'); change('Field code', 'COLOUR'); await select('Field type', 'Choice'); change('Choices (comma separated)', 'Blue, Black'); change('Unit', 'colour'); fireEvent.click(screen.getAllByLabelText('Required').at(-1)!); fireEvent.click(screen.getAllByLabelText('Customer visible').at(-1)!); fireEvent.click(screen.getAllByLabelText('Searchable').at(-1)!); fireEvent.click(screen.getAllByLabelText('Reportable').at(-1)!)
 fireEvent.click(screen.getByRole('tab', { name: 'Handover & return' })); const names = screen.getAllByLabelText('Template name'); fireEvent.change(names[0], { target: { value: 'Training handover' } }); fireEvent.change(screen.getAllByLabelText('Inspection section')[0], { target: { value: ' Exterior ' } }); fireEvent.change(screen.getAllByLabelText('Checks (one per line)')[0], { target: { value: ' Check paint \nCheck tyres\n' } })
 click('Review'); click('Save configuration'); await waitFor(() => expect(api.post).toHaveBeenCalled())
 const payload = vi.mocked(api.post).mock.calls[0][1] as SetupDocument
 expect(payload.services[0].categories[0].attributes.at(-1)).toEqual(expect.objectContaining({ name: 'Seat colour', code: 'COLOUR', optionsJson: '["Blue","Black"]' }))
 expect(JSON.parse(payload.services[0].categories[0].templates[0].checklistJson)[0]).toMatchObject({ section: 'Exterior', items: [{ label: 'Check paint' }, { label: 'Check tyres' }] })
})
it.each([
 ['missing division', (d: SetupDocument) => { d.division.name = '' }, 'Enter the division name and code.'],
 ['missing service', (d: SetupDocument) => { d.services[0].service.name = '' }, 'Enter a name and code for every service.'],
 ['duplicate service', (d: SetupDocument) => { d.services.push(structuredClone(d.services[0])) }, 'Service codes must be unique in this division.'],
 ['invalid question', (d: SetupDocument) => { d.flow.customerFields = [{ code: '1BAD', label: 'Invalid', type: 'Text', required: true }] }, 'Complete the customer question labels, codes and choices.'],
 ['invalid hire period', (d: SetupDocument) => { d.flow.maximumHireDays = 0 }, 'Check the minimum and maximum hire duration.'],
 ['invalid category', (d: SetupDocument) => { d.services[0].categories[0].category.name = '' }, 'Complete the asset category and field names and codes.'],
])('blocks review for %s', async (_name, patch, message) => {
 patch(documentData); await open(); click('Review'); await screen.findByText(message); expect(api.post).not.toHaveBeenCalled()
})
it('retains changes when saving fails and requires confirmation before discarding', async () => {
 vi.mocked(api.post).mockRejectedValue(new Error('Offline'))
 const { saved, close } = await open(); change(/Division name/, 'Unsaved division'); click('Review'); click('Save configuration'); await screen.findByText('Configuration could not be saved. Your changes remain open.')
 expect(saved).not.toHaveBeenCalled(); click('Cancel'); await screen.findByText('Discard unsaved configuration?'); click('Keep editing'); await waitFor(() => expect(screen.queryByText('Discard unsaved configuration?')).toBeNull()); click('Cancel'); click('Discard'); expect(close).toHaveBeenCalledOnce()
})
it('supports clean cancellation and reports load failures', async () => {
 const { close } = await open(); click('Cancel'); expect(close).toHaveBeenCalledOnce(); cleanup()
 vi.mocked(api.get).mockRejectedValue(new Error('Offline')); render(<DivisionSetupWizard open divisionId="division-1" onClose={vi.fn()} onSaved={vi.fn()} />); await screen.findByText('Division setup could not be loaded. Close and reopen it to retry.')
})
it('adds, edits and removes services, categories, fields and checklists',async()=>{
 await open();click('Add service');const serviceNames=screen.getAllByLabelText(/Service name/);fireEvent.change(serviceNames.at(-1)!,{target:{value:'Training equipment'}})
 const types=screen.getAllByRole('combobox',{name:'Service type'});fireEvent.mouseDown(types.at(-1)!);fireEvent.click(screen.getByRole('option',{name:'Equipment and event hire'}));fireEvent.click(screen.getAllByLabelText('Service active').at(-1)!)
 click('Asset fields & checks');await select('Service to configure','Training equipment · Inactive');click('Add asset category');change(/Category name/,'Temporary category');change(/Category code/,'TEMP')
 fireEvent.click(screen.getByRole('tab',{name:/Fields/}));click('Add field');change('Field name','Temporary field');change('Field code','TEMP_FIELD');click('Remove field')
 fireEvent.click(screen.getByRole('tab',{name:'Handover & return'}));fireEvent.click(screen.getAllByLabelText('Active')[0]);click('Remove category')
 click('General & services');fireEvent.click(screen.getAllByRole('button',{name:'Remove service'}).at(-1)!);expect(screen.queryByDisplayValue('Training equipment')).toBeNull()
})
it.each([
 ['missing counter unit','{"meterInterval":100}',null,'Enter a usage unit for each usage-based maintenance schedule.'],
 ['invalid usage interval','{"meterInterval":0}','hours','Maintenance intervals must be positive; day intervals must be whole numbers.'],
 ['fractional day interval','{"serviceIntervalDays":1.5}','hours','Maintenance intervals must be positive; day intervals must be whole numbers.'],
])('validates %s before saving the complete configuration',async(_name,rules,unit,message)=>{
 documentData.services[0].service.maintenanceRulesJson=rules;documentData.services[0].service.meterType=unit;documentData.services[0].service.inspectionRequirementsJson='{}';documentData.services[0].categories[0].category.defaultMeterType=null
 await open();click('Review');click('Save configuration');await screen.findByText(message);expect(api.post).not.toHaveBeenCalled()
})
it('loads organization defaults for a new division, validates its empty identity and guards closing',async()=>{
 const close=vi.fn();render(<DivisionSetupWizard open divisionId={null} onClose={close} onSaved={vi.fn()}/>);await screen.findByLabelText(/Division name/);click('Continue');await screen.findByText('Enter the division name and code.');change(/Division name/,'New');click('Close division setup');await screen.findByText('Discard unsaved configuration?');click('Keep editing');await waitFor(()=>expect(screen.queryByText('Discard unsaved configuration?')).toBeNull());expect(close).not.toHaveBeenCalled()
})
