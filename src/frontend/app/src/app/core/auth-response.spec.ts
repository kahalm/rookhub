import { isTokenExpired, jwtPayload } from './auth-response';

function jwt(payload: object): string {
  return `h.${btoa(JSON.stringify(payload)).replace(/\+/g, '-').replace(/\//g, '_')}.s`;
}

describe('auth-response (JWT-Hilfen, Zerlegung Z34)', () => {
  it('jwtPayload liest den Payload, auch in base64url', () => {
    expect(jwtPayload(jwt({ perm: ['a'], sub: '?>?' }))['perm']).toEqual(['a']);
  });

  it('jwtPayload wirft bei einem unlesbaren Token', () => {
    expect(() => jwtPayload('kaputt')).toThrow();
  });

  it('isTokenExpired: abgelaufen, gültig, ohne exp, unlesbar', () => {
    const now = Math.floor(Date.now() / 1000);
    expect(isTokenExpired(jwt({ exp: now - 5 }))).toBeTrue();
    expect(isTokenExpired(jwt({ exp: now + 3600 }))).toBeFalse();
    expect(isTokenExpired(jwt({}))).toBeFalse();
    expect(isTokenExpired('kaputt')).toBeTrue();
  });
});
