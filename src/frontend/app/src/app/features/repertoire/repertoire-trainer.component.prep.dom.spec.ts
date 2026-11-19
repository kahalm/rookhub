import { ChangeDetectorRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { EMPTY, of } from 'rxjs';
import { RepertoireTrainerComponent } from './repertoire-trainer.component';
import { RepertoireTrainingService } from './repertoire-training.service';
import { RepertoireExplorerService, DEFAULT_EXPLORER_SETTINGS } from './repertoire-explorer.service';
import { StockfishService } from '../puzzles/stockfish.service';
import { SnackbarService } from '../../core/snackbar.service';
import { lineKeyFromSans } from './repertoire-line-key.util';
import { DEFAULT_SR_LEVELS } from './repertoire-sr.util';

/**
 * „Prep: …"-Repertoire gegen den gespeicherten Gegner (0.999.3), gerendert: Knopf „Reihenfolge", Hinweis, Leerzustand mit
 * „Alle Linien üben" und der Abschluss der aufs Fällige beschränkten Sitzung. Der Trainer ist OnPush — gezeichnet wird
 * über autoDetectChanges + whenStable.
 */
describe('RepertoireTrainerComponent — gespeicherter Gegner (gerendert)', () => {
  const SANS = { A: ['e4', 'e5', 'Nf3', 'Nc6'], B: ['d4', 'd5', 'c4', 'e6'], C: ['c4', 'e5', 'Nc3', 'Nf6'] };
  const KEY = (n: keyof typeof SANS) => lineKeyFromSans(SANS[n]);
  const PGN = [
    '[Event "Rep"]', '[White "A"]', '[Black "Chapter A"]', '', '1. e4 e5 2. Nf3 Nc6 *', '',
    '[Event "Rep"]', '[White "B"]', '[Black "Chapter A"]', '', '1. d4 d5 2. c4 e6 *', '',
    '[Event "Rep"]', '[White "C"]', '[Black "Chapter A"]', '', '1. c4 e5 2. Nc3 Nf6 *', '',
  ].join('\n');
  const at = (ms: number) => new Date(Date.now() + ms).toISOString();
  const state = (n: keyof typeof SANS, level: number, dueAt: string) =>
    ({ lineKey: KEY(n), level, reps: level, lapses: 0, dueAt, lastReviewedAt: null, inPool: true, paused: false });

  let fixture: ComponentFixture<RepertoireTrainerComponent>;
  let makeDue: jasmine.Spy;

  async function render(states: unknown[], query: Record<string, string> = {}): Promise<HTMLElement> {
    localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'Chapter A': 'w' }));
    makeDue = jasmine.createSpy('makeDue').and.returnValue(of({ affected: 3 }));
    await TestBed.configureTestingModule({
      imports: [RepertoireTrainerComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => '1' }, queryParamMap: { get: (k: string) => query[k] ?? null } } } },
        { provide: RepertoireTrainingService, useValue: {
          getPgn: () => of(PGN), getLineStates: () => of(states), makeDue,
          getRepertoireMeta: () => of({ name: 'Prep: Schett, Franz 2026', opponent: { opponent: 'prep:154602', name: 'Schett, Franz', color: 'w', params: {} } }),
          getConfig: () => of({ effective: DEFAULT_SR_LEVELS, user: DEFAULT_SR_LEVELS, repertoire: null, source: 'default', lineOrder: null, hasOpponent: true }),
        } },
        { provide: StockfishService, useValue: { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } },
        { provide: RepertoireExplorerService, useValue: { run: () => EMPTY, effectiveSettings: () => of(DEFAULT_EXPLORER_SETTINGS) } },
        { provide: SnackbarService, useValue: { success: () => ({}), warn: () => ({}), show: () => ({ onAction: () => EMPTY }) } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(RepertoireTrainerComponent);
    fixture.autoDetectChanges();
    await fixture.whenStable();
    // die Reihung des Gegners: am ehesten C, dann A; B kennt er nicht
    TestBed.inject(HttpTestingController).expectOne(r => r.url === '/api/prep/player/154602/training-lines').flush({
      repertoires: [], repertoire: 1, color: 'w', colors: ['w', 'b'], games: 9, total: 2, more: 0, positions: null, lichessBand: null,
      lines: [KEY('C'), KEY('A')].map(key => ({ key, end: '', start: null, chapter: '', moves: [], probability: 0.5, reached: 1, lastYear: 2024, neverReached: false })),
    });
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  const queue = () => fixture.componentInstance.queue.map(l => l.headers['White']).join('');
  /** Die laufende Warteschlange zu Ende bringen (wie nach der letzten Linie → „Weiter"). */
  async function finishSession(): Promise<void> {
    const c = fixture.componentInstance;
    c.session.qIndex = c.queue.length;
    c.session.startCurrentLine();
    fixture.componentRef.injector.get(ChangeDetectorRef).markForCheck();
    await fixture.whenStable();
  }

  afterEach(() => {
    fixture?.componentInstance.session.clearTimers();
    localStorage.removeItem('rookhub_rep_train_chaptercolor_1');
  });

  it('Abfragen: nur fällige + untrainierte Linien, Zähler zählt sie; Knopf „Reihenfolge" und passender Hinweis stehen', async () => {
    // A später fällig, B fällig (Stufe 1), C untrainiert → C (neu) vor B; A kommt nicht
    const el = await render([state('A', 2, at(48 * 3_600_000)), state('B', 1, at(-3_600_000))]);
    expect(queue()).toBe('CB');
    expect(el.querySelector('.counts span')!.textContent).toContain('repertoireTrainer.lineProgress');
    expect(el.querySelector('.line-label')!.textContent).toContain('C');
    const order = el.querySelector('.bar .order-btn')!;
    expect(order).withContext('gegen den gespeicherten Gegner gilt die Einstellung').not.toBeNull();
    expect(order.textContent).toContain('repertoire.trainOrder.option.leastTrained');
    expect(el.querySelector('.bar > button.more')!.classList).not.toContain('more-solo');
    expect(el.querySelector('.opponent-notice')!.textContent).toContain('repertoireTrainer.opponentPrepTies');
    expect(el.querySelector('.opponent-notice')!.textContent).not.toContain('repertoireTrainer.opponentOrder');
  });

  it('nichts fällig, nichts neu: Leerzustand mit nächster Fälligkeit und GENAU EINER Aktion „Alle Linien üben" — sie startet die Sitzung', async () => {
    const later = at(48 * 3_600_000);
    const el = await render([state('A', 2, later), state('B', 1, later), state('C', 3, later)]);
    const card = el.querySelector('mat-card.msg')!;
    expect(card.textContent).toContain('repertoireTrainer.nextDue');
    const actions = card.querySelectorAll('.msg-actions button, .msg-actions a');
    expect(actions.length).toBe(1);
    expect(actions[0].classList).toContain('practice-all');
    expect(actions[0].textContent).toContain('repertoireTrainer.practiceAll');
    expect(card.textContent).not.toContain('repertoireTrainer.makeAllDue');
    expect(el.querySelector('.bar .order-btn')).toBeNull();

    (actions[0] as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(el.querySelector('mat-card.msg')).withContext('Leerzustand weg').toBeNull();
    expect(el.querySelector('.play')).not.toBeNull();
    expect(queue()).toBe('BAC');               // alle drei: Stufe 1, 2, 3
    expect(el.querySelector('.bar .order-btn')).not.toBeNull();
    expect(makeDue).withContext('am Server wird nichts fällig gemacht').not.toHaveBeenCalled();
  });

  it('Abschluss der aufs Fällige beschränkten Sitzung: sagt „fällige und neue Linien“, der Knopf bringt alle; danach „Von vorn“', async () => {
    const el = await render([state('A', 2, at(48 * 3_600_000)), state('B', 1, at(-3_600_000))]);
    await finishSession();
    let card = el.querySelector('mat-card.msg')!;
    expect(card.querySelector('.opponent-done')!.textContent).toContain('repertoireTrainer.opponentDueDone');
    expect(card.textContent).not.toContain('repertoireTrainer.opponentAgain');
    const all = card.querySelector('button.practice-all') as HTMLButtonElement;
    expect(all.textContent).toContain('repertoireTrainer.practiceAll');

    all.click();
    await fixture.whenStable();
    expect(queue()).toBe('CBA');               // alle drei: C neu, B Stufe 1, A Stufe 2

    await finishSession();
    card = el.querySelector('mat-card.msg')!;
    expect(card.querySelector('.opponent-done')!.textContent).toContain('repertoireTrainer.opponentDone');
    expect(card.querySelector('button.practice-all')).toBeNull();
    expect(card.textContent).toContain('repertoireTrainer.opponentAgain');
  });

  it('Lernen: alle Linien', async () => {
    await render([state('A', 2, at(48 * 3_600_000)), state('B', 1, at(-3_600_000))], { mode: 'learn' });
    expect(queue()).toBe('CBA');
  });
});
