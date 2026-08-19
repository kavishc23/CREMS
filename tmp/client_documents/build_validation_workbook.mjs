import fs from "node:fs/promises";
import path from "node:path";
import { SpreadsheetFile, Workbook } from "@oai/artifact-tool";

const root = "/Users/kavishchandra/Documents/CS400";
const threadId = "crems-client-data-validation";
const outputDir = path.join(root, "outputs", threadId);
const reviewDir = path.join(root, "tmp/client_documents/workbook_review");
await fs.mkdir(outputDir, { recursive: true });
await fs.mkdir(reviewDir, { recursive: true });

const prepared = JSON.parse(await fs.readFile(path.join(root, "tmp/client_documents/prepared/client_asset_import_anonymised.json"), "utf8"));
const assets = prepared.assets;

const vehicleMapping = [
  ["Location_Description", "Branch.Code → Asset.BranchId; Asset.CurrentLocation", "Lookup location against approved branch code", "Required", "Operational", "Current mapping collapses Suva Rona St and Foster Rd to SUV; confirm whether separate branches/sites"],
  ["Rego_No", "Asset.RegistrationNumber", "Trim and validate registration format", "Required for road vehicles", "Confidential identifier", "One value contains a vehicle model instead of a registration and one value is blank"],
  ["VIN", "Asset.VinOrChassisNumber", "Uppercase, trim punctuation, uniqueness check", "Required for vehicles", "Confidential identifier", "One VIN has trailing punctuation"],
  ["Registration_Expiry_Date", "AssetAttributeValue(registration_expiry)", "ISO date", "Required for road vehicles", "Operational", "No dedicated Asset column exists; configured attribute recommended"],
  ["Stock_No", "Asset.AssetNumber or AssetAttributeValue(stock_number)", "Client decision: canonical asset number vs secondary stock number", "Required", "Internal identifier", "Do not assume Stock_No is the permanent asset number"],
  ["Stocked_Date", "Asset.AcquisitionDate", "ISO date", "Optional until confirmed", "Commercial", "Confirm that stocked date equals acquisition/commissioning date"],
  ["DESCRIPTION", "Asset.Name; Asset.Manufacturer; Asset.Model", "Split and normalize using approved make/model catalogue", "Required", "Public/operational", "Descriptions are inconsistent and sometimes repeat manufacturer"],
  ["COLOUR", "AssetAttributeValue(colour)", "Title case; preserve approved colour vocabulary", "Optional", "Public/operational", "Several blanks"],
  ["DATE_FIRST_REGISTERED", "AssetAttributeValue(first_registered_date)", "ISO date", "Required for road vehicles", "Operational", "Several blanks"],
  ["Manufacture_Year", "Asset.ModelYear", "Extract four-digit year", "Required", "Public", "Source cells are formatted as dates rather than integer years"],
  ["Odometer", "Asset.CurrentMeterReading; Asset.MeterUnit='km'", "Numeric >= previous reading", "Required before go-live", "Operational", "Zeros/very low readings and one unusually high value require verification"],
];

const equipmentMapping = [
  ["Branch", "Branch.Code → Asset.BranchId; Asset.CurrentLocation", "Lookup approved branch", "Required", "Operational", "All supplied rows are Carptrac - Suva; confirm completeness"],
  ["Sort", "AssetCategory.Code → Asset.AssetCategoryId", "Translate using client-approved sort codebook", "Required", "Operational", "Codes FORK, GEPL, HEMD etc. need definitions"],
  ["Make", "Asset.Manufacturer", "Translate make code using approved manufacturer codebook", "Required", "Public", "Codes CAT, TM, OLY, PER, HHT, SEM, YYY need definitions"],
  ["Equipment", "Asset.AssetNumber", "Preserve as text; uniqueness check", "Required", "Internal identifier", "Workbook label is ambiguous; treated as internal asset/equipment number pending approval"],
  ["Desc.", "Asset.Name; AssetCategory.Name", "Normalize description and category", "Required", "Public/operational", "Description vocabulary is inconsistent"],
  ["Type", "Asset.Model or AssetAttributeValue(capacity/subtype)", "Preserve as text; classify after category is known", "Conditional", "Public/operational", "Column mixes model codes, capacities and subtype descriptions"],
];

