// @vitest-environment jsdom
import 'fake-indexeddb/auto'
import { afterEach, describe, expect, it } from 'vitest'
import { clearRentalDraft, readRentalDraft, saveRentalDraft } from './rentalDrafts'
afterEach(()=>sessionStorage.clear())
describe('persistent rental draft revisions',()=>{
 it('saves a snapshot rather than a mutable form reference',async()=>{const draft={step:2,photos:['photo'],readings:{meter:100}};const save=saveRentalDraft('pickup:snapshot',draft);draft.readings.meter=200;draft.photos.push('later');await save;expect(await readRentalDraft('pickup:snapshot')).toEqual({step:2,photos:['photo'],readings:{meter:100}})})
 it('serializes competing saves and deletion without resurrecting a discarded draft',async()=>{await Promise.all([saveRentalDraft('return:queue',{step:1}),saveRentalDraft('return:queue',{step:2}),clearRentalDraft('return:queue')]);expect(await readRentalDraft('return:queue')).toBeUndefined()})
 it('keeps pickup and return progress independent',async()=>{await saveRentalDraft('pickup:same',{step:2});await saveRentalDraft('return:same',{step:5});await clearRentalDraft('pickup:same');expect(await readRentalDraft('pickup:same')).toBeUndefined();expect(await readRentalDraft('return:same')).toEqual({step:5})})
 it('migrates a legacy session draft exactly once',async()=>{sessionStorage.setItem('pickup-draft:legacy',JSON.stringify({step:3}));expect(await readRentalDraft('pickup:legacy')).toEqual({step:3});expect(sessionStorage.getItem('pickup-draft:legacy')).toBeNull();expect(await readRentalDraft('pickup:legacy')).toEqual({step:3})})
 it('tolerates invalid legacy JSON and absent drafts',async()=>{sessionStorage.setItem('pickup-draft:bad','not-json');expect(await readRentalDraft('pickup:bad')).toBeUndefined();expect(await readRentalDraft('return:absent')).toBeUndefined()})
})
