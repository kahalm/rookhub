import { OffPathPolicy, playerEvalText, whiteEvalToPlayerPawns } from './off-path-policy';

describe('OffPathPolicy', () => {
  it('warnt ab dem Schwellwert-Zug genau einmal, wenn es nicht mehr klar gewonnen ist', () => {
    const p = new OffPathPolicy();
    p.userPlies = 2;
    expect(p.checkWarn(3, -1)).toBeFalse();     // zu früh
    p.userPlies = 3;
    expect(p.checkWarn(0, -5)).toBeFalse();     // 0 = nie
    expect(p.checkWarn(3, 2)).toBeFalse();      // noch +2 → kein Grund
    expect(p.checkWarn(3, null)).toBeFalse();   // keine Eval
    expect(p.checkWarn(3, 1.9)).toBeTrue();
    expect(p.warning).toBeTrue();
    expect(p.checkWarn(3, -9)).toBeFalse();     // einmalig
    p.reset();
    expect(p.warning).toBeFalse();
    expect(p.userPlies).toBe(0);
  });

  it('ausgeglichene Startstellung: erst unter −1 warnen', () => {
    const p = new OffPathPolicy();
    p.startEvalPawns = 0.3;
    p.userPlies = 3;
    expect(p.startWasBalanced).toBeTrue();
    expect(p.checkWarn(3, -0.5)).toBeFalse();
    expect(p.checkWarn(3, -1.2)).toBeTrue();
    p.reset();
    expect(p.startEvalPawns).toBe(0.3);         // Start-Eval überlebt den Reset
  });

  it('merkt nur die erste Abzweigung als eigene Zugnummer', () => {
    const p = new OffPathPolicy();
    expect(p.markDeviation(3, 0)).toBeTrue();   // Vorspiel 0 → Löserzüge 1,3,5 … → Zug 2
    expect(p.userMoveNumber).toBe(2);
    expect(p.markDeviation(5, 0)).toBeFalse();
    expect(p.userMoveNumber).toBe(2);
    const q = new OffPathPolicy();
    q.markDeviation(0, -1);                     // FEN = Trainingsstellung, erster Zug
    expect(q.userMoveNumber).toBe(1);
  });
});

describe('Eval aus Spieler-Sicht', () => {
  it('Bauern und Matt', () => {
    expect(whiteEvalToPlayerPawns('+1.5', 'white')).toBe(1.5);
    expect(whiteEvalToPlayerPawns('+1.5', 'black')).toBe(-1.5);
    expect(whiteEvalToPlayerPawns('#-2', 'white')).toBe(-100);
    expect(whiteEvalToPlayerPawns('', 'white')).toBeNull();
    expect(whiteEvalToPlayerPawns('x', 'white')).toBeNull();
  });

  it('Anzeige-Text', () => {
    expect(playerEvalText('-0.84', 'black')).toBe('+0.8');
    expect(playerEvalText('#3', 'black')).toBe('#-3');
    expect(playerEvalText('0', 'white')).toBe('0.0');
    expect(playerEvalText('', 'white')).toBeNull();
  });
});