const headerFields = [
  ["Vehicle", "Customer Name", "Booking.CustomerId → Customer.Name", "System-derived", "PreHire,PostHire"],
  ["Vehicle", "Rental Agreement No.", "RentalAgreement.AgreementNumber", "System-derived", "PreHire,PostHire"],
  ["Vehicle", "Driving License #", "AuthorizedDriver.LicenceNumber", "Verify and restrict access", "PreHire"],
  ["Vehicle", "Vehicle Reg. No.", "Asset.RegistrationNumber", "System-derived", "PreHire,PostHire"],
  ["Vehicle", "Vehicle Make/Model", "Asset.Manufacturer + Asset.Model", "System-derived", "PreHire,PostHire"],
  ["Vehicle", "Date Out / In", "BookingItem.StartAt / actual return timestamp", "System-derived", "PreHire,PostHire"],
  ["Vehicle", "Odometer Out / In", "AssetInspection.MeterReading", "Validate monotonic increase", "PreHire,PostHire"],
  ["Vehicle", "Fuel Out / In", "AssetInspection.FuelPercent", "Use percentage or fixed scale", "PreHire,PostHire"],
  ["Vehicle", "Existing scratches / dents and damage notes", "AssetInspection.DamageMapJson + Notes", "Capture before checkout and compare on return", "PreHire,PostHire"],
  ["Vehicle", "Keys / spare key / wheel brace / first-aid kit / fire extinguisher / GPS", "AssetInspection.ResponsesJson (Accessories issued section)", "Record issued and returned state per accessory", "PreHire,PostHire"],
  ["Vehicle", "Carbon copy Date-Time, Fuel and KM Out / In", "System-generated from completed inspections", "Do not recapture duplicate values", "PreHire,PostHire"],
  ["Vehicle", "CDW and excess clauses", "RentalAgreement versioned terms and charge rules", "Not an inspection question; customer accepts at agreement signature", "PreHire"],
  ["Equipment / Heavy", "PO #", "Booking purchase-order reference (field required)", "Customer supplied", "PreHire"],
  ["Equipment / Heavy", "Project / Cost Centre", "Booking/job-site metadata (field required)", "Customer supplied", "PreHire"],
  ["Equipment / Heavy", "Site Location / Supervisor / Contact", "Booking site/contact metadata", "Customer supplied", "PreHire"],
  ["Equipment / Heavy", "Rental Purpose", "Booking purpose metadata", "Customer supplied", "PreHire"],
  ["Equipment / Heavy", "Operator Required / Assigned Operator / Licence", "PersonnelAssignment + PersonnelQualification", "System-derived and verified", "PreHire"],
  ["Equipment / Heavy", "Delivery / Collection Date-Time", "DeliverySchedule", "System-derived", "PreHire,PostHire"],
  ["Equipment / Heavy", "Delivered By / Received By", "DeliverySchedule personnel and proof", "Captured at handover", "PreHire,PostHire"],
  ["Equipment / Heavy", "Fuel Out / In", "AssetInspection.FuelPercent", "Captured reading", "PreHire,PostHire"],
  ["Equipment / Heavy", "Hours Out / In", "AssetInspection.MeterReading", "Meter unit hours", "PreHire,PostHire"],
  ["Equipment / Heavy", "Photos Attached", "AssetInspection.EvidenceJson", "Require on issue/damage", "PreHire,PostHire"],
  ["Equipment / Heavy", "Damage / existing defects / delivery and return comments", "AssetInspection.DamageMapJson + Notes", "Compare stages and preserve evidence", "PreHire,PostHire"],
  ["Equipment / Heavy", "Customer acceptance statements", "AssetInspection.ResponsesJson (Acknowledgements section)", "Good condition, safety briefing, operator allocation, existing damage and meter/fuel verification", "PreHire"],
  ["Equipment / Heavy", "Operator daily hours / shift and customer supervisor approval", "PersonnelAssignment + Timesheet approval", "Capture against booking and assigned operator", "PreHire,PostHire"],
  ["Genset", "Model / Serial No.", "Asset.Model / Asset.SerialNumber", "System-derived", "PreHire,PostHire"],
  ["Genset", "Damage / fault notes and delivery / return comments", "AssetInspection.Notes + DamageMapJson", "Create maintenance referral only under approved rule", "PreHire,PostHire"],
  ["All", "Terms and conditions", "RentalAgreement versioned terms", "Keep outside checklist; store accepted version and timestamp", "PreHire"],
  ["All", "Customer / Staff signatures and dates", "AssetInspection signature fields + CompletedAt", "Immutable after completion", "PreHire,PostHire"],
];

