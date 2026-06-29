import {
  ChessableComponent,
  buildChessableBookmarklet, parseChessbearerFragment,
  effectiveTotalLines, estimateRemainingMinutes, CHESSABLE_LINES_PER_MIN, formatDuration,
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

describe('effectiveTotalLines', () => {
  it('prefers the exact linesTotal when known', () => {
    // Exakter Wert (aus includeVariations) schlägt die Hochrechnung.
    expect(effectiveTotalLines(100, 2, 10, 333)).toBe(333);
    expect(effectiveTotalLines(0, 0, 0, 299)).toBe(299); // sofort bekannt, noch nichts geholt
  });

  it('extrapolates total lines linearly when the exact total is not yet known', () => {
    // 100 Zeilen in 2 von 10 Kapiteln → ~500 gesamt.
    expect(effectiveTotalLines(100, 2, 10)).toBe(500);
    expect(effectiveTotalLines(60, 3, 6)).toBe(120);
  });

  it('returns 0 when neither exact nor estimable', () => {
    expect(effectiveTotalLines(0, 1, 10)).toBe(0);
    expect(effectiveTotalLines(50, 0, 10)).toBe(0);
    expect(effectiveTotalLines(50, 2, 0)).toBe(0);
  });
});

describe('estimateRemainingMinutes', () => {
  it('uses the exact total when known', () => {
    // 299 gesamt, 99 geholt → 200 verbleibend / Durchsatz.
    expect(estimateRemainingMinutes(99, 1, 17, 299)).toBe(Math.ceil(200 / CHESSABLE_LINES_PER_MIN));
  });

  it('falls back to extrapolation when the exact total is unknown', () => {
    expect(estimateRemainingMinutes(100, 2, 10)).toBe(Math.ceil(400 / CHESSABLE_LINES_PER_MIN));
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

describe('ChessableComponent active-import label caching', () => {
  function make(): { c: any; instantCalls: () => number } {
    let calls = 0;
    const translate = { instant: (k: string, _p?: unknown) => { calls++; return k; } };
    // Nur translate wird hier ausgeübt; restliche Deps als leere Stubs.
    const c: any = new ChessableComponent(
      {} as any, {} as any, translate as any, {} as any, {} as any, {} as any, {} as any);
    return { c, instantCalls: () => calls };
  }

  const imp = {
    id: 1, bid: 'b1', courseName: 'Course', target: 'book', status: 'running', phase: 'queued',
    error: null, resultId: null, imported: 0, skipped: 0, invalid: 0,
    chaptersDone: 0, chaptersTotal: 0, linesDone: 0, queuedAhead: 2,
    createdAt: '2026-06-24T00:00:00Z', startedAt: null, completedAt: null,
  } as any;

  it('precomputes queueLabelText once on update (not per change-detection read)', () => {
    const { c, instantCalls } = make();
    c.applyUpdate(imp);

    const entry = c.activeImports['b1'];
    expect(typeof entry.queueLabelText).toBe('string');
    expect(entry.queueLabelText.length).toBeGreaterThan(0);

    const afterUpdate = instantCalls();
    // Wiederholtes Lesen des Templates greift nur auf das Feld zu → KEINE weiteren translate.instant-Aufrufe.
    void entry.queueLabelText;
    void entry.queueLabelText;
    expect(instantCalls()).toBe(afterUpdate);
  });

  it('refreshes the cached label when the import is updated again', () => {
    const { c } = make();
    c.applyUpdate(imp);
    const first = c.activeImports['b1'].queueLabelText;
    c.applyUpdate({ ...imp, status: 'paused' });
    const second = c.activeImports['b1'].queueLabelText;
    // Anderer Status → anderer Label-Key (paused vs. queued).
    expect(second).not.toBe(first);
  });
});
