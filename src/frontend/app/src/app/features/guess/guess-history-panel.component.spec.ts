import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { GuessHistoryPanelComponent } from './guess-history-panel.component';
import { GuessHistoryMove } from './guess.service';
import { guessHistoryRows, skipReason } from './guess-history.util';

const HISTORY: GuessHistoryMove[] = [
  { ply: 6, moveNumber: 4, white: true, san: 'dxe5', uci: 'd4e5', fen: 'f6', comment: 'der Schluesselzug' },
  { ply: 7, moveNumber: 4, white: false, san: 'Bxf3', uci: 'g4f3', fen: 'f7', skipped: 'notScorable' },
];

describe('guessHistoryRows / skipReason', () => {
  it('fasst Weiss und Schwarz zu einer Zeile zusammen', () => {
    expect(guessHistoryRows(HISTORY)).toEqual([{
      no: 4, w: 'dxe5', wIdx: 0, wNoted: true, wSkipped: null,
      b: 'Bxf3', bIdx: 1, bNoted: false, bSkipped: 'notScorable',
    }]);
  });

  it('beginnt der Verlauf mit Schwarz, steht Weiss als „…"', () => {
    const rows = guessHistoryRows([HISTORY[1]]);
    expect(rows).toEqual([{ no: 4, w: '…', wIdx: -1, wNoted: false, wSkipped: null,
                            b: 'Bxf3', bIdx: 0, bNoted: false, bSkipped: 'notScorable' }]);
  });

  it('kennt den Grund nur fuer einen angesehenen Zug', () => {
    expect(skipReason(HISTORY, null)).toBeNull();
    expect(skipReason(HISTORY, -1)).toBeNull();
    expect(skipReason(HISTORY, 0)).toBeNull();
    expect(skipReason(HISTORY, 1)).toBe('notScorable');
  });
});

describe('GuessHistoryPanelComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GuessHistoryPanelComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
  });

  function create(inputs: Record<string, unknown> = {}) {
    const fixture = TestBed.createComponent(GuessHistoryPanelComponent);
    fixture.componentRef.setInput('history', HISTORY);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    return fixture;
  }

  it('zeigt die Zugliste und markiert kommentierte/uebersprungene Zuege', () => {
    const el: HTMLElement = create({ browseIndex: 0 }).nativeElement;
    const mvs = el.querySelectorAll('.orow .mv');
    expect(mvs.length).toBe(2);
    expect(mvs[0].classList).toContain('noted');
    expect(mvs[0].classList).toContain('on');
    expect(mvs[1].classList).toContain('skipped');
    expect(mvs[1].getAttribute('title')).toBe('guess.skipped.notScorable');
    expect(mvs[0].getAttribute('title')).toBe('guess.hasComment');
  });

  it('sagt beim uebersprungenen Zug, warum', () => {
    const el: HTMLElement = create({ browseIndex: 1 }).nativeElement;
    expect(el.querySelector('.skip-note')?.textContent).toContain('guess.skipped.notScorable');
  });

  it('meldet Klicks nach oben, blaettert aber nicht selbst', () => {
    const fixture = create({ atStart: false, atTask: false });
    const browsed: (number | null)[] = [];
    const steps: number[] = [];
    fixture.componentInstance.browse.subscribe(i => browsed.push(i));
    fixture.componentInstance.step.subscribe(d => steps.push(d));
    const el: HTMLElement = fixture.nativeElement;
    const nav = el.querySelectorAll<HTMLButtonElement>('.onav button');
    nav[0].click(); nav[1].click(); nav[2].click();
    el.querySelectorAll<HTMLButtonElement>('.orow .mv')[1].click();
    expect(browsed).toEqual([-1, 1]);
    expect(steps).toEqual([-1, 1]);
  });

  it('sperrt Anfang/zurueck an der Grundstellung und vor an der Aufgabe', () => {
    const el: HTMLElement = create({ atStart: true, atTask: true }).nativeElement;
    const nav = el.querySelectorAll<HTMLButtonElement>('.onav button');
    expect(nav[0].disabled).toBeTrue();
    expect(nav[1].disabled).toBeTrue();
    expect(nav[2].disabled).toBeTrue();
  });

  it('Sprachumschalter nur bei mehr als einer Sprache', () => {
    expect(create({ commentLanguages: ['de'] }).nativeElement.querySelector('.lang')).toBeNull();
    const fixture = create({ commentLanguages: ['de', 'en'], commentLanguage: 'en' });
    const chosen: string[] = [];
    fixture.componentInstance.chooseLanguage.subscribe(l => chosen.push(l));
    const langs = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.lang');
    expect(langs.length).toBe(2);
    expect(langs[1].classList).toContain('on');
    langs[0].click();
    expect(chosen).toEqual(['de']);
  });

  it('zeigt den Kommentar samt Sprachmarke', () => {
    const el: HTMLElement = create({ note: { move: '4.dxe5', text: 'der Schluesselzug', language: 'en' } }).nativeElement;
    expect(el.querySelector('.note .note-move')?.textContent).toContain('4.dxe5');
    expect(el.querySelector('.note .lang-tag')?.textContent).toContain('EN');
  });

  /** „und" heisst „Sprache nicht bestimmbar" — als Beschriftung taugt das Kuerzel nicht. */
  it('nennt die unbestimmte Sprache Original', () => {
    const c = create().componentInstance;
    expect(c.langLabel('de')).toBe('DE');
    expect(c.langLabel('und')).not.toBe('UND');
  });
});
