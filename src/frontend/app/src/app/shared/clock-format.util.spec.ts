import { formatSecondsClock } from './clock-format.util';

describe('formatSecondsClock', () => {
  it('m:ss unter einer Stunde, h:mm:ss darueber', () => {
    expect(formatSecondsClock(0)).toBe('0:00');
    expect(formatSecondsClock(59)).toBe('0:59');
    expect(formatSecondsClock(60)).toBe('1:00');
    expect(formatSecondsClock(65)).toBe('1:05');
    expect(formatSecondsClock(600)).toBe('10:00');
    expect(formatSecondsClock(3599)).toBe('59:59');
    expect(formatSecondsClock(3600)).toBe('1:00:00');
    expect(formatSecondsClock(3661)).toBe('1:01:01');
    expect(formatSecondsClock(3723)).toBe('1:02:03');
    expect(formatSecondsClock(86399)).toBe('23:59:59');
    expect(formatSecondsClock(90000)).toBe('25:00:00');
  });

  it('negativ oder ungueltig → 0:00, Bruchteile abgeschnitten', () => {
    expect(formatSecondsClock(-5)).toBe('0:00');
    expect(formatSecondsClock(NaN)).toBe('0:00');
    expect(formatSecondsClock(90.9)).toBe('1:30');
  });
});
