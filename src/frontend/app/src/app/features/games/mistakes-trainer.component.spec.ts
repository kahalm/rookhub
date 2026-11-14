import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
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

  function setup(t: Mistake = task, dismisser?: (m: Mistake) => void) {
    TestBed.configureTestingModule({
      imports: [MistakesTrainerComponent],
      providers: [provideNoopAnimations(), provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
    });
    const tr = TestBed.inject(TranslateService);
    tr.setTranslation('en', {
      puzzles: { hints: { show: 'Hint', next: 'Next hint', t1Quiet: 'Quiet move.', t2Piece: 'Move your {{piece}}.',
        t3Move: 'Play {{move}}.', pieces: { knight: 'knight' } } },
      games: { mistakes: { analyze: 'Analyse', retryKey: 'Try again (Space bar)', dismiss: "Don't show again", triedEval: 'After {{san}}: {{eval}} (best move: {{best}})' } },
    });
    tr.use('en');
    const fixture = TestBed.createComponent(MistakesTrainerComponent);
    const session = new MistakesSession({ white: [t], black: [] }, 'white', undefined, undefined, dismisser);
    fixture.componentInstance.session = session;
    fixture.detectChanges();
    return { fixture, session, el: fixture.nativeElement as HTMLElement };
  }

  it('Tipps wie beim Puzzle: Zugart → Figur → Zug, danach kein Knopf mehr', () => {
    const { fixture, el } = setup();
    const hint = () => el.querySelector('button.hint') as HTMLButtonElement | null;
    // d4-rk-tipp-zaehler-beschriftung: die Klammer nennt die Stufe, die der Klick aufdeckt (wie in den Puzzle-Lösern).
    expect(hint()!.textContent).toContain('(1/3)');
    hint()!.click(); fixture.detectChanges();
    expect(hint()!.textContent).toContain('(2/3)');
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
  // 0.969.0
  describe('Leertaste = „Nochmal"', () => {
    const space = (target: EventTarget = document.body, init: KeyboardEventInit = {}) => {
      const e = new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true, ...init });
      target.dispatchEvent(e);
      return e;
    };
    const wrongMove = (session: MistakesSession) => session.onMove({ from: 'b1', to: 'c3', san: 'Nc3', fen: 'nach Nc3' });

    it('nach einem Fehlversuch: zurück zur Aufgabe, die Seite scrollt nicht; der Knopf nennt die Taste im Tooltip', () => {
      const { fixture, session, el } = setup();
      wrongMove(session); fixture.detectChanges();
      expect(session.phase()).toBe('wrong');
      expect(el.querySelector('button.retry')).not.toBeNull();
      const e = space();
      expect(e.defaultPrevented).toBeTrue();
      expect(session.phase()).toBe('ask');
      fixture.detectChanges();
      expect(el.querySelector('button.retry')).toBeNull();
    });

    it('nur solange der Knopf dasteht: vor dem Zug und nach der Lösung bleibt die Leertaste frei', () => {
      const { fixture, session } = setup();
      const retry = spyOn(session, 'retry').and.callThrough();
      expect(space().defaultPrevented).toBeFalse();            // Phase ask
      session.showSolution(); fixture.detectChanges();
      expect(space().defaultPrevented).toBeFalse();            // Phase shown — gehört „nächste Aufgabe" der Seite
      expect(retry).not.toHaveBeenCalled();
    });

    it('nicht mit Fokus in Eingabefeld, Textarea, contenteditable, auf Knopf oder Link, nicht mit Strg und nicht als Tastenwiederholung', () => {
      const { fixture, session, el } = setup();
      wrongMove(session); fixture.detectChanges();
      const retry = spyOn(session, 'retry').and.callThrough();
      const host = document.createElement('div');
      host.innerHTML = '<input><textarea></textarea><div contenteditable="true">x</div><a href="#">l</a><button><span class="in">b</span></button>';
      document.body.appendChild(host);
      for (const sel of ['input', 'textarea', '[contenteditable]', 'a', 'button', 'button .in']) {
        expect(space(host.querySelector(sel)!).defaultPrevented).withContext(sel).toBeFalse();
      }
      expect(space(el.querySelector('button.retry')!).defaultPrevented).toBeFalse();   // der Knopf klickt sich selbst
      expect(space(document.body, { ctrlKey: true }).defaultPrevented).toBeFalse();
      expect(space(document.body, { repeat: true }).defaultPrevented).toBeFalse();
      host.remove();
      expect(retry).not.toHaveBeenCalled();
      expect(session.phase()).toBe('wrong');
    });
  });

  // 0.748.0
  it('„Nicht mehr zeigen" nur mit Melder; der Klick meldet die Aufgabe', () => {
    expect(setup().el.querySelector('button.dismiss')).toBeNull();
    TestBed.resetTestingModule();
    const gemeldet: number[] = [];
    const { fixture, el } = setup(task, m => gemeldet.push(m.ply));
    const btn = el.querySelector('button.dismiss') as HTMLButtonElement;
    expect(btn.textContent).toContain("Don't show again");
    btn.click(); fixture.detectChanges();
    expect(gemeldet).toEqual([2]);
  });

  it('Sammlung: nennt die Partie der Aufgabe und verlinkt sie; ohne „Analysieren", wenn abgeschaltet', () => {
    const { fixture, session, el } = setup({ ...task, gameId: 77, gameLabel: 'A – B · 01.10.2026' });
    fixture.componentInstance.analyzable = false;
    const a = el.querySelector('a.game') as HTMLAnchorElement;
    expect(a.textContent).toContain('A – B · 01.10.2026');
    expect(a.getAttribute('href')).toBe('/games/77?ply=2');
    session.showSolution(); fixture.detectChanges();
    expect(el.querySelector('button.analyze')).toBeNull();
  });
});
