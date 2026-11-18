// @vitest-environment jsdom
import React from 'react'
import { afterEach,beforeEach,describe,expect,it,vi } from 'vitest'
import { cleanup,fireEvent,render,screen,waitFor } from '@testing-library/react'
import { ChangePasswordPage } from './ChangePasswordPage'
const m=vi.hoisted(()=>({post:vi.fn(),refreshSession:vi.fn(),logout:vi.fn()}))
vi.mock('../api/client',()=>({api:{post:m.post}}));vi.mock('../auth/AuthContext',()=>({useAuth:()=>m}))
afterEach(cleanup);beforeEach(()=>{vi.clearAllMocks();m.post.mockResolvedValue({data:{challengeId:'challenge',maskedDestination:'s***@example.test'}})})
function fill(current='Temporary123',next='Replacement123',confirm=next){for(const[label,value]of [[/Temporary password/,current],[/^New password/,next],[/Confirm new password/,confirm]]as const)fireEvent.change(screen.getByLabelText(label),{target:{value}})}
describe('mandatory password replacement',()=>{
 it('rejects an unchanged password and mismatched confirmation before requesting email',async()=>{render(<ChangePasswordPage/>);fill('Temporary123','Temporary123');fireEvent.click(screen.getByText('Send verification code'));expect(await screen.findByText('Your new password must be different from your current password.')).toBeTruthy();expect(m.post).not.toHaveBeenCalled();fill('Temporary123','Replacement123','Mismatch123');fireEvent.click(screen.getByText('Send verification code'));expect(await screen.findByText('The new passwords do not match.')).toBeTruthy()})
 it('requests and confirms a six-digit verification challenge before refreshing the session',async()=>{render(<ChangePasswordPage/>);fill();fireEvent.click(screen.getByText('Send verification code'));const code=await screen.findByLabelText(/Verification code/);expect(m.post).toHaveBeenCalledWith('/auth/change-password/request',{currentPassword:'Temporary123',newPassword:'Replacement123'});fireEvent.change(code,{target:{value:'a12345678'}});expect((code as HTMLInputElement).value).toBe('123456');fireEvent.click(screen.getByText('Verify and continue'));await waitFor(()=>expect(m.post).toHaveBeenCalledWith('/auth/change-password/confirm',{challengeId:'challenge',code:'123456',currentPassword:'Temporary123',newPassword:'Replacement123'}));await waitFor(()=>expect(m.refreshSession).toHaveBeenCalledOnce())})
 it.each([[{isAxiosError:true,response:{data:{errors:{password:['Password policy rejected']}}}},'Password policy rejected'],[new Error('offline'),'Unable to change the password.']])('explains server rejection',async(error,message)=>{m.post.mockRejectedValue(error);render(<ChangePasswordPage/>);fill();fireEvent.click(screen.getByText('Send verification code'));expect(await screen.findByText(message)).toBeTruthy();expect(m.refreshSession).not.toHaveBeenCalled()})
 it('allows signing out without completing the replacement',()=>{render(<ChangePasswordPage/>);fireEvent.click(screen.getByText('Sign out'));expect(m.logout).toHaveBeenCalledOnce()})
})