const checklistGroups = [
  ["VEHICLE", "Vehicle condition", "OK_DAMAGE", ["Body Panels", "Front Bumper", "Rear Bumper", "Windshield", "Windows", "Headlights", "Tail Lights", "Tyres", "Spare Tyre", "Rims", "Interior Cleanliness", "Seats", "Air Conditioning", "Radio/USB", "Jack & Tools", "Registration Copy", "Fuel Level", "Odometer Reading"]],
  ["EQUIPMENT", "General equipment condition", "OK_ISSUE", ["Physical Condition", "Controls", "Power Supply", "Accessories", "Safety Labels", "Operational Test", "Cables/Hoses", "Cleanliness", "Damage Check"]],
  ["GENSET", "Genset condition", "OK_ISSUE", ["Engine Condition", "Control Panel", "Battery", "Fuel Tank", "Cooling System", "Exhaust System", "Alternator", "Canopy/Frame", "Meters & Gauges", "Safety Decals", "Circuit Breakers", "Cables & Connections", "Earth Stake Provided", "Service Log Book", "Fire Extinguisher", "Leaks Detected", "General Cleanliness"]],
  ["HEAVY_MACHINE", "Heavy machine condition", "OK_ISSUE", ["Engine", "Hydraulics", "Tracks/Tyres", "Cab", "Safety Alarms", "Attachments", "Leaks", "Fire Extinguisher", "Damage Diagram Reference"]],
];

const checklistRows = [];
const templateSeed = [];
for (const [categoryCode, section, responseType, items] of checklistGroups) {
  for (const stage of ["PreHire", "PostHire"]) {
    const checklist = items.map((label, index) => ({
      code: `${categoryCode.toLowerCase()}_${label.toLowerCase().replace(/[^a-z0-9]+/g, "_").replace(/^_|_$/g, "")}`,
      label,
      section,
      responseType,
      required: true,
      photoRequiredOnIssue: false,
      createMaintenanceOnIssue: false,
    }));
    if (categoryCode === "VEHICLE") {
      for (const label of ["Keys", "Spare Key", "Wheel Brace", "First Aid Kit", "Fire Extinguisher", "GPS"]) {
        checklist.push({
          code: `vehicle_accessory_${label.toLowerCase().replace(/[^a-z0-9]+/g, "_")}`,
          label,
          section: "Accessories issued / returned",
          responseType: "ISSUED_RETURNED_MISSING",
          required: true,
          photoRequiredOnIssue: false,
          createMaintenanceOnIssue: false,
        });
      }
    }
    if (["EQUIPMENT", "HEAVY_MACHINE"].includes(categoryCode) && stage === "PreHire") {
      for (const label of ["Safety briefing completed", "Existing damage acknowledged", "Fuel and hour meter verified", "Operator allocation confirmed if required"]) {
        checklist.push({
          code: `${categoryCode.toLowerCase()}_ack_${label.toLowerCase().replace(/[^a-z0-9]+/g, "_")}`,
          label,
          section: "Customer acceptance",
          responseType: "YES_NO",
          required: true,
          photoRequiredOnIssue: false,
          createMaintenanceOnIssue: false,
        });
      }
    }
    checklist.forEach((item, index) => checklistRows.push([categoryCode, stage, item.section, index + 1, item.code, item.label, item.responseType, "Yes", "Proposed – confirm", "Pending client rule"]));
    templateSeed.push({ categoryCode, name: `${categoryCode.replaceAll("_", " ")} ${stage === "PreHire" ? "pre-hire" : "post-hire"} inspection`, stage, requiresCustomerSignature: true, requiresStaffSignature: true, checklist });
  }
}

