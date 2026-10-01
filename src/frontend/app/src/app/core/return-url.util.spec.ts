import { authLinkQuery, sanitizeReturnUrl } from './return-url.util';

describe('sanitizeReturnUrl', () => {
  it('lässt app-interne Pfade durch, alles andere wird zum Rückfall', () => {
    expect(sanitizeReturnUrl('/courses/5')).toBe('/courses/5');
    expect(sanitizeReturnUrl('//evil.example')).toBe('/dashboard');
    expect(sanitizeReturnUrl('https://evil.example', '/')).toBe('/');
  });
});

/** UX-020: „Anmelden“/„Registrieren“ in der Kopfzeile behalten das Ziel. */
describe('authLinkQuery', () => {
  it('nimmt die aktuelle Seite als Ziel (samt Query)', () => {
    expect(authLinkQuery('/friends/7/revenge')).toEqual({ returnUrl: '/friends/7/revenge' });
    expect(authLinkQuery('/t/42?tab=2#x')).toEqual({ returnUrl: '/t/42?tab=2' });
  });

  it('auf der Maske selbst: deren Ziel weiterreichen, nicht die Maske', () => {
    expect(authLinkQuery('/login?returnUrl=%2Ffriends%2F7%2Frevenge&authRequired=1'))
      .toEqual({ returnUrl: '/friends/7/revenge' });
    expect(authLinkQuery('/register?returnUrl=%2Fverein%2Fneu', '/')).toEqual({ returnUrl: '/verein/neu' });
    expect(authLinkQuery('/forgot-password')).toEqual({});
    expect(authLinkQuery('/reset-password?token=abc', '/')).toEqual({ returnUrl: '/' });
  });

  it('ohne brauchbares Ziel: der Rückfall der Oberfläche, sonst keiner (die Maske nimmt ihren eigenen)', () => {
    expect(authLinkQuery('/login')).toEqual({});
    expect(authLinkQuery('/login', '/')).toEqual({ returnUrl: '/' });
    expect(authLinkQuery('/login?returnUrl=%2F%2Fevil.example')).toEqual({});
    expect(authLinkQuery('/analysis?pgn=https://evil.example', '/')).toEqual({ returnUrl: '/' });
  });
});
