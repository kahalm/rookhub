import { Subject } from 'rxjs';
import { ScoresheetPly, ScoresheetResolveResult } from './scoresheet.service';
import { SheetEditSession } from './sheet-edit-session';
import { fromServer, toServer } from './game-edit.util';

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

  // G3-004: „Stimmt so“ und Kommentar sind während des Neu-Einlesens bedienbar — die Antwort darf sie nicht überschreiben.
  it('während des Neu-Einlesens bestätigt oder kommentiert: bleibt nach der Antwort erhalten (Rest und Vorlauf)', () => {
    const { s, respond } = scanned();
    s.go(2); s.play('Nc3');                     // liest neu …
    expect(s.busy()).toBeTrue();
    s.go(4); s.confirm();                       // 3.Bc4 bestätigt, während die Antwort aussteht
    s.go(5); s.setComment('Läufer nach c5?');
    s.go(1); s.setComment('klassisch');         // Vorlauf: 1…e5 kommentiert
    respond({ plies: [ply(3, 'Nc6', 'b8c6', { uncertain: true }), ply(4, 'Bc4', 'f1c4', { uncertain: true }),
      ply(5, 'Bc5', 'f8c5', { uncertain: true })], unresolved: [], unresolvedFrom: null });
    const p = s.plies();
    expect(p.map(x => x.san)).toEqual(['e4', 'e5', 'Nc3', 'Nc6', 'Bc4', 'Bc5']);
    expect([p[4].confirmed, p[4].uncertain]).toEqual([true, false]);
    expect(p[5].comment).toBe('Läufer nach c5?');
    expect(p[1].comment).toBe('klassisch');
    expect(p[3].confirmed).toBeFalsy();         // nicht angefasst → Lesung des Servers
  });

  it('auch wenn das Neu-Einlesen scheitert, bleibt eine Bestätigung aus der Wartezeit', () => {
    let pending = new Subject<ScoresheetResolveResult>();
    const s = new SheetEditSession({ resolve: () => { pending = new Subject(); return pending; } });
    s.loadSheet({ plies: [ply(0, 'e4', 'e2e4'), ply(1, 'e5', 'e7e5'), ply(2, 'Nf3', 'g1f3'),
      ply(3, 'Nc6', 'b8c6', { uncertain: true })], unresolved: [], unresolvedFrom: null });
    s.go(2); s.play('Nc3');
    s.go(3); s.confirm();
    pending.error(new Error('offline'));
    expect(s.plies()[3].confirmed).toBeTrue();
  });
});

/**
 * „Partie hier beenden“ (Wunsch 2026-11-22, Partien 258/262): auf Formularen stehen nach dem echten Ende oft Kritzeleien
 * oder Analysezüge. `truncate()` streicht den gewählten Halbzug und alle folgenden, ohne den Rest neu zu lesen.
 */