const clarifications = [
  ["Critical", "Assets", "Which source identifier is the official permanent asset number: Stock_No, Equipment, registration, or a separate number?", "Carpenters fleet/asset owner", "Open"],
  ["Critical", "Branches", "Are Suva Rona St, Suva Foster Rd and Autofield Nakasi separate CREMS branches, depots or locations under one branch?", "Carpenters operations", "Open"],
  ["Critical", "Equipment taxonomy", "Provide definitions for every CAT Sort code and confirm the approved category hierarchy.", "Carptrac asset owner", "Open"],
  ["Critical", "Equipment taxonomy", "Provide definitions for Make codes CAT, TM, OLY, PER, HHT, SEM and YYY.", "Carptrac asset owner", "Open"],
  ["Critical", "Equipment", "Does Type represent model, capacity, subtype, or a mixture? How should each value be stored?", "Carptrac asset owner", "Open"],
  ["High", "Vehicles", "Confirm whether Stocked_Date means purchase, stock receipt, commissioning or availability date.", "Motors fleet owner", "Open"],
  ["High", "Vehicles", "Confirm and correct missing/invalid registrations, trailing VIN punctuation and abnormal odometer readings.", "Motors fleet owner", "Open"],
  ["High", "Metering", "What are the approved meter units by category and can an asset have more than one meter?", "Operations + maintenance", "Open"],
  ["High", "Inspections", "Should every issue automatically block checkout/return-to-service, or only safety-critical items?", "Rental + maintenance", "Open"],
  ["High", "Inspections", "Which checklist items are safety-critical and which require mandatory photographs?", "Rental + HSE", "Open"],
  ["High", "Inspections", "Who signs each stage: customer, rental officer, operator, delivery driver or supervisor?", "Rental operations", "Open"],
  ["High", "Damage", "Should post-hire differences automatically create damage cases, maintenance jobs and customer charges?", "Rental + finance", "Open"],
  ["Medium", "Fuel", "Should fuel be captured as percentage, eighths, litres or gauge level?", "Rental operations", "Open"],
  ["Medium", "Documents", "Confirm retention period and access rules for licences, signatures, photos and inspection PDFs.", "Management + IT", "Open"],
  ["Medium", "Scope", "Does the supplied Carptrac list cover only Suva, or are other branches/assets expected?", "Carptrac management", "Open"],
  ["Medium", "Rates", "No rental rates, acquisition costs, ownership or maintenance thresholds are present; identify the authoritative sources.", "Finance + asset owner", "Open"],
];

const qualityRows = [
  ["Vehicle records", assets.filter(a => a.asset_type === "Vehicle").length, "39 source rows analysed"],
  ["Carptrac records", assets.filter(a => a.asset_type === "Equipment").length, "79 source rows analysed"],
  ["Missing vehicle registration", assets.filter(a => a.asset_type === "Vehicle" && a.quality_flags.includes("Missing registration")).length, "Requires correction before import"],
  ["Missing vehicle colour", assets.filter(a => a.asset_type === "Vehicle" && a.quality_flags.includes("Missing colour")).length, "Optional unless required for identification"],
  ["Missing first-registration date", assets.filter(a => a.asset_type === "Vehicle" && a.quality_flags.includes("first-registration")).length, "Confirm mandatory status"],
  ["Suspicious odometer values", assets.filter(a => a.asset_type === "Vehicle" && /odometer/i.test(a.quality_flags)).length, "Verify against vehicle records"],
  ["Source identifiers exposed", 0, "Registration, VIN, stock and equipment numbers are anonymised in this package"],
  ["Inspection template variants", templateSeed.length, "Pre-hire and post-hire for four categories"],
];

const workbook = Workbook.create();
const palette = { black: "#151515", yellow: "#FFEA00", green: "#08783E", pale: "#F4F6F1", white: "#FFFFFF", gray: "#69706A", red: "#C62828", border: "#D7DBD6" };

function colName(index) {
  let n = index + 1, result = "";
  while (n) { const rem = (n - 1) % 26; result = String.fromCharCode(65 + rem) + result; n = Math.floor((n - 1) / 26); }
  return result;
}

