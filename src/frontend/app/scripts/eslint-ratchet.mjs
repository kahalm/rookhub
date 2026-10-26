#!/usr/bin/env node
// ESLint-Ratsche (Codereview F8-004, PHASE 2 + 3).
//
// eslint.config.mjs stellt jede Regel mit Bestand auf 'warn' — der Bestand (rund 1 200 Meldungen) bleibt stehen,
// aber er darf nicht WACHSEN. Dieses Skript zaehlt die Meldungen je Regel und vergleicht sie mit
// eslint-baseline.json:
//   - eine Regel liegt UEBER ihrer Baseline  → Exit 1, mit Regel, Baseline → jetzt und den Dateien
//     mit Treffern dieser Regel (dort steckt die neue Stelle). Abhilfe: Stelle fixen, NICHT die Baseline heben.
//   - eine Regel liegt DARUNTER               → Hinweis „Baseline kann gesenkt werden" (bleibt gruen).
//   - Regel ohne Baseline-Eintrag             → zaehlt wie Baseline 0 (neue Regeln starten sauber).
//   - Meldungen ohne Regel (Parse-Fehler)     → laufen unter „(Parse-Fehler)" mit Baseline 0, also rot.
//   - PHASE 3: eine 'warn'-Regel mit Baseline 0 und 0 Meldungen → Hinweis „kann auf 'error'" (bleibt gruen).
//     'error'-Regeln zaehlen genauso (Baseline 0); CiWorkflowTests erlaubt 'error' nur bei Baseline 0.
//
// Aufruf (aus src/frontend/app):
//   node scripts/eslint-ratchet.mjs                    pruefen (npm run lint:ratchet)
//   node scripts/eslint-ratchet.mjs --update           Baseline auf die aktuellen Zahlen SENKEN (npm run lint:ratchet:update)
//   node scripts/eslint-ratchet.mjs --update --force   auch anheben (nur mit Begruendung im Commit);
//                                                      fehlt die Datei, legt --update sie mit den aktuellen Zahlen an
//   node scripts/eslint-ratchet.mjs --summary <datei>  zusaetzlich Markdown-Tabelle an <datei> anhaengen (CI: $GITHUB_STEP_SUMMARY)
//
// Die Vergleichslogik (compare, applyUpdate, serializeBaseline, summaryMarkdown, phase3Candidates) ist exportiert und
// in scripts/eslint-ratchet.test.mjs per `node --test` geprueft.
import { appendFileSync, existsSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

export const PARSE_ERROR = '(Parse-Fehler)';

const APP_DIR = join(dirname(fileURLToPath(import.meta.url)), '..');
const BASELINE_FILE = join(APP_DIR, 'eslint-baseline.json');

/** Alle Regelnamen beider Seiten, sortiert (stabile Ausgabe). */
function allRules(baseline, current) {
  return [...new Set([...Object.keys(baseline), ...Object.keys(current)])].sort();
}

/**
 * Vergleicht Baseline und aktuelle Zahlen je Regel. Fehlende Eintraege zaehlen als 0.
 * @param {Record<string, number>} baseline
 * @param {Record<string, number>} current
 */
export function compare(baseline, current) {
  const rows = allRules(baseline, current).map((rule) => {
    const base = baseline[rule] ?? 0;
    const now = current[rule] ?? 0;
    return { rule, baseline: base, current: now, delta: now - base };
  });
  const increased = rows.filter((r) => r.delta > 0);
  const decreased = rows.filter((r) => r.delta < 0);
  return { rows, increased, decreased, ok: increased.length === 0 };
}

/**
 * Neue Baseline fuer --update: je Regel min(Baseline, jetzt); mit force die aktuellen Zahlen.
 * Regeln, die ganz verschwunden sind, bleiben mit 0 stehen (Baseline 0 = Kandidat fuer 'error').
 * @returns {{ next: Record<string, number>, raised: string[] }} raised = Regeln ueber der Baseline
 *   (ohne force NICHT angehoben, mit force angehoben)
 */
export function applyUpdate(baseline, current, { force = false } = {}) {
  /** @type {Record<string, number>} */
  const next = {};
  const raised = [];
  for (const { rule, baseline: base, current: now } of compare(baseline, current).rows) {
    if (now > base) raised.push(rule);
    next[rule] = force ? now : Math.min(base, now);
  }
  // Ohne force taucht eine neue Regel (Baseline fehlt, jetzt > 0) mit 0 auf — das ist ihre Baseline.
  return { next, raised };
}

/**
 * PHASE 3: Regeln, die auf 'error' duerfen — Baseline 0, jetzt 0 Meldungen, in der Konfiguration noch 'warn'.
 * @param {Record<string, number>} baseline
 * @param {Record<string, number>} current
 * @param {Record<string, 'off' | 'warn' | 'error'>} severities Schwere je Regel aus der wirksamen Konfiguration
 */
export function phase3Candidates(baseline, current, severities) {
  return Object.keys(severities)
    .filter((rule) => severities[rule] === 'warn' && (baseline[rule] ?? 0) === 0 && (current[rule] ?? 0) === 0)
    .sort();
}

/** ESLint-Schwere (0/1/2, 'off'/'warn'/'error' oder [schwere, …optionen]) als Wort. */
export function severityName(entry) {
  const raw = Array.isArray(entry) ? entry[0] : entry;
  return raw === 2 || raw === 'error' ? 'error' : raw === 1 || raw === 'warn' ? 'warn' : 'off';
}

/** Sortierte Schluessel, zwei Leerzeichen, Zeilenende am Schluss — byte-stabil. */
export function serializeBaseline(counts) {
  const sorted = Object.fromEntries(Object.keys(counts).sort().map((k) => [k, counts[k]]));
  return JSON.stringify(sorted, null, 2) + '\n';
}

/** Markdown-Tabelle Regel | Baseline | jetzt | Δ fuer die Job-Summary (auch von scripts/stylelint-ratchet.mjs genutzt). */
export function summaryMarkdown(result, fileCount, { title = 'ESLint-Ratsche (Frontend)', updateCmd = 'npm run lint:ratchet:update' } = {}) {
  const total = (key) => result.rows.reduce((sum, r) => sum + r[key], 0);
  const sign = (d) => (d > 0 ? `+${d}` : `${d}`);
  const status = result.ok ? 'gruen' : `ROT — ${result.increased.length} Regel(n) ueber der Baseline`;
  const lines = [
    `## ${title}`,
    '',
    `${total('current')} Meldungen in ${fileCount} Dateien (Baseline ${total('baseline')}) — ${status}`,
    '',
    '| Regel | Baseline | jetzt | Δ |',
    '|---|---:|---:|---:|',
    ...result.rows.map((r) => `| ${r.rule} | ${r.baseline} | ${r.current} | ${r.delta > 0 ? '**' + sign(r.delta) + '**' : sign(r.delta)} |`),
    `| **gesamt** | ${total('baseline')} | ${total('current')} | ${sign(total('current') - total('baseline'))} |`,
    '',
  ];
  if (result.decreased.length) lines.push(`Baseline kann gesenkt werden: \`${updateCmd}\``, '');
  return lines.join('\n') + '\n';
}

/** ESLint ueber die API laufen lassen; liefert Zahlen je Regel und Dateien je Regel. */
async function lint() {
  const { ESLint } = await import('eslint');
  const eslint = new ESLint({ cwd: APP_DIR });
  const results = await eslint.lintFiles(['.']);
  /** @type {Record<string, number>} */
  const counts = {};
  /** @type {Record<string, Map<string, number>>} */
  const files = {};
  let filesWithMessages = 0;
  for (const result of results) {
    if (result.messages.length) filesWithMessages++;
    for (const message of result.messages) {
      const rule = message.ruleId ?? PARSE_ERROR;
      counts[rule] = (counts[rule] ?? 0) + 1;
      const perFile = (files[rule] ??= new Map());
      const path = relative(APP_DIR, result.filePath);
      perFile.set(path, (perFile.get(path) ?? 0) + 1);
    }
  }
  // Wirksame Schwere je Regel (fuer den Phase-3-Hinweis); alle Projekte teilen eine Konfiguration (src*/**/*.ts).
  const config = await eslint.calculateConfigForFile(join(APP_DIR, 'src', 'main.ts'));
  /** @type {Record<string, 'off' | 'warn' | 'error'>} */
  const severities = {};
  for (const [rule, entry] of Object.entries(config?.rules ?? {})) severities[rule] = severityName(entry);
  return { counts, files, filesWithMessages, severities };
}

function readBaseline() {
  if (!existsSync(BASELINE_FILE)) return {};
  return JSON.parse(readFileSync(BASELINE_FILE, 'utf8'));
}

function parseArgs(argv) {
  const opts = { update: false, force: false, summary: null };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === '--update') opts.update = true;
    else if (arg === '--force') opts.force = true;
    else if (arg === '--summary') opts.summary = argv[++i] ?? null;
    else if (arg.startsWith('--summary=')) opts.summary = arg.slice('--summary='.length);
    else throw new Error(`Unbekannte Option: ${arg}`);
  }
  if (opts.force && !opts.update) throw new Error('--force gilt nur zusammen mit --update');
  if (opts.summary === '') opts.summary = null; // leeres $GITHUB_STEP_SUMMARY lokal
  return opts;
}

