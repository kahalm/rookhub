import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { EMPTY, of } from 'rxjs';
import { RepertoireTrainerComponent, SHOW_COMMENTS_KEY } from './repertoire-trainer.component';
import { LineStateDto, RepertoireTrainingService } from './repertoire-training.service';
import { RepertoireExplorerService, DEFAULT_EXPLORER_SETTINGS } from './repertoire-explorer.service';
import { StockfishService } from '../puzzles/stockfish.service';
import { lineKeyFromSans } from './repertoire-line-key.util';

/** Schalter „Kommentare zeigen" im Repertoire-Trainer (0.893.0), gerendert im Abfragen-Modus. */
describe('RepertoireTrainerComponent (gerendert, Kommentare zeigen)', () => {
  const PGN = '[Event "Rep"]\n[White "1.e4"]\n[Black "Chapter A"]\n\n1. e4 {[%cal Ge4d5]Zentrum.} e5 2. Nf3 Nc6 *\n';
  const KEY = lineKeyFromSans(['e4', 'e5', 'Nf3', 'Nc6']);
  const due: LineStateDto = { lineKey: KEY, level: 2, reps: 1, lapses: 0, dueAt: '2000-01-01T00:00:00Z',
    lastReviewedAt: null, inPool: true, paused: false };
  let fixture: ComponentFixture<RepertoireTrainerComponent>;

  afterEach(() => {
    fixture?.destroy();
    localStorage.removeItem('rookhub_rep_train_chaptercolor_1');
    localStorage.removeItem(SHOW_COMMENTS_KEY);
  });

  async function render(stored: string | null): Promise<HTMLElement> {
    localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'Chapter A': 'w' }));
    if (stored === null) localStorage.removeItem(SHOW_COMMENTS_KEY); else localStorage.setItem(SHOW_COMMENTS_KEY, stored);
    await TestBed.configureTestingModule({
      imports: [RepertoireTrainerComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => '1' }, queryParamMap: { get: () => null } } } },
        { provide: RepertoireTrainingService, useValue: { getPgn: () => of(PGN), getLineStates: () => of([due]),
          getChessableMisses: () => of({}), reviewLine: () => of(due) } },
        { provide: StockfishService, useValue: { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } },
        { provide: RepertoireExplorerService, useValue: { run: () => EMPTY, effectiveSettings: () => of(DEFAULT_EXPLORER_SETTINGS) } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(RepertoireTrainerComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const key = (k: string) => { window.dispatchEvent(new KeyboardEvent('keydown', { key: k })); fixture.detectChanges(); };
  const toggle = (el: HTMLElement) => el.querySelector('button.comments-toggle') as HTMLButtonElement;
  const play = () => { fixture.componentInstance.onMove({ orig: 'e2' as any, dest: 'e4' as any }); fixture.detectChanges(); };

  it('Vorgabe aus: im Abfragen-Modus kein Kommentar, Schalter nicht gedrückt, keine Formen', async () => {
    const el = await render(null);
    expect(fixture.componentInstance.phase).toBe('PLAYING');
    expect(toggle(el)).not.toBeNull();
    expect(toggle(el).getAttribute('aria-pressed')).toBe('false');
    play();
    expect(el.querySelector('.played-comment')).toBeNull();
    expect(fixture.componentInstance.boardShapes).toEqual([]);
  });

  it('an: Kommentar des gespielten Zugs ohne [%…] in der Kontextkarte, ohne Halt und ohne Formen', async () => {
    const el = await render('1');
    expect(toggle(el).getAttribute('aria-pressed')).toBe('true');
    play();
    const box = el.querySelector('.played-comment.ctx-comment')!;
    expect(box).not.toBeNull();
    expect(box.textContent).toContain('1. e4');
    expect(box.textContent).toContain('Zentrum.');
    expect(box.textContent).not.toContain('[%');
    expect(fixture.componentInstance.phase).toBe('FEEDBACK');        // läuft normal weiter (kein COMMENT-Halt)
    expect(fixture.componentInstance.boardShapes).toEqual([]);     // der Pfeil e4d5 kommt erst am Linienende
  });

  it('Taste „k" und Knopf schalten um und merken es je Gerät', async () => {
    const el = await render(null);
    play();
    key('k');
    expect(fixture.componentInstance.showComments).toBeTrue();
    expect(localStorage.getItem(SHOW_COMMENTS_KEY)).toBe('1');
    expect(el.querySelector('.played-comment')).not.toBeNull();
    expect(toggle(el).getAttribute('aria-pressed')).toBe('true');
    toggle(el).click();
    fixture.detectChanges();
    expect(fixture.componentInstance.showComments).toBeFalse();
    expect(localStorage.getItem(SHOW_COMMENTS_KEY)).toBe('0');
    expect(el.querySelector('.played-comment')).toBeNull();
  });

  it('gesperrter Speicher: Schalter geht trotzdem (nur für diese Sitzung)', async () => {
    const el = await render(null);
    spyOn(Storage.prototype, 'setItem').and.throwError('QuotaExceededError');
    toggle(el).click();
    fixture.detectChanges();
    expect(fixture.componentInstance.showComments).toBeTrue();
  });
});