function addSheet(name, title, subtitle, headers, rows, widths, tableName) {
  const sheet = workbook.worksheets.add(name);
  sheet.showGridLines = false;
  const lastCol = colName(headers.length - 1);
  sheet.getRange(`A1:${lastCol}1`).merge();
  sheet.getRange("A1").values = [[title]];
  sheet.getRange(`A2:${lastCol}2`).merge();
  sheet.getRange("A2").values = [[subtitle]];
  sheet.getRange(`A1:${lastCol}1`).format = { fill: palette.black, font: { color: palette.white, bold: true, size: 18 }, rowHeight: 30, verticalAlignment: "center" };
  sheet.getRange(`A2:${lastCol}2`).format = { fill: palette.yellow, font: { color: palette.black, italic: true, size: 10 }, rowHeight: 25, wrapText: true, verticalAlignment: "center" };
  sheet.getRange(`A4:${lastCol}4`).values = [headers];
  sheet.getRange(`A4:${lastCol}4`).format = { fill: palette.green, font: { color: palette.white, bold: true }, wrapText: true, verticalAlignment: "center", rowHeight: 30 };
  if (rows.length) {
    sheet.getRange(`A5:${lastCol}${rows.length + 4}`).values = rows;
    sheet.getRange(`A5:${lastCol}${rows.length + 4}`).format = { borders: { preset: "all", style: "thin", color: palette.border }, wrapText: true, verticalAlignment: "top" };
    for (let row = 5; row <= rows.length + 4; row += 2) sheet.getRange(`A${row}:${lastCol}${row}`).format.fill = palette.pale;
    sheet.tables.add(`A4:${lastCol}${rows.length + 4}`, true, tableName);
  }
  widths.forEach((width, index) => sheet.getRange(`${colName(index)}:${colName(index)}`).format.columnWidth = width);
  sheet.freezePanes.freezeRows(4);
  return sheet;
}

const summary = workbook.worksheets.add("Read Me");
summary.showGridLines = false;
summary.getRange("A1:H1").merge(); summary.getRange("A1").values = [["CREMS CLIENT DATA MAPPING & VALIDATION PACK"]];
summary.getRange("A2:H2").merge(); summary.getRange("A2").values = [["ANONYMISED • FOR CARPENTERS CONFIRMATION • DO NOT IMPORT INTO PRODUCTION"]];
summary.getRange("A1:H1").format = { fill: palette.black, font: { color: palette.white, bold: true, size: 20 }, rowHeight: 34 };
summary.getRange("A2:H2").format = { fill: palette.yellow, font: { color: palette.black, bold: true }, rowHeight: 26 };
summary.getRange("A4:B10").values = [
  ["Purpose", "Validate source-to-CREMS mappings, inspection templates and data-quality decisions before import"],
  ["Vehicle records", 39], ["Carptrac records", 79], ["Total asset records", 118],
  ["Checklist documents", 4], ["Generated templates", 8], ["Production import status", "BLOCKED pending written client approval"],
];
summary.getRange("A4:A10").format = { fill: palette.green, font: { color: palette.white, bold: true } };
summary.getRange("A4:B10").format.borders = { preset: "all", style: "thin", color: palette.border };
summary.getRange("A12:H12").merge(); summary.getRange("A12").values = [["Recommended approval sequence"]];
summary.getRange("A12:H12").format = { fill: palette.black, font: { color: palette.white, bold: true } };
summary.getRange("A13:B18").values = [
  ["1", "Carpenters confirms branch/location hierarchy"],
  ["2", "Asset owners confirm identifiers, category codes, make codes and Type semantics"],
  ["3", "Rental, HSE and maintenance teams approve checklist items, blocking rules and signatures"],
  ["4", "Carpenters returns corrected source data with missing values resolved"],
  ["5", "CREMS team runs a non-production dry-run and reconciles counts"],
  ["6", "Authorized owner signs off production import and rollback plan"],
];
for (let row = 13; row <= 18; row++) summary.getRange(`B${row}:H${row}`).merge();
summary.getRange("A13:H18").format = { borders: { preset: "all", style: "thin", color: palette.border }, wrapText: true };
summary.getRange("A:A").format.columnWidth = 28; summary.getRange("B:H").format.columnWidth = 18;

