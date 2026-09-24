import { formatTimeControl } from './time-control.util';

describe('formatTimeControl', () => {
  it('schreibt Blitz mit Inkrement wie chess.com', () => {
    expect(formatTimeControl('180+2')).toEqual({ key: 'plus', params: { main: 3, inc: 2 } });
    expect(formatTimeControl('900+10')).toEqual({ key: 'plus', params: { main: 15, inc: 10 } });
    expect(formatTimeControl('60+1')).toEqual({ key: 'plus', params: { main: 1, inc: 1 } });
  });

  it('ohne Inkrement Minuten, unter einer Minute Sekunden', () => {
    expect(formatTimeControl('600')).toEqual({ key: 'min', params: { count: 10 } });
    expect(formatTimeControl('600+0')).toEqual({ key: 'min', params: { count: 10 } });
    expect(formatTimeControl('30')).toEqual({ key: 'sec', params: { count: 30 } });
  });

  it('eine krumme Grundzeit behält ihr Inkrement statt gerundet zu werden', () => {
    // 45+1 auf „1 + 1" zu runden behauptete eine Bedenkzeit, die es nicht gibt.
    expect(formatTimeControl('45+1')).toEqual({ key: 'plusSec', params: { main: 45, inc: 1 } });
    expect(formatTimeControl('90+30')).toEqual({ key: 'plusSec', params: { main: 90, inc: 30 } });
  });

  it('Fernschach zählt in Tagen — die Sekunden hinter dem Schrägstrich', () => {
    expect(formatTimeControl('1/86400')).toEqual({ key: 'days', params: { count: 1 } });
    expect(formatTimeControl('1/259200')).toEqual({ key: 'days', params: { count: 3 } });
  });

  it('nichts anzeigen ist besser als etwas raten', () => {
    for (const raw of [null, undefined, '', '   ', '-', 'blitz', '3 + 2', '0', '1/3600', 'x/y']) {
      expect(formatTimeControl(raw)).withContext(String(raw)).toBeNull();
    }
  });
});
