/**
 * Die semantischen Farb-Tokens aus `src/_tokens.scss` (Codereview 2026-09-29, F8-003). Die globalen
 * Styles laufen im Test mit (angular.json → test.styles), also werden die Tokens hier so aufgeloest,
 * wie der Browser sie sieht: hell ohne, dunkel mit der Klasse `dark-theme` am `<html>` (ThemeService).
 *
 * Die Pflicht jedes Tokens: 4,5:1 (WCAG AA fuer Text) auf den Flaechen, auf denen Text steht — Seite
 * (Browser-Leinwand), Karte und Dialog. Genau daran scheiterten die festen Hex-Werte im
 * Standard-Dunkelmodus (#c62828 ≈ 3:1, #1976d2 ≈ 3,6:1 auf #1e1e1e).
 */
const TOKENS = ['--rh-error', '--rh-success', '--rh-warn', '--rh-info', '--rh-accent'] as const;

/** Leinwand + die Material-Flaechen, auf denen Text steht (Seite, Karte, Dialog; ohne -highest). */
const LIGHT_SURFACES = ['#ffffff', 'var(--mat-sys-surface)', 'var(--mat-sys-surface-container-low)',
  'var(--mat-sys-surface-container)', 'var(--mat-sys-surface-container-high)'];
const DARK_SURFACES = ['#121212', '#1e1e1e', 'var(--mat-sys-surface)', 'var(--mat-sys-surface-container-lowest)',
  'var(--mat-sys-surface-container-low)', 'var(--mat-sys-surface-container)',
  'var(--mat-sys-surface-container-high)'];

type Rgb = [number, number, number];

function luminance([r, g, b]: Rgb): number {
  const lin = (c: number) => {
    const s = c / 255;
    return s <= 0.04045 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
  };
  return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b);
}

function contrast(a: Rgb, b: Rgb): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

describe('Design-Tokens (_tokens.scss)', () => {
  let probe: HTMLElement;
  let wasDark: boolean;

  /** Loest einen Farbausdruck (auch `var(...)`) am Dokument auf; null = nicht gesetzt/keine Farbe. */
  function resolve(expr: string): Rgb | null {
    probe.style.color = '';
    probe.style.color = expr;
    if (!probe.style.color) return null;
    const m = /^rgba?\((\d+),\s*(\d+),\s*(\d+)(?:,\s*([\d.]+))?\)$/.exec(getComputedStyle(probe).color);
    if (!m || (m[4] !== undefined && Number(m[4]) < 1)) return null;
    return [Number(m[1]), Number(m[2]), Number(m[3])];
  }

  function token(name: string): Rgb | null {
    // Ohne das Token faellt `var()` auf den (durchsichtigen) Rueckfall — das zaehlt als „nicht da".
    return resolve(`var(${name}, transparent)`);
  }

  function setDark(dark: boolean): void {
    document.documentElement.classList.toggle('dark-theme', dark);
  }

  beforeEach(() => {
    wasDark = document.documentElement.classList.contains('dark-theme');
    probe = document.createElement('span');
    document.body.appendChild(probe);
  });

  afterEach(() => {
    probe.remove();
    setDark(wasDark);
  });

  for (const mode of ['hell', 'dunkel'] as const) {
    const dark = mode === 'dunkel';
    const surfaces = dark ? DARK_SURFACES : LIGHT_SURFACES;

    for (const name of TOKENS) {
      it(`${name} ist ${mode} gesetzt und erreicht 4,5:1 auf Seite, Karte und Dialog`, () => {
        setDark(dark);
        const color = token(name);
        expect(color).withContext(`${name} (${mode}) fehlt`).not.toBeNull();
        for (const surface of surfaces) {
          const bg = resolve(surface);
          expect(bg).withContext(`Flaeche ${surface} (${mode}) nicht aufloesbar`).not.toBeNull();
          const ratio = contrast(color!, bg!);
          expect(ratio).withContext(`${name} auf ${surface} (${mode}): ${ratio.toFixed(2)}:1`)
            .toBeGreaterThanOrEqual(4.5);
        }
      });
    }
  }

  it('Fehler und Akzent sind die Material-Rollen error/primary — in beiden Modi', () => {
    for (const dark of [false, true]) {
      setDark(dark);
      expect(token('--rh-error')).toEqual(resolve('var(--mat-sys-error)'));
      expect(token('--rh-accent')).toEqual(resolve('var(--mat-sys-primary)'));
    }
  });

  it('jeder Token wechselt mit dem Dunkelmodus', () => {
    setDark(false);
    const light = TOKENS.map(token);
    setDark(true);
    const dark = TOKENS.map(token);
    TOKENS.forEach((name, i) => expect(dark[i]).withContext(name).not.toEqual(light[i]));
  });
});
