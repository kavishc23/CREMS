
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');

const base = process.env.CREMS_TEST_URL || 'http://localhost:5173';
const bookingId = '00000000-0000-0000-0000-000000000001';
const returnedAt = '2026-11-15T02:00:00Z';

async function drawSignature(page) {
  const canvas = page.locator('canvas');
  await canvas.evaluate(element => element.scrollIntoView({ block: 'center' }));
  const rect = await canvas.boundingBox();
  await page.mouse.move(rect.x + 20, rect.y + 20);
  await page.mouse.down();
  await page.mouse.move(rect.x + 80, rect.y + 50, { steps: 4 });
  await page.mouse.up();
  assert.ok(await page.getByRole('button', { name: 'Clear', exact: true }).isEnabled(), 'Signature stroke was captured');
}

async function inspect(page) {
  await page.getByRole('button', { name: 'Mark all as passed' }).click();
  await page.getByLabel('No condition issues observed').check();
  await page.getByLabel('Inspection photo files').setInputFiles('src/crems-web/public/catalog/sedan.jpg');
  await page.getByAltText('Inspection photo 1', { exact: true }).waitFor();
}

(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    const signIn = await browser.newPage();
    await signIn.route('**/api/auth/session', route => route.fulfill({ status: 401, json: {} }));
    await signIn.goto(base + '/staff/login');
    await signIn.getByRole('button', { name: 'Sign in', exact: true }).click();
    await signIn.getByText('Enter email address.', { exact: true }).waitFor();
    await signIn.getByText('Enter password.', { exact: true }).waitFor();
    await signIn.getByLabel('Email address').fill('officer@example.invalid');
    await signIn.getByText('Enter email address.', { exact: true }).waitFor({ state: 'hidden' });
    assert.equal(await signIn.getByText('Enter password.', { exact: true }).count(), 1);
    await signIn.close();
    const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const writes = [];
    let returning = false;
    let pickupBlocked = true;
    const row = {
      id: bookingId, bookingNumber: 'BK-TEST', customerName: 'Test Customer', branchName: 'Test Branch',
      assetNumber: 'TEST-ASSET', assetName: 'Test bin', assetCategoryCode: 'BIG_BIN', assetCategory: 'Big bin',
      startAt: '2026-11-13T00:00:00Z', endAt: '2026-11-15T00:00:00Z',
      rentalCharge: 100, rentalRevenue: 100, balanceDue: 100, amountPaid: 0, depositRequired: 30,
      bondAmountHeld: 10, bondDeductionAmount: 0, bondRefundAmount: 0, bondStatus: 'AwaitingPayment',
    };
    const future = { ...row, id: '00000000-0000-0000-0000-000000000002', bookingNumber: 'BK-FUTURE', status: 'Confirmed' };
    await page.route('**/api/**', async route => {
      const { pathname: path, searchParams } = new URL(route.request().url());
      if (route.request().method() === 'POST') {
        writes.push({ path, body: route.request().postDataJSON() });
        await route.fulfill({ json: {} });
        return;
      }
      if (path === '/api/auth/session') return route.fulfill({ json: {
        id: 'staff', fullName: 'Test Officer', email: 'test@example.invalid', roles: ['RentalOfficer'], pages: ['rentals'],
      } });
      if (path.endsWith('/work-queue')) {
        const search = searchParams.get('search');
        const id = searchParams.get('bookingId');
        const items = search || id
          ? search?.toLowerCase() === 'bk-future' || id === future.id ? [future] : []
          : [{ ...row, status: returning ? 'ConvertedToRental' : 'Confirmed', bondAmountHeld: returning ? 30 : 10 }];
        return route.fulfill({ json: {
          items, counts: { pickupToday: 1, onHire: 0, dueToday: 0, overdue: 0, returnInProgress: 0, recentlyCompleted: 0 },
          page: 1, pageSize: 25, total: items.length,
        } });
      }
      if (path.endsWith('/pickup-readiness')) return route.fulfill({ json: {
        canPickup: !pickupBlocked, canDeferHirePayment: false,
        reasons: pickupBlocked ? ['Complete the outstanding return for BK-PREVIOUS before releasing TEST-ASSET.'] : [],
        paidHire: 0, bondAmountHeld: 10, depositRequired: 30,
      } });
      if (path.endsWith('/inspection-context')) return route.fulfill({ json: {
        tracksMeter: false, tracksFuel: false, requiresDrivingLicence: false,
        templates: [{ id: 'template', name: 'Asset condition', stage: returning ? 'PostHire' : 'PreHire',
          checklistJson: JSON.stringify([{ section: 'Condition', items: [{ label: 'Clean' }] }]) }],
        preHire: returning ? { conditionNotes: 'No condition issues observed.', evidenceJson: JSON.stringify({ photos: writes[0]?.body.evidenceDataUrls || [] }) } : null,
      } });
      if (path.endsWith('/return-charges')) return route.fulfill({ json: {
        returnedAt, lateFee: 15, hireSubtotal: 100, taxAmount: 0, paidHire: 100, bondHeld: 30,
      } });
      if (path.includes('/rental-agreements/')) return route.fulfill({ json: {
        pricing: { total: 100, depositRequired: 30 }, terms: [{ title: 'Return', content: 'Review the due time.' }],
      } });
      if (path === '/api/notifications') return route.fulfill({ json: { items: [], unreadCount: 0, total: 0, page: 1, pageSize: 20 } });
      return route.fulfill({ json: [] });
    });

    await page.goto(base + '/staff/hire-operations?bookingId=' + future.id);
    await page.getByText('Selected booking', { exact: true }).waitFor();
    await page.getByRole('row').filter({ hasText: 'BK-FUTURE' }).waitFor();
    const pickupTab = page.getByRole('tab', { name: 'Pickup today 1' });
    assert.equal(await pickupTab.getAttribute('aria-selected'), 'false');
    await page.getByRole('button', { name: 'Back to queue' }).click();
    await page.getByRole('row').filter({ hasText: 'BK-TEST' }).waitFor();
    assert.equal(new URL(page.url()).searchParams.has('bookingId'), false);
    const search = page.getByPlaceholder('Search rental, customer or asset');
    await search.fill('bk-future');
    await page.getByRole('row').filter({ hasText: 'BK-FUTURE' }).waitFor();
    await page.getByText('1 rental matches “bk-future”', { exact: true }).waitFor();
    assert.equal(await pickupTab.getAttribute('aria-selected'), 'false');
    await search.fill('missing');
    await page.getByText('No matching rentals', { exact: true }).first().waitFor();
    await page.getByRole('button', { name: 'Clear search' }).click();
    await page.getByRole('row').filter({ hasText: 'BK-TEST' }).waitFor();
    assert.equal(await pickupTab.getAttribute('aria-selected'), 'true');
    await page.getByRole('button', { name: 'Begin pickup', exact: true }).click();
    const blocker = page.getByRole('dialog', { name: 'Pickup unavailable' });
    await blocker.getByRole('alert').getByText(/BK-PREVIOUS/).waitFor();
    assert.equal(writes.length, 0);
    await blocker.getByRole('button', { name: 'Close', exact: true }).click();
    pickupBlocked = false;
    await page.getByRole('button', { name: 'Begin pickup', exact: true }).click();
    await page.getByRole('button', { name: 'Continue', exact: true }).click();
    await page.getByText('Confirm the asset number on the physical label.').waitFor();
    await page.getByLabel(/I checked the physical label matches/).check();
    await page.getByLabel('Customer identification sighted and matches the booking').check();
    await page.getByRole('button', { name: 'Continue', exact: true }).click();
    const pickupDialog = page.getByRole('dialog');
    const scrollArea = pickupDialog.locator('.MuiDialogContent-root').first();
    await scrollArea.evaluate(element => { element.scrollTop = element.scrollHeight; });
    await page.getByRole('button', { name: 'Save inspection and continue' }).click();
    const photoError = pickupDialog.locator('#pickup-photos').getByText('Attach at least one inspection photo.');
    await photoError.waitFor();
    assert.equal(await photoError.count(), 1);
    await pickupDialog.getByText('Describe the pre-hire condition.', { exact: true }).waitFor();
    await page.waitForFunction(() => document.activeElement?.matches('#pickup-checklist, #pickup-checklist button, #pickup-condition'));
    const issueNavigation = pickupDialog.getByRole('button', { name: 'Go to next issue' });
    await issueNavigation.waitFor();
    await page.getByLabel('Pre-hire condition and checklist result').fill('No issues found during inspection.');
    await pickupDialog.getByText('Describe the pre-hire condition.', { exact: true }).waitFor({state:'hidden'});
    assert.equal(await photoError.count(), 1, 'Fixing condition leaves the photo error intact');
    await inspect(page);
    await page.getByRole('button', { name: 'Save inspection and continue' }).click();
    await page.getByLabel(/Full hire balance received/).check();
    await drawSignature(page);
    await page.getByLabel('Customer accepts the agreement, charges and recorded condition').check();
    await page.getByLabel('I witnessed the signature and authorize this asset release').check();
    await page.getByRole('button', { name: 'Release asset' }).click();
    const bondMessage = 'Confirm receipt of the remaining refundable bond.';
    await page.getByText(bondMessage).waitFor();
    await page.evaluate(message => window.dispatchEvent(new CustomEvent('crems:toast', {
      detail: { severity: 'error', message, apiFallback: true },
    })), bondMessage);
    await page.waitForFunction(message => ![...document.querySelectorAll('.MuiSnackbar-root')]
      .some(element => element.textContent.includes(message)), bondMessage);
    assert.equal(await page.getByText(bondMessage, { exact: true }).count(), 1);
    assert.equal(writes.length, 1, 'The first write saves the inspection only');
    await page.getByLabel(/Remaining refundable bond received/).check();
    await page.getByLabel('Payment method').click();
    await page.getByRole('option', { name: 'Cash', exact: true }).click();
    await page.getByRole('listbox').waitFor({ state: 'hidden' });
    assert.equal(await page.getByLabel('Existing till receipt (optional)').inputValue(), '');
    await drawSignature(page);
    await page.getByLabel('Customer accepts the agreement, charges and recorded condition').check();
    await page.getByLabel('I witnessed the signature and authorize this asset release').check();
    const pickup = page.waitForResponse(response => response.url().endsWith('/pickup'));
    await page.getByRole('button', { name: 'Release asset' }).click();
    await pickup.catch(async error => { console.error(await page.getByRole('dialog').innerText()); throw error; });
    assert.equal(writes[1].body.rentalAmountCollected, 100);
    assert.equal(writes[1].body.amountCollected, 20);
    assert.equal(writes[1].body.expectedBondBalance, 20);
    assert.equal(writes[1].body.paymentMethod, 'Cash');
    assert.ok(!writes[1].body.receiptNumber);
    assert.equal(writes[1].body.fuelLevelPercent, null);
    assert.ok(writes[1].body.customerSignatureDataUrl);

    returning = true;
    await page.setViewportSize({ width: 390, height: 844 });
    await page.reload();
    await page.getByRole('button', { name: 'Begin return', exact: true }).click();
    await page.getByLabel(/I checked the physical label matches/).check();
    await inspect(page);
    await page.getByText('Compare pickup and return photos').click();
    await page.getByAltText('Pre-hire photos 1', { exact: true }).waitFor();
    await page.getByRole('button', { name: 'Continue', exact: true }).click();
    await page.getByLabel(/Apply calculated late fee/).check();
    await page.getByLabel('Use the bond toward charges').check();
    await page.getByLabel('Bond applied (FJD)').fill('5');
    await page.getByLabel('Reason for bond deduction').fill('Apply part of the bond to the late charge.');
    await page.getByLabel(/Refund already paid/).check();
    await page.getByLabel('Refund transaction reference').fill('REFUND-001');
    await page.getByLabel(/Full outstanding balance received/).check();
    await page.getByLabel('Receipt / transaction reference').fill('RETURN-001');
    await page.getByRole('button', { name: 'Continue', exact: true }).click();
    await page.getByText('Customer settlement', { exact: true }).waitFor();
    await drawSignature(page);
    await page.getByLabel('Customer acknowledges the return record, charges and bond settlement').check();
    const completed = page.waitForResponse(response => response.url().endsWith('/return'));
    await page.getByRole('button', { name: 'Complete return & create invoice' }).click();
    await completed;
    assert.equal(writes[2].body.expectedInvoiceTotal, 115);
    assert.equal(writes[2].body.amountCollected, 10);
    assert.equal(writes[2].body.bondDeductionAmount, 5);
    assert.equal(writes[2].body.bondRefundReference, 'REFUND-001');
    assert.equal(writes[2].body.returnedAt, returnedAt);
    assert.ok(writes[2].body.signatureDataUrl);
    assert.deepEqual(errors, []);
    console.log('PASS: pickup and return, receipts, bond, evidence and signatures at desktop and mobile widths. API writes mocked.');
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
