import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { applyMoveBonus, RepertoireService } from './repertoire.service';

describe('RepertoireService', () => {
  let service: RepertoireService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [RepertoireService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(RepertoireService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list GETs /api/repertoires', () => {
    service.list().subscribe();
    const req = httpMock.expectOne('/api/repertoires');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('create POSTs the dto', () => {
    service.create({ name: 'Sicilian' }).subscribe();
    const req = httpMock.expectOne('/api/repertoires');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Sicilian' });
    req.flush({});
  });

  it('update PUTs to the id route', () => {
    service.update(7, { name: 'X' }).subscribe();
    const req = httpMock.expectOne('/api/repertoires/7');
    expect(req.request.method).toBe('PUT');
    req.flush({});
  });

  it('remove DELETEs the id route', () => {
    service.remove(9).subscribe();
    const req = httpMock.expectOne('/api/repertoires/9');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('downloadPgn requests a blob from the pgn route', () => {
    service.downloadPgn(3).subscribe();
    const req = httpMock.expectOne('/api/repertoires/3/pgn');
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['1. e4 *']));
  });

  it('getDetail GETs the repertoire route', () => {
    service.getDetail<unknown>(5).subscribe();
    expect(httpMock.expectOne('/api/repertoires/5').request.method).toBe('GET');
  });

  it('getPgnText requests the pgn route as text', () => {
    service.getPgnText(5).subscribe();
    const req = httpMock.expectOne('/api/repertoires/5/pgn');
    expect(req.request.responseType).toBe('text');
    req.flush('1. e4 *');
  });

  it('uploadFile POSTs FormData to the files route', () => {
    const form = new FormData();
    form.append('file', new Blob(['x']), 'a.pgn');
    service.uploadFile(5, form).subscribe();
    const req = httpMock.expectOne('/api/repertoires/5/files');
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBeTrue();
    req.flush({});
  });

  it('downloadFile requests a blob; deleteFile DELETEs the file route', () => {
    service.downloadFile(5, 9).subscribe();
    const dl = httpMock.expectOne('/api/repertoires/5/files/9');
    expect(dl.request.responseType).toBe('blob');
    dl.flush(new Blob(['x']));

    service.deleteFile(5, 9).subscribe();
    const del = httpMock.expectOne('/api/repertoires/5/files/9');
    expect(del.request.method).toBe('DELETE');
    del.flush({});
  });

  it('findSimilarPositions POSTs the documented request body (move + onlyWithMove, NO minScore)', () => {
    service.findSimilarPositions({
      fen: '8/8/8/8/8/8/8/8 w - - 0 1', repertoireIds: [7, 9],
      preset: 'stellungsbild', includeMirrored: false, sameSideToMove: true, limit: 40,
      move: { from: 'c3', to: 'd5' }, onlyWithMove: true,
    }).subscribe();
    const req = httpMock.expectOne('/api/repertoires/similar-positions');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      fen: '8/8/8/8/8/8/8/8 w - - 0 1', repertoireIds: [7, 9],
      preset: 'stellungsbild', includeMirrored: false, sameSideToMove: true, limit: 40,
      move: { from: 'c3', to: 'd5' }, onlyWithMove: true,
    });
    // Die Schwelle gehört dem Server — schickte das Frontend eine mit, drifteten die Defaults.
    expect('minScore' in (req.request.body as object)).toBeFalse();
    req.flush({ matches: [] });
  });

  it('findSimilarPositions accepts the breakdown flat OR nested', () => {
    let flat: any, nested: any;
    service.findSimilarPositions({ fen: 'x', repertoireIds: [], preset: 'ausgewogen', includeMirrored: true, sameSideToMove: false, limit: 40 })
      .subscribe(r => flat = r.matches[0]);
    httpMock.expectOne('/api/repertoires/similar-positions').flush({
      matches: [{
        repertoireId: 7, repertoireName: 'R', chapter: 'C', lineName: 'L', gameIndex: 1, ply: 6,
        fen: 'f', score: 88, mirrored: true, pawnScore: 90, materialScore: 80, pieceScore: 70, kingScore: 60,
      }],
    });
    expect(flat.pawnScore).toBe(90);
    expect(flat.kingScore).toBe(60);
    expect(flat.mirrored).toBeTrue();

    service.findSimilarPositions({ fen: 'x', repertoireIds: [], preset: 'ausgewogen', includeMirrored: true, sameSideToMove: false, limit: 40 })
      .subscribe(r => nested = r.matches[0]);
    httpMock.expectOne('/api/repertoires/similar-positions').flush({
      matches: [{
        repertoireId: 7, repertoireName: 'R', gameIndex: 1, ply: 6, score: 88,
        breakdown: { pawns: 90, material: 80, pieces: 70, king: 60 },
      }],
    });
    expect(nested.pawnScore).toBe(90);
    expect(nested.materialScore).toBe(80);
    expect(nested.pieceScore).toBe(70);
    expect(nested.kingScore).toBe(60);
    expect(nested.chapter).toBe('');     // fehlende Felder werden nie undefined
    expect(nested.mirrored).toBeFalse();
    // Ohne Zug-Angabe bleibt der Endwert der Stellungswert — die alte Antwortform ändert nichts.
    expect(nested.score).toBe(88);
    expect(nested.positionScore).toBe(88);
    expect(nested.moveMatch).toBeNull();
    expect(nested.moveSan).toBe('');
  });

  it('findSimilarPositions accepts the continuation flat OR nested and never double-counts the bonus', () => {
    let flat: any, nested: any;
    service.findSimilarPositions({ fen: 'x', repertoireIds: [], preset: 'ausgewogen', includeMirrored: true, sameSideToMove: false, limit: 40 })
      .subscribe(r => flat = r.matches[0]);
    // Server MIT eigener Verrechnung: er nennt beide Zahlen, `score` IST der Endwert.
    httpMock.expectOne('/api/repertoires/similar-positions').flush({
      matches: [{
        repertoireId: 7, gameIndex: 1, ply: 6, score: 82, positionScore: 64,
        moveSan: 'Nd5', moveFrom: 'c3', moveTo: 'd5', moveMatch: 'exact',
      }],
    });
    expect(flat.score).toBe(82);          // NICHT noch einmal 0,5 · (100-82) draufgerechnet
    expect(flat.positionScore).toBe(64);
    expect(flat.moveMatch).toBe('exact');
    expect(flat.moveFrom).toBe('c3');

    service.findSimilarPositions({ fen: 'x', repertoireIds: [], preset: 'ausgewogen', includeMirrored: true, sameSideToMove: false, limit: 40 })
      .subscribe(r => nested = r.matches[0]);
    // Server OHNE eigene Verrechnung (nur Stellungswert + Stufe, verschachtelte Zugform):
    // der Lücken-Schluss wird hier nachgeholt — 60 + 0,25 · 40 = 70.
    httpMock.expectOne('/api/repertoires/similar-positions').flush({
      matches: [{
        repertoireId: 7, gameIndex: 1, ply: 6, score: 60,
        move: { san: 'Nd5', from: 'f4', to: 'd5', match: 'same_target' },
      }],
    });
    expect(nested.positionScore).toBe(60);
    expect(nested.score).toBe(70);
    expect(nested.moveMatch).toBe('sameTarget');
    expect(nested.moveSan).toBe('Nd5');
    expect(nested.moveTo).toBe('d5');
  });

  it('applyMoveBonus closes the gap, never exceeds 100 and leaves a hit-less match alone', () => {
    expect(applyMoveBonus(60, 'exact')).toBe(80);         // 60 + 0,5 · 40
    expect(applyMoveBonus(60, 'sameTarget')).toBe(70);    // 60 + 0,25 · 40
    expect(applyMoveBonus(88, 'exact')).toBe(94);
    expect(applyMoveBonus(88, 'sameTarget')).toBe(91);
    expect(applyMoveBonus(100, 'exact')).toBe(100);       // kann nie über 100 laufen
    expect(applyMoveBonus(0, 'exact')).toBe(50);
    expect(applyMoveBonus(73, null)).toBe(73);
    expect(applyMoveBonus(NaN, 'exact')).toBe(0);
    // Ordnungstreue innerhalb einer Stufe: der bessere Stellungswert bleibt vorn.
    expect(applyMoveBonus(70, 'exact')).toBeGreaterThan(applyMoveBonus(60, 'exact'));
  });

  it('an unknown match level is read as "no move hit" instead of blowing up the list', () => {
    let m: any;
    service.findSimilarPositions({ fen: 'x', repertoireIds: [], preset: 'ausgewogen', includeMirrored: true, sameSideToMove: false, limit: 40 })
      .subscribe(r => m = r.matches[0]);
    httpMock.expectOne('/api/repertoires/similar-positions').flush({
      matches: [{ repertoireId: 7, score: 64, moveMatch: 'legal' }],
    });
    expect(m.moveMatch).toBeNull();
    expect(m.score).toBe(64);
  });
});
