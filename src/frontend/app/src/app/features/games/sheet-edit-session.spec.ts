import { Subject } from 'rxjs';
import { ScoresheetPly, ScoresheetResolveResult } from './scoresheet.service';
import { SheetEditSession } from './sheet-edit-session';

/**
 * Codereview 2026-09-29, UX-070: „Zug löschen" liest den Rest neu — Folgezüge und ihre Bestätigungen können sich dabei
 * ändern. Die Sitzung merkt sich den Stand davor und holt ihn per `undoRemove()` zurück (RookHub-Korrekturseite und
 * LeagueHub-Formular teilen sie).
 */
describe('SheetEditSession – Zug löschen rückgängig', () => {
  const ply = (w: number, san: string, uci: string, extra: Partial<ScoresheetPly> = {}): ScoresheetPly =>
    ({ w, written: san, san, uci, match: 'written', uncertain: false, ...extra });

  function scanned() {
    let pending = new Subject<ScoresheetResolveResult>();
    const host = {
      resolve: jasmine.createSpy('resolve').and.callFake(() => { pending = new Subject(); return pending; }),
      changed: jasmine.createSpy('changed'),
    };
    const s = new SheetEditSession(host);
    s.loadSheet({
      plies: [
        ply(0, 'e4', 'e2e4'), ply(1, 'e5', 'e7e5'),
        ply(2, 'Nf3', 'g1f3', { match: 'fuzzy', uncertain: true }),
        ply(3, 'Nc6', 'b8c6', { confirmed: true }),
        ply(4, 'Bb5', 'f1b5'),
      ],
      unresolved: [], unresolvedFrom: null,
    });
    return { s, host, respond: (r: ScoresheetResolveResult) => pending.next(r) };
  }

  it('holt nach dem Neulesen Züge, Bestätigungen, offene Einträge und Cursor von vorher zurück', () => {
    const { s, host, respond } = scanned();
    expect(s.cursor()).toBe(2);
    const before = s.plies();

    s.remove();
    expect(host.resolve).toHaveBeenCalled();
    expect(s.canUndoRemove()).toBeTrue();
    // Während der Rest neu gelesen wird, geht es nicht — die Antwort überschriebe den zurückgeholten Stand.
    s.undoRemove();
    expect(s.busy()).toBeTrue();
    expect(s.canUndoRemove()).toBeTrue();

    // Der Server liest den Rest anders: ein Eintrag bleibt offen.
    respond({ plies: [ply(3, 'Nc6', 'b8c6')], unresolved: ['Lb5'], unresolvedFrom: 4 });
    expect(s.plies().map(p => p.san)).toEqual(['e4', 'e5', 'Nc6']);
    expect(s.unresolved()).toEqual(['Lb5']);

    s.undoRemove();
    expect(s.plies()).toBe(before);
    expect(s.plies()[3].confirmed).toBeTrue();
    expect(s.unresolved()).toEqual([]);
    expect(s.unresolvedFrom()).toBeNull();
    expect(s.cursor()).toBe(2);
    expect(s.canUndoRemove()).toBeFalse();
  });

  it('jede andere Änderung nach dem Löschen verwirft das Rückgängig (sonst nähme es sie still mit zurück)', () => {
    const { s, respond } = scanned();
    s.remove();
    respond({ plies: [ply(3, 'Nc6', 'b8c6'), ply(4, 'Bb5', 'f1b5')], unresolved: [] });
    expect(s.canUndoRemove()).toBeTrue();

    s.go(3);
    expect(s.canUndoRemove()).toBeTrue();      // nur geblättert: bleibt
    s.confirm();
    expect(s.canUndoRemove()).toBeFalse();

    s.go(1);
    s.remove();
    respond({ plies: [ply(2, 'Nf3', 'g1f3')], unresolved: [] });
    expect(s.canUndoRemove()).toBeTrue();
    s.setComment('x');
    expect(s.canUndoRemove()).toBeFalse();
  });

  it('auch in einer gewöhnlichen Partie (ohne Neulesen)', () => {
    const s = new SheetEditSession({ resolve: () => { throw new Error('nicht erwartet'); } });
    s.plies.set(['e4', 'e5', 'Nf3'].map((san, i) => ({
      san, uci: ['e2e4', 'e7e5', 'g1f3'][i], w: null, written: '', match: 'user', uncertain: false, confirmed: false,
      options: null, comment: null, illegal: false,
    })));
    s.go(1);
    s.remove();
    expect(s.plies().map(p => p.san)).toEqual(['e4', 'Nf3']);
    s.undoRemove();
    expect(s.plies().map(p => p.san)).toEqual(['e4', 'e5', 'Nf3']);
    expect(s.cursor()).toBe(1);
  });
});

