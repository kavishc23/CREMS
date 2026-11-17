import { useEffect, useId, useRef } from 'react'
import { Button } from '@mui/material'
import './inlineValidation.css'

type Control = HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement
type InlineIssue = { target: string; message: string }

const controlSelector = 'input:not([type="hidden"]), select, textarea, [role="combobox"], [tabindex="0"], button'
const hostIssueCount = new WeakMap<HTMLElement, number>()
const controlIssueCount = new WeakMap<HTMLElement, { count: number; previous: string | null }>()
const isControl = (element: EventTarget | null): element is Control =>
  element instanceof HTMLInputElement || element instanceof HTMLSelectElement || element instanceof HTMLTextAreaElement

function focusIssue(target: HTMLElement) {
  for (let panel = target.closest('.MuiAccordion-root'); panel; panel = panel.parentElement?.closest('.MuiAccordion-root') ?? null) {
    panel.querySelector<HTMLButtonElement>('.MuiAccordionSummary-root[aria-expanded="false"]')?.click()
  }
  for (let details = target.closest('details'); details; details = details.parentElement?.closest('details') ?? null) details.open = true
  requestAnimationFrame(() => {
    if (!target.isConnected) return
    const control = issueControl(target)
    target.scrollIntoView({ block: 'center', inline: 'nearest' })
    control.focus({ preventScroll: true })
  })
}

function issueControl(target: HTMLElement) {
  if (target.matches('.MuiSelect-nativeInput')) {
    return target.closest('.MuiInputBase-root')?.querySelector<HTMLElement>('[role="combobox"]') ?? target
  }
  if (target.matches(controlSelector)) return target
  return Array.from(target.querySelectorAll<HTMLElement>(controlSelector))
    .find(element => !element.matches(':disabled') && element.getClientRects().length) ?? target
}

function showIssue(target: HTMLElement, id: string, message: string) {
  const host = target.closest<HTMLElement>('.MuiFormControl-root, .MuiFormControlLabel-root') ?? target
  const control = issueControl(target)
  const output = document.createElement('span')
  output.id = id
  output.className = 'crems-field-message'
  output.textContent = message
  if (host.matches('.MuiFormControlLabel-root')) host.after(output)
  else if (host.matches('.MuiFormControl-root')) host.append(output)
  else if (host === target && !host.matches(controlSelector)) host.append(output)
  else host.after(output)
  const describedBy = new Set((control.getAttribute('aria-describedby') ?? '').split(/\s+/).filter(Boolean))
  describedBy.add(id)
  control.setAttribute('aria-describedby', [...describedBy].join(' '))
  const controlState = controlIssueCount.get(control) ?? { count: 0, previous: control.getAttribute('aria-invalid') }
  controlState.count++
  controlIssueCount.set(control, controlState)
  control.setAttribute('aria-invalid', 'true')
  hostIssueCount.set(host, (hostIssueCount.get(host) ?? 0) + 1)
  host.dataset.cremsInvalid = ''
  return () => {
    output.remove()
    const remaining = (hostIssueCount.get(host) ?? 1) - 1
    if (remaining) hostIssueCount.set(host, remaining)
    else { hostIssueCount.delete(host); delete host.dataset.cremsInvalid }
    if (--controlState.count === 0) {
      controlIssueCount.delete(control)
      if (controlState.previous === null) control.removeAttribute('aria-invalid')
      else control.setAttribute('aria-invalid', controlState.previous)
    }
    const descriptions = (control.getAttribute('aria-describedby') ?? '').split(/\s+/).filter(value => value && value !== id)
    if (descriptions.length) control.setAttribute('aria-describedby', descriptions.join(' '))
    else control.removeAttribute('aria-describedby')
  }
}

function nativeMessage(control: Control) {
  const label = control.labels?.[0]?.textContent?.trim()
  const name = (label || control.getAttribute('aria-label') || 'this field').replace(/\s*\*\s*$/, '').toLowerCase()
  const validity = control.validity
  if (control instanceof HTMLInputElement && control.type === 'number') {
    if (validity.badInput) return 'Enter a number.'
    if (validity.rangeUnderflow) return 'Enter ' + control.min + ' or more.'
    if (validity.rangeOverflow) return 'Enter ' + control.max + ' or less.'
  }
  if (validity.valueMissing) {
    if (control instanceof HTMLInputElement && control.type === 'checkbox') return 'Select this confirmation to continue.'
    if (control instanceof HTMLInputElement && control.type === 'file') return 'Attach a file.'
    const choice = control instanceof HTMLSelectElement ||
      control instanceof HTMLInputElement && (control.type === 'radio' || control.matches('.MuiSelect-nativeInput'))
    return (choice ? 'Select ' : 'Enter ') + name.replace(/^(enter|select|choose)\s+/, '') + '.'
  }
  if (validity.typeMismatch && control instanceof HTMLInputElement && control.type === 'email') return 'Enter a valid email address.'
  if (validity.patternMismatch) return 'Use the format shown for ' + name + '.'
  return control.validationMessage
}

