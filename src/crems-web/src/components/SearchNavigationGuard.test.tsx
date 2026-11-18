// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { useSearchNavigationGuard } from './useSearchNavigationGuard'
function View({active,discard,leave}:{active:boolean;discard:()=>void;leave:()=>void}){const guard=useSearchNavigationGuard(active,discard);return <><button onClick={guard.markResults}>Mark results</button><button onClick={()=>guard.leave(leave)}>Leave</button></>}
beforeEach(()=>{window.history.replaceState({},'','/');vi.spyOn(window,'confirm').mockReturnValue(true)});afterEach(()=>{cleanup();vi.restoreAllMocks();window.history.replaceState({},'','/')})
it('marks one ephemeral result entry and discards it on confirmed navigation',()=>{
 const discard=vi.fn(),leave=vi.fn();render(<View active discard={discard} leave={leave}/>);fireEvent.click(screen.getByText('Mark results'));fireEvent.click(screen.getByText('Mark results'));expect(window.location.search).toBe('?rentalResults=1');fireEvent.click(screen.getByText('Leave'));expect(discard).toHaveBeenCalledOnce();expect(leave).toHaveBeenCalledOnce();expect(window.location.search).toBe('')
})
it('keeps results when navigation is cancelled and suppresses its restoring popstate',()=>{
 vi.mocked(window.confirm).mockReturnValue(false);const forward=vi.spyOn(window.history,'forward').mockImplementation(()=>{}),discard=vi.fn(),leave=vi.fn();render(<View active discard={discard} leave={leave}/>);fireEvent.click(screen.getByText('Mark results'));fireEvent.click(screen.getByText('Leave'));expect(leave).not.toHaveBeenCalled();act(()=>window.dispatchEvent(new PopStateEvent('popstate',{state:{}})));expect(forward).toHaveBeenCalledOnce();act(()=>window.dispatchEvent(new PopStateEvent('popstate',{state:{}})));expect(discard).not.toHaveBeenCalled()
})
it('guards unloading, permits a result-state pop and discards on a real back navigation',()=>{
 const discard=vi.fn();render(<View active discard={discard} leave={vi.fn()}/>);fireEvent.click(screen.getByText('Mark results'));const event=new Event('beforeunload',{cancelable:true});window.dispatchEvent(event);expect(event.defaultPrevented).toBe(true);act(()=>window.dispatchEvent(new PopStateEvent('popstate',{state:{cremsSearchActive:true}})));expect(discard).not.toHaveBeenCalled();act(()=>window.dispatchEvent(new PopStateEvent('popstate',{state:{}})));expect(discard).toHaveBeenCalledOnce()
})
it('cleans stale inactive entries and discards restored cached pages',()=>{
 window.history.replaceState({cremsSearchActive:true},'','/?rentalResults=1');const discard=vi.fn(),leave=vi.fn();render(<View active={false} discard={discard} leave={leave}/>);expect(window.location.search).toBe('');fireEvent.click(screen.getByText('Leave'));expect(leave).toHaveBeenCalledOnce();act(()=>window.dispatchEvent(new PopStateEvent('popstate',{state:{cremsSearchActive:true}})));expect(discard).toHaveBeenCalledOnce();const event=new Event('pageshow');Object.defineProperty(event,'persisted',{value:true});act(()=>window.dispatchEvent(event));expect(discard).toHaveBeenCalledTimes(2)
})
