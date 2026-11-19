import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { EMPTY, of, throwError } from 'rxjs';
import { RepertoireTrainerComponent } from './repertoire-trainer.component';
import { RepertoireTrainingService } from './repertoire-training.service';
import { RepertoireExplorerService, DEFAULT_EXPLORER_SETTINGS } from './repertoire-explorer.service';
import { StockfishService } from '../puzzles/stockfish.service';
import { SnackbarService } from '../../core/snackbar.service';
import { REPERTOIRE_OFFLINE_PREFIX } from '../../core/offline.service';
import { lineKeyFromSans } from './repertoire-line-key.util';
import { DEFAULT_SR_LEVELS } from './repertoire-sr.util';

/**
 * Knopf „Reihenfolge“ in der Kopfleiste des Trainers (0.996.0), gerendert. Der Trainer ist OnPush: gezeichnet wird hier
 * nur über autoDetectChanges + whenStable — die neue Beschriftung nach der Wahl im Dialog muss von selbst erscheinen.
 */
describe('RepertoireTrainerComponent — Knopf „Reihenfolge“ (gerendert)', () => {
  const PGN = [
    '[Event "Rep"]', '[White "A"]', '[Black "Chapter A"]', '', '1. e4 e5 2. Nf3 Nc6 *', '',
    '[Event "Rep"]', '[White "B"]', '[Black "Chapter A"]', '', '1. d4 d5 2. c4 e6 *', '',
  ].join('\n');
  const KEY_A = lineKeyFromSans(['e4', 'e5', 'Nf3', 'Nc6']);
  const KEY_B = lineKeyFromSans(['d4', 'd5', 'c4', 'e6']);
  const dueState = (lineKey: string, level: number) => ({
    lineKey, level, reps: level, lapses: 0, dueAt: new Date(Date.now() - 3_600_000).toISOString(), lastReviewedAt: null, inPool: true, paused: false,
  });
  const OFFLINE_KEY = REPERTOIRE_OFFLINE_PREFIX + '1';

  let fixture: ComponentFixture<RepertoireTrainerComponent>;
  let setLineOrder: jasmine.Spy;

  async function render(opts: { lineOrder?: string | null; query?: Record<string, string>; offline?: boolean } = {}): Promise<HTMLElement> {
    localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'Chapter A': 'w' }));
    const query = opts.query ?? {};
    setLineOrder = jasmine.createSpy('setLineOrder').and.returnValue(of(undefined));
    const down = () => throwError(() => new Error('offline'));
    await TestBed.configureTestingModule({
      imports: [RepertoireTrainerComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => '1' }, queryParamMap: { get: (k: string) => query[k] ?? null } } } },
        { provide: RepertoireTrainingService, useValue: {
          getPgn: () => opts.offline ? down() : of(PGN),
          getLineStates: () => opts.offline ? down() : of([dueState(KEY_A, 2), dueState(KEY_B, 0)]),
          getRepertoireName: () => of('Weiß: Italienisch'),
          getConfig: () => of({ effective: DEFAULT_SR_LEVELS, user: DEFAULT_SR_LEVELS, repertoire: null, source: 'default', lineOrder: opts.lineOrder ?? null }),
          setLineOrder,
        } },
        { provide: StockfishService, useValue: { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } },
        { provide: RepertoireExplorerService, useValue: { run: () => EMPTY, effectiveSettings: () => of(DEFAULT_EXPLORER_SETTINGS) } },
        { provide: SnackbarService, useValue: { success: () => ({}), warn: () => ({}), show: () => ({ onAction: () => EMPTY }) } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(RepertoireTrainerComponent);
    fixture.autoDetectChanges();
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  const orderButton = (el: HTMLElement) => el.querySelector<HTMLButtonElement>('.bar .order-btn');
  const panel = () => document.querySelector('mat-dialog-container') as HTMLElement | null;
  const queue = () => fixture.componentInstance.queue.map(l => l.headers['White']).join('');

  afterEach(() => {
    TestBed.inject(MatDialog).closeAll();
    document.querySelectorAll('.cdk-overlay-container').forEach(o => o.innerHTML = '');
    localStorage.removeItem('rookhub_rep_train_chaptercolor_1');
    localStorage.removeItem(OFFLINE_KEY);
  });

  it('zeigt die gewählte Reihenfolge; die Wahl im Dialog speichert, baut neu auf und der Knopf zeigt sie von selbst (OnPush)', async () => {
    const el = await render({ lineOrder: 'mostTrained' });
    expect(orderButton(el)!.textContent).toContain('repertoire.trainOrder.option.mostTrained');
    expect(el.querySelector('.bar > button.more')!.classList).not.toContain('more-solo');
    expect(queue()).toBe('AB');                                   // A (2×) vor B (nie)

    orderButton(el)!.click();
    await fixture.whenStable();
    expect(panel()).withContext('Dialog offen').not.toBeNull();
    expect(panel()!.querySelector('.subtitle')!.textContent).toContain('Weiß: Italienisch');
    const checked = () => Array.from(panel()!.querySelectorAll('mat-radio-button'))
      .filter(r => r.querySelector('input')!.checked).map(r => r.getAttribute('data-order'));
    expect(checked()).toEqual(['mostTrained']);                   // die geltende ist markiert, ohne Abruf

    panel()!.querySelector<HTMLInputElement>('mat-radio-button[data-order="leastTrained"] input')!.click();
    await fixture.whenStable();
    panel()!.querySelector<HTMLButtonElement>('button.save')!.click();
    await fixture.whenStable();

    expect(setLineOrder).toHaveBeenCalledOnceWith(1, 'leastTrained');
    expect(panel()).toBeNull();
    expect(queue()).toBe('BA');                                   // neu aufgebaut: B (nie) vor A (2×)
    expect(orderButton(el)!.textContent).toContain('repertoire.trainOrder.option.leastTrained');
    expect(orderButton(el)!.getAttribute('aria-label'))
      .toBe('repertoire.trainOrder.title: repertoire.trainOrder.option.leastTrained');
  });

  it('Gegner-Sitzung: kein Knopf — dort bestimmt der Gegner die Reihenfolge', async () => {
    const el = await render({ lineOrder: 'mostTrained', query: { opponent: 'prep:42', color: 'w' } });
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(r => r.url === '/api/prep/player/42/training-lines').flush({
      repertoires: [], repertoire: 1, color: 'w', colors: ['w', 'b'], games: 9, total: 2, more: 0,
      lines: [KEY_B, KEY_A].map(key => ({ key, end: '', start: null, chapter: '', moves: [], probability: 0.5, reached: 1, lastYear: 2024, neverReached: false })),
    });
    await fixture.whenStable();

    expect(queue()).toBe('BA');
    expect(el.querySelector('.opponent-notice')).not.toBeNull();
    expect(orderButton(el)).toBeNull();
    // das ⋮ bleibt — und steht ohne den Knopf am Handy in der Titelzeile statt allein in einer eigenen (DESIGN 5.5)
    expect(el.querySelector('.bar > button.more')!.classList).toContain('more-solo');
  });

  it('offline: kein Knopf (die Wahl liegt am Server) — die Reihenfolge der Kopie gilt trotzdem', async () => {
    localStorage.setItem(OFFLINE_KEY, JSON.stringify({
      meta: { id: 1, name: 'Rep' }, pgn: PGN, states: [dueState(KEY_A, 2), dueState(KEY_B, 0)], config: null, lineOrder: 'mostTrained', savedAt: '',
    }));
    const el = await render({ offline: true });
    expect(fixture.componentInstance.offlineSession).toBeTrue();
    expect(queue()).toBe('AB');
    expect(orderButton(el)).toBeNull();
  });
});
