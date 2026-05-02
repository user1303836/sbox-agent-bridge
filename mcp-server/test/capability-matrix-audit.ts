import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const matrixPath = resolve(process.cwd(), "..", "docs", "capability-matrix.md");
const content = readFileSync(matrixPath, "utf8");
const allowedStatuses = new Set(["Verified", "Verified gap"]);
const rows: Array<{ section: string; capability: string; status: string; note: string }> = [];
let section = "";

for (const line of content.split(/\r?\n/)) {
  const sectionMatch = line.match(/^##\s+(.+)$/);
  if (sectionMatch) {
    section = sectionMatch[1].trim();
    continue;
  }

  if (!line.startsWith("|") || line.includes("|---")) {
    continue;
  }

  const cells = line
    .split("|")
    .slice(1, -1)
    .map((cell) => cell.trim());

  if (cells[0] === "Capability") {
    continue;
  }

  const status = cells.length >= 5 ? cells[3] : cells[1];
  const note = cells.length >= 5 ? cells[4] : cells[2] ?? "";
  rows.push({ section, capability: cells[0], status, note });
}

const failures = rows.filter((row) => !allowedStatuses.has(row.status));
const verified = rows.filter((row) => row.status === "Verified").length;
const verifiedGaps = rows.filter((row) => row.status === "Verified gap").length;

if (failures.length > 0) {
  console.error(
    JSON.stringify(
      {
        ok: false,
        matrixPath,
        allowedStatuses: [...allowedStatuses],
        failures
      },
      null,
      2
    )
  );
  process.exit(1);
}

console.log(
  JSON.stringify(
    {
      ok: true,
      matrixPath,
      totalCapabilities: rows.length,
      verified,
      verifiedGaps,
      verificationCoverage: 1
    },
    null,
    2
  )
);
