import { Type } from '@angular/core';
import { GamesListComponent } from './games/games-list.component';
import { SharedGameComponent } from './games/shared-game.component';
import { GameRecapComponent } from './games/game-recap.component';
import { WorksheetListComponent } from './worksheets/worksheet-list.component';
import { WorksheetDetailComponent } from './worksheets/worksheet-detail.component';
import { EbookListComponent } from './ebooks/ebook-list.component';
import { GuessListComponent } from './guess/guess-list.component';
import { ReconstructListComponent } from './reconstruct/reconstruct-list.component';
import { CourseListComponent } from './courses/course-list.component';
import { CourseDetailComponent } from './courses/course-detail.component';
import { FlashcardsComponent } from './courses/flashcards/flashcards.component';
import { NavbarComponent } from '../shared/navbar/navbar.component';
import { RepertoireDetailComponent } from './repertoire/repertoire-detail.component';

/**
 * 0.808.x · Boox 200 % Falz (DESIGN 4.6 „Niedrige Fenster“): Boox Tab X C mit UI-Größe 200 % rechnet 480 × 640 CSS-px —
 * der Vorlauf lag auf diesen Seiten bei 42–54 %, die Lernkarte ragte unter die Falz. Eine globale Regel (styles.scss)
 * und je Seite eine Abfrage `(max-width: 768px) and (max-height: 720px)`. Gelesen werden die KOMPILIERTEN Stile.
 */
const LOW = '@media (max-width: 768px) and (max-height: 720px)';

