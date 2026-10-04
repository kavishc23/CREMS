const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const flush = () => new Promise(resolve => setImmediate(resolve));
async function setup(settings = {}) {
  const elements = new Map(); const calls = []; let load, signedIn = false, mfa = false;
  const el = id => { if (!elements.has(id)) elements.set(id, { value: '', checked: false, disabled: false, hidden: false, addEventListener(event, handler) { this[event] = handler; } }); return elements.get(id); };
  const panel = { setAttribute(){}, querySelector: el, querySelectorAll: () => [] };
  const store = new Map();
  const window = { addEventListener(event, handler) { load = handler; } };
  const context = { window, document: { createElement: () => panel, body: { prepend(){} } }, location: { origin: 'http://localhost:5080' }, URL, crypto: require('node:crypto').webcrypto, sessionStorage: { getItem: k => store.get(k), setItem: (k,v) => store.set(k,v) }, fetch: async (path, options={}) => {
    calls.push({path, options}); let status=200, data=null;
    if(path.includes('environment')) data={environment:'Development',server:'localhost',database:'TestOnly'};
    else if(path === '/api/auth/login') { if(settings.loginFailure) {status=401;} else if(mfa) {status=202;data={requiresMfa:true,challengeId:'challenge1'};} else signedIn=true; }
    else if(path.endsWith('/resend')) data={challengeId:'challenge2'};
    else if(path === '/api/auth/mfa/verify') signedIn=true;
    else if(path === '/api/auth/logout' || path === '/api/customer-account/logout') signedIn=false;
    else if(path === '/api/customer-account/session' && signedIn && settings.customer) data={fullName:'Test Customer',email:'customer@example.invalid'};
    else if(path === '/api/auth/session' && signedIn && !settings.customer) data={fullName:'Test Officer',email:'test@example.invalid',roles:['MaintenanceOfficer']};
    else status=401;
    return {ok:status>=200&&status<300,status,json:async()=>data};
  }};
  vm.runInNewContext(fs.readFileSync('src/CREMS.Api/Swagger/panel.js','utf8'), context); load(); await flush();
  return {el,calls,window,store,setMfa:()=>{mfa=true;}};
}
test('blocks writes until acknowledged and allows reads',async()=>{const x=await setup(); assert.throws(()=>x.window.cremsSwaggerGuard({url:'/api/customers',method:'POST'}),/Allow data changes/); x.window.cremsSwaggerGuard({url:'/api/customers',method:'GET'});x.el('#crems-writes').checked=true;x.window.cremsSwaggerGuard({url:'/api/customers',method:'POST'});assert.match(x.el('#crems-target').textContent,/TestOnly/);});
test('login clears password, reports role, sends session header and logs out',async()=>{const x=await setup(); x.el('#crems-email').value='test@example.invalid';x.el('#crems-password').value='test password';x.el('#crems-login').submit({preventDefault(){}});await flush();assert.equal(x.el('#crems-password').value,'');assert.match(x.el('#crems-account').textContent,/MaintenanceOfficer/);assert(x.calls.find(c=>c.path==='/api/auth/login').options.headers['X-CREMS-Window-Id']);assert.equal(x.store.size,1);await x.el('#crems-logout').onclick();assert.match(x.el('#crems-account').textContent,/Signed out/);});
test('MFA resend replaces challenge and verification clears code',async()=>{const x=await setup();x.setMfa();x.el('#crems-login').submit({preventDefault(){}});await flush();assert.equal(x.el('#crems-mfa').hidden,false);await x.el('#crems-resend').onclick();x.el('#crems-code').value='123456';x.el('#crems-mfa').submit({preventDefault(){}});await flush();assert.equal(JSON.parse(x.calls.find(c=>c.path==='/api/auth/mfa/verify').options.body).challengeId,'challenge2');assert.equal(x.el('#crems-code').value,'');assert.equal(x.el('#crems-mfa').hidden,true);});

test('invalid credentials show an error without claiming a session',async()=>{const x=await setup({loginFailure:true});x.el('#crems-password').value='incorrect';x.el('#crems-login').submit({preventDefault(){}});await flush();assert.match(x.el('#crems-message').textContent,/Sign-in failed/);assert.match(x.el('#crems-account').textContent,/Signed out/);assert.equal(x.el('#crems-password').value,'');assert.equal(x.el('#crems-logout').disabled,true);});
test('customer account uses customer session and logout endpoints',async()=>{const x=await setup({customer:true});x.el('#crems-login').submit({preventDefault(){}});await flush();assert.match(x.el('#crems-account').textContent,/Customer/);await x.el('#crems-logout').onclick();assert(x.calls.some(c=>c.path==='/api/customer-account/logout'));assert.match(x.el('#crems-account').textContent,/Signed out/);});