export function NativeFormValidation() {
  useEffect(() => {
    const active = new Map<Control, { id: string; message: string; clear: () => void }>()
    const navigation = new Map<HTMLFormElement, HTMLButtonElement>()
    let sequence = 0
    let first: Control | undefined
    function refreshNavigation(form: HTMLFormElement) {
      const fields = [...active.keys()].filter(control => control.form === form)
      const existing = navigation.get(form)
      if (!fields.length || !form.isConnected) {
        existing?.remove()
        navigation.delete(form)
        return
      }
      const text = fields.length === 1 ? 'Go to next issue' : 'Go to next issue (' + fields.length + ' remaining)'
      if (existing?.isConnected) {
        if (existing.textContent !== text) existing.textContent = text
        return
      }
      const button = document.createElement('button')
      button.type = 'button'
      button.className = 'crems-issue-navigation'
      button.textContent = text
      let last: Control | undefined
      button.addEventListener('click', () => {
        const current = [...active.keys()].filter(control => control.form === form)
        if (!current.length) return
        const start = current.indexOf(last ?? current[0])
        last = current[(start + 1) % current.length]
        focusIssue(last)
      })
      const host = form.closest('[role="dialog"]')?.querySelector('.MuiDialogActions-root') ?? form
      host.append(button)
      navigation.set(form, button)
    }
    function update(control: Control) {
      const previous = active.get(control)
      const message = control.willValidate && !control.validity.valid ? nativeMessage(control) : ''
      if (previous?.message === message) return
      previous?.clear()
      active.delete(control)
      if (message) {
        const id = previous?.id ?? 'crems-native-error-' + ++sequence
        active.set(control, { id, message, clear: showIssue(control, id, message) })
      }
      if (control.form) refreshNavigation(control.form)
    }
    function invalid(event: Event) {
      if (!isControl(event.target) || !event.target.form) return
      event.preventDefault()
      const target = event.target
      if (target instanceof HTMLInputElement && target.type === 'radio' && target.name &&
        [...active.keys()].some(control => control instanceof HTMLInputElement && control.type === 'radio' &&
          control !== target && control.form === target.form && control.name === target.name)) return
      update(target)
      if (first) return
      first = target
      queueMicrotask(() => {
        const target = first
        first = undefined
        if (target?.isConnected) focusIssue(target)
      })
    }
    function edited(event: Event) {
      const target = event.target
      if (!isControl(target)) return
      if (active.has(target)) update(target)
      if (target instanceof HTMLInputElement && target.type === 'radio' && target.name) {
        for (const control of active.keys()) {
          if (control instanceof HTMLInputElement && control.type === 'radio' &&
            control.form === target.form && control.name === target.name) update(control)
        }
      }
    }
    function blurred(event: FocusEvent) {
      if (!isControl(event.target) || !event.target.form || !event.target.willValidate) return
      if (event.target instanceof HTMLInputElement && ['checkbox', 'radio'].includes(event.target.type)) return
      if (!event.target.validity.valid) update(event.target)
    }
    function prune() {
      for (const [control, issue] of active) {
        if (!control.isConnected) { issue.clear(); active.delete(control) }
        else if (!control.willValidate || control.validity.valid) update(control)
      }
      for (const form of navigation.keys()) refreshNavigation(form)
    }
    const observer = new MutationObserver(prune)
    observer.observe(document.body, { childList: true, subtree: true })
    document.addEventListener('invalid', invalid, true)
    document.addEventListener('input', edited)
    document.addEventListener('change', edited)
    document.addEventListener('focusout', blurred)
    return () => {
      observer.disconnect()
      for (const issue of active.values()) issue.clear()
      for (const button of navigation.values()) button.remove()
      document.removeEventListener('invalid', invalid, true)
      document.removeEventListener('input', edited)
      document.removeEventListener('change', edited)
      document.removeEventListener('focusout', blurred)
    }
  }, [])
  return null
}

export function InlineIssueNavigation({ issues, attempted }: { issues: InlineIssue[]; attempted: boolean }) {
  const id = useId()
  const active = useRef<string | undefined>(undefined)
  const rendered = useRef(new Map<string, { element: HTMLElement; message: string; clear: () => void }>())
  const focusPending = useRef(true)
  const orderedIssues = () => [...rendered.current.entries()].sort(([, a], [, b]) =>
    a.element.compareDocumentPosition(b.element) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1)
  useEffect(() => {
    const current = rendered.current
    return () => { for (const issue of current.values()) issue.clear(); current.clear() }
  }, [])
  useEffect(() => {
    const next = new Map(attempted ? issues.filter(issue => issue.message).map(issue => [issue.target, issue.message]) : [])
    for (const [target, issue] of rendered.current) {
      if (next.get(target) !== issue.message || !issue.element.isConnected) { issue.clear(); rendered.current.delete(target) }
    }
    for (const [target, message] of next) {
      const element = document.getElementById(target)
      if (!element || rendered.current.has(target)) continue
      rendered.current.set(target, { element, message, clear: showIssue(element, id + target, message) })
    }
    if (!attempted) { focusPending.current = true; active.current = undefined }
    const first = orderedIssues()[0]
    if (attempted && first && focusPending.current) {
      focusPending.current = false
      active.current = first[0]
      focusIssue(first[1].element)
    }
  })
  if (!attempted || !issues.some(issue => issue.message)) return null
  return <Button type="button" onClick={() => {
    const targets = orderedIssues().map(([target]) => target)
    if (!targets.length) return
    const index = active.current ? targets.indexOf(active.current) : -1
    const target = targets[(index + 1) % targets.length]
    active.current = target
    focusIssue(rendered.current.get(target)!.element)
  }}>Go to next issue</Button>
}
