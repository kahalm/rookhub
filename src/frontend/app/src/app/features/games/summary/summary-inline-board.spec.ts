import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { BreakpointObserver } from '@angular/cdk/layout';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { ClubGameSummaryComponent } from './club-game-summary.component';
import { ClubGameSummaryService } from './club-game-summary.service';
import { GamesService } from '../games.service';
import { AuthService } from '../../../core/auth.service';
import { PreferencesService } from '../../../core/preferences.service';
import { inlineBoardPlan } from './summary-inline-board.util';
import { gamePositions, linkSummaryMoves, parseSummary, positionsOf } from './summary-markdown.util';
import { mergeGameSection } from './summary-game-text.util';

/**
 * Wunsch 2026-11-28: „wenn die summary so schmal ist dass das brett nicht da ist … im fließtext immer wieder ein diagramm
 * einblenden das die stellung zeigt die du gerade beschreibst … mit <> tasten zum durchklicken“.
 */
const PGN = '1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6';
const MD = [
  '# A – B', '',
  'Nach 1.e4 e5 kam 2.Nf3 und 2...Nc6.', '',
  'Mit 3.Bb5 a6? und 4.Ba4 ging es weiter.', '',
  'Hier stand schon 4.Ba4 auf dem Brett.', '',
  '```diagram', 'ply: 7', '```', '',
  'Statt dessen wäre 3...Nf6 die Berliner Verteidigung gewesen.', '',
  'Zum Schluss 4...Nf6.',
].join('\n');

describe('inlineBoardPlan', () => {
  const positions = positionsOf(PGN);
  const game = gamePositions(positions).at(-1)!.path;
  const blocks = linkSummaryMoves(parseSummary(MD), positions);

  it('je Absatz mit Partiezug ein Brett: erster Zug mit Zugzeichen, sonst der letzte; nicht vor einem Diagramm, nicht nur für Varianten', () => {
    const plan = inlineBoardPlan(blocks, game);
    // 0 Titel, 1 Absatz, 2 Absatz, 3 Absatz vor Diagramm, 4 Diagramm, 5 nur Variante, 6 Absatz
    expect([...plan.keys()]).toEqual([1, 2, 6]);
    expect(plan.get(1)!.at).toBe(4);   // 2...Nc6, letzter Partiezug
    expect(plan.get(2)!.at).toBe(6);   // 3...a6? hat ein Zugzeichen
    expect(plan.get(6)!.at).toBe(8);
    expect(plan.get(1)!.line.length).toBe(8);   // ◀ ▶ blättern durch die ganze Partie
  });

  it('kein zweites Brett mit derselben Stellung direkt dahinter, keins hinter einem Diagramm derselben Stellung', () => {
    const md = ['Erst 2.Nf3.', '', 'Wieder 2.Nf3.', '', '```diagram', 'ply: 5', '```', '', 'Dann 3.Bb5.'].join('\n');
    const plan = inlineBoardPlan(linkSummaryMoves(parseSummary(md), positions), game);
    expect([...plan.entries()].map(([i, j]) => [i, j.at])).toEqual([[0, 3]]);
  });

  it('verschmolzener Abschnitt „Die Partie“: Absätze mit Partiezügen, Varianten in Klammern zählen nicht', () => {
    const md = ['## Die Partie', '', 'Spanisch: 1.e4 e5 2.Sf3 Sc6 3.Lb5 a6 (3...Sf6!) 4.La4 Sf6 und Schluss.'].join('\n');
    const merged = mergeGameSection(parseSummary(md), positions);
    expect(merged.merged).toBeTrue();
    const plan = inlineBoardPlan(merged.blocks, game);
    expect([...plan.entries()].map(([i, j]) => [i, j.at])).toEqual([[1, 8]]);
  });
});