function css(cmp: Type<unknown>): string {
  return ((cmp as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n')
    .replace(/%NS%/g, '').replace(/\[_ngcontent-%COMP%\]/g, '').replace(/\s+/g, ' ');
}

/** Inhalt aller Blöcke der Niedrig-Abfrage, in Reihenfolge. */
function lowBlocks(cmp: Type<unknown>): string {
  const all = css(cmp);
  const out: string[] = [];
  let at = all.indexOf(LOW);
  while (at >= 0) {
    const open = all.indexOf('{', at);
    let depth = 1, i = open + 1;
    for (; i < all.length && depth > 0; i++) depth += all[i] === '{' ? 1 : all[i] === '}' ? -1 : 0;
    out.push(all.slice(open + 1, i - 1));
    at = all.indexOf(LOW, i);
  }
  return out.join(' ');
}

/** Die Niedrig-Regel steht NACH der Grundregel desselben Selektors (gleiche Spezifität — die spätere gewinnt). */
function lowAfterBase(cmp: Type<unknown>, selector: string): boolean {
  const all = css(cmp);
  const base = all.indexOf(`${selector} {`);
  return base >= 0 && all.lastIndexOf(LOW) > base;
}

describe('Niedrige Fenster (Boox 200 %): gemeinsame Regel in styles.scss', () => {
  function lowRuleText(): string {
    const out: string[] = [];
    for (const sheet of Array.from(document.styleSheets)) {
      let rules: CSSRuleList;
      try { rules = sheet.cssRules; } catch { continue; }
      for (const r of Array.from(rules)) {
        if (r instanceof CSSMediaRule && /max-width: 768px\) and \(max-height: 720px/.test(r.conditionText)
            && r.cssText.includes('app-navbar')) out.push(r.cssText);
      }
    }
    return out.join('\n').replace(/\s+/g, ' ');
  }

  it('App-Kopf 48 px, Suchfelder 44 px, Reiter 40 px, h1 1,5 rem, h2 1,125 rem — nur hinter app-navbar/trn-navbar', () => {
    const t = lowRuleText();
    expect(t).withContext('Regel geladen').not.toBe('');
    expect(t).toMatch(/:is\(app-navbar, ?trn-navbar\) mat-toolbar \{[^}]*--mat-toolbar-mobile-height: 48px/);
    expect(t).toMatch(/:is\(app-navbar, ?trn-navbar\) ~ main \{[^}]*--mat-form-field-container-height: 44px/);
    expect(t).toMatch(/~ main \{[^}]*--mat-tab-container-height: 40px/);
    expect(t).toMatch(/~ main h1 \{[^}]*font-size: 1\.5rem/);
    expect(t).toMatch(/~ main h2 \{[^}]*font-size: 1\.125rem/);
  });

  it('wirkt nur im niedrigen, schmalen Fenster — Handy hochkant 390 × 844 und Laptop bleiben unberührt', () => {
    const t = lowRuleText();
    const cond = /@media ([^{]+)\{/.exec(t)![1].trim();
    // Die Bedingung selbst gegen die Prüfansichten: nur 480 × 640 (Boox 200 %) und 375 × 667 (kleines Handy) passen.
    const fits = (w: number, h: number) => w <= 768 && h <= 720;
    expect(cond).toContain('max-width: 768px');
    expect(cond).toContain('max-height: 720px');
    expect(fits(480, 640)).toBeTrue();
    expect(fits(390, 844)).toBeFalse();
    expect(fits(1366, 768)).toBeFalse();
    expect(fits(820, 1180)).toBeFalse();
  });

  it('wirkt: unter einem app-navbar-Geschwister bekommt h1 in einem 480 × 640-Fenster 24 px (iframe)', async () => {
    const frame = document.createElement('iframe');
    frame.style.cssText = 'width:480px;height:640px;border:0;position:absolute;left:-9999px';
    document.body.appendChild(frame);
    try {
      const doc = frame.contentDocument!;
      const rule = lowRuleText();
      doc.open();
      doc.write(`<!doctype html><style>${rule}</style><app-navbar></app-navbar><main><h1>Titel</h1></main>`
        + '<div class="other"><h1>Anders</h1></div>');
      doc.close();
      await new Promise(r => setTimeout(r, 0));
      const [inMain, outside] = Array.from(doc.querySelectorAll('h1')) as HTMLElement[];
      expect(frame.contentWindow!.getComputedStyle(inMain).fontSize).toBe('24px');
      expect(frame.contentWindow!.getComputedStyle(outside).fontSize).toBe('32px');
      frame.style.height = '900px';
      await new Promise(r => setTimeout(r, 50));
      expect(frame.contentWindow!.getComputedStyle(inMain).fontSize).withContext('900 px hoch: keine Wirkung').toBe('32px');
    } finally {
      frame.remove();
    }
  });
});

describe('Niedrige Fenster (Boox 200 %): Seiten', () => {
  it('Gespeicherte Partien: oben 8 px, Hinweis und Filterzeile knapper', () => {
    const b = lowBlocks(GamesListComponent);
    expect(b).toMatch(/\.games-page \{ ?padding-top: 8px/);
    expect(b).toMatch(/\.hint \{ ?margin-bottom: 8px/);
    expect(b).toMatch(/\.filters-bar \{ ?margin: 4px 0 8px/);
    expect(lowAfterBase(GamesListComponent, '.hint')).toBeTrue();
  });

  it('Partieseite: Kopf 8 px Polster, „Kurz erzählt“ 0,875 rem mit 6 px Polster', () => {
    expect(lowBlocks(SharedGameComponent)).toMatch(/\.header \{ ?padding-block: 8px/);
    expect(lowAfterBase(SharedGameComponent, '.header')).toBeTrue();
    const r = lowBlocks(GameRecapComponent);
    expect(r).toMatch(/\.recap \{[^}]*padding-block: 6px[^}]*font-size: 0\.875rem/);
  });

  it('Aufgabenblätter: Hinweis, Zwischenablage und Abschnittstitel mit 8 px Abstand', () => {
    const b = lowBlocks(WorksheetListComponent);
    expect(b).toMatch(/\.wl-page \{ ?padding-top: 8px/);
    expect(b).toMatch(/\.wl-clip \{ ?margin-bottom: 0\.5rem/);
    expect(b).toMatch(/\.wl-section-title \{ ?margin: 0\.5rem 0 0\.375rem/);
    expect(lowAfterBase(WorksheetListComponent, '.wl-section-title')).toBeTrue();
  });

  it('Aufgabenblatt: Drucken und Link kopieren als Symbolknöpfe (Beschriftung aus), oben 8 px', () => {
    const b = lowBlocks(WorksheetDetailComponent);
    expect(b).toMatch(/\.wd-btn-label \{ ?display: none/);
    expect(b).toMatch(/\.wd-print, \.wd-copy \{ ?min-width: 44px/);
    expect(b).toMatch(/\.wd-page \{ ?padding-top: 8px/);
  });

  it('E-Books: Anmelde-Banner 0,875 rem, oben 8 px', () => {
    const b = lowBlocks(EbookListComponent);
    expect(b).toMatch(/\.eb-page \{ ?padding-top: 0\.5rem/);
    expect(b).toMatch(/\.eb-anon \{[^}]*font-size: 0\.875rem/);
  });

  it('Punktepartie: Abstände in Kopf, Weiterspielen und Reitern 0–6 px', () => {
    const b = lowBlocks(GuessListComponent);
    expect(b).toMatch(/\.gl-container \{ ?margin-top: 4px/);
    expect(b).toMatch(/\.run \{ ?margin-bottom: 4px/);
    expect(b).toMatch(/\.mat-mdc-tab-body-content \{ ?padding-top: 0/);
    expect(lowAfterBase(GuessListComponent, '.run')).toBeTrue();
  });

  it('Rekonstruktion: Einleitung ohne Absatzränder, Anlege-Karte 8 px', () => {
    const b = lowBlocks(ReconstructListComponent);
    expect(b).toMatch(/\.header \.muted \{ ?margin: 0\.25rem 0 0/);
    expect(b).toMatch(/\.new-card \{ ?margin: 0\.5rem 0/);
  });

  it('Kurse: oben 8 statt 24 px, Kopf/Import-Hinweis/Abschnittskopf knapper', () => {
    const b = lowBlocks(CourseListComponent);
    expect(b).toMatch(/\.courses-container \{ ?margin-top: 8px/);
    expect(b).toMatch(/\.reimport-legend \{ ?margin-bottom: 0\.5rem/);
    expect(lowAfterBase(CourseListComponent, '.section-head')).toBeTrue();
  });

  it('Kursseite: „Zurück“ als ← vor dem Titel (Beschriftung aus), Titel 1,125 rem', () => {
    const b = lowBlocks(CourseDetailComponent);
    expect(b).toMatch(/\.back-label \{ ?display: none/);
    expect(b).toMatch(/\.topbar \{ ?flex-wrap: nowrap/);
    expect(b).toMatch(/\.page-title \{[^}]*font-size: 1\.125rem/);
  });

  it('Lernkarten: Karte richtet sich nach der Fensterhöhe (passt ganz über die Falz)', () => {
    const b = lowBlocks(FlashcardsComponent);
    // Sass verkürzt `calc()` innerhalb von `min()` — beide Schreibweisen sind gleichwertig.
    expect(b).toMatch(/\.fc-study-card, \.fc-fs-btn \{ ?width: min\(92vw, (calc\()?100vh - 230px\)/);
    expect(b).toMatch(/\.fc-flip-face \{ ?min-height: min\(92vw, (calc\()?100vh - 230px\)/);
  });

  it('App-Kopf: Abzeichen an Glocke/Post rutschen in die 48-px-Leiste (nicht über den Fensterrand)', () => {
    expect(lowBlocks(NavbarComponent)).toMatch(/\.mat-badge-content \{ ?top: 4px; ?bottom: auto; ?margin-top: 0/);
  });
});

// d4-rk-repertoire-brett-winzig: das Repertoire-Brett (Baum/Lochfinder/Schwarz) war bei 480 × 640 nur ≈ 160 px groß —
// `100vh − 480px` ohne Untergrenze.
describe('Niedrige Fenster (Boox 200 %): Repertoire-Brett', () => {
  it('480 × 640 (iframe): Brett mindestens 300 px breit, ≈ Fensterhöhe − 300 px', async () => {
    const frame = document.createElement('iframe');
    frame.style.cssText = 'width:480px;height:640px;border:0;position:absolute;left:-9999px';
    document.body.appendChild(frame);
    try {
      const doc = frame.contentDocument!;
      doc.open();
      doc.write(`<!doctype html><style>${css(RepertoireDetailComponent)}</style>`
        + '<div class="viewer-layout"><div class="board-section"></div></div>');
      doc.close();
      await new Promise(r => setTimeout(r, 0));
      const board = doc.querySelector('.board-section') as HTMLElement;
      const w = board.getBoundingClientRect().width;
      expect(w).toBeGreaterThanOrEqual(300);
      expect(w).toBeCloseTo(340, -1);
    } finally {
      frame.remove();
    }
  });
});