addSheet("Column Mapping", "SOURCE COLUMN → CREMS DATABASE MAPPING", "All source columns are accounted for. Open decisions must be resolved before import.", ["Source", "Source column", "CREMS target", "Transformation", "Requirement", "Classification", "Decision / issue"],
  [...vehicleMapping.map(r => ["Vehicle stock list", ...r]), ...equipmentMapping.map(r => ["Carptrac stock list", ...r])], [18, 24, 34, 32, 19, 22, 48], "ColumnMappingTable");

const assetHeaders = ["Source row", "Asset number", "Division", "Service", "Branch", "Source location", "Type", "Category", "Name", "Manufacturer", "Model", "Model year", "Registration (anonymised)", "VIN/chassis (anonymised)", "Serial", "Colour", "Registration expiry", "Acquisition date", "First registered", "Meter unit", "Meter reading", "Status", "Ownership", "Daily rate", "Quality flags"];
const assetRows = assets.map(a => [a.source_row, a.asset_number, a.division_code, a.service_code, a.branch_code, a.source_location, a.asset_type, a.category, a.name, a.manufacturer, a.model, a.model_year, a.registration_number, a.vin_or_chassis_number, a.serial_number, a.colour, a.registration_expiry, a.acquisition_date, a.first_registered_date, a.meter_unit, a.current_meter_reading, a.status, a.ownership_type, a.daily_rate, a.quality_flags]);
const assetSheet = addSheet("Asset Import", "ANONYMISED ASSET IMPORT CANDIDATES", "118 records mapped to the current CREMS Asset model. Identifiers are non-production placeholders.", assetHeaders, assetRows, [11, 18, 13, 20, 10, 25, 12, 22, 40, 18, 28, 12, 22, 24, 18, 14, 17, 16, 17, 12, 14, 13, 14, 12, 48], "AssetImportTable");
assetSheet.getRange(`W5:W${assetRows.length + 4}`).dataValidation = { rule: { type: "list", values: ["", "Owned", "Leased", "Financed"] } };
assetSheet.getRange(`V5:V${assetRows.length + 4}`).dataValidation = { rule: { type: "list", values: ["Available", "Reserved", "Maintenance", "OutOfService", "Retired"] } };

addSheet("Checklist Fields", "CONFIGURABLE INSPECTION TEMPLATE ITEMS", "Each paper checklist is split into pre-hire and post-hire templates. An issue can require evidence and create a maintenance referral.", ["Category code", "Stage", "Section", "Order", "Item code", "Checklist label", "Response type", "Required", "Photo on issue", "Create maintenance on issue"], checklistRows, [18, 14, 28, 10, 38, 28, 17, 12, 16, 24], "ChecklistItemsTable");

addSheet("Header Fields", "INSPECTION HEADER & SIGN-OFF FIELD MAPPING", "Fields that are not checklist questions should be system-derived or captured in the guided handover/return workflow.", ["Checklist", "Paper field", "CREMS target", "Capture rule", "Stage"], headerFields, [22, 31, 48, 34, 20], "HeaderFieldsTable");

addSheet("Data Quality", "SOURCE DATA QUALITY SUMMARY", "No source rows were deleted. Issues are flagged for correction or explicit acceptance.", ["Measure", "Count", "Action"], qualityRows, [34, 14, 62], "DataQualityTable");

const clarificationSheet = addSheet("Client Decisions", "CLIENT CONFIRMATION REGISTER", "Carpenters should complete Decision / answer, Approved by and Approval date before production import.", ["Priority", "Area", "Question / business rule", "Suggested owner", "Status", "Decision / answer", "Approved by", "Approval date"], clarifications.map(r => [...r, "", "", ""]), [13, 20, 72, 27, 14, 55, 22, 16], "ClientDecisionsTable");
clarificationSheet.getRange(`E5:E${clarifications.length + 4}`).dataValidation = { rule: { type: "list", values: ["Open", "Confirmed", "Rejected", "Deferred"] } };