describe('ClubGameSummaryComponent — Bretter im Fließtext (schmal)', () => {
  function render(narrow: boolean) {
    TestBed.configureTestingModule({
      imports: [ClubGameSummaryComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        provideHttpClient(), provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '285' }), data: {} } } },
        { provide: GamesService, useValue: { clubGame: () => of({ pgn: PGN, ownerSide: 'white' }) } },
        { provide: ClubGameSummaryService, useValue: { get: () => of({ markdown: MD }), suggestions: () => of([]), exercises: () => of([]) } },
        { provide: AuthService, useValue: { isLoggedIn: false, currentUser$: of(null) } },
        { provide: PreferencesService, useValue: { boardTheme: 'brown', pieceSet: 'cburnett' } },
        { provide: BreakpointObserver, useValue: { isMatched: () => narrow, observe: () => of({ matches: narrow, breakpoints: {} }) } },
      ],
    });
    const fixture = TestBed.createComponent(ClubGameSummaryComponent);
    fixture.detectChanges();
    return fixture;
  }
  const label = (el: Element) => el.querySelector('.ib-move')!.textContent!.trim();

  it('breit: keine Bretter im Text', () => {
    const el = render(false).nativeElement as HTMLElement;
    expect(el.querySelectorAll('app-summary-inline-board').length).toBe(0);
    expect(el.querySelector('app-summary-game-board')).not.toBeNull();
  });

  it('schmal: ein Brett hinter jedem Absatz mit Partiezug, direkt hinter dem Absatz, mit dem gewählten Zug', () => {
    const el = render(true).nativeElement as HTMLElement;
    const boards = Array.from(el.querySelectorAll('app-summary-inline-board'));
    expect(boards.length).toBe(3);
    expect(boards.map(label)).toEqual(['2…Nc6', '3…a6', '4…Nf6']);
    expect(boards[0].previousElementSibling!.textContent).toContain('Nach 1.e4 e5');
    // vor dem Diagramm-Block kein eigenes Brett
    expect(el.querySelector('figure.diagram')!.previousElementSibling!.tagName).toBe('P');
  });

  it('◀ ▶ blättern durch die Partie, am Ende/Anfang gesperrt', () => {
    const fixture = render(true);
    const board = (fixture.nativeElement as HTMLElement).querySelectorAll('app-summary-inline-board')[0];
    (board.querySelector('.ib-forward') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(label(board)).toBe('3.Bb5');
    for (let i = 0; i < 5; i++) { (board.querySelector('.ib-back') as HTMLButtonElement).click(); fixture.detectChanges(); }
    expect(label(board)).toBe('gameAnalysis.startPosition');
    expect((board.querySelector('.ib-back') as HTMLButtonElement).disabled).toBeTrue();
    const last = (fixture.nativeElement as HTMLElement).querySelectorAll('app-summary-inline-board')[2];
    expect((last.querySelector('.ib-forward') as HTMLButtonElement).disabled).toBeTrue();
  });

  it('Klick auf einen Zug im Absatz stellt dessen Brett um und öffnet kein Blatt; der Knopf daneben öffnet das große Brett', () => {
    const fixture = render(true);
    const el = fixture.nativeElement as HTMLElement;
    const calls: unknown[] = [];
    spyOn(fixture.componentInstance, 'showOnBoard').and.callFake(j => { calls.push(j); });
    (el.querySelector('p button.mv') as HTMLButtonElement).click();   // 1.e4
    fixture.detectChanges();
    const board = el.querySelectorAll('app-summary-inline-board')[0];
    expect(label(board)).toBe('1.e4');
    expect(calls).toEqual([]);
    (board.querySelector('.ib-open') as HTMLButtonElement).click();
    expect(calls).toEqual([{ ply: 1 }]);
  });

  it('Variantenzug ohne eigenes Brett im Absatz öffnet wie bisher das Blatt', () => {
    const fixture = render(true);
    const el = fixture.nativeElement as HTMLElement;
    const calls: { line?: { san: string }[] }[] = [];
    spyOn(fixture.componentInstance, 'showOnBoard').and.callFake(j => { calls.push(j as never); });
    const p = Array.from(el.querySelectorAll('p')).find(x => x.textContent!.includes('Berliner'))!;
    (p.querySelector('button.mv') as HTMLButtonElement).click();
    expect(calls.length).toBe(1);
    expect(calls[0].line!.at(-1)!.san).toBe('Nf6');
  });
});
