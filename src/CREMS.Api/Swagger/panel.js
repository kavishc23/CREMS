(() => {
  const key = 'crems.swagger.windowId';
  function windowId() { let id = sessionStorage.getItem(key); if (!id) { id = crypto.randomUUID(); sessionStorage.setItem(key, id); } return id; }
  let panel, challenge, busy = false;
  const active = new Set();
  const text = (id, value) => { panel.querySelector('#' + id).textContent = value; };
  async function request(path, body) {
    const response = await fetch(path, { method: body === undefined ? 'GET' : 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', 'X-CREMS-Window-Id': windowId() }, ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
    const data = await response.json().catch(() => null);
    return { response, data };
  }
  async function refresh() {
    active.clear(); const accounts = [];
    for (const [kind, path] of [['Staff', '/api/auth/session'], ['Customer', '/api/customer-account/session']]) {
      const { response, data } = await request(path);
      if (response.ok && data) { active.add(kind); accounts.push(`${data.fullName || data.customerName || data.email} · ${data.email} · ${(data.roles || [kind]).join(', ')}${data.branchName ? ' · ' + data.branchName : ''}`); }
      else if (![401, 403].includes(response.status)) throw new Error('Session check failed. Please try again.');
    }
    text('crems-account', accounts.join(' | ') || 'Signed out, expired, or active in another window. Sign in here to continue.');
    panel.querySelector('#crems-logout').disabled = active.size === 0;
  }
  async function action(work) {
    if (busy) return; busy = true; panel.querySelectorAll('button').forEach(b => b.disabled = true);
    try { text('crems-message', ''); await work(); } catch (error) { text('crems-message', error.message || 'Request failed. Check that the API is running.'); }
    finally { busy = false; panel.querySelectorAll('button').forEach(b => b.disabled = false); panel.querySelector('#crems-logout').disabled = active.size === 0; }
  }
  function failed(response, data) { return new Error(response.status === 429 ? 'Too many attempts. Wait before trying again.' : data?.detail || data?.message || 'Sign-in failed. Check your credentials or verification code.'); }
  async function signedIn() { challenge = null; panel.querySelector('#crems-mfa').hidden = true; panel.querySelector('#crems-code').value = ''; await refresh(); text('crems-message', 'Session refreshed. Endpoint permissions still apply.'); }
  window.cremsSwaggerGuard = request => {
    const path = new URL(request.url, location.origin).pathname;
    if (!['GET', 'HEAD', 'OPTIONS'].includes((request.method || 'GET').toUpperCase()) && !path.startsWith('/api/auth/') && path !== '/api/customer-account/logout' && !panel?.querySelector('#crems-writes').checked) {
      throw new Error('Enable “Allow data changes” in the environment banner before executing write requests.');
    }
    return request;
  };
  window.addEventListener('load', () => {
    panel = document.createElement('section'); panel.className = 'crems-panel'; panel.setAttribute('aria-label', 'API environment and login');
    panel.innerHTML = `<div class="crems-environment"><strong id="crems-target">Loading environment…</strong><label><input id="crems-writes" type="checkbox" disabled> Allow data changes against this database</label><span>Write requests affect real records in the displayed database. Example IDs must be replaced.</span></div><h2>API session</h2><p id="crems-account" aria-live="polite">Checking session…</p><form id="crems-login"><label>Email<input id="crems-email" type="email" autocomplete="username" required></label><label>Password<input id="crems-password" type="password" autocomplete="current-password" required></label><button type="submit">Sign in</button></form><form id="crems-mfa" hidden><label>Verification code<input id="crems-code" inputmode="numeric" autocomplete="one-time-code" required></label><button type="submit">Verify code</button><button id="crems-resend" type="button">Resend code</button><button id="crems-cancel" type="button">Cancel MFA</button></form><div class="crems-actions"><button id="crems-refresh">Refresh session</button><button id="crems-logout" disabled>Sign out</button></div><p id="crems-message" role="status"></p><small>No bearer token needed. Login cookies stay HttpOnly. Lock icons describe endpoint requirements, not live session status.</small>`;
    document.body.prepend(panel);
    fetch('/swagger/crems-environment.json', { credentials: 'omit' }).then(r => { if (!r.ok) throw new Error(); return r.json(); }).then(data => { text('crems-target', `${data.environment} · Server: ${data.server} · Database: ${data.database}`); panel.querySelector('#crems-writes').disabled = false; }).catch(() => text('crems-target', 'Environment unavailable — data changes disabled'));
    panel.querySelector('#crems-login').addEventListener('submit', event => { event.preventDefault(); void action(async () => {
      const password = panel.querySelector('#crems-password'); const body = { email: panel.querySelector('#crems-email').value, password: password.value }; password.value = '';
      const { response, data } = await request('/api/auth/login', body); body.password = '';
      if (!response.ok) throw failed(response, data);
      if (response.status === 202 && data?.requiresMfa) { challenge = data.challengeId; panel.querySelector('#crems-mfa').hidden = false; text('crems-message', `Enter the code sent to ${data.maskedDestination || 'your registered email'}.`); } else await signedIn();
    }); });
    panel.querySelector('#crems-mfa').addEventListener('submit', event => { event.preventDefault(); void action(async () => { if (!challenge) return; const input = panel.querySelector('#crems-code'); const code = input.value; input.value = ''; const { response, data } = await request('/api/auth/mfa/verify', { challengeId: challenge, code }); if (!response.ok) throw failed(response, data); await signedIn(); }); });
    panel.querySelector('#crems-resend').onclick = () => action(async () => { if (!challenge) return; const { response, data } = await request(`/api/auth/mfa/${encodeURIComponent(challenge)}/resend`, {}); if (!response.ok) throw failed(response, data); if (data?.challengeId) challenge = data.challengeId; text('crems-message', data?.message || 'Verification code requested.'); });
    panel.querySelector('#crems-cancel').onclick = () => { challenge = null; panel.querySelector('#crems-code').value = ''; panel.querySelector('#crems-mfa').hidden = true; };
    panel.querySelector('#crems-refresh').onclick = () => action(refresh);
    panel.querySelector('#crems-logout').onclick = () => action(async () => { for (const kind of active) { const { response } = await request(kind === 'Staff' ? '/api/auth/logout' : '/api/customer-account/logout', {}); if (!response.ok && response.status !== 401) throw new Error('Sign-out failed. Refresh the session and try again.'); } await signedIn(); });
    void action(refresh);
  });
})();
