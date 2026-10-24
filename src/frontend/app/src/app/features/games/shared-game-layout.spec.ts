import { SharedGameComponent } from './shared-game.component';
import { GameRecapComponent } from './game-recap.component';

/** Stilregeln als Text (Karma läuft 1 400 px breit — Media-Query-Regeln lassen sich nur so prüfen). */
function cssOf(cmp: unknown): string {
  return ((cmp as any).ɵcmp.styles as string[]).join('\n').replace(/\s+/g, ' ')
    .replace(/\[_ngcontent-%COMP%\]/g, '').replace(/\[_nghost-%COMP%\]/g, '').replace(/--%NS%/g, '--');
}

function blockOf(css: string, query: string): string {
  const start = css.indexOf(query);
  if (start < 0) return '';
  let depth = 0;
  for (let i = css.indexOf('{', start); i < css.length; i++) {
    if (css[i] === '{') depth++;
    if (css[i] === '}' && --depth === 0) return css.slice(start, i + 1);
  }
  return '';
}

/** Regeln außerhalb aller @media-Blöcke. */
function baseOf(css: string): string {
  let out = css;
  for (let i = out.indexOf('@media'); i >= 0; i = out.indexOf('@media')) out = out.replace(blockOf(out, out.slice(i, out.indexOf('{', i))), '');
  return out;
}

