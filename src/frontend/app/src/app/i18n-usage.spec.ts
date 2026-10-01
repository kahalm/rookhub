/**
 * Codereview F8-011: Tote Sprachschlüssel. 67 Schlüssel standen in allen 25 Sprachdateien, ohne dass Code sie
 * holte (courses.upload.*, book.viz.*, dashboard.subscriptions.*, reprocess.needsReimport.* …) — 16 davon nur
 * über die nie eingebundene ThemePickerComponent. Sie wurden in en/de/hr/hu gepflegt, von der Paritätsprüfung
 * erzwungen und blähten die Doppel-Zählung (F8-010) auf.
 *
 * Jeder Schlüssel aus en.json muss im Quelltext eines der Projekte (src, src-turnier, src-kidhub, src-leaguehub,
 * src-clubhub; ohne Specs) vorkommen — als vollständiges Literal ('ns.key'), unter einem dynamischen Präfix
 * ('ns.' + x, `ns.${x}`, oder ein Literal 'ns.sub', das selbst kein Schlüssel ist, aber Schlüssel darunter hat:
 * i18nPrefix = 'courses.share') oder mit einem dynamischen Suffix (x + '.title', `${x}.title`). Dieselbe Logik
 * wie scripts/i18n_usage.py des Codereviews 2026-09-29, nur ohne Kommentar-Filter (ein Schlüssel, der bloß in
 * einem Kommentar steht, gilt hier als benutzt — lieber ein toter zu wenig als ein Fehlalarm).
 *
 * Die Quelltexte liefert karma.conf.js aus (files, included: false); gelesen werden sie per fetch.
 */
type Served = { __karma__?: { files?: Record<string, unknown> } };

function flatKeys(obj: unknown, prefix = '', out: string[] = []): string[] {
  if (obj && typeof obj === 'object') {
    for (const [k, v] of Object.entries(obj as Record<string, unknown>)) {
      const key = prefix ? `${prefix}.${k}` : k;
      if (v && typeof v === 'object') flatKeys(v, key, out);
      else out.push(key);
    }
  }
  return out;
}

const LITERAL = /(['"`])([A-Za-z][\w.\-]*)\1/g;
const PREFIX_PLUS = /(['"`])([A-Za-z][\w.\-]*)\1\s*\+/g;
const PREFIX_TPL = /`([A-Za-z][\w.\-]*)\$\{/g;
const SUFFIX_PLUS = /\+\s*(['"`])(\.[A-Za-z][\w.\-]*)\1/g;
const SUFFIX_TPL = /\}(\.[A-Za-z][\w.\-]*)`/g;

/** Schlüssel, die in keinem der Quelltexte vorkommen — weder als Literal noch über einen Präfix/Suffix. */
function unusedKeys(keys: readonly string[], sources: readonly string[]): string[] {
  const keySet = new Set(keys);
  const literals = new Set<string>();
  const prefixes = new Set<string>();
  const suffixes = new Set<string>();
  for (const src of sources) {
    for (const m of src.matchAll(LITERAL)) literals.add(m[2]);
    for (const m of src.matchAll(PREFIX_PLUS)) prefixes.add(m[2]);
    for (const m of src.matchAll(PREFIX_TPL)) prefixes.add(m[1]);
    for (const m of src.matchAll(SUFFIX_PLUS)) suffixes.add(m[2]);
    for (const m of src.matchAll(SUFFIX_TPL)) suffixes.add(m[1]);
  }
  for (const lit of literals) if (lit.includes('.') && !keySet.has(lit)) prefixes.add(`${lit}.`);
  const pre = [...prefixes].filter(p => p.includes('.') && p.length >= 3);
  const suf = [...suffixes].filter(s => s.length >= 2);
  return keys.filter(k => !literals.has(k)
    && !pre.some(p => k !== p && k.startsWith(p))
    && !suf.some(s => k.endsWith(s)));
}

/** Von karma.conf.js ausgelieferte Quelltexte der Projekte, ohne Specs und ohne den Changelog (Fließtext). */
function sourceUrls(): string[] {
  const served = (globalThis as Served).__karma__?.files ?? {};
  return Object.keys(served).filter(u => /^\/base\/src[\w-]*\/.+\.(ts|html)$/.test(u)
    && !u.endsWith('.spec.ts') && !u.includes('/environments/changelog'));
}

async function fetchText(url: string): Promise<string> {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`${url}: HTTP ${res.status}`);
  return res.text();
}

describe('i18n: jeder Schlüssel wird benutzt (F8-011)', () => {
  it('erkennt Literal, Präfix, Namensraum-Literal und Suffix — und meldet den Rest', () => {
    const keys = ['a.lit', 'b.plus.x', 'c.tpl.x', 'd.ns.x', 'e.x.suffix', 'f.x.tplSuffix', 'g.dead', 'h.only'];
    const sources = [
      `t('a.lit'); t('b.plus.' + x); t(\`c.tpl.\${x}\`); const p = 'd.ns';`,
      `<p>{{ base + '.suffix' | translate }}</p> \${y}.tplSuffix\``,
      // h.only steht nur als Teil eines längeren Literals — das ist keine Verwendung.
      `t('h.only.more')`,
    ];
    expect(unusedKeys(keys, sources)).toEqual(['g.dead', 'h.only']);
  });

  it('kein Schlüssel in en.json ohne Verwendung im Quelltext', async () => {
    const urls = sourceUrls();
    expect(urls.length).withContext('Quelltexte nicht ausgeliefert — files in karma.conf.js?').toBeGreaterThan(300);
    expect(urls.some(u => u.startsWith('/base/src-turnier/'))).withContext('src-turnier fehlt').toBeTrue();
    const [en, ...sources] = await Promise.all([
      fetchText('/i18n/en.json').catch(() => fetchText('/base/i18n/en.json')),
      ...urls.map(fetchText),
    ]);
    const keys = flatKeys(JSON.parse(en));
    expect(keys.length).toBeGreaterThan(1000);
    expect(unusedKeys(keys, sources))
      .withContext('Sprachschlüssel ohne Verwendung — aus allen Sprachdateien löschen (oder wieder benutzen)')
      .toEqual([]);
  });
});
