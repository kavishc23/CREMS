// Prepared with Codex assistance. Real React UI; all API calls are intercepted.
// No live customer records, emails or database writes are used.
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || path.resolve(__dirname, '../../tmp/customer-test-tools/node_modules/playwright'));
let browser;
before(async () => { browser = await chromium.launch({ channel: 'chrome', headless: true }); });
after(async () => { await browser?.close(); });
const session = { email: 'portfolio@example.test', fullName: 'Portfolio Customer', customerNumber: 'CUS-TEST', customerName: 'Portfolio Customer', phone: '1234567', address: 'Suva', identificationNumber: null, hirePreferences: [], emailConfirmed: true, type: 'Individual', driverLicenceNumber: 'TEST-LICENCE' };
const asset = { id: '11111111-1111-4111-8111-111111111111', assetNumber: 'TEST-CAR', name: 'Portfolio test vehicle', type: 'PassengerVehicle', status: 'Available', branchId: 'branch-test', branchName: 'Suva', dailyRate: 100, isAvailable: true, divisionId: 'division-test', divisionName: 'Carpenters Motors', category: 'Car', categoryCode: 'CAR', bondAmount: 200, personnelRequirement: 'Optional', serviceName: 'Vehicle hire', requiresQuote: false, requiresDelivery: false, photoUrlsJson: '[]', attributes: [] };

async function fixture(t, { signedIn = false, registrationStatus = 201, loginStatus = 204, catalogue = false, requestForm = false, quotation = false } = {}) {
  const page = await browser.newPage();
  t.after(() => page.close());
  page.setDefaultTimeout(10000);
  const writes = [], errors = [], unexpected = [];
  let current = { ...session, hirePreferences: [] }, branchCalls = 0;
  if (requestForm) {
    const startDate = new Date(Date.now() + 30 * 86400000).toISOString().slice(0, 10);
    const endDate = new Date(Date.now() + 32 * 86400000).toISOString().slice(0, 10);
    await page.addInitScript(pending => sessionStorage.setItem('crems.pendingBooking', JSON.stringify(pending)), { assetId: asset.id, startDate, endDate, quote: quotation });
  }
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => { if (message.type() === 'error' && message.text().startsWith('MUI:')) errors.push(message.text()); });
  page.on('requestfailed', request => errors.push(`${request.url()}: ${request.failure()?.errorText}`));
  // HMR websocket diagnostics are development-server output, not application errors.
  await page.route('**/api/**', async route => {
    const request = route.request(), url = new URL(request.url()).pathname;
    if (!url.startsWith('/api/')) return route.continue();
    const json = (data, status = 200) => route.fulfill({ status, json: data });
    if (request.method() !== 'GET') writes.push({ url, body: request.postDataJSON(), headers: request.headers() });
    if (url === '/api/customer-account/session') return signedIn ? json(current) : json({}, 401);
    if (url === '/api/customer-account/register') { if (registrationStatus === 201) signedIn = true; return json({ message: 'An account already uses this email address.' }, registrationStatus); }
    if (url === '/api/auth/login') { if (loginStatus === 204) signedIn = true; return route.fulfill({ status: loginStatus }); }
    if (url === '/api/customer-account/logout') { signedIn = false; return route.fulfill({ status: 204 }); }
    if (url === '/api/customer-account/profile') { current = { ...current, ...request.postDataJSON() }; return json(current); }
    if (url === '/api/customer-account/bookings' || url === '/api/customer-account/quotes') return json([]);
    if (url === '/api/customer-account/documents') return json({ registered: [], agreements: [], invoices: [] });
    if (url === '/api/customer-account/licence') return json({ status: 'Required', licenceNumber: null, classes: [], hasImage: false });
    if (url.includes('/notifications')) return json({ items: [], unreadCount: 0 });
    if (url === '/api/public/branches') { branchCalls++; return !requestForm && branchCalls <= 2 ? json({}, 503) : json([{ id: 'branch-test', name: 'Suva', address: null, phone: null }]); }
    if (url === '/api/public/divisions') return json(requestForm ? [{ id: 'division-test', code: 'MOTORS', name: 'Carpenters Motors', capabilities: 1 }] : []);
    if (url === '/api/public/assets') return json(requestForm ? [asset] : []);
    if (url === `/api/public/assets/${asset.id}`) return json({ ...asset, taxRate: 12.5, service: null, charges: [] });
    if (url === '/api/public/booking-requests') return json({ reference: quotation ? 'QUO-TEST' : 'BKR-TEST', requestType: quotation ? 'Quotation' : 'Booking' });
    unexpected.push(`${request.method()} ${url}`); return json({ message: 'Unexpected API call in isolated test' }, 500);
  });
  await page.route('**/__customer_regression', route => route.fulfill({ contentType: 'text/html', body: `<div id="root"></div><script type="module">
import RefreshRuntime from '/@react-refresh';RefreshRuntime.injectIntoGlobalHook(window);window.$RefreshReg$=()=>{};window.$RefreshSig$=()=>type=>type;window.__vite_plugin_react_preamble_installed__=true;
window.backCalls=0;window.expiryEvents=0;window.addEventListener('crems:customer-session-expired',()=>window.expiryEvents++);
const {default:React}=await import('/node_modules/.vite/deps/react.js');const {default:{createRoot}}=await import('/node_modules/.vite/deps/react-dom_client.js');
const {${catalogue ? 'PublicRentalPage' : 'CustomerPortalPage'}}=await import('/src/pages/${catalogue ? 'PublicRentalPage' : 'CustomerPortalPage'}.tsx');
createRoot(document.getElementById('root')).render(React.createElement(${catalogue ? 'PublicRentalPage' : 'CustomerPortalPage'},${catalogue ? "{customerAuthenticated:true,customerName:'Portfolio Customer',onCustomerAccount:()=>{}}" : '{onBack:()=>window.backCalls++}'}));</script>` }));
  t.after(() => { assert.deepEqual(errors, [], 'No unhandled React errors'); assert.deepEqual(unexpected, [], 'No unmocked API requests'); });
  await page.goto(`${process.env.CREMS_TEST_URL || 'http://127.0.0.1:5174'}/__customer_regression`);
  return { page, writes, branches: () => branchCalls };
}

