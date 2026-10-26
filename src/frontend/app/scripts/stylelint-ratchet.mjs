#!/usr/bin/env node
// stylelint-Ratsche (Codereview F8-003: fest verdrahtete Farbwerte) — nach dem Muster der ESLint-Ratsche.
//
// stylelint.config.mjs stellt die Farb-Regeln auf 'warning'; der Bestand (Hex-Werte in Komponenten-Styles) bleibt
// stehen, darf aber nicht WACHSEN. Dieses Skript zaehlt die Meldungen je Regel und vergleicht mit
// stylelint-baseline.json — dieselbe Logik wie scripts/eslint-ratchet.mjs (compare/applyUpdate/serializeBaseline
// von dort):
//   - eine Regel liegt UEBER ihrer Baseline → Exit 1, mit Regel, Baseline → jetzt und den Dateien mit Treffern.
//     Abhilfe: Token aus src/_tokens.scss nehmen (`color: var(--rh-error)`), NICHT die Baseline heben.
//   - darunter → Hinweis „Baseline kann gesenkt werden" (bleibt gruen); Regel ohne Baseline zaehlt wie 0;
//     CSS-Syntaxfehler laufen unter ihrem Namen (CssSyntaxError) mit Baseline 0, also rot.
//
// Gelintet werden src*/**/*.scss|css UND die Inline-Styles der Komponenten: stylelint kennt keine TS-Dateien,
// deshalb zieht extractInlineStyles() die Template-Literale aus `styles: [`…`]` / `styles: `…`` (Zeilen bleiben
// auf die .ts-Datei bezogen). Literale mit `${…}` werden uebersprungen und gezaehlt.
//
// Aufruf (aus src/frontend/app):
//   node scripts/stylelint-ratchet.mjs                    pruefen (npm run lint:css:ratchet)
//   node scripts/stylelint-ratchet.mjs --update           Baseline SENKEN (npm run lint:css:ratchet:update)
//   node scripts/stylelint-ratchet.mjs --update --force   auch anheben (nur mit Begruendung im Commit)
//   node scripts/stylelint-ratchet.mjs --summary <datei>  Markdown-Tabelle anhaengen (CI: $GITHUB_STEP_SUMMARY)
//
// extractInlineStyles ist exportiert und in scripts/stylelint-ratchet.test.mjs per `node --test` geprueft.
import { appendFileSync, existsSync, globSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { applyUpdate, compare, serializeBaseline, summaryMarkdown } from './eslint-ratchet.mjs';

const APP_DIR = join(dirname(fileURLToPath(import.meta.url)), '..');
const BASELINE_FILE = join(APP_DIR, 'stylelint-baseline.json');
const UPDATE_CMD = 'npm run lint:css:ratchet:update';

/** 1-basierte Zeile des Zeichens an `index`. */
function lineAt(text, index) {
  let line = 1;
  for (let i = 0; i < index; i++) if (text.charCodeAt(i) === 10) line++;
  return line;
}

/** Liest ein Template-Literal ab dem oeffnenden Backtick; liefert Inhalt und Index hinter dem schliessenden. */
function readTemplate(src, open) {
  let i = open + 1;
  let interpolated = false;
  while (i < src.length) {
    const c = src[i];
    if (c === '\\') { i += 2; continue; }
    if (c === '`') return { code: src.slice(open + 1, i), end: i + 1, interpolated };
    if (c === '$' && src[i + 1] === '{') interpolated = true;
    i++;
  }
  return null;
}

/**
 * Inline-Styles einer Komponenten-Datei: jedes Template-Literal in `styles: [ … ]` bzw. direkt hinter `styles:`.
 * @returns {{ code: string, line: number, interpolated: boolean }[]} line = Zeile des ersten Inhaltszeichens
 */
export function extractInlineStyles(src) {
  const out = [];
  const re = /\bstyles\s*:\s*/g;
  let m;
  while ((m = re.exec(src))) {
    let i = m.index + m[0].length;
    if (src[i] === '`') {
      const t = readTemplate(src, i);
      if (t) { out.push({ code: t.code, line: lineAt(src, i), interpolated: t.interpolated }); re.lastIndex = t.end; }
      continue;
    }
    if (src[i] !== '[') continue;
    i++;
    // Bis zur schliessenden Klammer: Backtick-Literale einsammeln, Leerraum, Kommas und Kommentare ueberspringen.
    while (i < src.length && src[i] !== ']') {
      if (src[i] === '`') {
        const t = readTemplate(src, i);
        if (!t) break;
        out.push({ code: t.code, line: lineAt(src, i), interpolated: t.interpolated });
        i = t.end;
      } else if (src.startsWith('//', i)) {
        const nl = src.indexOf('\n', i);
        i = nl < 0 ? src.length : nl + 1;
      } else if (src.startsWith('/*', i)) {
        const close = src.indexOf('*/', i + 2);
        i = close < 0 ? src.length : close + 2;
      } else if (/[\s,]/.test(src[i])) {
        i++;
      } else {
        break;   // etwas anderes als Literale (Variable, Funktionsaufruf) — nicht unser Fall
      }
    }
    re.lastIndex = i;
  }
  return out;
}

/** stylelint ueber Style-Dateien und Inline-Styles; liefert Zahlen je Regel und Dateien je Regel. */
async function lint() {
  const { default: stylelint } = await import('stylelint');
  const { default: config } = await import(pathToFileURL(join(APP_DIR, 'stylelint.config.mjs')).href);
  /** @type {Record<string, number>} */
  const counts = {};
  /** @type {Record<string, Map<string, number>>} */
  const files = {};
  const touched = new Set();
  let chunks = 0;
  let skipped = 0;
  const add = (path, rule) => {
    counts[rule] = (counts[rule] ?? 0) + 1;
    const perFile = (files[rule] ??= new Map());
    perFile.set(path, (perFile.get(path) ?? 0) + 1);
    touched.add(path);
  };
  const lintCode = async (code, path) => {
    const r = await stylelint.lint({ code, codeFilename: join(APP_DIR, path), config, configBasedir: APP_DIR });
    return r.results[0]?.warnings ?? [];
  };

  const styleFiles = globSync('src*/**/*.{scss,css}', { cwd: APP_DIR }).sort();
  for (const path of styleFiles) {
    chunks++;
    for (const w of await lintCode(readFileSync(join(APP_DIR, path), 'utf8'), path)) add(path, w.rule);
  }
  const tsFiles = globSync('src*/**/*.ts', { cwd: APP_DIR }).filter((p) => !p.endsWith('.spec.ts')).sort();
  for (const path of tsFiles) {
    const src = readFileSync(join(APP_DIR, path), 'utf8');
    if (!src.includes('styles')) continue;
    for (const style of extractInlineStyles(src)) {
      if (style.interpolated) { skipped++; continue; }
      chunks++;
      for (const w of await lintCode(style.code, path)) add(path, w.rule);
    }
  }
  return { counts, files, filesWithMessages: touched.size, chunks, skipped };
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
  if (opts.summary === '') opts.summary = null;
  return opts;
}

async function main() {
  const opts = parseArgs(process.argv.slice(2));
  const baseline = readBaseline();
  const { counts, files, filesWithMessages, chunks, skipped } = await lint();
  const result = compare(baseline, counts);

  if (opts.summary)
    appendFileSync(opts.summary, summaryMarkdown(result, filesWithMessages, { title: 'stylelint-Ratsche (Frontend, Farbwerte)', updateCmd: UPDATE_CMD }));

  if (opts.update) {
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
    console.log(text === old ? 'stylelint-baseline.json unveraendert.' : 'stylelint-baseline.json geschrieben.');
    process.exitCode = raised.length && !opts.force ? 1 : 0;
    return;
  }

  const total = Object.values(counts).reduce((a, b) => a + b, 0);
  console.log(`stylelint-Ratsche: ${total} Meldungen in ${filesWithMessages} Dateien (${chunks} Style-Bloecke`
    + `${skipped ? `, ${skipped} mit \${…} uebersprungen` : ''}).`);
  for (const r of result.increased) {
    console.error(`\nFEHLER ${r.rule}: Baseline ${r.baseline} → jetzt ${r.current} (+${r.delta}). Dateien mit Treffern dieser Regel:`);
    for (const [path, n] of [...files[r.rule]].sort((a, b) => a[0].localeCompare(b[0])))
      console.error(`  ${path} (${n})`);
  }
  for (const r of result.decreased)
    console.log(`Hinweis ${r.rule}: Baseline ${r.baseline} → jetzt ${r.current} — Baseline kann gesenkt werden (${UPDATE_CMD}).`);
  if (!result.ok) {
    console.error('\nDie Ratsche ist rot: Farbe aus einem Token nehmen (src/_tokens.scss, var(--rh-…)), NICHT die Baseline heben.');
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
