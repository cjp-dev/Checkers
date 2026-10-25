import fs from 'node:fs';
import path from 'node:path';

const DOCS_DIR = path.resolve('docs/brain');
const GUI_SPEC_FILE = path.resolve('docs/general-game-gui-specification.md');
const REPORT_FILE = path.resolve('scratch/checkers-audit-report.md');
const INVENTORY_FILE = path.resolve('scratch/checkers-snippet-inventory.json');

// Files to audit
const mdFiles = [];
if (fs.existsSync(DOCS_DIR)) {
  const brainFiles = fs.readdirSync(DOCS_DIR)
    .filter(f => f.endsWith('.md'))
    .map(f => path.join(DOCS_DIR, f));
  mdFiles.push(...brainFiles);
}
if (fs.existsSync(GUI_SPEC_FILE)) {
  mdFiles.push(GUI_SPEC_FILE);
}

// Discrepancy patterns to scan for
const PATTERNS = [
  {
    id: 'METHOD_NAME_NEGAMAX',
    pattern: /NegamaxBitboard/g,
    severity: 'ERROR',
    description: "References 'NegamaxBitboard' instead of active C# method 'NegaMax'",
    expected: 'NegaMax'
  },
  {
    id: 'TT_GHOST_POINTER',
    pattern: /_entriesPtr/g,
    severity: 'ERROR',
    description: "References non-existent cached pointer '_entriesPtr' (production uses MemoryMarshal.GetArrayDataReference)",
    expected: 'ref MemoryMarshal.GetArrayDataReference(_entries)'
  },
  {
    id: 'TT_BOUND_FLAG_EXACT_1',
    pattern: /Exact[^\d\n|]*\b1\b/gi,
    severity: 'ERROR',
    description: "Cites ancestral C macro 'PV_NODE 1' / 'Exact (1)' instead of active C# enum 'Exact = 0'",
    expected: 'Exact = 0'
  },
  {
    id: 'TT_BOUND_FLAG_LOWER_2',
    pattern: /LowerBound[^\d\n|]*\b2\b/gi,
    severity: 'ERROR',
    description: "Cites ancestral C macro 'LOWER_BOUND 2' instead of active C# enum 'LowerBound = 1'",
    expected: 'LowerBound = 1'
  },
  {
    id: 'TT_BOUND_FLAG_UPPER_3',
    pattern: /UpperBound[^\d\n|]*\b3\b/gi,
    severity: 'ERROR',
    description: "Cites ancestral C macro 'UPPER_BOUND 3' instead of active C# enum 'UpperBound = 2'",
    expected: 'UpperBound = 2'
  },
  {
    id: 'TT_FLAG_BIT_2_EVAL',
    pattern: /Bit\s*`?2`?\s*(?:\(`?0x04`?\))?:\s*`?HasStaticEval/gi,
    severity: 'ERROR',
    description: "Claims bit 2 (0x04) is HasStaticEval; in production 0x04 is OccupiedBit and 0x10 is HasStaticEvalBit",
    expected: 'OccupiedBit = 0x04, HasStaticEvalBit = 0x10'
  },
  {
    id: 'ORDERING_FICTITIOUS_CAPTURE_BRANCH',
    pattern: /if\s*\(\s*moves\[0\]\.IsCapture\s*\)/g,
    severity: 'ERROR',
    description: "Fictitious 'if (moves[0].IsCapture)' branch in OrderBitMoves snippet (not present in MinimaxPlayer.cs)",
    expected: 'Inline scores[i] capture scoring'
  },
  {
    id: 'ORDERING_CAPTURE_MOVES_IN_ORDER_BIT_MOVES',
    pattern: /OrderCaptureMoves\s*\(\s*moves\s*\)/g,
    severity: 'ERROR',
    description: "Calls OrderCaptureMoves(moves) inside OrderBitMoves snippet; in production it is only called by Quiescence",
    expected: 'Only called in Quiescence'
  },
  {
    id: 'BITMOVE_RECORD_STRUCT',
    pattern: /BitMove[^\n]*readonly\s+record\s+struct/gi,
    severity: 'WARNING',
    description: "Describes BitMove as 'readonly record struct'; in BitMove.cs it is 'readonly struct'",
    expected: 'readonly struct'
  },
  {
    id: 'BITBOARD_UPMOVERS_PSEUDOCODE',
    pattern: /upMovers/g,
    severity: 'WARNING',
    description: "Uses pseudocode variable 'upMovers' in bitboard move snippet instead of active C# 'ownMen'",
    expected: 'ownMen'
  },
  {
    id: 'BITBOARD_BARE_NOTCOL',
    pattern: /(?<!BitboardMasks\.)NotCol[AHBGH]+/g,
    severity: 'WARNING',
    description: "Uses unqualified 'NotCol*' constant in C# snippet without 'BitboardMasks.' qualifier",
    expected: 'BitboardMasks.NotColA'
  },
  {
    id: 'OUTDATED_TEST_COUNT_CORE',
    pattern: /123\s+(?:unit\s+)?tests?\s+(?:validating|in\s+Checkers\.Core)/gi,
    severity: 'WARNING',
    description: "Cites 123 Core tests, but Checkers.Core.Tests currently has 124 passing tests",
    expected: '124 unit tests'
  },
  {
    id: 'OUTDATED_TEST_COUNT_APP',
    pattern: /40\s+(?:unit\s+)?tests?\s+(?:covering|in\s+Checkers\.App)/gi,
    severity: 'WARNING',
    description: "Cites 40 App tests, but Checkers.App.Tests currently has 70 passing tests",
    expected: '70 unit tests'
  }
];

// Extract code blocks and scan for discrepancies
const allBlocks = [];
const allDiscrepancies = [];
const fileStats = [];

for (const filePath of mdFiles) {
  const relPath = path.relative('.', filePath).replace(/\\/g, '/');
  const fileName = path.basename(filePath);
  const content = fs.readFileSync(filePath, 'utf8');
  const lines = content.split(/\r?\n/);

  let inBlock = false;
  let startLine = 0;
  let lang = '';
  let blockLines = [];
  let fileBlockCount = 0;

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const match = line.match(/^```([a-zA-Z0-9_\-]*)/);

    if (match) {
      if (!inBlock) {
        inBlock = true;
        startLine = i + 1;
        lang = match[1] || 'text';
        blockLines = [];
      } else {
        inBlock = false;
        const endLine = i + 1;
        fileBlockCount++;
        const blockText = blockLines.join('\n');

        allBlocks.push({
          id: `${fileName}#L${startLine}-L${endLine}`,
          file: relPath,
          fileName,
          startLine,
          endLine,
          language: lang,
          lineCount: endLine - startLine + 1,
          preview: blockLines.slice(0, 3).join(' ').slice(0, 100),
          content: blockText
        });
      }
    } else if (inBlock) {
      blockLines.push(line);
    }

    // Line-by-line pattern matching across all content (both prose and code)
    for (const pat of PATTERNS) {
      pat.pattern.lastIndex = 0;
      let m;
      while ((m = pat.pattern.exec(line)) !== null) {
        allDiscrepancies.push({
          file: relPath,
          fileName,
          line: i + 1,
          inCodeBlock: inBlock,
          patternId: pat.id,
          severity: pat.severity,
          matchedText: m[0],
          description: pat.description,
          expected: pat.expected,
          snippet: line.trim()
        });
      }
    }
  }

  fileStats.push({
    file: relPath,
    fileName,
    lineCount: lines.length,
    blockCount: fileBlockCount
  });
}

