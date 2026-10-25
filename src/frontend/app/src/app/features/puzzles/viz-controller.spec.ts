import { Key } from 'chessground/types';
import { ManualClock } from '../../testing/manual-clock';
import { opponentArrowModeFor, VizController } from './viz-controller';

describe('VizController', () => {
  const E4: [Key, Key] = ['e2' as Key, 'e4' as Key];
  const make = () => {
    const clock = new ManualClock();
    const calls = { hide: 0, unhide: 0 };
    const v = new VizController(() => calls.hide++, clock, () => calls.unhide++);
    return { v, clock, calls };
  };

  it('verdeckt nach drei Sekunden und deckt bei endHide wieder auf', () => {
    const { v, clock, calls } = make();
    v.startCountdown();
    expect(v.countdownSeconds).toBe(3);
    clock.advance(2);
    expect(v.piecesHidden).toBeFalse();
    clock.advance();
    expect(v.piecesHidden).toBeTrue();
    expect(calls.hide).toBe(1);
    expect(v.countdownSeconds).toBe(0);
    v.toggleShow();
    v.endHide();
    expect(v.piecesHidden).toBeFalse();
    expect(v.showPressed).toBeFalse();
    expect(calls.unhide).toBe(1);
  });

  it('„Anzeigen" ist ein Umschalter und zählt nur das Aufdecken', () => {
    const { v } = make();
    v.toggleShow();
    v.toggleShow();
    v.toggleShow();
    expect(v.showPressed).toBeTrue();
    expect(v.showCount).toBe(2);
  });

  it('Pfeil: timed verschwindet nach Ablauf, persist bleibt, off zeigt nichts', () => {
    const { v, clock } = make();
    v.showOpponentArrow('off', true, E4);
    expect(v.opponentLastMove).toBeUndefined();
    v.showOpponentArrow('timed', false, E4);
    expect(v.opponentLastMove).toBeUndefined();
    v.showOpponentArrow('timed', true, E4);
    expect(v.opponentLastMove).toEqual(E4);
    clock.flushTimeouts();
    expect(v.opponentLastMove).toBeUndefined();
    v.showOpponentArrow('persist', false, E4);
    clock.flushTimeouts();
    expect(v.opponentLastMove).toEqual(E4);
    v.clearOpponentArrow();
    expect(v.opponentLastMove).toBeUndefined();
  });

  it('Pfeil-Modus: crazy bleibt, sonst nach Viz-Stufe', () => {
    expect(opponentArrowModeFor('crazy', 0)).toBe('persist');
    expect(opponentArrowModeFor('fixed', 2)).toBe('timed');
    expect(opponentArrowModeFor('random', 0)).toBe('off');
  });
});
