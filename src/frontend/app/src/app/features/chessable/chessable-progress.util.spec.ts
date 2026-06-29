import { TranslateService } from '@ngx-translate/core';
import { ChessableImport } from './chessable.service';
import {
  effectiveTotalLines,
  estimateRemainingMinutes,
  chessableStatusLabel,
  chessableQueueLabel,
  compareImportsByQueue,
} from './chessable-progress.util';

/** Fake TranslateService: gibt Key (+ JSON-Parameter) zurück → Texte gut prüfbar. */
const T = {
  instant: (key: string, params?: any) => params ? `${key}|${JSON.stringify(params)}` : key,
} as unknown as TranslateService;

function imp(over: Partial<ChessableImport>): ChessableImport {
  return {
    id: 1, bid: 'b', courseName: 'C', target: 'repertoire', status: 'running', phase: 'queued',
    error: null, resultId: null, imported: 0, skipped: 0, invalid: 0,
    chaptersDone: 0, chaptersTotal: 0, linesDone: 0, linesTotal: 0, queuedAhead: 0,
    createdAt: '2026-06-29T10:00:00Z', startedAt: null, completedAt: null, ...over,
  };
}

describe('chessableStatusLabel', () => {
  it('renders phase + chapter/total lines + ETA when fetching with a known total', () => {
    const label = chessableStatusLabel(
      imp({ phase: 'fetching', chaptersDone: 7, chaptersTotal: 36, linesDone: 82, linesTotal: 1000 }), T);
    expect(label).toContain('chessable.phase_fetching');
    expect(label).toContain('chessable.fetchProgressTotal|{"ch":7,"total":36,"lines":82,"linesTotal":1000}');
    // ETA = ceil((1000-82)/40) = 23
    expect(label).toContain('chessable.etaRemaining|{"min":23}');
  });

  it('falls back to fetchProgress (no total) when linesTotal is unknown', () => {
    const label = chessableStatusLabel(
      imp({ phase: 'fetching', chaptersDone: 1, chaptersTotal: 4, linesDone: 10, linesTotal: 0 }), T);
    expect(label).toContain('chessable.fetchProgress|{"ch":1,"total":4,"lines":10}');
    expect(label).not.toContain('fetchProgressTotal');
  });

  it('shows just the phase while queued', () => {
    expect(chessableStatusLabel(imp({ phase: 'queued' }), T)).toBe('chessable.phase_queued');
  });
});

describe('chessableQueueLabel', () => {
  it('shows the paused label for paused imports', () => {
    expect(chessableQueueLabel(imp({ status: 'paused', phase: 'fetching' }), T)).toBe('chessable.statusPaused');
  });

  it('shows the 1-based queue position while queued', () => {
    expect(chessableQueueLabel(imp({ phase: 'queued', queuedAhead: 2 }), T)).toBe('chessable.queuePos|{"pos":3}');
  });

  it('delegates to the fetch status label while fetching', () => {
    const label = chessableQueueLabel(imp({ phase: 'fetching', chaptersTotal: 4, chaptersDone: 1, linesDone: 5 }), T);
    expect(label).toContain('chessable.phase_fetching');
  });
});

describe('compareImportsByQueue', () => {
  it('orders by queue position (#) ascending, then by creation time', () => {
    const a = imp({ bid: 'a', queuedAhead: 0, createdAt: '2026-06-29T10:00:00Z' });
    const b = imp({ bid: 'b', queuedAhead: 2, createdAt: '2026-06-29T09:00:00Z' });
    const c = imp({ bid: 'c', queuedAhead: 1, createdAt: '2026-06-29T11:00:00Z' });
    const d = imp({ bid: 'd', queuedAhead: 0, createdAt: '2026-06-29T08:00:00Z' });
    const sorted = [a, b, c, d].sort(compareImportsByQueue).map(i => i.bid);
    // queuedAhead 0 (d älter, dann a), dann 1 (c), dann 2 (b)
    expect(sorted).toEqual(['d', 'a', 'c', 'b']);
  });
});

describe('effectiveTotalLines / estimateRemainingMinutes (re-tested via util)', () => {
  it('prefers the exact total and computes ETA at 40 lines/min', () => {
    expect(effectiveTotalLines(82, 7, 36, 1000)).toBe(1000);
    expect(estimateRemainingMinutes(82, 7, 36, 1000)).toBe(23);
  });
});
