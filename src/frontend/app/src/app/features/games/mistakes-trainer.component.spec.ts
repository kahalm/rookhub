import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { MistakesTrainerComponent } from './mistakes-trainer.component';
import { MistakesSession } from './mistakes-session';
import { Mistake } from './mistakes.util';

describe('MistakesTrainerComponent', () => {
  const NACH_E4_E5 = 'rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2';
  const task: Mistake = {
    ply: 2, white: true, cls: 'blunder', fenBefore: NACH_E4_E5, playedSan: 'Qh5', playedUci: 'd1h5',
    bestUci: 'g1f3', bestSan: 'Nf3', acceptUci: ['g1f3'], acceptSan: ['Nf3'], checkUnlisted: false,
    evalBefore: { cp: 30 }, evalAfter: { cp: -250 }, lostPercent: 24.3,
    candidates: [{ uci: 'g1f3', score: { cp: 30 } }, { uci: 'b1c3', score: { cp: -45 } }],
  };

  function setup() {
    TestBed.configureTestingModule({
      imports: [MistakesTrainerComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    });
    const tr = TestBed.inject(TranslateService);
    tr.setTranslation('en', {
      puzzles: { hints: { show: 'Hint', next: 'Next hint', t1Quiet: 'Quiet move.', t2Piece: 'Move your {{piece}}.',
        t3Move: 'Play {{move}}.', pieces: { knight: 'knight' } } },
      games: { mistakes: { analyze: 'Analyse', triedEval: 'After {{san}}: {{eval}} (best move: {{best}})' } },
    });
    tr.use('en');
    const fixture = TestBed.createComponent(MistakesTrainerComponent);
    const session = new MistakesSession({ white: [task], black: [] }, 'white');
    fixture.componentInstance.session = session;
    fixture.detectChanges();
    return { fixture, session, el: fixture.nativeElement as HTMLElement };
  }

  it('Tipps wie beim Puzzle: Zugart → Figur → Zug, danach kein Knopf mehr', () => {
    const { fixture, el } = setup();
    const hint = () => el.querySelector('button.hint') as HTMLButtonElement | null;
    expect(hint()!.textContent).toContain('(0/3)');
    hint()!.click(); fixture.detectChanges();
    hint()!.click(); fixture.detectChanges();
    hint()!.click(); fixture.detectChanges();
    expect(Array.from(el.querySelectorAll('.hint-list li')).map(li => li.textContent!.trim()))
      .toEqual(['Quiet move.', 'Move your knight.', 'Play Nf3.']);
    expect(hint()).toBeNull();
  });

  it('daneben: Bewertung des Fehlversuchs und der Knopf „Analysieren"', () => {
    const { fixture, session, el } = setup();
    let analyzed = 0;
    fixture.componentInstance.analyze.subscribe(() => analyzed++);
    expect(el.querySelector('button.analyze')).toBeNull();      // vor dem Urteil nicht

    session.onMove({ from: 'b1', to: 'c3', san: 'Nc3', fen: 'nach Nc3' });
    fixture.detectChanges();
    expect(el.querySelector('.tried-eval')!.textContent).toContain('After Nc3: -0.45 (best move: +0.30)');
    (el.querySelector('button.analyze') as HTMLButtonElement).click();
    expect(analyzed).toBe(1);

    session.retry(); session.onMove({ from: 'g1', to: 'f3', san: 'Nf3', fen: 'nach Nf3' });
    fixture.detectChanges();
    expect(el.querySelector('button.analyze')).not.toBeNull(); // auch nach einer richtigen Lösung
  });
});
