import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { RepertoireService } from '../../core/repertoire.service';
import { TrainingNotebookComponent } from './training-notebook.component';
import { nbNote } from './training-notebook.util.spec';
import { TrainingNotebook, TrainingVideosService } from './training-videos.service';

const FRENCH = 'rnbqkbnr/ppp2ppp/4p3/3p4/3PP3/8/PPP2PPP/RNBQKBNR w KQkq - 0 3';

function notebook(): TrainingNotebook {
  return {
    notes: [
      nbNote({ id: 1, lessonId: 2, date: '2026-08-18', category: 'plan', text: 'Gegen das Läuferpaar: halbieren.', rookhubGameId: 9 }),
      nbNote({ id: 2, lessonId: 1, date: '2025-05-20', category: 'plan', text: 'Das Läuferpaar halbieren.', quote: 'halbieren, ja' }),
      nbNote({ id: 3, lessonId: 1, date: '2025-05-20', category: 'eroeffnung', text: 'Gegen Französisch Tarrasch.', gameKind: 'lesson' }),
      nbNote({ id: 4, lessonId: 1, date: '2025-05-20', category: 'endspiel', text: 'Turm hinter den Freibauern.', hasVideo: false }),
    ],
    themes: [{ lessons: 2, noteId: 1, category: 'plan', noteIds: [1, 2] }],
    openings: [{ noteId: 3, fen: FRENCH, segmentId: 11, segmentMove: null,
      repertoire: [{ repertoireId: 5, repertoireName: 'Weiß', chapter: 'Französisch', lineName: 'Tarrasch', gameIndex: 0, ply: 4, moves: ['Nd2', 'Nc3'], sameSide: true }] }],
    hasRepertoire: true,
    repertoireTruncated: false,
  };
}

describe('TrainingNotebookComponent', () => {
  async function render(svc: Partial<TrainingVideosService>) {
    TestBed.configureTestingModule({
      imports: [TrainingNotebookComponent],
      providers: [provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: TrainingVideosService, useValue: svc },
        { provide: RepertoireService, useValue: { getPgnText: () => of('') } }],
    });
    const fixture = TestBed.createComponent(TrainingNotebookComponent);
    fixture.autoDetectChanges();
    await fixture.whenStable();
    return fixture;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('zeigt Themen, Eröffnung mit Repertoire-Zug und Reiter je Kategorie mit Anzahl', async () => {
    const fixture = await render({ notebook: () => of(notebook()) });
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelectorAll('.theme').length).toBe(1);
    expect(el.querySelector('.theme-text')!.textContent).toContain('Gegen das Läuferpaar');
    expect(el.querySelector('.opening app-chess-board')).not.toBeNull();
    expect(el.querySelector('.rep-moves')!.textContent).toContain('Nd2');
    expect(el.querySelector('.rep-moves')!.textContent).toContain('Nc3');
    expect(el.querySelector('.nb-open-board')!.getAttribute('href')).toContain('/analysis?fen=');

    const chips = Array.from(el.querySelectorAll('.tabs mat-chip-option')).map(c => c.textContent!.trim());
    expect(chips.length).toBe(3);   // eroeffnung, plan, endspiel — in fester Reihenfolge
    expect(chips[1]).toContain('(2)');
    // erster Reiter = Eröffnung; Partie-Link der Lehrpartie führt zur Stunde an der Notizzeit
    const link = el.querySelector('.all .nb-game')!.getAttribute('href')!;
    expect(link).toBe('/training-videos/1?t=600');
  });

  it('„▶ anhören“ klappt den Ausschnitt-Player auf, Rohzitat separat; ohne Video kein Knopf', async () => {
    const fixture = await render({ notebook: () => of(notebook()), accessToken: () => of('tok'), videoUrl: () => '/v' });
    const cmp = fixture.componentInstance;
    cmp.category.set('plan');
    await fixture.whenStable();
    const el: HTMLElement = fixture.nativeElement;
    const listen = el.querySelector('.all .nb-listen') as HTMLButtonElement;
    listen.click();
    await fixture.whenStable();
    expect(el.querySelector('.all app-training-clip-player')).not.toBeNull();
    (el.querySelectorAll('.all .nb-quote')[0] as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(el.querySelector('.all .quote')!.textContent).toContain('halbieren, ja');

    cmp.category.set('endspiel');
    await fixture.whenStable();
    expect(el.querySelector('.all .nb-listen')).toBeNull();
  });

  it('Volltextsuche filtert auf die Notiz-Treffer der vorhandenen Suche', async () => {
    const searchText = jasmine.createSpy('searchText').and.returnValue(of({ hits: [
      { kind: 'note', lessonId: 1, date: '2025-05-20', segmentId: null, startSec: 600, fen: null, move: null, text: '', gameLabel: null, category: 'endspiel', sameSide: null, noteId: 4 },
      { kind: 'segment', lessonId: 1, date: '2025-05-20', segmentId: 8, startSec: 600, fen: null, move: null, text: '', gameLabel: null, category: null, sameSide: null },
    ], truncated: false }));
    const fixture = await render({ notebook: () => of(notebook()), searchText });
    const cmp = fixture.componentInstance;
    cmp.query.set('Freibauer');
    cmp.submitSearch();
    await fixture.whenStable();

    expect(searchText).toHaveBeenCalledWith('Freibauer');
    expect(cmp.tabs()).toEqual([{ category: 'endspiel', count: 1 }]);
    expect(cmp.activeCategory()).toBe('endspiel');
    cmp.clearSearch();
    expect(cmp.tabs().length).toBe(3);
  });

  it('Ladefehler zeigt „Erneut versuchen“, nicht den Leerzustand', async () => {
    const fixture = await render({ notebook: () => throwError(() => new Error('x')) });
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('app-load-error')).not.toBeNull();
    expect(el.querySelector('.empty-state')).toBeNull();
  });
});