async function registration(page, password = 'Portfolio123!', confirmation = password) {
  await page.getByRole('button', { name: 'Create customer account', exact: true }).click();
  await page.getByLabel(/^Your full name(?:\s*\*)?$/).fill('Portfolio Customer');
  await page.getByLabel(/^Email(?:\s*\*)?$/).fill('portfolio@example.test');
  await page.getByLabel(/^Phone(?:\s*\*)?$/).fill('1234567');
  await page.getByLabel(/^Password(?:\s*\*)?$/).fill(password);
  await page.getByLabel(/^Confirm password(?:\s*\*)?$/).fill(confirmation);
}

test('Registration: matching valid passwords submit only account fields with a window session ID', async t => {
  const { page, writes } = await fixture(t);
  await registration(page);
  await page.getByRole('button', { name: 'Create account', exact: true }).click();
  await page.waitForFunction(() => window.backCalls === 1);
  const write = writes.find(item => item.url.endsWith('/register'));
  assert.equal(write.body.email, 'portfolio@example.test');
  assert.equal(write.body.password, 'Portfolio123!');
  assert.equal('confirmPassword' in write.body, false);
  assert.match(write.headers['x-crems-window-id'], /^[a-f0-9-]{36}$/i);
});

test('Registration: mismatched passwords show validation and send no request', async t => {
  const { page, writes } = await fixture(t);
  await registration(page, 'Portfolio123!', 'Different123!');
  await page.getByRole('button', { name: 'Create account', exact: true }).click();
  await page.getByText('The passwords do not match.', { exact: true }).waitFor();
  assert.equal(writes.length, 0);
});

test('Registration: weak password shows validation and sends no request', async t => {
  const { page, writes } = await fixture(t);
  await registration(page, 'weak');
  await page.getByRole('button', { name: 'Create account', exact: true }).click();
  await page.getByText('Use at least 10 characters with an uppercase letter and a number.', { exact: true }).waitFor();
  assert.equal(writes.length, 0);
});

test('Registration: duplicate email response is displayed without entering the dashboard', async t => {
  const { page } = await fixture(t, { registrationStatus: 409 });
  await registration(page);
  await page.getByRole('button', { name: 'Create account', exact: true }).click();
  await page.getByText('An account already uses this email address.', { exact: true }).waitFor();
  assert.equal(await page.evaluate(() => window.backCalls), 0);
});

test('Customer login: successful response hydrates the customer session and returns to the website', async t => {
  const { page, writes } = await fixture(t);
  await page.getByLabel(/^Email(?:\s*\*)?$/).fill(session.email);
  await page.getByLabel(/^Password(?:\s*\*)?$/).fill('Portfolio123!');
  await page.locator('form').getByRole('button', { name: 'Sign in', exact: true }).click();
  await page.waitForFunction(() => window.backCalls === 1);
  assert.equal(writes[0].url, '/api/auth/login');
  assert.equal(await page.evaluate(() => sessionStorage.getItem('crems.customerName')), session.fullName);
});

test('Customer login: rejected credentials stay on sign-in without expiring unrelated sessions', async t => {
  const { page } = await fixture(t, { loginStatus: 401 });
  await page.getByLabel(/^Email(?:\s*\*)?$/).fill(session.email);
  await page.getByLabel(/^Password(?:\s*\*)?$/).fill('wrong-password');
  const expiryBeforeLogin = await page.evaluate(() => window.expiryEvents);
  await page.locator('form').getByRole('button', { name: 'Sign in', exact: true }).click();
  await page.getByText('The email address or password is incorrect, or this is not a customer account.', { exact: true }).waitFor();
  assert.equal(await page.evaluate(() => window.expiryEvents), expiryBeforeLogin);
  assert.equal(await page.evaluate(() => window.backCalls), 0);
});

