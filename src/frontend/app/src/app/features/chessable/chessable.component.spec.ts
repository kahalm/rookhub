import {
  buildChessableBookmarklet, parseChessbearerFragment,
  estimateTotalLines, estimateRemainingMinutes, CHESSABLE_LINES_PER_MIN, formatDuration,
} from './chessable.component';

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

describe('estimateTotalLines', () => {
  it('extrapolates total lines linearly from chapter progress', () => {
    // 100 Zeilen in 2 von 10 Kapiteln → ~500 gesamt.
    expect(estimateTotalLines(100, 2, 10)).toBe(500);
    expect(estimateTotalLines(60, 3, 6)).toBe(120);
  });

  it('returns 0 when not yet estimable', () => {
    expect(estimateTotalLines(0, 1, 10)).toBe(0);
    expect(estimateTotalLines(50, 0, 10)).toBe(0);
    expect(estimateTotalLines(50, 2, 0)).toBe(0);
  });
});

describe('estimateRemainingMinutes', () => {
  it('estimates remaining minutes from extrapolated lines and the throughput', () => {
    // 100 in 2/10 → ~500 gesamt, 400 verbleibend, /16,7 ≈ 24 min.
    expect(estimateRemainingMinutes(100, 2, 10)).toBe(Math.ceil(400 / CHESSABLE_LINES_PER_MIN));
    // Faustregel-Check: 500 Zeilen frisch → ca. 30 min.
    expect(estimateRemainingMinutes(50, 1, 10)).toBe(Math.ceil(450 / CHESSABLE_LINES_PER_MIN));
  });

  it('is 0 when already done or not estimable', () => {
    expect(estimateRemainingMinutes(100, 10, 10)).toBe(0); // alle Kapitel durch → 0 verbleibend
    expect(estimateRemainingMinutes(0, 0, 0)).toBe(0);
  });
});

describe('formatDuration', () => {
  it('formats ms compactly as h/min/s', () => {
    expect(formatDuration(0)).toBe('0 s');
    expect(formatDuration(45_000)).toBe('45 s');
    expect(formatDuration(90_000)).toBe('1 min');
    expect(formatDuration(3_661_000)).toBe('1 h 1 min');
  });

  it('returns a dash for invalid/negative input', () => {
    expect(formatDuration(-5)).toBe('—');
    expect(formatDuration(NaN)).toBe('—');
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
