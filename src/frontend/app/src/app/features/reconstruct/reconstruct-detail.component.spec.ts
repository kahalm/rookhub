import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { ReconstructDetailComponent } from './reconstruct-detail.component';
import { PartKind, Reconstruction, ReconstructionPart } from './reconstruct.service';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

/** Eine Stellung mitten in der Partie (nach 1.e4 e5 2.Nf3 Nc6 3.Bc4). */
const MIDDLE = 'r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3';

function part(over: Partial<ReconstructionPart>): ReconstructionPart {
  return {
    id: 1, ordinal: 0, kind: PartKind.Moves, moves: 'e4 e5', fen: null, fromPly: null,
    continuesPrevious: false, certain: true, blackToMove: false, generated: false, note: null, anchored: true, valid: true, startFen: START,
    endFen: START, plyCount: 2, startPly: 0, firstBadMove: null, mismatch: false, ...over,
  };
}

function data(parts: ReconstructionPart[]): Reconstruction {
  return {
    id: 5, title: 'Runde 3', partCount: parts.length, knownPlies: 2, gaps: 0,
    updatedAt: '2026-09-20T10:00:00Z', parts, prefixSan: 'e4 e5',
  };
}

/**
 * Der Wortlaut, den die API WIRKLICH schickt. Die Enum-Werte kommen als Namen
 * (`JsonStringEnumConverter`) — mit einem Zahlen-Enum im Client war jede Stellung eine Zugfolge,
 * und an keiner Lücke stand ein Knopf. Deshalb steht hier die rohe Zeichenkette, nicht `PartKind`.
 */
describe('ReconstructDetailComponent Drahtformat', () => {
  it('erkennt eine Stellung an dem, was der Server sendet', () => {
    const wire = JSON.parse('{"kind":"Position"}') as { kind: PartKind };
    expect(wire.kind).toBe(PartKind.Position);
    expect(JSON.parse('{"kind":"Moves"}').kind).toBe(PartKind.Moves);
  });
});

