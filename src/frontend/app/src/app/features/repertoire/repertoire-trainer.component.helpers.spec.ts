import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { EMPTY, of } from 'rxjs';
import { RepertoireTrainerComponent } from './repertoire-trainer.component';
import { LineStateDto, RepertoireTrainingService } from './repertoire-training.service';
import { RepertoireExplorerService, DEFAULT_EXPLORER_SETTINGS } from './repertoire-explorer.service';
import { StockfishService } from '../puzzles/stockfish.service';
import { lineKeyFromSans } from './repertoire-line-key.util';
import { ChessableMisses } from './repertoire-trainer-misses.util';

/** Repertoire-Trainer, gerendert im Abfragen-Modus (0.886.0): Fehlerzähler, „Nochmal", Chessable-Link, Tipp und Überspringen
 *  samt Tasten „h"/„s". */
describe('RepertoireTrainerComponent (gerendert, Fehlerzähler/Tipp/Überspringen)', () => {
  const PGN = '[Event "Rep"]\n[White "1.e4"]\n[Black "Chapter A"]\n[ChessableOid "777"]\n\n1. e4 e5 2. Nf3 Nc6 *\n\n'
    + '[Event "Rep"]\n[White "1.d4"]\n[Black "Chapter A"]\n\n1. d4 d5 2. c4 *\n';
  const KEY1 = lineKeyFromSans(['e4', 'e5', 'Nf3', 'Nc6']);
  const KEY2 = lineKeyFromSans(['d4', 'd5', 'c4']);
  const due = (lineKey: string, lapses = 0): LineStateDto => ({ lineKey, level: 2, reps: 1, lapses, dueAt: '2000-01-01T00:00:00Z',
    lastReviewedAt: null, inPool: true, paused: false });
  let fixture: ComponentFixture<RepertoireTrainerComponent>;
  let review: jasmine.Spy;

  afterEach(() => localStorage.removeItem('rookhub_rep_train_chaptercolor_1'));

  async function render(misses: ChessableMisses, states: LineStateDto[]): Promise<HTMLElement> {
    localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'Chapter A': 'w' }));
    review = jasmine.createSpy('reviewLine').and.returnValue(of(due(KEY1)));
    await TestBed.configureTestingModule({
      imports: [RepertoireTrainerComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => '1' }, queryParamMap: { get: () => null } } } },
        { provide: RepertoireTrainingService, useValue: { getPgn: () => of(PGN), getLineStates: () => of(states),
          getChessableMisses: () => of(misses), reviewLine: review } },
        { provide: StockfishService, useValue: { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } },
        { provide: RepertoireExplorerService, useValue: { run: () => EMPTY, effectiveSettings: () => of(DEFAULT_EXPLORER_SETTINGS) } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(RepertoireTrainerComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    // die Warteschlange ist gemischt — die Chessable-Linie nach vorn holen
    const s = fixture.componentInstance.session;
    if (s.currentLine?.headers['ChessableOid'] !== '777') s.skipLine();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const key = (k: string) => { window.dispatchEvent(new KeyboardEvent('keydown', { key: k })); fixture.detectChanges(); };

  it('falscher Zug: bleibt stehen, „Nochmal", und sagt wie oft schon falsch (Chessable am Zug, RookHub die Linie)', async () => {
    const el = await render({ '777': [{ mid: 0, mode: 'w', count: 2, last: 'd4' }, { mid: 0, mode: null, count: 1, last: 'c4' }] },
      [due(KEY1, 2), due(KEY2)]);
    expect(el.querySelector('a.chessable-link')!.getAttribute('href')).toBe('https://www.chessable.com/variation/777/');
    expect(el.querySelector('a.chessable-link')!.getAttribute('rel')).toBe('noopener');
    fixture.componentInstance.onMove({ orig: 'a2' as any, dest: 'a3' as any });
    fixture.detectChanges();
    const miss = el.querySelector('.miss-info')!.textContent!;
    expect(miss).toContain('repertoireTrainer.missChessableLast');
    expect(fixture.componentInstance.currentMiss).toEqual({ chessable: 3, last: '1.c4', rookhub: 2 });
    expect(el.querySelector('button.retry-wrong')).not.toBeNull();
    expect(fixture.componentInstance.isPlayable).toBeFalse();
    key(' ');                                                       // Leertaste nimmt zurück
    expect(fixture.componentInstance.isPlayable).toBeTrue();
    expect(el.querySelector('button.retry-wrong')).toBeNull();
  });

  it('ohne Daten keine Fehlerzeile', async () => {
    const el = await render({}, [due(KEY1), due(KEY2)]);
    fixture.componentInstance.onMove({ orig: 'a2' as any, dest: 'a3' as any });
    fixture.detectChanges();
    expect(el.querySelector('.miss-info')).toBeNull();
  });

  it('Taste „h": Tipp in Stufen; Knopf zeigt die nächste Stufe; Taste „s" überspringt ohne Bewertung', async () => {
    const el = await render({}, [due(KEY1), due(KEY2)]);
    expect(el.querySelector('button.hint-btn')!.textContent).toContain('repertoireTrainer.hint');
    key('h');
    expect(fixture.componentInstance.hintLevel).toBe(1);
    expect(el.querySelector('button.hint-btn')!.textContent).toContain('repertoireTrainer.hintArrow');
    key('h');
    expect(fixture.componentInstance.boardShapes.length).toBe(1);
    expect(fixture.componentInstance.boardShapes[0].dest).toBe('e4' as any);
    const before = fixture.componentInstance.qIndex;
    key('s');
    expect(fixture.componentInstance.qIndex).toBe(before + 1);
    expect(review).not.toHaveBeenCalled();
  });
});
