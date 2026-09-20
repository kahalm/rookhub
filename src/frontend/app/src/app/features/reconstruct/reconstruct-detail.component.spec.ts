import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { ReconstructDetailComponent } from './reconstruct-detail.component';
import { PartKind, Reconstruction, ReconstructionPart } from './reconstruct.service';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

/** Eine Stellung mitten in der Partie (nach 1.e4 e5 2.Nf3 Nc6 3.Bc4). */
const MIDDLE = 'r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3';

function part(over: Partial<ReconstructionPart>): ReconstructionPart {
  return {
    id: 1, ordinal: 0, kind: PartKind.Moves, moves: 'e4 e5', fen: null, fromPly: null,
    continuesPrevious: false, certain: true, note: null, anchored: true, valid: true, startFen: START,
    endFen: START, plyCount: 2, startPly: 0, firstBadMove: null, mismatch: false, ...over,
  };
}

function data(parts: ReconstructionPart[]): Reconstruction {
  return {
    id: 5, title: 'Runde 3', partCount: parts.length, knownPlies: 2, gaps: 0,
    updatedAt: '2026-09-20T10:00:00Z', parts, prefixSan: 'e4 e5',
  };
}

describe('ReconstructDetailComponent', () => {
  let http: HttpTestingController;

  function open(parts: ReconstructionPart[]): ReconstructDetailComponent {
    const fixture = TestBed.createComponent(ReconstructDetailComponent);
    fixture.detectChanges();
    http.expectOne('/api/reconstructions/5').flush(data(parts));
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ReconstructDetailComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => '5' } } } },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  it('spielt die eingetippten Züge lokal mit und zeigt die Stellung danach', () => {
    const c = open([]);
    c.startNew(PartKind.Moves);
    c.onMovesInput('1. e4 e5 2. Nf3');

    expect(c.editBoardFen()).toContain('5N2');          // Springer steht auf f3
    expect(c.editBoardFen()).toContain(' b ');          // Schwarz ist am Zug
    expect(c.editBadMove()).toBeNull();
  });

  it('nimmt beim BEARBEITEN eines gespeicherten Teils weiter Züge am Brett an', () => {
    // Gemeldet: nach dem Speichern und erneutem Öffnen ließ sich nur noch EIN Zug setzen.
    const afterTwo = 'rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2';
    const saved = part({ moves: 'e4 e5', startFen: START, endFen: afterTwo });
    const c = open([saved]);

    c.edit(saved);
    const before = c.editBoardFen();
    c.onBoardMove({ from: 'g1', to: 'f3', san: 'Nf3', fen: '' });
    const afterFirst = c.editBoardFen();
    c.onBoardMove({ from: 'b8', to: 'c6', san: 'Nc6', fen: '' });

    expect(c.editMoves).toBe('e4 e5 Nf3 Nc6');
    expect(afterFirst).not.toBe(before);
    expect(c.editBoardFen()).not.toBe(afterFirst);
    expect(c.editBadMove()).toBeNull();
  });

  it('nennt den ersten unmöglichen Zug sofort, ohne den Server zu fragen', () => {
    const c = open([]);
    c.startNew(PartKind.Moves);
    c.onMovesInput('e4 e5 Qh9');

    expect(c.editBadMove()).toBe('Qh9');
    http.expectNone({ method: 'POST' });
  });

  it('ein Zug am Brett hängt hinten an die Zugfolge an', () => {
    const c = open([]);
    c.startNew(PartKind.Moves);
    c.onMovesInput('e4');
    c.onBoardMove({ from: 'e7', to: 'e5', san: 'e5', fen: START });

    expect(c.editMoves).toBe('e4 e5');
    c.undoMove();
    expect(c.editMoves).toBe('e4');
  });

  // Die Grundannahme ist LÜCKE: Bruchstücke stammen von verschiedenen Stellen der Partie.
  // Nach einer STELLUNG ist die Fortsetzung dagegen der Normalfall.
  it('schlägt den Anschluss nur nach einer Stellung vor', () => {
    const afterMoves = open([part({ id: 1, kind: PartKind.Moves })]);
    afterMoves.startNew(PartKind.Moves);
    expect(afterMoves.editContinues).toBeFalse();

    const afterPosition = open([part({ id: 2, kind: PartKind.Position, fen: MIDDLE, endFen: MIDDLE, moves: null })]);
    afterPosition.startNew(PartKind.Moves);
    expect(afterPosition.editContinues).toBeTrue();
  });

  it('speichert ein neues Teil mit Art, Zügen und Anschluss', () => {
    const c = open([part({ id: 3, kind: PartKind.Position, fen: MIDDLE, endFen: MIDDLE, moves: null })]);
    c.startNew(PartKind.Moves);
    c.onMovesInput('Bc5');
    c.editNote = '  danach Turmtausch  ';
    c.savePart();

    const req = http.expectOne({ url: '/api/reconstructions/5/parts', method: 'POST' });
    expect(req.request.body).toEqual({
      kind: PartKind.Moves, moves: 'Bc5', fen: null, fromPly: null,
      continuesPrevious: true, certain: true, note: 'danach Turmtausch',
    });
    req.flush(data([]));
    expect(c.editingId()).toBeNull();
  });

  it('das Bearbeiten eines Teils beginnt an dessen eigener Startstellung', () => {
    const c = open([part({ id: 4, moves: 'Bc5', startFen: MIDDLE, endFen: MIDDLE, continuesPrevious: true })]);
    c.edit(c.data()!.parts[0]);

    expect(c.editMoves).toBe('Bc5');
    expect(c.editContinues).toBeTrue();
    expect(c.editBoardFen()).not.toBe(START);
  });

  it('sperrt das Brett, solange ein Zug der Eingabe nicht spielbar ist', () => {
    // Sonst landete jeder am Brett gespielte Zug hinter dem unmöglichen Zug im Text und käme nie
    // auf dem Brett an — gemeldet als „ich kann nur einen Zug machen".
    const c = open([]);
    c.startNew(PartKind.Moves);
    expect(c.boardPlayable()).toBeTrue();

    c.onMovesInput('e4 Qh9');
    expect(c.boardPlayable()).toBeFalse();

    c.onMovesInput('e4 e5');
    expect(c.boardPlayable()).toBeTrue();
  });

  it('sucht die Lücke nur vor einer STELLUNG, die nicht schon anschließt', () => {
    const moves = part({ id: 1, kind: PartKind.Moves });
    const position = part({ id: 2, ordinal: 1, kind: PartKind.Position, fen: MIDDLE, moves: null });
    const c = open([moves, position]);

    expect(c.canCloseGap(moves, 0)).toBeFalse();                                   // erstes Teil
    expect(c.canCloseGap(position, 1)).toBeTrue();
    expect(c.canCloseGap({ ...position, continuesPrevious: true }, 1)).toBeFalse(); // keine Lücke
    expect(c.canCloseGap({ ...position, kind: PartKind.Moves }, 1)).toBeFalse();    // kein Ziel
  });

  it('holt die Wege durch die Lücke und setzt den gewählten ein', () => {
    const position = part({ id: 2, ordinal: 1, kind: PartKind.Position, fen: MIDDLE, moves: null });
    const c = open([part({ id: 1 }), position]);

    c.closeGap(position);
    http.expectOne({ url: '/api/reconstructions/5/parts/2/gap', method: 'POST' })
      .flush({ partId: 2, maxPlies: 4, nodes: 120, budgetExhausted: false, reason: null,
               solutions: [{ san: 'Nc6 Bb5', plies: 2 }] });

    expect(c.gapResult()!.solutions.length).toBe(1);
    expect(c.gapMessageKey()).toBeNull();

    c.applyGap(c.gapResult()!.solutions[0]);
    const applied = http.expectOne({ url: '/api/reconstructions/5/parts/2/gap/apply', method: 'POST' });
    expect(applied.request.body).toEqual({ moves: 'Nc6 Bb5' });
    applied.flush(data([]));
    expect(c.gapFor()).toBeNull();
  });

  it('sagt beim leeren Ergebnis, WARUM es keinen Weg gibt', () => {
    const position = part({ id: 2, ordinal: 1, kind: PartKind.Position, fen: MIDDLE, moves: null });
    const c = open([part({ id: 1 }), position]);

    c.closeGap(position);
    http.expectOne({ url: '/api/reconstructions/5/parts/2/gap', method: 'POST' })
      .flush({ partId: 2, maxPlies: 4, nodes: 9, budgetExhausted: false, reason: 'unreachable', solutions: [] });

    // „gibt es nicht" und „nicht gefunden" sind verschiedene Auskünfte und haben eigene Texte.
    expect(c.gapMessageKey()).toBe('reconstruct.gap.unreachable');
  });

  it('benennt den Zustand eines Teils für die Anzeige', () => {
    const c = open([]);
    expect(c.state(part({ anchored: true, valid: true }))).toBe('ok');
    expect(c.state(part({ anchored: false, valid: false }))).toBe('floating');
    expect(c.state(part({ anchored: true, valid: false }))).toBe('broken');
    expect(c.state(part({ anchored: true, valid: false, mismatch: true }))).toBe('mismatch');
  });

  it('rechnet Halbzüge in Zugnummern um', () => {
    const c = open([]);
    expect(c.moveNo(0)).toBe(1);
    expect(c.moveNo(1)).toBe(1);
    expect(c.moveNo(24)).toBe(13);
  });
});
