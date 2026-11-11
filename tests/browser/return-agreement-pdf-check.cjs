const fs=require('node:fs');
const path=require('node:path');
const {chromium}=require('/Users/kavishchandra/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async()=>{
 const folder=path.resolve('tmp/return-bookings-qa');
 const browser=await chromium.launch({channel:'chrome',headless:true});
 try{
  const page=await browser.newPage({acceptDownloads:true});
  await page.route('**/__agreement_pdf_check',r=>r.fulfill({contentType:'text/html',body:'<html><body>Agreement export verification</body></html>'}));
  await page.goto('http://localhost:5173/__agreement_pdf_check');
  for(const file of fs.readdirSync(folder).filter(f=>f.endsWith('.json'))){
   const data=JSON.parse(fs.readFileSync(path.join(folder,file),'utf8'));
   const downloadPromise=page.waitForEvent('download');
   await page.evaluate(async data=>{const m=await import('/src/utils/rentalAgreementPdf.ts');await m.downloadRentalAgreementPdf(data);},data);
   const download=await downloadPromise;await download.saveAs(path.join(folder,download.suggestedFilename()));
   console.log('PASS: signed printable agreement exported: '+download.suggestedFilename());
  }
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
