import { HttpErrorResponse } from '@angular/common/http';
import { CHESSBASE_UPLOAD_EXTENSIONS, chessBaseErrorText, chessBaseNote, chessBaseSelection, packForUpload } from './chessbase-upload';
import { ChessBaseResult } from './club.models';

const file = (name: string, text = 'x') => new File([text], name);
const RESULT: ChessBaseResult = { format: '2cbh', name: 'MeineSpiele', pgn: '', games: 80, converted: 78, deleted: 1, truncated: false,
  skippedCount: 2, skipped: [{ id: 12, white: 'A', black: 'B', reason: 'Chess960 wird nicht gelesen.' }, { id: 40, white: 'C', black: 'D', reason: 'x' }] };

describe('chessbase-upload', () => {
  it('lädt dieselben Endungen hoch, die der Server liest (SPIEGEL von ChessBaseFiles.Upload)', () => {
    expect(CHESSBASE_UPLOAD_EXTENSIONS).toEqual(['.cbh', '.cbg', '.cbp', '.cbt', '.cbc', '.2cbh', '.2cbg', '.2lid']);
  });

  it('aus dem ganzen Ordner gehen nur die nötigen Dateien — ohne Kopf ist es keine Datenbank', () => {
    const all = ['World-ch.cbh', 'World-ch.CBG', 'World-ch.cbp', 'World-ch.cbt', 'World-ch.cbc', 'World-ch.cba', 'World-ch.cbb', 'World-ch.ini'].map(n => file(n));
    const r = chessBaseSelection(all);
    expect(r.send.map(f => f.name)).toEqual(['World-ch.cbh', 'World-ch.CBG', 'World-ch.cbp', 'World-ch.cbt', 'World-ch.cbc']);
    expect(r.hasDatabase).toBeTrue();
    expect(chessBaseSelection([file('a.2cbg'), file('a.2lid')]).hasDatabase).toBeFalse();
    expect(chessBaseSelection([file('db.zip')]).hasDatabase).toBeTrue();
    expect(chessBaseSelection([file('partien.pgn')]).send).toEqual([]);
  });

  it('packt jede Datei einzeln als .gz — ein ZIP bleibt, wie es ist', async () => {
    const packed = await packForUpload(file('a.2cbg', 'abc'.repeat(1000)));
    expect(packed.name).toBe('a.2cbg.gz');
    expect(packed.blob.size).toBeLessThan(3000);
    const back = await new Response(packed.blob.stream().pipeThrough(new DecompressionStream('gzip'))).text();
    expect(back).toBe('abc'.repeat(1000));
    const zip = file('db.zip');
    expect((await packForUpload(zip)).blob).toBe(zip);
  });

  it('der Hinweis nennt Gelesenes und Übersprungenes (mit Nummer)', () => {
    expect(chessBaseNote(RESULT)).toBe('MeineSpiele: 78 Partien gelesen, 2 übersprungen (#12 A – B: Chess960 wird nicht gelesen.; #40 C – D: x).');
    expect(chessBaseNote({ ...RESULT, skippedCount: 0, skipped: [], converted: 1100 })).toBe('MeineSpiele: 1.100 Partien gelesen.');
    expect(chessBaseNote({ ...RESULT, truncated: true, games: 5000 })).toContain('gelesen wurden die ersten 5.000 Partien');
  });

  it('jede Absage des Servers hat einen Satz', () => {
    const err = (reason: string, status = 400) => new HttpErrorResponse({ status, error: { reason, message: 'MeineSpiele.2lid fehlt.' } });
    for (const r of ['noFile', 'noDatabase', 'multipleDatabases', 'tooLarge', 'invalidZip', 'invalidFile', 'unreadable', 'busy']) {
      expect(chessBaseErrorText(err(r))).not.toBe('Die Datenbank ließ sich nicht hochladen.');
    }
    expect(chessBaseErrorText(err('missingFile'))).toBe('MeineSpiele.2lid fehlt.');
    expect(chessBaseErrorText(new HttpErrorResponse({ status: 413 }))).toContain('zu groß');
    expect(chessBaseErrorText(new HttpErrorResponse({ status: 404 }))).toContain('abgelaufen');
  });
});