describe('SharedGameComponent Layout (UI-Sweep a1)', () => {
  const css = cssOf(SharedGameComponent);
  const base = baseOf(css);

  it('a1-rk-shared-kopf-grund: Knopf + Grund in einer schmalen Hülle, der Grund ist keine 100-%-Zeile mehr', () => {
    expect(base).toMatch(/\.analyze-wrap \{ ?display: flex; flex-direction: column; align-items: flex-end;[^}]*max-width: 280px/);
    expect(base).not.toMatch(/\.analyze-no-engine \{[^}]*flex: 1 1 100%/);
    const mobile = blockOf(css, '@media (max-width: 768px)');
    expect(mobile).toContain('.header-actions > .analyze-wrap');
    // Nur das ⋮: die Hülle zählt als Knopf (sonst rückte die Zeile fälschlich hoch).
    expect(mobile).toContain(':has(> .analyze-wrap,');
  });

  it('a1-rk-partie-kopf-tablet: 769–1279 px Knöpfe in eigener Zeile, Abzeichen brechen nicht in sich', () => {
    const tablet = blockOf(css, '@media (min-width: 769px) and (max-width: 1279.98px)');
    expect(tablet).withContext('Tablet-Media-Query fehlt').not.toBe('');
    expect(tablet).toMatch(/\.header \{ ?grid-template-columns: auto minmax\(0, ?1fr\);/);
    expect(tablet).toMatch(/\.header-actions \{ ?grid-column: 1 \/ -1; justify-content: flex-start/);
    expect(base).toMatch(/\.meta > span \{ ?white-space: nowrap;/);
  });

  it('a1-rk-elo-verwaist: Name und Elo als Einheit, „(Elo n)" ohne Umbruch', () => {
    expect(base).toMatch(/\.players \.pl \{ ?display: inline-block; max-width: 100%/);
    expect(base).toMatch(/\.players \.elo \{[^}]*white-space: nowrap/);
  });

  it('a1-rk-partie-tablet-brett: Brett rechnet mit 400 px Abzug, Brettleiste nie breiter als das Brett', () => {
    expect(base).toContain('calc(100vw - 400px)');
    expect(base).not.toContain('100vw - 440px');
    expect(base).toMatch(/\.board-section \{[^}]*min-width: 0/);
    expect(base).toMatch(/\.nav \{[^}]*max-width: 100%; box-sizing: border-box/);
    expect(base).toMatch(/\.nav \.nav-prev, \.nav \.nav-next \{ ?flex: 0 1 64px; width: 64px; min-width: 44px/);
    expect(base).toContain('margin-right: clamp(6px, calc(var(--board-size) / 10 - 30px), 20px)');
  });

  it('a1-rk-brettleiste-plus: am Handy Nebenknöpfe 36 px, Sprung-Abstände 6 px', () => {
    const phone = blockOf(css, '@media (max-width: 480px)');
    expect(phone).toMatch(/\.nav \.mat-mdc-icon-button:not\(\.nav-prev\):not\(\.nav-next\) \{ ?width: 36px; height: 36px/);
    const mobile = blockOf(css, '@media (max-width: 768px)');
    expect(mobile).toContain('.nav .nav-start { margin-right: 6px; }');
    expect(mobile).toContain('.nav .nav-end { margin-left: 6px; }');
  });

  it('a1-rk-ultrawide-seitenspalten: ab 2400 px zweite Rasterzeile flexibel (Meisterpartien direkt unter Repertoires)', () => {
    const wide = blockOf(css, '@media (min-width: 2400px)');
    expect(wide).toContain('grid-template-rows: min-content 1fr');
    expect(wide).not.toContain('grid-auto-rows: min-content');
    expect(baseOf(css.replace(wide, ''))).not.toContain('grid-template-rows: min-content 1fr');
  });

  it('d5-rk-game-disabled-reason-narrow-wrap: am Tablet löst sich die Hülle, der Grund steht als eigene Zeile unter der Knopfreihe', () => {
    const tablet = blockOf(css, '@media (min-width: 769px) and (max-width: 1279.98px)');
    expect(tablet).toContain('.analyze-wrap { display: contents; }');
    expect(tablet).toMatch(/\.analyze-no-engine \{ ?order: 99; flex: 1 1 100%; width: auto; min-width: 0; max-width: 60ch;/);
    expect(tablet).toContain('.header-actions:has(.analyze-no-engine) { align-items: center; }');
    expect(tablet).not.toContain('width: max-content');
    // Außerhalb des Tablets bleibt die schmale Hülle (Laptop/PC rechts, Handy volle Breite).
    expect(base).toMatch(/\.analyze-wrap \{ ?display: flex;/);
  });

  it('d5-rk-game-disabled-reason-narrow-wrap: bei 820 px bricht der Grund höchstens zweizeilig um', async () => {
    const tablet = blockOf(css, '@media (min-width: 769px) and (max-width: 1279.98px)');
    const inner = tablet.slice(tablet.indexOf('{') + 1, -1);
    const style = document.createElement('style');
    style.textContent = base + inner;
    const host = document.createElement('div');
    host.style.width = '820px';
    host.innerHTML = `<div class="header-actions" style="font-size:14px">
        <div class="analyze-wrap"><button class="analyze" style="width:180px">Partie analysieren</button>
          <p class="analyze-no-engine">Gerade steht keine Engine bereit — versuch es später noch einmal.</p></div>
        <button class="share" style="width:180px">Teilen-Link kopieren</button><button class="game-menu" style="width:40px">⋮</button></div>`;
    document.head.appendChild(style);
    document.body.appendChild(host);
    const reason = host.querySelector('.analyze-no-engine') as HTMLElement;
    const lh = parseFloat(getComputedStyle(reason).lineHeight) || 12 * 1.4;
    expect(reason.offsetHeight).toBeLessThanOrEqual(2 * lh + 1);
    const btn = (host.querySelector('.analyze') as HTMLElement).getBoundingClientRect();
    const menu = (host.querySelector('.game-menu') as HTMLElement).getBoundingClientRect();
    expect(reason.getBoundingClientRect().top).toBeGreaterThanOrEqual(btn.bottom - 4);
    expect(Math.abs(menu.top - btn.top)).toBeLessThanOrEqual(2);
    host.remove(); style.remove();
  });

  it('d5-rk-mobile-karte-randabstand: am Handy behält die Sperr-/Fehlerkarte 16 px Polster, die Partieansicht bleibt randlos', () => {
    const mobile = blockOf(css, '@media (max-width: 768px)');
    expect(mobile).toContain('.shared-page { padding: 0; }');
    expect(mobile).toMatch(/\.shared-page:has\(> ?\.empty-state, ?> ?\.load-card\) \{ ?max-width: none; padding: 16px; ?\}/);
    // Nach der randlosen Regel, damit sie gewinnt.
    expect(mobile.indexOf('.shared-page:has(')).toBeGreaterThan(mobile.indexOf('.shared-page { padding: 0; }'));
  });

  it('a1-rk-recap-zeilenlaenge: „Kurz erzählt" bricht nach ~75 Zeichen um', () => {
    expect(cssOf(GameRecapComponent)).toMatch(/\.recap-text \{ ?flex: 1 1 auto; min-width: 0; max-width: 75ch/);
  });
});
