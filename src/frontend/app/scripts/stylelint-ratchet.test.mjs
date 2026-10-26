// node --test scripts/stylelint-ratchet.test.mjs — Inline-Styles fuer die stylelint-Ratsche (F8-003).
// Die Vergleichslogik (compare/applyUpdate/serializeBaseline) teilt sie mit der ESLint-Ratsche (eslint-ratchet.test.mjs).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { extractInlineStyles } from './stylelint-ratchet.mjs';
import { compare, summaryMarkdown } from './eslint-ratchet.mjs';

test('Array mit mehreren Literalen, Kommentaren und Kommas — Zeilen auf die .ts-Datei bezogen', () => {
  const src = [
    "@Component({",
    "  selector: 'x',",
    "  styles: [`",
    "    .a { color: #c62828; }",
    "  `, // zweiter Block",
    "  /* noch einer */ `.b { color: red; }`],",
    "})",
  ].join('\n');
  const styles = extractInlineStyles(src);
  assert.equal(styles.length, 2);
  assert.equal(styles[0].line, 3);
  assert.match(styles[0].code, /\.a \{ color: #c62828; \}/);
  assert.equal(styles[1].line, 6);
  assert.equal(styles[1].code, '.b { color: red; }');
  assert.equal(styles.some((s) => s.interpolated), false);
});

test('einzelnes Literal direkt hinter styles:, maskierte Backticks und ${…}', () => {
  const styles = extractInlineStyles('styles: `.a::after { content: "\\`"; }`, x = { styles: `.c { width: ${w}px }` }');
  assert.equal(styles.length, 2);
  assert.equal(styles[0].code, '.a::after { content: "\\`"; }');
  assert.equal(styles[0].interpolated, false);
  assert.equal(styles[1].interpolated, true);
});

test('styles mit Variable statt Literal liefert nichts', () => {
  assert.deepEqual(extractInlineStyles('styles: [SHARED_STYLES, `.a{}`]'), []);
  assert.deepEqual(extractInlineStyles('const styles = computeStyles();'), []);
});

test('Job-Summary traegt den Titel und den Update-Befehl der stylelint-Ratsche', () => {
  const md = summaryMarkdown(compare({ 'color-no-hex': 3 }, { 'color-no-hex': 2 }), 1,
    { title: 'stylelint-Ratsche (Frontend, Farbwerte)', updateCmd: 'npm run lint:css:ratchet:update' });
  assert.match(md, /^## stylelint-Ratsche \(Frontend, Farbwerte\)/);
  assert.match(md, /`npm run lint:css:ratchet:update`/);
});