const approvalRows = [
  ["Branch/location structure approved", "", "", ""],
  ["Vehicle source mapping approved", "", "", ""],
  ["Carptrac codebooks and category mapping approved", "", "", ""],
  ["Inspection templates and signatures approved", "", "", ""],
  ["Data corrections supplied", "", "", ""],
  ["Non-production dry-run reconciled (118 expected)", "", "", ""],
  ["Production import authorized", "", "", ""],
];
const approvals = addSheet("Sign-off", "IMPORT APPROVAL GATE", "Production import must remain blocked until every mandatory gate is approved by an authorized Carpenters representative.", ["Approval gate", "Status", "Approved by", "Date / notes"], approvalRows, [58, 18, 28, 44], "ApprovalGateTable");
approvals.getRange("B5:B11").dataValidation = { rule: { type: "list", values: ["Not approved", "Approved", "Not applicable"] } };

const seedPackage = {
  metadata: {
    classification: "ANONYMISED CLIENT VALIDATION SEED - APPROVAL REQUIRED",
    productionImportAllowed: false,
    expectedAssetCount: assets.length,
    generatedAt: new Date().toISOString(),
    targetEntities: ["Asset", "AssetCategory", "AssetAttributeDefinition", "AssetAttributeValue", "InspectionTemplate"],
    maintenanceReferralPolicy: "Pending Carpenters approval; all generated template flags default to false",
  },
  assets,
  inspectionTemplates: templateSeed,
  requiredAttributeDefinitions: [
    { categoryScope: "Vehicle", code: "colour", name: "Colour", dataType: "Text", required: false, searchable: true, customerVisible: true },
    { categoryScope: "Vehicle", code: "registration_expiry", name: "Registration expiry", dataType: "Date", required: true, searchable: false, customerVisible: false },
    { categoryScope: "Vehicle", code: "first_registered_date", name: "First registered date", dataType: "Date", required: false, searchable: false, customerVisible: false },
    { categoryScope: "Equipment", code: "source_sort_code", name: "Source sort code", dataType: "Text", required: false, searchable: true, customerVisible: false },
    { categoryScope: "Equipment", code: "capacity_or_subtype", name: "Capacity / subtype", dataType: "Text", required: false, searchable: true, customerVisible: true },
  ],
};

await fs.writeFile(path.join(outputDir, "CREMS_Client_Validation_Seed.json"), JSON.stringify(seedPackage, null, 2));
await fs.writeFile(path.join(outputDir, "IMPORT_BLOCKED.txt"), "Production import is intentionally blocked. Obtain written Carpenters approval and resolve every Critical/High decision before using the seed package.\n");

const output = await SpreadsheetFile.exportXlsx(workbook);
const workbookPath = path.join(outputDir, "CREMS_Client_Data_Mapping_and_Import.xlsx");
await output.save(workbookPath);

for (const sheetName of ["Read Me", "Column Mapping", "Asset Import", "Checklist Fields", "Header Fields", "Data Quality", "Client Decisions", "Sign-off"]) {
  const preview = await workbook.render({ sheetName, autoCrop: "all", scale: sheetName === "Asset Import" ? 0.75 : 1.1, format: "png" });
  await fs.writeFile(path.join(reviewDir, `${sheetName.replaceAll(" ", "_")}.png`), new Uint8Array(await preview.arrayBuffer()));
}

const inspect = await workbook.inspect({ kind: "workbook,sheet,table", maxChars: 16000, tableMaxRows: 6, tableMaxCols: 12, tableMaxCellChars: 100 });
await fs.writeFile(path.join(reviewDir, "workbook_overview.ndjson"), inspect.ndjson);
const errors = await workbook.inspect({ kind: "match", searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A", options: { useRegex: true, maxResults: 200 }, maxChars: 8000 });
await fs.writeFile(path.join(reviewDir, "formula_errors.ndjson"), errors.ndjson);
console.log(JSON.stringify({ workbookPath, seedPath: path.join(outputDir, "CREMS_Client_Validation_Seed.json"), sheets: 8, assets: assets.length, templates: templateSeed.length }, null, 2));