describe('SheetEditSession – Partie hier beenden', () => {
  const ply = (w: number, san: string, uci: string, extra: Partial<ScoresheetPly> = {}): ScoresheetPly =>
    ({ w, written: san, san, uci, match: 'written', uncertain: false, ...extra });

  function scanned() {
    const host = { resolve: jasmine.createSpy('resolve'), changed: jasmine.createSpy('changed') };
    const s = new SheetEditSession(host);
    s.loadSheet({
      plies: [
        ply(0, 'e4', 'e2e4'), ply(1, 'e5', 'e7e5'), ply(2, 'Nf3', 'g1f3', { confirmed: true }),
        ply(3, 'Nc6', 'b8c6'), ply(4, 'Bb5', 'f1b5', { uncertain: true }), ply(5, 'a6', 'a7a6'),
      ],
      unresolved: ['Lxc6', '?'], unresolvedFrom: 6,
      boxes: [[0, 0, 10, 10], [10, 0, 20, 10], [0, 10, 10, 20], [10, 10, 20, 20], [0, 20, 10, 30], [10, 20, 20, 30], [0, 30, 10, 40], [10, 30, 20, 40]],
      written: ['e4', 'e5', 'Sf3', 'Sc6', 'Lb5', 'a6', 'Lxc6', '?'],
    });
    s.photoSize.set({ w: 1000, h: 1000 });
    return { s, host };
  }

  it('ab einem Zug von Weiß: dieser und alle folgenden fallen weg, Bestätigungen davor bleiben', () => {
    const { s, host } = scanned();
    s.go(4);                                                            // 3. Bb5
    expect(s.truncateInfo()).toEqual({ ply: '3.', san: 'Bb5', count: 2 });
    s.truncate();
    expect(s.plies().map(p => p.san)).toEqual(['e4', 'e5', 'Nf3', 'Nc6']);
    expect(s.plies()[2].confirmed).toBeTrue();
    expect(s.cursor()).toBe(4);                                         // am Ende der Partie
    expect(host.resolve).not.toHaveBeenCalled();                        // nichts wird neu gelesen
    expect(host.changed).toHaveBeenCalled();
  });

  it('ab einem Zug von Schwarz; die offenen Einträge dahinter gehören nicht mehr zur Partie', () => {
    const { s } = scanned();
    s.go(3);                                                            // 2… Nc6
    expect(s.truncateInfo()).toEqual({ ply: '2…', san: 'Nc6', count: 3 });
    s.truncate();
    expect(s.plies().map(p => p.san)).toEqual(['e4', 'e5', 'Nf3']);
    expect(s.unresolved()).toEqual([]);
    expect(s.unresolvedFrom()).toBeNull();
    // am Ende kein Ausschnitt eines gestrichenen/offenen Eintrags mehr
    expect(s.crop()).toBeNull();
    expect(s.mark()).toBeNull();
  });

  it('die gespeicherten Halbzüge (plies) werden mitgekürzt, die übrigen behalten Eintrag und Kasten', () => {
    const { s } = scanned();
    s.go(4);
    s.truncate();
    const saved = toServer(s.plies());
    expect(saved.map(p => [p.w, p.san])).toEqual([[0, 'e4'], [1, 'e5'], [2, 'Nf3'], [3, 'Nc6']]);
    s.go(3);
    expect(s.crop()?.written).toBe('Sc6');
    expect(s.mark()).toEqual(jasmine.objectContaining({ left: 1, top: 1, width: 1, height: 1 }));
  });

  it('nicht am ersten Halbzug (die Partie wäre leer) und nicht am Ende der Zugliste', () => {
    const { s } = scanned();
    s.go(0);
    expect(s.truncateInfo()).toBeNull();
    s.truncate();
    expect(s.plies().length).toBe(6);
    s.go(6);
    expect(s.truncateInfo()).toBeNull();
    s.go(5);
    expect(s.truncateInfo()).toEqual({ ply: '3…', san: 'a6', count: 1 });
  });

  it('Rückgängig holt Züge, offene Einträge und Cursor zurück und nennt, was gestrichen wurde', () => {
    const { s } = scanned();
    const before = s.plies();
    s.go(4);
    s.truncate();
    expect(s.undoInfo()).toEqual({ kind: 'truncate', count: 2, ply: '3.' });
    s.undoRemove();
    expect(s.plies()).toBe(before);
    expect(s.unresolved()).toEqual(['Lxc6', '?']);
    expect(s.unresolvedFrom()).toBe(6);
    expect(s.cursor()).toBe(4);
    expect(s.undoInfo()).toBeNull();
  });

  it('auch in einer gewöhnlichen Partie (ohne Formular)', () => {
    const s = new SheetEditSession({ resolve: jasmine.createSpy('resolve') });
    s.plies.set(fromServer([ply(0, 'd4', 'd2d4'), ply(1, 'd5', 'd7d5'), ply(2, 'c4', 'c2c4')]));
    s.go(1);
    s.truncate();
    expect(s.plies().map(p => p.san)).toEqual(['d4']);
  });
});

/**
 * „Partie hier beenden“ beendet auch das FORMULAR (Folgefix 2026-11-22): danach las ein neuer Zug am Ende das Formular ab
 * dem nächsten Eintrag wieder ein, und die gestrichenen Kritzeleien kamen zurück — auch nach dem Neuladen, weil der Server
 * die offenen Einträge nie anpasste. Jetzt gilt `sheetEnd` (gespeichert, beim Laden wieder da).
 */