// Ensure scratch dir exists
fs.mkdirSync('scratch', { recursive: true });

// Write JSON inventory
fs.writeFileSync(INVENTORY_FILE, JSON.stringify({
  generatedAt: new Date().toISOString(),
  totalFiles: mdFiles.length,
  totalCodeBlocks: allBlocks.length,
  totalDiscrepancies: allDiscrepancies.length,
  fileStats,
  codeBlocks: allBlocks,
  discrepancies: allDiscrepancies
}, null, 2));

// Generate Markdown Report
let mdReport = `# Checkers Documentation & Code Snippet Audit Report (Phase 1 Baseline)

Generated: ${new Date().toISOString()}  
Target: **${mdFiles.length} files** (${mdFiles.length - 1} chapters in \`docs/brain/\` + \`docs/general-game-gui-specification.md\`)  
Total Fenced Code Blocks: **${allBlocks.length}**  
Detected Discrepancies: **${allDiscrepancies.length}** (${allDiscrepancies.filter(d => d.severity === 'ERROR').length} Errors, ${allDiscrepancies.filter(d => d.severity === 'WARNING').length} Warnings)

---

## 1. Discrepancy Summary by Category

| Issue ID | Severity | Occurrences | Description | Expected Correction |
|---|:---:|:---:|---|---|
${PATTERNS.map(p => {
  const count = allDiscrepancies.filter(d => d.patternId === p.id).length;
  return `| \`${p.id}\` | **${p.severity}** | ${count} | ${p.description} | \`${p.expected}\` |`;
}).join('\n')}

