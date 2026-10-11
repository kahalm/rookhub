// Reine Syntaxprüfung einer .ts-Datei (keine Typen) mit dem TypeScript des Frontends.
const path = require('path'), fs = require('fs');
const file = process.argv[2];
const candidates = [
  path.join(__dirname, '../../src/frontend/app/node_modules/typescript'),
  path.join(require('os').homedir(), 'claude/node_modules/typescript'),
];
let ts;
for (const c of candidates) { try { ts = require(c); break; } catch {} }
if (!ts) process.exit(0); // ohne TypeScript nicht prüfen statt fälschlich blockieren
const sf = ts.createSourceFile(file, fs.readFileSync(file, 'utf8'), ts.ScriptTarget.Latest, true);
const diags = sf.parseDiagnostics || [];
for (const d of diags.slice(0, 5)) {
  const { line, character } = sf.getLineAndCharacterOfPosition(d.start);
  console.error(`${line + 1}:${character + 1} ${ts.flattenDiagnosticMessageText(d.messageText, ' ')}`);
}
process.exit(diags.length ? 1 : 0);
