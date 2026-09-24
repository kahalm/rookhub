import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { Mistake, MistakesBySide } from './mistakes.util';
import { MistakesTrainerComponent } from './mistakes-trainer.component';

describe('MistakesTrainerComponent', () => {
  const NACH_E4_E5 = 'rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2';
  const NACH_D4_D5 = 'rnbqkbnr/ppp1pppp/8/3p4/3P4/8/PPP1PPPP/RNBQKBNR w KQkq - 0 2';

  function fehler(over: Partial<Mistake> = {}): Mistake {
    return {
      ply: 2, white: true, cls: 'blunder', fenBefore: NACH_E4_E5,
      playedSan: 'Qh5', playedUci: 'd1h5', bestUci: 'g1f3', bestSan: 'Nf3',
      evalBefore: { cp: 30 }, evalAfter: { cp: -250 }, lostPercent: 24.3, ...over,
    };
  }

  async function setup(bySide: MistakesBySide, side: 'white' | 'black' = 'white') {
    await TestBed.configureTestingModule({
      imports: [MistakesTrainerComponent],
      providers: [
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MAT_DIALOG_DATA, useValue: { bySide, side } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(MistakesTrainerComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  const zwei: MistakesBySide = {
    white: [fehler(), fehler({ ply: 4, fenBefore: NACH_D4_D5, playedSan: 'h4', playedUci: 'h2h4', bestUci: 'c2c4', bestSan: 'c4' })],
    black: [],
  };

  it('startet bei der ersten Aufgabe mit der Stellung VOR dem Fehler', async () => {
    const c = await setup(zwei);

    expect(c.phase()).toBe('ask');
    expect(c.boardFen()).toBe(NACH_E4_E5);
    expect(c.index()).toBe(0);
    expect(c.flipped()).toBeFalse();
    expect(c.list().length).toBe(2);
  });

  it('der Zug der Engine zaehlt als gefunden', async () => {
    const c = await setup(zwei);

    c.onMove({ from: 'g1', to: 'f3', san: 'Nf3', fen: 'danach' });

    expect(c.phase()).toBe('right');
    expect(c.solved()).toBe(1);
    expect(c.boardFen()).toBe('danach');
    expect(c.lastMove()).toEqual(['g1', 'f3']);
  });

  it('ein anderer Zug ist daneben; nach „Nochmal" steht die Ausgangsstellung wieder da', async () => {
    const c = await setup(zwei);

    c.onMove({ from: 'b1', to: 'c3', san: 'Nc3', fen: 'falsch' });

    expect(c.phase()).toBe('wrong');
    expect(c.tried()).toBe('Nc3');
    expect(c.boardFen()).toBe('falsch');

    c.retry();

    expect(c.phase()).toBe('ask');
    expect(c.boardFen()).toBe(NACH_E4_E5);
    expect(c.lastMove()).toBeUndefined();
  });

  it('wer erst danebengreift, bekommt die Aufgabe nicht als selbst gefunden gutgeschrieben', async () => {
    const c = await setup(zwei);

    c.onMove({ from: 'b1', to: 'c3', san: 'Nc3', fen: 'falsch' });
    c.retry();
    c.onMove({ from: 'g1', to: 'f3', san: 'Nf3', fen: 'danach' });

    expect(c.phase()).toBe('right');
    expect(c.solved()).toBe(0);
  });

  it('nach dem Urteil nimmt das Brett keine Zuege mehr an', async () => {
    const c = await setup(zwei);

    c.onMove({ from: 'g1', to: 'f3', san: 'Nf3', fen: 'danach' });
    c.onMove({ from: 'b1', to: 'c3', san: 'Nc3', fen: 'zuspaet' });

    expect(c.boardFen()).toBe('danach');
    expect(c.solved()).toBe(1);
  });

  it('„Loesung zeigen" spielt den Zug vor und zaehlt nicht als gefunden', async () => {
    const c = await setup(zwei);

    c.showSolution();

    expect(c.phase()).toBe('shown');
    expect(c.lastMove()).toEqual(['g1', 'f3']);
    expect(c.boardFen()).toContain('N');   // Springer steht jetzt auf f3
    expect(c.boardFen()).not.toBe(NACH_E4_E5);
    expect(c.solved()).toBe(0);
  });

  it('eine Umwandlung zaehlt auch ohne genannte Figur — das Brett wandelt ohne Rueckfrage in eine Dame um', async () => {
    const c = await setup({ white: [fehler({ fenBefore: '4k3/4P3/8/8/8/8/8/4K3 w - - 0 1', bestUci: 'e7e8q', bestSan: 'e8=Q+' })], black: [] });

    c.onMove({ from: 'e7', to: 'e8', san: 'e8=Q+', fen: 'danach' });

    expect(c.phase()).toBe('right');
  });

  it('am Ende steht die Bilanz', async () => {
    const c = await setup(zwei);

    c.onMove({ from: 'g1', to: 'f3', san: 'Nf3', fen: 'danach' });
    c.next();

    expect(c.index()).toBe(1);
    expect(c.phase()).toBe('ask');
    expect(c.boardFen()).toBe(NACH_D4_D5);
    expect(c.last()).toBeTrue();

    c.next();

    expect(c.phase()).toBe('done');
    expect(c.solved()).toBe(1);

    c.restart();

    expect(c.phase()).toBe('ask');
    expect(c.index()).toBe(0);
    expect(c.solved()).toBe(0);
  });

  it('mit Fehlern auf beiden Seiten laesst sich umschalten — Brett gedreht, Zaehler zurueck', async () => {
    const c = await setup({ white: zwei.white, black: [fehler({ white: false, fenBefore: NACH_D4_D5 })] }, 'white');

    expect(c.bothSides).toBeTrue();
    c.onMove({ from: 'g1', to: 'f3', san: 'Nf3', fen: 'danach' });
    c.chooseSide('black');

    expect(c.side()).toBe('black');
    expect(c.flipped()).toBeTrue();
    expect(c.list().length).toBe(1);
    expect(c.index()).toBe(0);
    expect(c.solved()).toBe(0);
    expect(c.phase()).toBe('ask');
  });

  it('ohne Fehler auf der gewaehlten Seite gibt es nichts zu tun', async () => {
    const c = await setup({ white: zwei.white, black: [] }, 'black');

    expect(c.list()).toEqual([]);
    expect(c.bothSides).toBeFalse();
  });
});