---

## 2. Itemized Discrepancy Register

| # | File | Line | Context | Severity | Detected Text | Description |
|---|---|:---:|:---:|:---:|---|---|
${allDiscrepancies.map((d, idx) => {
  const loc = `[${d.fileName}:${d.line}](../${d.file}#L${d.line})`;
  const ctx = d.inCodeBlock ? 'Code' : 'Prose';
  return `| ${idx + 1} | ${loc} | ${d.line} | ${ctx} | **${d.severity}** | \`${d.matchedText.replace(/\|/g, '\\|')}\` | ${d.description} |`;
}).join('\n')}

---

## 3. Fenced Code Block Inventory (All ${allBlocks.length} Blocks)

| # | File | Line Range | Language | Size | Preview |
|---|---|:---:|:---:|:---:|---|
${allBlocks.map((b, idx) => {
  const loc = `[${b.fileName}](../${b.file}#L${b.startLine}-L${b.endLine})`;
  const prev = b.preview.replace(/\|/g, '\\|').replace(/\n/g, ' ');
  return `| ${idx + 1} | ${loc} | L${b.startLine}–L${b.endLine} | \`${b.language}\` | ${b.lineCount} lines | \`${prev}\` |`;
}).join('\n')}

---

## 4. File-by-File Code Block Distribution

| File | Lines | Code Blocks | Languages Present |
|---|---:|---:|---|
${fileStats.map(f => {
  const blocks = allBlocks.filter(b => b.fileName === f.fileName);
  const langs = [...new Set(blocks.map(b => b.language))].map(l => `${l} (${blocks.filter(b => b.language === l).length})`).join(', ');
  return `| [${f.fileName}](../${f.file}) | ${f.lineCount} | ${f.blockCount} | ${langs || '—'} |`;
}).join('\n')}
`;

fs.writeFileSync(REPORT_FILE, mdReport);

console.log(`Phase 1 Audit Complete!`);
console.log(`- Files audited: ${mdFiles.length}`);
console.log(`- Total code blocks cataloged: ${allBlocks.length}`);
console.log(`- Discrepancies flagged: ${allDiscrepancies.length} (${allDiscrepancies.filter(d => d.severity === 'ERROR').length} Errors, ${allDiscrepancies.filter(d => d.severity === 'WARNING').length} Warnings)`);
console.log(`- Inventory saved to: ${INVENTORY_FILE}`);
console.log(`- Detailed report saved to: ${REPORT_FILE}`);