test('Customer dashboard: account identity is read-only; multiple preferences can be saved and cleared', async t => {
  const { page, writes } = await fixture(t, { signedIn: true });
  await page.getByText('Personal details', { exact: true }).waitFor();
  assert.equal(await page.getByLabel('Full name', { exact: true }).getAttribute('readonly'), '');
  assert.equal(await page.getByLabel('Account email', { exact: true }).getAttribute('readonly'), '');
  await page.getByRole('checkbox', { name: 'Vehicles', exact: true }).check();
  await page.getByRole('checkbox', { name: 'Construction and industrial equipment', exact: true }).check();
  await page.getByRole('button', { name: 'Save preferences', exact: true }).click();
  await page.getByText('Your rental preferences have been updated.', { exact: true }).waitFor();
  assert.deepEqual(writes[0].body.hirePreferences, ['Vehicles', 'Equipment']);
  await page.getByRole('checkbox', { name: 'Vehicles', exact: true }).uncheck();
  await page.getByRole('checkbox', { name: 'Construction and industrial equipment', exact: true }).uncheck();
  const response = page.waitForResponse(r => r.url().includes('/customer-account/profile') && r.request().method() === 'PUT');
  await page.getByRole('button', { name: 'Save preferences', exact: true }).click(); await response;
  assert.deepEqual(writes[1].body.hirePreferences, []);
});

test('Customer dashboard: expired-session event clears customer state and shows sign-in', async t => {
  const { page } = await fixture(t, { signedIn: true });
  await page.getByText('Personal details', { exact: true }).waitFor();
  await page.evaluate(() => window.dispatchEvent(new CustomEvent('crems:customer-session-expired')));
  await page.getByText('Customer sign in', { exact: true }).waitFor();
  assert.equal(await page.evaluate(() => sessionStorage.getItem('crems.customerName')), null);
  assert.equal(await page.getByText('Personal details', { exact: true }).count(), 0);
});

test('Asset catalogue: failed branch read recovers with Try again and leaves customer session active', async t => {
  const { page, writes, branches } = await fixture(t, { signedIn: true, catalogue: true });
  await page.getByRole('button', { name: 'Try again', exact: true }).click();
  await page.getByRole('combobox').first().click();
  await page.getByRole('option', { name: 'Suva', exact: true }).waitFor();
  assert(branches() >= 3);
  assert.equal(await page.evaluate(() => window.expiryEvents), 0);
  assert.equal(writes.length, 0);
});

test('Customer account menu: keyboard navigation reaches logout and returns to sign-in', async t => {
  const { page, writes } = await fixture(t, { signedIn: true });
  await page.getByText('Personal details', { exact: true }).waitFor();
  await page.getByRole('banner').getByRole('button', { name: session.fullName, exact: true }).click();
  await page.getByRole('menuitem', { name: /Customer Profile/ }).waitFor();
  await page.keyboard.press('End');
  assert.match(await page.evaluate(() => document.activeElement.textContent), /Log out/);
  await page.keyboard.press('Enter');
  await page.getByText('Customer sign in', { exact: true }).waitFor();
  assert.equal(writes[0].url, '/api/customer-account/logout');
  assert.equal(await page.evaluate(() => sessionStorage.getItem('crems.customerName')), null);
});

for (const quotation of [false, true]) {
  test(`${quotation ? 'Quotation' : 'Booking'} form: restored selection, required contact validation, terms consent and successful payload`, async t => {
    const { page, writes } = await fixture(t, { signedIn: true, catalogue: true, requestForm: true, quotation });
    const dialog = page.getByRole('dialog');
    await dialog.getByText('When and where?', { exact: true }).waitFor();
    await dialog.getByRole('button', { name: 'Continue', exact: true }).click();
    await dialog.getByRole('button', { name: 'Continue', exact: true }).click();
    assert.equal(await dialog.getByLabel(/^Full name(?:\s*\*)?$/).inputValue(), session.fullName);
    assert.equal(await dialog.getByLabel(/^Driver licence number(?:\s*\*)?$/).inputValue(), 'TEST-LICENCE');
    await dialog.getByLabel(/^Phone(?:\s*\*)?$/).fill('');
    await dialog.getByRole('button', { name: 'Continue', exact: true }).click();
    assert.equal(await dialog.getByText('Review and submit', { exact: true }).count(), 0);
    assert.equal(writes.length, 0);
    await dialog.getByLabel(/^Phone(?:\s*\*)?$/).fill('7654321');
    await dialog.getByRole('button', { name: 'Continue', exact: true }).click();
    const submit = dialog.getByRole('button', { name: quotation ? 'Request quotation' : 'Submit booking', exact: true });
    assert.equal(await submit.isDisabled(), true);
    await dialog.getByRole('checkbox', { name: /^I understand/ }).check();
    await submit.click();
    await dialog.getByText(quotation ? 'Quotation request submitted' : 'Booking request submitted', { exact: true }).waitFor();
    const payload = writes.find(item => item.url === '/api/public/booking-requests').body;
    assert.equal(payload.assetId, asset.id);
    assert.equal(payload.fullName, session.fullName);
    assert.equal(payload.phone, '7654321');
    assert.equal(payload.driverLicence, 'TEST-LICENCE');
    assert.equal(payload.requestQuotation, quotation);
    assert(payload.endDate > payload.startDate);
  });
}
