import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { SharedReconstructionComponent, buildRows } from './shared-reconstruction.component';
import { PartKind, SharedReconstruction, SharedReconstructionPart } from './reconstruct.service';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
/** Stellung nach 1.e4 e5 2.Nf3 Nc6 3.Bb5. */
const MIDDLE = 'r1bqkbnr/pppp1ppp/2n5/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3';

function part(over: Partial<SharedReconstructionPart>): SharedReconstructionPart {
  return {
    kind: PartKind.Moves, moves: 'e4 e5', fen: null, continuesPrevious: false, certain: true,
    blackToMove: false, note: null, startFen: START, endFen: START, plyCount: 2, startPly: 0, ...over,
  };
}

/**
 * Die Zeilen der geteilten Partie. Geprüft wird das, was den Betrachter betrifft: die Züge stehen
 * mit der richtigen Nummer da, die LÜCKEN stehen sichtbar dazwischen, und ein Bruchstück ohne
 * bekannte Vorstellung fängt trotzdem auf dem richtigen Brett an.
 */
describe('buildRows', () => {
  it('nummeriert die Züge, solange die Kette ab der Grundstellung durchgeht', () => {
    const rows = buildRows([part({ moves: 'e4 e5 Nf3', startPly: 0 })]);

    expect(rows.map(r => r.san)).toEqual(['e4', 'e5', 'Nf3']);
    expect(rows.map(r => r.moveNo)).toEqual([1, 1, 2]);
    expect(rows.map(r => r.black)).toEqual([false, true, false]);
    expect(rows[0].fen).toContain(' b ');              // nach 1.e4 ist Schwarz am Zug
  });

  it('setzt zwischen zwei Teile ohne Anschluss eine Lücke', () => {
    const rows = buildRows([
      part({ moves: 'e4 e5', startPly: 0 }),
      part({ kind: PartKind.Position, fen: MIDDLE, moves: null, startPly: null, endFen: MIDDLE }),
    ]);

    expect(rows.map(r => r.type)).toEqual(['move', 'move', 'gap', 'position']);
    expect(rows[3].fen).toBe(MIDDLE);
  });

  it('lässt die Lücke weg, wo ein Teil ausdrücklich anschließt', () => {
    const rows = buildRows([
      part({ moves: 'e4 e5', startPly: 0 }),
      part({ moves: 'Nf3', continuesPrevious: true, startPly: 2, startFen: START }),
    ]);

    expect(rows.every(r => r.type === 'move')).toBeTrue();
  });

  it('beginnt ein Bruchstück ohne Anker bei der Seite, die es nennt — ohne Zugnummern', () => {
    const rows = buildRows([part({ moves: 'Rxf7 Kxf7', startFen: null, startPly: null, blackToMove: true })]);

    expect(rows.map(r => r.moveNo)).toEqual([null, null]);
    expect(rows[0].black).toBeTrue();
  });

  it('behält einen unmöglichen Zug in der Liste, aber ohne Stellung dahinter', () => {
    const rows = buildRows([part({ moves: 'e4 Qh9 e5', startPly: 0 })]);

    expect(rows.map(r => r.san)).toEqual(['e4', 'Qh9', 'e5']);
    expect(rows[0].fen).toBeDefined();
    expect(rows[1].fen).toBeUndefined();
    expect(rows[2].fen).toBeUndefined();
  });

  it('reicht die Unsicherheit eines Teils an seine Zeilen durch', () => {
    const rows = buildRows([part({ moves: 'e4 e5', certain: false, note: 'aus der Erinnerung' })]);

    expect(rows.every(r => !r.certain)).toBeTrue();
    expect(rows[0].note).toBe('aus der Erinnerung');
    expect(rows[1].note).toBeNull();                   // die Notiz steht nur an der ersten Zeile
  });
});

describe('SharedReconstructionComponent', () => {
  const shared = (parts: SharedReconstructionPart[]): SharedReconstruction => ({
    title: 'Runde 3', white: 'Ich', black: 'Der andere', event: null, playedOn: null, result: '1-0',
    note: null, knownPlies: 2, gaps: 1, prefixSan: 'e4 e5', updatedAt: '2026-09-20T10:00:00Z', parts,
  });

  async function setup() {
    await TestBed.configureTestingModule({
      imports: [SharedReconstructionComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    return { fixture: TestBed.createComponent(SharedReconstructionComponent), http: TestBed.inject(HttpTestingController) };
  }

  it('holt die Partie über das Token und blättert durch ihre Stellungen', async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges();
    http.expectOne(req => req.url.startsWith('/api/reconstructions/shared/')).flush(shared([
      part({ moves: 'e4 e5', startPly: 0 }),
      part({ kind: PartKind.Position, fen: MIDDLE, moves: null, startPly: null, endFen: MIDDLE }),
    ]));
    fixture.detectChanges();

    const c = fixture.componentInstance;
    // Die Lücke steht in der Liste, ist aber nichts zum Anspringen — sie hat keine Stellung.
    expect(c.rows.length).toBe(4);
    expect(c.steps.length).toBe(3);
    expect(c.boardFen()).toContain(' b ');             // nach 1.e4

    c.go('end');
    expect(c.boardFen()).toBe(MIDDLE);
    c.go(-1);
    expect(c.boardFen()).not.toBe(MIDDLE);
    c.go('start');
    expect(c.stepIndex).toBe(0);
  });

  it('sagt es, wenn hinter dem Link nichts (mehr) steht', async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges();
    http.expectOne(req => req.url.startsWith('/api/reconstructions/shared/'))
      .flush(null, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(fixture.componentInstance.game).toBeNull();
    expect(fixture.componentInstance.loading).toBeFalse();
  });
});
