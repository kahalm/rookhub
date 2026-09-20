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
    continuesPrevious: false, note: null, anchored: true, valid: true, startFen: START,
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
      continuesPrevious: true, note: 'danach Turmtausch',
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
