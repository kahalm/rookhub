import { ANON_PUZZLE_SESSION_KEY, getOrCreateAnonSessionId, newAnonSessionId, readAnonSessionId } from './anon-session';

/**
 * Die Kennung muss durch `ValidationConstants.SessionIdPattern` des Servers passen (Hex +
 * Bindestrich, 32–36 Zeichen) — SONST antwortet jeder anonyme Endpunkt mit 400. Genau daran lief
 * die frühere Rückfallebene auf, die `s-<zeit>-<zufall>` vergab.
 */
const SERVER_PATTERN = /^[a-fA-F0-9-]{32,36}$/;

describe('anon-session', () => {
  it('vergibt eine Kennung im Muster des Servers', () => {
    expect(newAnonSessionId()).toMatch(SERVER_PATTERN);
  });

  it('erfüllt das Muster auch OHNE crypto.randomUUID (HTTP-Dev-Stack)', () => {
    const real = (crypto as { randomUUID?: () => string }).randomUUID;
    try {
      (crypto as { randomUUID?: () => string }).randomUUID = undefined;
      expect(newAnonSessionId()).toMatch(SERVER_PATTERN);
    } finally {
      (crypto as { randomUUID?: () => string }).randomUUID = real;
    }
  });

  it('erfüllt das Muster auch ohne getRandomValues (letzte Rückfallebene)', () => {
    const realUuid = (crypto as { randomUUID?: () => string }).randomUUID;
    const realRnd = (crypto as { getRandomValues?: unknown }).getRandomValues;
    try {
      (crypto as { randomUUID?: () => string }).randomUUID = undefined;
      (crypto as { getRandomValues?: unknown }).getRandomValues = undefined;
      expect(newAnonSessionId()).toMatch(SERVER_PATTERN);
    } finally {
      (crypto as { randomUUID?: () => string }).randomUUID = realUuid;
      (crypto as { getRandomValues?: unknown }).getRandomValues = realRnd;
    }
  });

  it('bleibt stabil, auch wenn der Speicher gesperrt ist', () => {
    // spyOn(Storage.prototype) statt direkter Zuweisung: eine eigene Property am localStorage
    // würde die Prototyp-Spies ANDERER Suiten verdecken.
    spyOn(Storage.prototype, 'getItem').and.throwError('SecurityError');
    spyOn(Storage.prototype, 'setItem').and.throwError('SecurityError');
    const first = getOrCreateAnonSessionId('rookhub_spec_key');
    expect(first).toMatch(SERVER_PATTERN);
    expect(getOrCreateAnonSessionId('rookhub_spec_key')).toBe(first);
  });

  // F2-018: Lesen ohne Anlegen — für das Übernehmen ins Konto beim Anmelden.
  it('readAnonSessionId legt nichts an und liefert die gespeicherte Kennung', () => {
    const key = 'rookhub_spec_read_key';
    localStorage.removeItem(key);
    try {
      expect(readAnonSessionId(key)).toBeNull();
      expect(localStorage.getItem(key)).toBeNull();
      const id = getOrCreateAnonSessionId(key);
      expect(readAnonSessionId(key)).toBe(id);
    } finally {
      localStorage.removeItem(key);
    }
  });

  it('readAnonSessionId kennt bei gesperrtem Speicher die Kennung der Rückfallebene', () => {
    spyOn(Storage.prototype, 'getItem').and.throwError('SecurityError');
    spyOn(Storage.prototype, 'setItem').and.throwError('SecurityError');
    expect(readAnonSessionId('rookhub_spec_blocked_read')).toBeNull();
    const id = getOrCreateAnonSessionId('rookhub_spec_blocked_read');
    expect(readAnonSessionId('rookhub_spec_blocked_read')).toBe(id);
  });

  it('der Schlüssel der Puzzle-Sitzung bleibt, was im Browser schon liegt', () => {
    expect(ANON_PUZZLE_SESSION_KEY).toBe('rookhub_puzzle_session');
  });
});
