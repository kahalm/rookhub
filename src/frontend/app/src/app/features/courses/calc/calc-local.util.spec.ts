import {
  CALC_LOCAL_MAX_POSITIONS, CALC_LOCAL_MAX_TREE_CHARS, CALC_LOCAL_PREFIX,
  clearCalcLocal, deleteCalcLocalTree, readCalcLocal, readCalcLocalEntry, readCalcLocalReview,
  writeCalcLocalReview, writeCalcLocalTree,
} from './calc-local.util';

const BOOK = 4242;
const KEY = `${CALC_LOCAL_PREFIX}${BOOK}`;

describe('calc-local.util', () => {
  beforeEach(() => clearCalcLocal(BOOK));
  afterEach(() => clearCalcLocal(BOOK));

  it('legt einen Baum ab und findet ihn wieder', () => {
    const at = writeCalcLocalTree(BOOK, 7, '{"a":1}');
    expect(at).not.toBeNull();
    const entry = readCalcLocalEntry(BOOK, 7)!;
    expect(entry.tree).toBe('{"a":1}');
    expect(entry.updatedAt).toBe(at);
  });

  it('führt Festlegung/Zeit/Stufe wie der Server: Zeit ADDIERT, fehlende Felder bleiben', () => {
    writeCalcLocalReview(BOOK, 7, { chosenSan: 'Sf3', chosenUci: 'g1f3' });
    writeCalcLocalReview(BOOK, 7, { secondsDelta: 30 });
    // Nicht-null = tatsächlich geschrieben (der Rückgabewert IST die Erfolgsmeldung).
    const after = writeCalcLocalReview(BOOK, 7, { secondsDelta: 12, grade: 3 })!;

    expect(after).toEqual({ chosenSan: 'Sf3', chosenUci: 'g1f3', secondsSpent: 42, grade: 3 });
    expect(readCalcLocalReview(BOOK, 7)).toEqual(after);
  });

  it('behält beim Löschen des Baums die Trainings-Werte — und räumt die leere Zeile ganz weg', () => {
    writeCalcLocalTree(BOOK, 7, '{"a":1}');
    writeCalcLocalReview(BOOK, 7, { grade: 2 });
    deleteCalcLocalTree(BOOK, 7);
    expect(readCalcLocalEntry(BOOK, 7)!.tree).toBeNull();
    expect(readCalcLocalEntry(BOOK, 7)!.grade).toBe(2);

    writeCalcLocalReview(BOOK, 8, { grade: null });
    writeCalcLocalTree(BOOK, 8, '{"a":1}');
    deleteCalcLocalTree(BOOK, 8);
    expect(readCalcLocalEntry(BOOK, 8)).toBeNull();
  });

  it('weist einen zu großen Baum ab, statt den Speicher zu sprengen', () => {
    const huge = 'x'.repeat(CALC_LOCAL_MAX_TREE_CHARS + 1);
    expect(writeCalcLocalTree(BOOK, 7, huge)).toBeNull();
    expect(readCalcLocalEntry(BOOK, 7)).toBeNull();
  });

  it('deckelt die Zahl der Stellungen und verdrängt die am längsten nicht angefasste', () => {
    // Ältester Eintrag zuerst; `touchedAt` kommt aus Date.now(), deshalb hier fest gesetzt.
    for (let i = 1; i <= CALC_LOCAL_MAX_POSITIONS; i++) writeCalcLocalTree(BOOK, i, '{"a":1}');
    const store = JSON.parse(localStorage.getItem(KEY)!);
    for (const id of Object.keys(store.entries)) store.entries[id].touchedAt = Number(id);
    localStorage.setItem(KEY, JSON.stringify(store));

    writeCalcLocalTree(BOOK, 9999, '{"a":1}');

    const entries = readCalcLocal(BOOK);
    expect(Object.keys(entries).length).toBe(CALC_LOCAL_MAX_POSITIONS);
    expect(entries['9999']).toBeDefined();     // die gerade bearbeitete bleibt immer
    expect(entries['1']).toBeUndefined();      // die älteste fliegt
    expect(entries[String(CALC_LOCAL_MAX_POSITIONS)]).toBeDefined();
  });

  it('übersteht kaputten Speicherinhalt, ohne zu werfen', () => {
    localStorage.setItem(KEY, 'kein JSON');
    expect(readCalcLocal(BOOK)).toEqual({});
    expect(readCalcLocalEntry(BOOK, 7)).toBeNull();
    expect(readCalcLocalReview(BOOK, 7)).toEqual({ chosenSan: null, chosenUci: null, secondsSpent: 0, grade: null });
    // Und lässt sich danach normal weiterbenutzen.
    expect(writeCalcLocalTree(BOOK, 7, '{"a":1}')).not.toBeNull();
  });

  it('wirft Müll-Einträge still raus (fremde Schlüssel, falsche Typen)', () => {
    localStorage.setItem(KEY, JSON.stringify({
      v: 1,
      entries: {
        7: { tree: '{"a":1}', secondsSpent: 'viel', grade: 99, chosenSan: 5 },
        abc: { tree: '{"b":2}' },
        9: 'kaputt',
      },
    }));
    const entries = readCalcLocal(BOOK);
    expect(Object.keys(entries)).toEqual(['7']);
    expect(entries['7'].secondsSpent).toBe(0);
    expect(entries['7'].grade).toBe(4);        // außerhalb der Skala → geklemmt, nicht verworfen
    expect(entries['7'].chosenSan).toBeNull();
  });

  it('wirft nicht, wenn der Speicher gesperrt ist (Privatmodus/Quota)', () => {
    const setItem = spyOn(Storage.prototype, 'setItem').and.throwError('QuotaExceededError');
    expect(writeCalcLocalTree(BOOK, 7, '{"a":1}')).toBeNull();
    expect(() => writeCalcLocalReview(BOOK, 7, { grade: 1 })).not.toThrow();
    expect(() => deleteCalcLocalTree(BOOK, 7)).not.toThrow();
    expect(setItem).toHaveBeenCalled();
  });

  it('meldet einen gescheiterten Schreibversuch, statt Erfolg vorzutäuschen', () => {
    // Nicht werfen, aber auch nicht schweigen: wer den gerechneten Stand zurückbekommt, obwohl
    // nichts im Speicher landete, zeigt dem Nutzer „gespeichert" — nach dem Neuladen ist es weg.
    spyOn(Storage.prototype, 'setItem').and.throwError('QuotaExceededError');
    expect(writeCalcLocalReview(BOOK, 7, { chosenSan: 'Sf3', chosenUci: 'g1f3', grade: 3, secondsDelta: 20 }))
      .toBeNull();
    expect(readCalcLocalReview(BOOK, 7)).toEqual({ chosenSan: null, chosenUci: null, secondsSpent: 0, grade: null });
  });

  it('wirft nicht, wenn schon das LESEN gesperrt ist', () => {
    spyOn(Storage.prototype, 'getItem').and.throwError('SecurityError');
    expect(readCalcLocal(BOOK)).toEqual({});
  });
});
