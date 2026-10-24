import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { EMPTY, of } from 'rxjs';
import { RepertoireTrainerComponent } from './repertoire-trainer.component';
import { RepertoireTrainingService } from './repertoire-training.service';
import { RepertoireExplorerService, DEFAULT_EXPLORER_SETTINGS } from './repertoire-explorer.service';
import { StockfishService } from '../puzzles/stockfish.service';
import { lineKeyFromSans } from './repertoire-line-key.util';

/** Gerenderte Lern-Ansicht des Trainers (UI-Sweep 2026-10-12, v1-rk-trainer-learn-*). */
describe('RepertoireTrainerComponent (gerendert, Lernmodus)', () => {
  afterEach(() => localStorage.removeItem('rookhub_rep_train_chaptercolor_1'));

  async function render(pgn: string): Promise<HTMLElement> {
    localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'Chapter A': 'w', '?': 'w' }));
    await TestBed.configureTestingModule({
      imports: [RepertoireTrainerComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: {
          paramMap: { get: () => '1' },
          queryParamMap: { get: (k: string) => k === 'mode' ? 'learn' : null },
        } } },
        { provide: RepertoireTrainingService, useValue: { getPgn: () => of(pgn), getLineStates: () => of([]) } },
        { provide: StockfishService, useValue: { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } },
        { provide: RepertoireExplorerService, useValue: { run: () => EMPTY, effectiveSettings: () => of(DEFAULT_EXPLORER_SETTINGS) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(RepertoireTrainerComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('zeigt den Kommentar zum vorgezeigten Zug nur einmal (Kontextkarte), nicht noch als Kommentarbox', async () => {
    const el = await render('[Event "Rep"]\n[White "1.e4"]\n[Black "Chapter A"]\n\n1. e4 {Weiß besetzt das Zentrum.} e5 2. Nf3 *');
    expect(el.querySelectorAll('.ctx-comment').length).toBe(1);
    expect(el.querySelector('.ctx-comment')!.textContent).toContain('Weiß besetzt das Zentrum.');
    expect(el.querySelectorAll('.comment-box').length).toBe(0);
    expect(el.querySelector('.tap-hint')).not.toBeNull();   // Halt bis zum Weitertippen bleibt
  });

  // d4-rk-lernen-anweisung-widerspruch
  it('LEARN_SHOW (Trainer spielt vor, Brett gesperrt): Überschrift ohne „spiel …“-Imperativ', async () => {
    // Mit Kommentar hält LEARN_SHOW bis zum Weitertippen (ohne springt es nach 1 s zurück).
    const el = await render('[Event "Rep"]\n[White "1.e4"]\n[Black "Chapter A"]\n\n1. e4 {Zentrum.} e5 2. Nf3 *');
    expect(el.querySelector('.feedback.learn')).withContext('LEARN_SHOW erwartet').not.toBeNull();
    const prompt = el.querySelector('.prompt')!.textContent!;
    expect(prompt).toContain('repertoireTrainer.whiteToMoveLearn');
    expect(prompt).not.toMatch(/repertoireTrainer\.whiteToMove(?!Learn)/);
  });

  it('ein PGN-Platzhalter „?" als Linienname erscheint nicht', async () => {
    const el = await render('[Event "?"]\n[White "?"]\n[Black "?"]\n\n1. e4 e5 2. Nf3 *');
    expect(el.querySelector('.line-label')).toBeNull();
    expect(el.querySelector('.line-moves')).not.toBeNull();
  });
});

/** Design-Runde d3: Kopfleiste und Leerzustand des Trainers. */
describe('RepertoireTrainerComponent Kopf + Leerzustand (d3)', () => {
  afterEach(() => {
    localStorage.removeItem('rookhub_rep_train_chaptercolor_1'); localStorage.removeItem('rookhub_rep_train_freq_order');
    document.querySelectorAll('.cdk-overlay-container').forEach(o => o.innerHTML = '');
  });

  /** Ein fälliger Pool-Eintrag für die Linie 1.e4 e5 2.Nf3 — sonst ist der Trainer im Leerzustand. */
  let lastFixture: ComponentFixture<RepertoireTrainerComponent>;
  const DUE = [{ lineKey: lineKeyFromSans(['e4', 'e5', 'Nf3']), level: 1, reps: 1, lapses: 0,
    dueAt: new Date(Date.now() - 3_600_000).toISOString(), lastReviewedAt: null, inPool: true, paused: false }];

  async function renderQuiz(name: string | null, states: unknown[] = []): Promise<HTMLElement> {
    localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'Chapter A': 'w' }));
    await TestBed.configureTestingModule({
      imports: [RepertoireTrainerComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => '1' }, queryParamMap: { get: () => null } } } },
        { provide: RepertoireTrainingService, useValue: {
          getPgn: () => of('[Event "Rep"]\n[White "1.e4"]\n[Black "Chapter A"]\n\n1. e4 e5 2. Nf3 *'),
          getLineStates: () => of(states), getRepertoireName: () => of(name),
        } },
        { provide: StockfishService, useValue: { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } },
        { provide: RepertoireExplorerService, useValue: { run: () => EMPTY, effectiveSettings: () => of(DEFAULT_EXPLORER_SETTINGS) } },
      ],
    }).compileComponents();
    localStorage.setItem('rookhub_rep_train_freq_order', '0');
    const fixture = lastFixture = TestBed.createComponent(RepertoireTrainerComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  // d3-rk-trainer-symbolknoepfe-ohne-text
  it('„Häufigste zuerst“ ist ein beschrifteter Umschalter; Einstellungen und Zurücksetzen liegen im ⋮', async () => {
    const el = await renderQuiz(null, DUE);
    const toggle = el.querySelector('.bar .freq-toggle')!;
    expect(toggle.textContent).toContain('repertoireTrainer.freqOrder');
    expect(toggle.getAttribute('aria-pressed')).toBe('false');
    const icons = Array.from(el.querySelectorAll('.bar > button mat-icon')).map(i => i.textContent!.trim());
    expect(icons).not.toContain('trending_up');
    expect(icons).not.toContain('restart_alt');   // der Reset steht nicht offen in der Leiste
    expect(icons).not.toContain('tune');
    expect(el.querySelector('.bar > button.more')!.getAttribute('aria-label')).toBe('common.moreActions');
  });

  // d3-rk-trainer-leer-zwei-aktionen
  it('Leerzustand: genau eine Aktion; Titel = Name des Repertoires', async () => {
    const el = await renderQuiz('Schwarz-Repertoire');
    const actions = el.querySelectorAll('mat-card.msg .msg-actions a, mat-card.msg .msg-actions button');
    expect(actions.length).toBe(1);
    expect(el.querySelector('mat-card.msg')!.textContent).not.toContain('repertoireTrainer.manageLines');
    expect(el.querySelector('.bar .rep-name')!.textContent!.trim()).toBe('Schwarz-Repertoire');
    expect(el.querySelector('.bar .title-kind')!.textContent!.trim()).toBe('repertoireTrainer.title');
  });

  // d4-rk-trainer-leerzustand-bedienelemente
  it('Leerzustand: kein „Häufigste Linien zuerst“, im ⋮ kein „Fortschritt zurücksetzen“ (Übungszyklen bleiben)', async () => {
    const el = await renderQuiz('Leer');
    expect(el.querySelector('.bar .freq-toggle')).toBeNull();
    const more = el.querySelector('.bar > button.more') as HTMLButtonElement;
    expect(more).not.toBeNull();
    more.click();
    lastFixture.detectChanges();
    await lastFixture.whenStable();
    const menu = document.querySelector('.mat-mdc-menu-panel');
    expect(menu).not.toBeNull();
    expect(menu!.querySelector('.reset-item')).toBeNull();
    expect(menu!.textContent).toContain('srConfig.title');
  });

  // d5-rk-trainer-mobil-menue-eigene-zeile + d5-rk-trainer-mobile-menue-eigene-zeile
  it('Leerzustand am Handy: das ⋮ steht in der Titelzeile, nicht allein in einer dritten Zeile', async () => {
    const el = await renderQuiz('Leer');
    expect(el.querySelector('.bar > button.more')!.classList).toContain('more-solo');
    const css = ((RepertoireTrainerComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles)
      .join('\n').replace(/%NS%/g, '').replace(/\s/g, '');
    const at = css.indexOf('@media(max-width:599.98px)');
    expect(at).toBeGreaterThan(-1);
    const media = css.slice(at, css.indexOf('}}', at));
    const rule = media.slice(media.indexOf('.more-solo'));
    expect(rule).toContain('order:0');
    expect(rule).toContain('margin-left:0');
  });

  it('mit Linien im Pool sind Umschalter und „Fortschritt zurücksetzen“ da', async () => {
    const el = await renderQuiz('Voll', DUE);
    expect(el.querySelector('.bar .freq-toggle')).not.toBeNull();
    expect(el.querySelector('.bar > button.more')!.classList).not.toContain('more-solo');
    (el.querySelector('.bar > button.more') as HTMLButtonElement).click();
    lastFixture.detectChanges();
    await lastFixture.whenStable();
    expect(document.querySelector('.mat-mdc-menu-panel .reset-item')).not.toBeNull();
  });

  // d4-rk-trainer-titel-handy-gekuerzt + d4-rk-trainer-title-truncated-mobile
  it('Name vor Zusatz: der leise „Repertoire-Trainer“ schrumpft zuerst, der Name bleibt voll', async () => {
    const el = await renderQuiz('Weiß: Italienisch + Abtausch');
    const title = el.querySelector('.bar .title') as HTMLElement;
    title.style.flex = '0 0 330px';
    title.style.width = '330px';
    document.body.appendChild(el);
    try {
      const name = el.querySelector('.bar .rep-name') as HTMLElement;
      const kind = el.querySelector('.bar .title-kind') as HTMLElement;
      expect(name.scrollWidth).toBeLessThanOrEqual(name.clientWidth + 1);   // Name nicht gekürzt
      expect(kind.scrollWidth).toBeGreaterThan(kind.clientWidth);           // Zusatz mit „…“
    } finally {
      el.remove();
    }
  });

  it('unter 900 px steht nur der Name (Zusatz nur für Bildschirmleser)', () => {
    const css = ((RepertoireTrainerComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles)
      .join('\n').replace(/%NS%/g, '').replace(/\s/g, '');
    const at = css.indexOf('@media(max-width:899.98px)');
    expect(at).toBeGreaterThan(-1);
    const media = css.slice(at, css.indexOf('}}', at));
    const block = media.slice(media.indexOf('.title-kind'));
    expect(media).toContain('.title-kind');
    expect(block).toContain('position:absolute');
    expect(block).toContain('clip-path:inset(50%)');
    expect(block).not.toContain('display:none');
  });

  it('ohne Namen bleibt der bisherige Titel', async () => {
    const el = await renderQuiz(null);
    expect(el.querySelector('.bar .rep-name')).toBeNull();
    expect(el.querySelector('.bar .title')!.textContent).toContain('repertoireTrainer.title');
  });
});

/** Brettgröße des Trainers (UI-Sweep a1). Karma läuft 1 400 px breit — Media-Query-Regeln als CSS-Text geprüft. */
describe('RepertoireTrainerComponent Brettgröße (a1)', () => {
  const css = ((RepertoireTrainerComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles)
    .join('\n').replace(/%NS%/g, '').replace(/\s/g, '');
  function block(media: string): string {
    const at = css.indexOf(media);
    expect(at).withContext(media).toBeGreaterThan(-1);
    return css.slice(at, css.indexOf('}}', at) + 2);
  }

  // a1-rk-trainer-brett-hoehe
  it('Laptop: Brett höchstens Fensterhöhe − 200 px (keine Mini-Scrollleiste)', () => {
    expect(css).toMatch(/app-puzzle-board(\[[^\]]*\])?\{flex:101480px;max-width:min\(820px,(calc\()?100vh-200px\)?\)/);
  });

  // b1-rk-trainer-brett-1920
  it('Brett wächst vor der Seitenspalte (bei 1 920 × 1 080 bis zum 820-px-Deckel), Gruppe mittig, Umbruchbasis unverändert', () => {
    expect(css).toMatch(/\.side(\[[^\]]*\])?\{flex:11340px;min-width:300px;max-width:520px/);
    const play = /\.play(\[[^\]]*\])?\{([^}]*)\}/.exec(css)!;
    expect(play[2]).toContain('justify-content:center');
    expect(play[2]).toContain('flex-wrap:wrap');
  });

  // b1-rk-trainer-brett-handy-schmal
  it('Handy: Brett volle Inhaltsbreite (der Höhenabzug − 540 px gilt nur 600–900 px)', () => {
    const h = block('@media(max-width:599.98px){app-puzzle-board');
    expect(h).toMatch(/app-puzzle-board(\[[^\]]*\])?\{max-width:100%;?\}/);
    expect(css.indexOf('@media(max-width:599.98px){app-puzzle-board')).toBeGreaterThan(css.indexOf('@media(max-width:899.98px)'));
  });

  it('einspaltig: Brett volle Zeile bis Fensterhöhe − 540 px (Nachprüfung a1: Kommentar + Einprägen-Knopf über der Falz), Infospalte ohne 520-px-Deckel', () => {
    const n = block('@media(max-width:899.98px)');
    expect(n).toMatch(/app-puzzle-board(\[[^\]]*\])?\{flex-basis:100%;max-width:min\(100%,(calc\()?100vh-540px\)?\)/);
    expect(n).toMatch(/\.side(\[[^\]]*\])?\{max-width:none/);
  });

  // a1-rk-trainer-ultrawide-brett
  it('ab 2400 px Brett bis Fensterhöhe (≤ 1 100 px), Seitenspalte ≤ 640 px, mittig', () => {
    const w = block('@media(min-width:2400px)');
    expect(w).toMatch(/app-puzzle-board(\[[^\]]*\])?\{flex:00auto;width:min\((calc\()?100vh-200px\)?,1100px\);max-width:none/);
    expect(w).toMatch(/\.side(\[[^\]]*\])?\{max-width:640px/);
    expect(w).toMatch(/\.play(\[[^\]]*\])?\{justify-content:center/);
    expect(css.slice(0, css.indexOf('@media(min-width:2400px)'))).not.toContain('1100px');
  });
});
