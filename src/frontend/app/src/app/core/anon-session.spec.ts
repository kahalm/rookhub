import { getOrCreateAnonSessionId, newAnonSessionId } from './anon-session';

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
});