/** Gemeldet 2026-11-08: „wenn ich in der Mitte einen Zug anpasse, sind alle späteren Züge wieder unsicher". */
describe('SheetEditSession – Änderung in der Mitte behält die Entscheidungen danach', () => {
  const ply = (w: number, san: string, uci: string, extra: Partial<ScoresheetPly> = {}): ScoresheetPly =>
    ({ w, written: san, san, uci, match: 'written', uncertain: false, ...extra });

  function scanned() {
    let pending = new Subject<ScoresheetResolveResult>();
    const host = { resolve: jasmine.createSpy('resolve').and.callFake(() => { pending = new Subject(); return pending; }) };
    const s = new SheetEditSession(host);
    // 1.e4 e5 2.Nf3 Nc6 3.Bc4 Bc5 — 2…Nc6 und 3.Bc4 unsicher gelesen
    s.loadSheet({
      plies: [
        ply(0, 'e4', 'e2e4'), ply(1, 'e5', 'e7e5'), ply(2, 'Nf3', 'g1f3'),
        ply(3, 'Nc6', 'b8c6', { uncertain: true }), ply(4, 'Bc4', 'f1c4', { uncertain: true }), ply(5, 'Bc5', 'f8c5', { uncertain: true }),
      ],
      unresolved: [], unresolvedFrom: null,
    });
    return { s, respond: (r: ScoresheetResolveResult) => pending.next(r) };
  }

  it('bestätigte Züge danach bleiben bestätigt, wenn der Server sie wieder so liest', () => {
    const { s, respond } = scanned();
    s.go(3); s.confirm();                       // 2…Nc6 bestätigt
    s.go(4); s.confirm();                       // 3.Bc4 bestätigt
    s.go(2); s.play('Nc3');                     // jetzt 2.Nf3 → 2.Nc3 geändert
    // Der Server liest den Rest neu, ohne Bestätigungen und wieder als unsicher
    respond({ plies: [ply(3, 'Nc6', 'b8c6', { uncertain: true }), ply(4, 'Bc4', 'f1c4', { uncertain: true }),
      ply(5, 'Bc5', 'f8c5', { uncertain: true })], unresolved: [], unresolvedFrom: null });
    const p = s.plies();
    expect(p.map(x => x.san)).toEqual(['e4', 'e5', 'Nc3', 'Nc6', 'Bc4', 'Bc5']);
    expect([p[3].confirmed, p[4].confirmed, p[5].confirmed]).toEqual([true, true, false]);
    expect(s.uncertainLeft()).toBe(1);          // nur 3…Bc5, das nie bestätigt war
  });

  it('ein später von Hand gesetzter Zug bleibt, solange er in der neuen Stellung legal ist', () => {
    const { s, respond } = scanned();
    s.go(5); s.play('Nf6');                     // 3…Bc5 → 3…Nf6 von Hand
    respond({ plies: [], unresolved: [], unresolvedFrom: null });
    s.go(2); s.play('Nc3');
    respond({ plies: [ply(3, 'Nc6', 'b8c6'), ply(4, 'Bc4', 'f1c4'), ply(5, 'Bc5', 'f8c5', { uncertain: true })],
      unresolved: [], unresolvedFrom: null });
    expect(s.plies().map(x => x.san)).toEqual(['e4', 'e5', 'Nc3', 'Nc6', 'Bc4', 'Nf6']);
    expect(s.plies()[5].match).toBe('user');
  });
});
