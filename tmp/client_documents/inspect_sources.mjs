import fs from "node:fs/promises";
import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const sources = [
  ["cat", "/Users/kavishchandra/Downloads/Rental Stock List - CAT (1).xlsx"],
  ["vehicles", "/Users/kavishchandra/Downloads/Rental Vehicle Stock List (1).xlsx"],
];

for (const [key, path] of sources) {
  const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(path));
  const overview = await workbook.inspect({
    kind: "workbook,sheet,table",
    maxChars: 12000,
    tableMaxRows: 12,
    tableMaxCols: 20,
    tableMaxCellChars: 120,
  });
  await fs.writeFile(`tmp/client_documents/spreadsheet_review/${key}-overview.ndjson`, overview.ndjson);
  const sheets = await workbook.inspect({ kind: "sheet", include: "id,name", maxChars: 4000 });
  console.log(`=== ${key} sheets ===\n${sheets.ndjson}`);
  console.log(`=== ${key} overview ===\n${overview.ndjson}`);

  const sheetNames = [];
  for (const line of sheets.ndjson.trim().split("\n")) {
    try {
      const obj = JSON.parse(line);
      const name = obj.name || obj.sheetName;
      if (name) sheetNames.push(name);
    } catch {}
  }
  if (!sheetNames.length) {
    const first = workbook.worksheets.getItemAt(0);
    sheetNames.push(first.name);
  }
  for (let index = 0; index < sheetNames.length; index += 1) {
    const sheetName = sheetNames[index];
    const region = await workbook.inspect({
      kind: "region",
      sheetId: sheetName,
      range: "A1:Z100",
      maxChars: 20000,
      tableMaxRows: 100,
      tableMaxCols: 26,
      tableMaxCellChars: 150,
    });
    await fs.writeFile(`tmp/client_documents/spreadsheet_review/${key}-${index + 1}-region.ndjson`, region.ndjson);
    console.log(`=== ${key} ${sheetName} region ===\n${region.ndjson}`);
    const preview = await workbook.render({ sheetName, autoCrop: "all", scale: 1.4, format: "png" });
    await fs.writeFile(`tmp/client_documents/spreadsheet_review/${key}-${index + 1}.png`, new Uint8Array(await preview.arrayBuffer()));
  }
}
