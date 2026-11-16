import {
  activeSegmentAt, attemptOutcome, clipRange, countResults, drillFen, drillItemsOfLesson, drillItemsOfQuestions, drillTarget,
  isTargetMove, isTaskCandidate, normalizeSan, resolveMove, startsGroup, targetSan,
} from './training-drill.util';
import { TrainingLesson, TrainingSegment, TrainingStatus } from './training-videos.service';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const AFTER_E4 = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1';

function seg(p: Partial<TrainingSegment>): TrainingSegment {
  return {
    id: 1, startSec: 0, endSec: null, gameKey: 'g1', fen: START, move: null, moveKind: null, comment: null, quote: null,
    variations: null, tags: [], hasQuestion: false, quoteStartSec: null, quoteEndSec: null, question: null, progress: null, ...p,
  };
}

describe('training-drill.util', () => {
  it('normalizeSan entfernt Wertung, Schach, Zugnummer und schreibt Rochade mit O', () => {
    expect(normalizeSan('12...Nf3+!')).toBe('Nf3');
    expect(normalizeSan('4. e4!?')).toBe('e4');
    expect(normalizeSan('0-0-0')).toBe('O-O-O');
    expect(normalizeSan('Qxf7#')).toBe('Qxf7');
  });

  it('resolveMove findet den legalen Zug tolerant, sonst null', () => {
    expect(resolveMove(START, 'e4!')?.to).toBe('e4');
    expect(resolveMove(START, 'Nf3+')?.from).toBe('g1');
    expect(resolveMove(START, 'e5')).toBeNull();          // nicht legal (Weiß am Zug)
    expect(resolveMove('kaputt', 'e4')).toBeNull();
    expect(resolveMove(null, 'e4')).toBeNull();
  });

  it('Aufgabe = gespielter Zug oder Frage; Ziel = Antwort vor Zug', () => {
    expect(isTaskCandidate(seg({ moveKind: 'played', move: 'e4' }))).toBeTrue();
    expect(isTaskCandidate(seg({ moveKind: 'analysis', move: 'e4' }))).toBeFalse();
    expect(isTaskCandidate(seg({ move: 'e4' }))).toBeFalse();
    const q = seg({ moveKind: 'played', move: 'e4', question: { prompt: 'Was nun?', answer: 'd4', answerLine: null, answerSec: null, explanation: null } });
    expect(isTaskCandidate(q)).toBeTrue();
    expect(targetSan(q)).toBe('d4');
    expect(drillTarget(q)?.to).toBe('d4');
  });

  it('Zug aus der FEN nicht legal → keine Aufgabe (nur ansehen)', () => {
    // FEN zeigt schon die Stellung NACH e4 — „e4“ geht dort nicht
    expect(drillTarget(seg({ fen: AFTER_E4, moveKind: 'played', move: 'e4' }))).toBeNull();
    expect(drillTarget(seg({ fen: AFTER_E4, moveKind: 'played', move: 'e5' }))?.to).toBe('e5');
  });

  it('Server-Aufgabe (taskFen/taskMove) gilt vor der Segment-FEN — auch wenn die FEN schon die Stellung danach zeigt', () => {
    const after = seg({ fen: AFTER_E4, moveKind: 'played', move: 'e4', taskFen: START, taskMove: 'e4' });
    expect(drillTarget(after)?.to).toBe('e4');
    expect(drillFen(after)).toBe(START);
    // Server sagt „keine Aufgabe“ → nur ansehen, Brett zeigt die Segment-FEN
    const none = seg({ fen: AFTER_E4, moveKind: 'played', move: 'e5', taskFen: null, taskMove: null });
    expect(drillTarget(none)).toBeNull();
    expect(drillFen(none)).toBe(AFTER_E4);
  });

  it('attemptOutcome: richtig = solved, drei Fehlversuche = wrong, sonst weiter', () => {
    expect(attemptOutcome(true, 2)).toBe('solved');
    expect(attemptOutcome(false, 1)).toBeNull();
    expect(attemptOutcome(false, 2)).toBeNull();
    expect(attemptOutcome(false, 3)).toBe('wrong');
  });

  it('isTargetMove vergleicht Felder und Umwandlung', () => {
    const t = resolveMove(START, 'e4')!;
    expect(isTargetMove(t, { from: 'e2', to: 'e4' })).toBeTrue();
    expect(isTargetMove(t, { from: 'e2', to: 'e3' })).toBeFalse();
  });

  it('clipRange: Zitat vor Segment, Ende fehlt → 60 s, Ende vor Start → 60 s', () => {
    expect(clipRange({ startSec: 100, endSec: 140, quoteStartSec: 110, quoteEndSec: 120 })).toEqual({ start: 110, end: 120 });
    expect(clipRange({ startSec: 100, endSec: 140, quoteStartSec: null, quoteEndSec: null })).toEqual({ start: 100, end: 140 });
    expect(clipRange({ startSec: 100, endSec: null, quoteStartSec: null, quoteEndSec: null })).toEqual({ start: 100, end: 160 });
    expect(clipRange({ startSec: 100, endSec: 90, quoteStartSec: null, quoteEndSec: null })).toEqual({ start: 100, end: 160 });
  });

  it('activeSegmentAt: zuletzt begonnenes passendes Segment, ohne Ende bis zum nächsten', () => {
    const list = [{ id: 1, startSec: 0, endSec: 100 }, { id: 2, startSec: 50, endSec: 60 }, { id: 3, startSec: 120, endSec: null }, { id: 4, startSec: 200, endSec: null }];
    expect(activeSegmentAt(list, 55)?.id).toBe(2);
    expect(activeSegmentAt(list, 70)?.id).toBe(1);
    expect(activeSegmentAt(list, 110)).toBeNull();
    expect(activeSegmentAt(list, 150)?.id).toBe(3);
    expect(activeSegmentAt(list, 5000)?.id).toBe(4);
  });

  it('drillItemsOfLesson: nur lesbare FEN, nach Zeit, mit Partiename; Gruppenköpfe beim Partiewechsel', () => {
    const lesson: TrainingLesson = {
      id: 7, date: '2026-10-27', summary: null, durationSec: 100, hasVideo: true, ownerName: null,
      games: [{ key: 'g1', kind: 'own', white: 'Anna', black: '?', event: null, year: null, eco: null, result: null, fromSec: null, toSec: null, rookhubGameId: null, matchConfidence: null, hasPgn: false }],
      segments: [
        seg({ id: 3, startSec: 30, gameKey: 'g2' }),
        seg({ id: 1, startSec: 10, moveKind: 'played', move: 'e4' }),
        seg({ id: 2, startSec: 20, fen: null }),
        seg({ id: 4, startSec: 40, fen: 'unsinn' }),
      ],
      pgns: {}, analysisPgns: {},
    };
    const items = drillItemsOfLesson(lesson, 'Unbekannt');
    expect(items.map(i => i.segment.id)).toEqual([1, 3]);
    expect(items[0].gameLabel).toBe('Anna – Unbekannt');
    expect(items[0].target?.to).toBe('e4');
    expect(items[1].target).toBeNull();
    expect(startsGroup(items, 0)).toBeTrue();
    expect(startsGroup(items, 1)).toBeTrue();
  });

  it('drillItemsOfQuestions: Stunden-Reihenfolge des Servers, darin nach Zeit', () => {
    const q = { prompt: 'p', answer: 'e4', answerLine: null, answerSec: null, explanation: null };
    const items = drillItemsOfQuestions([
      { lessonId: 9, date: '2026-11-01', hasVideo: true, gameLabel: 'A – B', rookhubGameId: 121, segment: seg({ id: 11, startSec: 50, question: q }) },
      { lessonId: 9, date: '2026-11-01', hasVideo: true, gameLabel: 'A – B', rookhubGameId: 121, segment: seg({ id: 10, startSec: 5, question: q }) },
      { lessonId: 2, date: '2026-10-01', hasVideo: false, gameLabel: 'C – D', rookhubGameId: null, segment: seg({ id: 1, startSec: 1, question: q }) },
    ]);
    expect(items.map(i => i.segment.id)).toEqual([10, 11, 1]);
    expect(items.map(i => i.rookhubGameId)).toEqual([121, 121, null]);
    expect(startsGroup(items, 1)).toBeFalse();
    expect(startsGroup(items, 2)).toBeTrue();
  });

  it('countResults zählt je Ausgang', () => {
    expect(countResults(new Map<number, TrainingStatus>([[1, 'solved'], [2, 'wrong'], [3, 'solved']]))).toEqual({ solved: 2, wrong: 1, skipped: 0 });
  });
});
