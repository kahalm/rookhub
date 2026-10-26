/**
 * Codereview F8-008: das Changelog (environments/changelog-data.ts, ~2,2 MB Prosa) ist DATEN, kein Code. Der Build
 * schreibt es per scripts/changelog-json.mjs nach /assets/changelog.json, das Overlay (shared/app-footer) laedt es per
 * HttpClient. Wuerde irgendein Quelltext changelog-data.ts importieren — statisch ODER per dynamic import() —, laege
 * es wieder im Bundle: statisch im Initial-Bundle, dynamisch als Lazy-Chunk unter `/*.js` in der prefetch-Gruppe des
 * Service Workers, der nach jedem Release auf jedem installierten Geraet neu geladen wird.
 *
 * Die Quelltexte aller Oberflaechen liefert karma.conf.js aus (files, included: false); gelesen per fetch wie in
 * i18n-usage.spec.ts.
 */
export {};   // Modul, nicht Skript: sonst kollidieren sourceUrls/Served mit i18n-usage.spec.ts im globalen Namensraum

type Served = { __karma__?: { files?: Record<string, unknown> } };

const IMPORTS_CHANGELOG_DATA = [
  /\bimport\s*\(\s*['"`][^'"`\n]*changelog-data(\.ts)?['"`]/,   // dynamic import()
  /\bfrom\s+['"][^'"\n]*changelog-data(\.ts)?['"]/,             // import { … } from / export … from
  /\bimport\s+['"][^'"\n]*changelog-data(\.ts)?['"]/,           // import '…' (Seiteneffekt)
  /\brequire\s*\(\s*['"][^'"\n]*changelog-data(\.ts)?['"]/,
];

function importsChangelogData(source: string): boolean {
  return IMPORTS_CHANGELOG_DATA.some(re => re.test(source));
}

function sourceUrls(): string[] {
  const served = (globalThis as Served).__karma__?.files ?? {};
  return Object.keys(served).filter(u => /^\/base\/src[\w-]*\/.+\.ts$/.test(u)
    && !u.endsWith('.spec.ts') && !u.endsWith('/environments/changelog-data.ts'));
}

describe('Changelog als Daten, nicht im Bundle (F8-008)', () => {
  it('erkennt statische, dynamische und Seiteneffekt-Importe — Kommentare mit dem Dateinamen nicht', () => {
    expect(importsChangelogData(`const m = await import('../../../environments/changelog-data');`)).toBeTrue();
    expect(importsChangelogData(`import { CHANGELOG } from './changelog-data';`)).toBeTrue();
    expect(importsChangelogData(`export { CHANGELOG } from "../environments/changelog-data.ts";`)).toBeTrue();
    expect(importsChangelogData(`import '../environments/changelog-data';`)).toBeTrue();
    expect(importsChangelogData(`// Quelle bleibt \`environments/changelog-data.ts\` (nie importieren)`)).toBeFalse();
    expect(importsChangelogData(`import { APP_VERSION } from './changelog';`)).toBeFalse();
  });

  it('kein Quelltext einer Oberflaeche importiert changelog-data.ts', async () => {
    const urls = sourceUrls();
    expect(urls.length).withContext('karma.conf.js liefert die Quelltexte nicht aus').toBeGreaterThan(500);
    const offenders: string[] = [];
    // In Portionen statt alle ~1 000 auf einmal: unter Last (paralleler Build) scheitern sonst einzelne fetch-Aufrufe.
    for (let i = 0; i < urls.length; i += 50) {
      await Promise.all(urls.slice(i, i + 50).map(async url => {
        const res = await fetch(url);
        if (!res.ok) throw new Error(`${url}: HTTP ${res.status}`);
        if (importsChangelogData(await res.text())) offenders.push(url.replace(/^\/base\//, ''));
      }));
    }
    expect(offenders.sort()).withContext('Changelog per HttpClient aus /assets/changelog.json laden').toEqual([]);
  }, 60_000);
});
