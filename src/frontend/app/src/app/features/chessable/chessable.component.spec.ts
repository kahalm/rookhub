import { buildChessableBookmarklet, parseChessbearerFragment } from './chessable.component';

describe('parseChessbearerFragment', () => {
  it('extracts and url-decodes the bearer from the fragment', () => {
    const jwt = 'eyJabc.def.ghi';
    expect(parseChessbearerFragment('#chessbearer=' + encodeURIComponent(jwt))).toBe(jwt);
  });

  it('finds the param even when not first in the fragment', () => {
    expect(parseChessbearerFragment('#foo=1&chessbearer=tok123')).toBe('tok123');
  });

  it('returns null when no chessbearer is present', () => {
    expect(parseChessbearerFragment('#something=else')).toBeNull();
    expect(parseChessbearerFragment('')).toBeNull();
  });
});

describe('buildChessableBookmarklet', () => {
  const code = buildChessableBookmarklet('https://rookhub.example/chessable', 'no login');

  it('produces a javascript: bookmarklet targeting the given handoff URL via fragment', () => {
    expect(code.startsWith('javascript:')).toBeTrue();
    expect(code).toContain("'https://rookhub.example/chessable#chessbearer='+encodeURIComponent(j)");
  });

  it('scans storages and cookies and validates the JWT payload by user.uid', () => {
    expect(code).toContain('s(localStorage);s(sessionStorage);');
    expect(code).toContain('document.cookie.split');
    expect(code).toContain('x.user&&x.user.uid');
  });

  it('embeds the localized no-login message, escaping single quotes', () => {
    const c = buildChessableBookmarklet('https://x/y', "it's missing");
    expect(c).toContain("alert('it\\'s missing')");
  });
});
