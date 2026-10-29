/**
 * Hinweistext im Dialog im Ton von mat-hint (DESIGN 5.6, d7-rk-dialog-hint-kontrast / d8-rk-dialog-hinweis-kontrast):
 * im Dialog ist currentColor schon on-surface-variant — `color-mix(currentColor 60 %)` davon lag bei ≈ 3:1. Damals wurde
 * nur ein Dialog umgestellt, die Kopien blieben. Diese Wache meldet jede `.hint`/`.hint-small`/`.empty`-Regel in einer
 * Dialog-Datei, die die Textfarbe aus einem Anteil von currentColor mischt.
 *
 * Die Quelltexte liefert karma.conf.js aus (files, included: false); gelesen werden sie per fetch.
 */
type Served = { __karma__?: { files?: Record<string, unknown> } };

const PALE_HINT = /\.(hint|hint-small|empty)\s*\{[^}]*color:\s*color-mix\(in srgb,\s*currentColor \d+%/g;

export function paleDialogHints(source: string): string[] {
  return [...source.matchAll(PALE_HINT)].map(m => `.${m[1]}`);
}

describe('Dialog-Hinweistext (DESIGN 5.6)', () => {
  it('erkennt den abgeblassten Hinweis und lässt on-surface-variant durch', () => {
    expect(paleDialogHints(`.hint { margin: 0; color: color-mix(in srgb, currentColor 65%, transparent); }`)).toEqual(['.hint']);
    expect(paleDialogHints(`.hint { color: var(--mat-sys-on-surface-variant); } .seen { color: color-mix(in srgb, currentColor 60%, transparent); }`))
      .toEqual([]);
  });

  it('keine Dialog-Datei blasst ihren Hinweistext über currentColor ab', async () => {
    const served = (globalThis as Served).__karma__?.files ?? {};
    const urls = Object.keys(served).filter(u => /^\/base\/src[\w-]*\/.+dialog[\w.-]*\.ts$/.test(u) && !u.endsWith('.spec.ts'));
    expect(urls.length).withContext('karma liefert keine Dialog-Quelltexte aus').toBeGreaterThan(20);
    const hits: string[] = [];
    await Promise.all(urls.map(async u => {
      for (const h of paleDialogHints(await (await fetch(u)).text())) hits.push(`${u}: ${h}`);
    }));
    expect(hits).toEqual([]);
  });
});