describe('ReconstructDetailComponent', () => {
  let http: HttpTestingController;
  /** Antwort der Rückfrage — der Dialog selbst gehört nicht in jeden Test. */
  let confirmAnswer = true;

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
        { provide: ConfirmService, useValue: { ask: () => of(confirmAnswer) } },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    confirmAnswer = true;
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
    // Ein angeklicktes Teil zeigt seinen ANFANG — gespielt wird am Ende.
    expect(c.plyShown()).toBe(0);
    expect(c.boardPlayable()).toBeFalse();

    c.goPly('end');
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
      continuesPrevious: true, certain: true, blackToMove: false, note: 'danach Turmtausch',
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

  it('öffnet bei einer leeren Rekonstruktion gleich den Zug-Editor', () => {
    const c = open([]);

    expect(c.editingId()).toBe(0);
    expect(c.editKind()).toBe(PartKind.Moves);
  });

  it('speichert die Züge und macht direkt mit einer Stellung weiter', () => {
    // Der Fluss beim Erinnern: Züge, bis es nicht mehr weitergeht, dann die nächste Stellung.
    const c = open([]);
    c.onMovesInput('e4 e5');
    c.toPosition();

    const req = http.expectOne({ url: '/api/reconstructions/5/parts', method: 'POST' });
    expect(req.request.body.moves).toBe('e4 e5');
    req.flush(data([part({ id: 9, moves: 'e4 e5' })]));

    expect(c.editingId()).toBe(0);                  // der Editor bleibt offen …
    expect(c.editKind()).toBe(PartKind.Position);   // … jetzt für die Stellung
  });

  it('der leere Zug-Editor wechselt zur Stellung, ohne ein Teil anzulegen', () => {
    const c = open([part({ id: 1 })]);
    c.startNew(PartKind.Moves);
    c.toPosition();

    http.expectNone({ method: 'POST' });
    expect(c.editKind()).toBe(PartKind.Position);
    expect(c.editContinues).toBeFalse();            // nach einer Zugfolge liegt eine Lücke
  });

  it('nach einer übernommenen Stellung geht es mit Zügen ab ihr weiter', () => {
    const c = open([]);
    c.startNew(PartKind.Position);
    c.onPositionApplied(MIDDLE);

    const req = http.expectOne({ url: '/api/reconstructions/5/parts', method: 'POST' });
    expect(req.request.body.fen).toBe(MIDDLE);
    req.flush(data([part({ id: 8, kind: PartKind.Position, fen: MIDDLE, endFen: MIDDLE, moves: null })]));

    expect(c.editKind()).toBe(PartKind.Moves);
    expect(c.editContinues).toBeTrue();             // Züge nach einer Stellung schließen an sie an
  });

  it('legt den Sicher-Haken eines Teils ohne den Editor um', () => {
    const saved = part({ id: 6, moves: 'e4 e5', note: 'Notiz' });
    const c = open([saved]);
    c.toggleCertain(saved);

    const req = http.expectOne({ url: '/api/reconstructions/5/parts/6', method: 'PUT' });
    expect(req.request.body).toEqual({
      kind: PartKind.Moves, moves: 'e4 e5', fen: null, fromPly: null,
      continuesPrevious: false, certain: false, blackToMove: false, note: 'Notiz',
    });
    req.flush(data([]));
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

  it('bietet die Lückensuche an einer Stellung an, wie der Server sie schickt', () => {
    // Genau der Fall, der in 0.487–0.493 nie funktioniert hat: „Position" statt 1.
    const rawPosition = { ...part({ id: 2, ordinal: 1, moves: null, fen: MIDDLE }), kind: 'Position' as PartKind };
    const c = open([part({ id: 1 }), rawPosition]);

    expect(c.canCloseGap(c.data()!.parts[1], 1)).toBeTrue();
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

  it('setzt die gefundenen Wege als VORSCHLÄGE in die Liste und öffnet den ersten', () => {
    const position = part({ id: 2, ordinal: 1, kind: PartKind.Position, fen: MIDDLE, moves: null });
    const c = open([part({ id: 1 }), position]);
    const proposal = part({ id: 9, ordinal: 1, moves: 'Nc6 Bb5', generated: true, continuesPrevious: true,
                            certain: false, startFen: null, endFen: null });
    const withProposal = data([part({ id: 1 }), proposal, { ...position, ordinal: 2 }]);

    c.closeGap(position);
    const req = http.expectOne({ url: '/api/reconstructions/5/parts/2/gap/propose', method: 'POST' });
    expect(req.request.body).toEqual({ maxPlies: 4 });
    req.flush({ partId: 2, maxPlies: 4, nodes: 120, budgetExhausted: false, reason: null,
                inserted: 1, detail: withProposal });

    expect(c.data()!.parts.length).toBe(3);
    expect(c.editingId()).toBe(9);          // gleich zum Durchsehen geöffnet
    expect(c.editingGenerated()).toBeTrue();
    expect(c.generatedCount()).toBe(1);
  });

  it('bestätigt eine Stellung aus dem Vorschlag als eigenes Teil', () => {
    const position = part({ id: 2, ordinal: 2, kind: PartKind.Position, fen: MIDDLE, moves: null });
    const proposal = part({ id: 9, ordinal: 1, moves: 'Nc6 Bb5', generated: true, continuesPrevious: true,
                            certain: false, startFen: START, endFen: null });
    const c = open([part({ id: 1 }), proposal, position]);

    c.edit(proposal);
    expect(c.canAcceptWaypoint()).toBeFalse();   // am Anfang steht die Stellung davor
    c.goPly(1);                                  // eine Stellung MITTEN im Vorschlag
    expect(c.canAcceptWaypoint()).toBeTrue();
    const fen = c.editBoardFen();
    c.acceptWaypoint();

    const req = http.expectOne({ url: '/api/reconstructions/5/parts/2/gap/waypoint', method: 'POST' });
    expect(req.request.body).toEqual({ fen, certain: true });
    req.flush(data([]));
  });

  it('übernimmt auf Wunsch die ganze vorgeschlagene Linie', () => {
    const position = part({ id: 2, ordinal: 2, kind: PartKind.Position, fen: MIDDLE, moves: null });
    const proposal = part({ id: 9, ordinal: 1, moves: 'Nc6 Bb5', generated: true, continuesPrevious: true,
                            certain: false });
    const c = open([part({ id: 1 }), proposal, position]);

    c.edit(proposal);
    c.acceptProposal();

    const req = http.expectOne({ url: '/api/reconstructions/5/parts/2/gap/apply', method: 'POST' });
    expect(req.request.body).toEqual({ moves: 'Nc6 Bb5' });
    req.flush(data([]));
  });

  it('blendet die Vorschläge aus, ohne sie zu löschen', () => {
    const proposal = part({ id: 9, ordinal: 1, moves: 'Nc6 Bb5', generated: true });
    const c = open([part({ id: 1 }), proposal]);

    expect(c.visibleParts().length).toBe(2);
    c.showGenerated = false;
    expect(c.visibleParts().length).toBe(1);
    expect(c.generatedCount()).toBe(1);
  });

  it('sagt beim leeren Ergebnis, WARUM es keinen Weg gibt', () => {
    const position = part({ id: 2, ordinal: 1, kind: PartKind.Position, fen: MIDDLE, moves: null });
    const c = open([part({ id: 1 }), position]);

    c.closeGap(position);
    http.expectOne({ url: '/api/reconstructions/5/parts/2/gap/propose', method: 'POST' })
      .flush({ partId: 2, maxPlies: 4, nodes: 9, budgetExhausted: false, reason: 'unreachable',
               inserted: 0, detail: data([part({ id: 1 }), position]) });

    // „gibt es nicht" und „nicht gefunden" sind verschiedene Auskünfte und haben eigene Texte.
    expect(c.gapMessageKey()).toBe('reconstruct.gap.unreachable');
  });

  it('springt am Ende einer Linie zum nächsten Teil und am Anfang zum vorigen', () => {
    const first = part({ id: 1, ordinal: 0, moves: 'e4 e5' });
    const second = part({ id: 2, ordinal: 1, kind: PartKind.Position, fen: MIDDLE, moves: null });
    const c = open([first, second]);

    c.edit(first);
    expect(c.plyShown()).toBe(0);            // Klick zeigt den Anfang
    c.goPly('end');
    c.goPly(1);                              // am Ende → nächstes Teil
    expect(c.editingId()).toBe(2);

    c.goPly(-1);                             // am Anfang → zurück ins vorige, dort ans Ende
    expect(c.editingId()).toBe(1);
    expect(c.atEnd()).toBeTrue();
  });

  it('blättert mit den Pfeiltasten durch die eingetippten Züge', () => {
    const c = open([]);
    c.onMovesInput('e4 e5 Nf3');
    expect(c.plyTotal()).toBe(3);
    expect(c.atEnd()).toBeTrue();

    c.onKeyDown(new KeyboardEvent('keydown', { key: 'ArrowLeft' }));
    expect(c.plyShown()).toBe(2);
    expect(c.editBoardFen()).not.toContain('5N2');     // der Springer steht noch auf g1
    expect(c.atEnd()).toBeFalse();
    expect(c.boardPlayable()).toBeFalse();             // hier wird geschaut, nicht gespielt

    c.onKeyDown(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    expect(c.plyShown()).toBe(3);
    expect(c.atEnd()).toBeTrue();
    expect(c.boardPlayable()).toBeTrue();
  });

  it('lässt die Pfeiltasten in Textfeldern in Ruhe', () => {
    const c = open([]);
    c.onMovesInput('e4 e5');
    const input = document.createElement('textarea');
    const event = new KeyboardEvent('keydown', { key: 'ArrowLeft' });
    Object.defineProperty(event, 'target', { value: input });

    c.onKeyDown(event);

    expect(c.plyShown()).toBe(2);   // der Cursor im Textfeld gehört dem Textfeld
  });

  it('schneidet mit „ab hier weiterspielen" den Rest der Zugfolge ab', () => {
    const c = open([]);
    c.onMovesInput('e4 e5 Nf3');
    c.goPly('start');
    c.goPly(1);
    c.truncateHere();

    expect(c.editMoves).toBe('e4');
    expect(c.atEnd()).toBeTrue();
  });

  it('stellt die Seite am Zug um und löst das Teil dabei vom vorigen', () => {
    // „Und dann schlug ER auf f7": ohne diese Umstellung ließe sich ein Bruchstück, das mit einem
    // schwarzen Zug beginnt, am Brett gar nicht eingeben.
    // MIDDLE ist die Stellung nach 3.Lc4, es steht also Schwarz am Zug.
    const c = open([part({ id: 1, kind: PartKind.Position, fen: MIDDLE, moves: null, endFen: MIDDLE })]);
    c.startNew(PartKind.Moves);
    expect(c.editContinues).toBeTrue();
    expect(c.sideBlack()).toBeTrue();

    c.setSideBlack(false);

    expect(c.sideBlack()).toBeFalse();
    expect(c.editContinues).toBeFalse();   // die Stellung davor sagt etwas anderes
    c.onMovesInput('d3');                  // ein weißer Zug, der vorher nicht möglich wäre
    expect(c.editBadMove()).toBeNull();
  });

  it('schickt beim Speichern mit, wer am Zug ist', () => {
    const c = open([]);
    c.startNew(PartKind.Moves);
    c.setSideBlack(true);
    c.onMovesInput('e5');
    c.savePart();

    const req = http.expectOne({ url: '/api/reconstructions/5/parts', method: 'POST' });
    expect(req.request.body.blackToMove).toBeTrue();
    req.flush(data([]));
  });

  it('löscht ein Teil erst nach der Rückfrage', () => {
    const saved = part({ id: 7 });
    confirmAnswer = false;
    const c = open([saved]);

    c.removePart(saved);
    http.expectNone({ method: 'DELETE' });

    confirmAnswer = true;
    c.removePart(saved);
    http.expectOne({ url: '/api/reconstructions/5/parts/7', method: 'DELETE' }).flush(data([]));
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