async function main() {
  const opts = parseArgs(process.argv.slice(2));
  const baseline = readBaseline();
  const { counts, files, filesWithMessages, severities } = await lint();
  const result = compare(baseline, counts);

  if (opts.summary) appendFileSync(opts.summary, summaryMarkdown(result, filesWithMessages));

  if (opts.update) {
    // Erste Baseline (Datei fehlt): die aktuellen Zahlen uebernehmen, sonst stuende alles auf 0.
    const initial = !existsSync(BASELINE_FILE);
    if (initial) opts.force = true;
    const { next, raised } = applyUpdate(baseline, counts, { force: opts.force });
    const text = serializeBaseline(next);
    const old = existsSync(BASELINE_FILE) ? readFileSync(BASELINE_FILE, 'utf8') : '';
    if (text !== old) writeFileSync(BASELINE_FILE, text);
    for (const rule of initial ? [] : raised) {
      const r = result.rows.find((x) => x.rule === rule);
      console.warn(opts.force
        ? `WARNUNG: ${rule} ANGEHOBEN ${r.baseline} → ${r.current} (--force) — im Commit begruenden.`
        : `${rule}: ${r.baseline} → ${r.current} liegt UEBER der Baseline und wurde NICHT angehoben (Stelle fixen; anheben nur mit --force).`);
    }
    console.log(text === old ? 'eslint-baseline.json unveraendert.' : 'eslint-baseline.json geschrieben.');
    process.exitCode = raised.length && !opts.force ? 1 : 0;
    return;
  }

  const total = Object.values(counts).reduce((a, b) => a + b, 0);
  console.log(`ESLint-Ratsche: ${total} Meldungen in ${filesWithMessages} Dateien.`);
  for (const r of result.increased) {
    console.error(`\nFEHLER ${r.rule}: Baseline ${r.baseline} → jetzt ${r.current} (+${r.delta}). Dateien mit Treffern dieser Regel:`);
    for (const [path, n] of [...files[r.rule]].sort((a, b) => a[0].localeCompare(b[0])))
      console.error(`  ${path} (${n})`);
  }
  for (const r of result.decreased)
    console.log(`Hinweis ${r.rule}: Baseline ${r.baseline} → jetzt ${r.current} — Baseline kann gesenkt werden (npm run lint:ratchet:update).`);
  for (const rule of phase3Candidates(baseline, counts, severities))
    console.log(`Hinweis ${rule}: Baseline 0, keine Meldung — kann in eslint.config.mjs auf 'error' (Phase 3, docs/ci.md).`);
  if (!result.ok) {
    console.error('\nDie Ratsche ist rot: neue Stelle fixen (die Dateiliste oben zeigt, wo die Regel trifft), NICHT die Baseline heben.');
    process.exitCode = 1;
  } else {
    console.log('Keine Regel ueber der Baseline.');
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main().catch((err) => {
    console.error(err);
    process.exitCode = 2;
  });
}