describe('SheetEditSession – nach „Partie hier beenden“ endet das Formular', () => {
  const ply = (w: number | null, san: string, uci: string, extra: Partial<ScoresheetPly> = {}): ScoresheetPly =>
    ({ w, written: san, san, uci, match: 'written', uncertain: false, ...extra });
  const WRITTEN = ['e4', 'e5', 'Sf3', 'Sc6', 'Lb5', 'a6', 'Lxc6', '?'];

  function scanned(extra: { sheetEnd?: number | null; unresolved?: string[]; unresolvedFrom?: number | null; plies?: number } = {}) {
    let pending = new Subject<ScoresheetResolveResult>();
    const host = {
      resolve: jasmine.createSpy('resolve').and.callFake(() => { pending = new Subject(); return pending; }),
      changed: jasmine.createSpy('changed'),
    };
    const s = new SheetEditSession(host);
    const all = [ply(0, 'e4', 'e2e4'), ply(1, 'e5', 'e7e5'), ply(2, 'Nf3', 'g1f3'), ply(3, 'Nc6', 'b8c6'), ply(4, 'Bb5', 'f1b5'),
      ply(5, 'a6', 'a7a6')];
    s.loadSheet({
      plies: all.slice(0, extra.plies ?? 6),
      unresolved: extra.unresolved ?? ['Lxc6', '?'], unresolvedFrom: extra.unresolvedFrom === undefined ? 6 : extra.unresolvedFrom,
      sheetEnd: extra.sheetEnd ?? null, written: WRITTEN,
    });
    return { s, host, respond: (r: ScoresheetResolveResult) => pending.next(r) };
  }

  it('Kürzen setzt das Ende auf den Eintrag des gestrichenen Halbzugs; ein Zug am Ende kommt ohne Formular-Eintrag', () => {
    const { s, host } = scanned();
    s.go(4);                                                            // 3. Bb5 (Eintrag 4)
    s.truncate();
    expect(s.sheetEnd()).toBe(4);
    expect(s.sheetEndedHere()).toBeTrue();

    s.play('Bc4');                                                      // statt der Kritzelei „Lb5“ ein eigener Zug
    expect(host.resolve).not.toHaveBeenCalled();                        // nichts wird ab Eintrag 5 neu gelesen
    expect(s.plies().map(p => p.san)).toEqual(['e4', 'e5', 'Nf3', 'Nc6', 'Bc4']);
    expect(s.plies()[4]).toEqual(jasmine.objectContaining({ w: null, written: '', match: 'user', confirmed: true }));
    expect(s.unresolved()).toEqual([]);
    expect(s.crop()).toBeNull();                                        // kein Ausschnitt eines gestrichenen Eintrags
    s.play('Nf6');
    expect(s.plies().length).toBe(6);
    expect(s.plies()[5].w).toBeNull();
    expect(toServer(s.plies()).map(p => p.w)).toEqual([0, 1, 2, 3, null, null]);
  });

  it('Rückgängig nach dem Kürzen holt auch das Formular zurück: ein Zug am Ende liest wieder weiter', () => {
    const { s, host } = scanned({ plies: 4, unresolved: [], unresolvedFrom: null });
    s.go(3);
    s.truncate();
    expect(s.sheetEnd()).toBe(3);
    s.undoRemove();
    expect(s.sheetEnd()).toBeNull();
    expect(s.plies().length).toBe(4);
    s.go(4);
    s.play('Bb5');
    expect(host.resolve).toHaveBeenCalledWith(['e4', 'e5', 'Nf3', 'Nc6', 'Bb5'], 5);
  });

  it('nach dem Neuladen (gespeichertes sheetEnd): kein Weiterlesen, keine offenen Einträge ab dem Ende', () => {
    // Ein älterer Stand, in dem der Server die offenen Einträge noch führt — die Sitzung zeigt ab dem Ende keine.
    const { s, host } = scanned({ plies: 4, sheetEnd: 4, unresolved: ['Lb5', 'a6', 'Lxc6', '?'], unresolvedFrom: 4 });
    expect(s.unresolved()).toEqual([]);
    expect(s.unresolvedFrom()).toBeNull();
    expect(s.restInfo()).toBeNull();                                    // nichts mehr anzubieten
    s.go(4);
    expect(s.sheetEndedHere()).toBeTrue();
    s.play('Bc4');
    expect(host.resolve).not.toHaveBeenCalled();
    expect(s.plies()[4].w).toBeNull();
  });

  it('ein Ersetzen in der Mitte liest neu, bringt aber nichts ab dem Ende zurück', () => {
    const { s, host, respond } = scanned({ plies: 4, sheetEnd: 4, unresolved: [], unresolvedFrom: null });
    s.go(3);
    s.play('Nc6');                                                      // „Stimmt so“ per Brett: derselbe Zug
    expect(host.resolve).toHaveBeenCalled();
    // der Server liest das ganze Formular: Lb5, a6 aus Einträgen 4/5 und offen ab 6
    respond({ plies: [], unresolved: [], unresolvedFrom: null });
    s.go(2);
    s.play('Nf3');
    respond({
      plies: [ply(3, 'Nc6', 'b8c6'), ply(null, 'h3', 'h2h3', { match: 'inserted' }), ply(4, 'Bb5', 'f1b5'), ply(5, 'a6', 'a7a6')],
      unresolved: ['Lxc6', '?'], unresolvedFrom: 6,
    });
    expect(s.plies().map(p => p.san)).toEqual(['e4', 'e5', 'Nf3', 'Nc6']);
    expect(s.unresolved()).toEqual([]);
    expect(s.unresolvedFrom()).toBeNull();
  });

  it('offene Einträge VOR dem Ende bleiben (der Server stockt früher), die danach fallen weg', () => {
    const { s, respond } = scanned({ plies: 6, sheetEnd: 7, unresolved: ['Lxc6'], unresolvedFrom: 6 });
    expect(s.unresolved()).toEqual(['Lxc6']);
    s.go(5);
    s.play('a6');
    respond({ plies: [], unresolved: ['Lxc6', '?'], unresolvedFrom: 6 });
    expect(s.unresolved()).toEqual(['Lxc6']);
    expect(s.unresolvedFrom()).toBe(6);
  });

  it('am Ende der Zugliste: „Partie hier beenden“ verwirft den Rest des Formulars, ohne Züge zu streichen', () => {
    const { s, host } = scanned();
    s.go(6);
    expect(s.truncateInfo()).toBeNull();
    expect(s.restInfo()).toEqual({ count: 2 });
    s.endHere();
    expect(s.plies().length).toBe(6);
    expect(s.sheetEnd()).toBe(6);
    expect(s.unresolved()).toEqual([]);
    expect(s.restInfo()).toBeNull();
    expect(s.undoInfo()).toEqual({ kind: 'rest', count: 2, ply: '4.' });
    expect(host.changed).toHaveBeenCalled();
    s.play('Bxc6');
    expect(host.resolve).not.toHaveBeenCalled();
    expect(s.plies()[6].w).toBeNull();
  });

  it('am Ende: Rückgängig holt die offenen Einträge zurück; ohne Rest kein Angebot', () => {
    const { s } = scanned();
    s.go(6);
    s.endHere();
    s.undoRemove();
    expect(s.unresolved()).toEqual(['Lxc6', '?']);
    expect(s.sheetEnd()).toBeNull();
    expect(s.restInfo()).toEqual({ count: 2 });
    // alle Einträge verbraucht — nichts übrig
    const full = new SheetEditSession({ resolve: jasmine.createSpy('resolve') });
    full.loadSheet({ plies: [ply(0, 'e4', 'e2e4'), ply(1, 'e5', 'e7e5')], written: ['e4', 'e5'] });
    full.go(2);
    expect(full.restInfo()).toBeNull();
    // eine gewöhnliche Partie hat kein Formular
    const plain = new SheetEditSession({ resolve: jasmine.createSpy('resolve') });
    plain.plies.set(fromServer([ply(0, 'd4', 'd2d4')]));
    plain.go(1);
    expect(plain.restInfo()).toBeNull();
  });
});
