import { fakeAsync, tick } from '@angular/core/testing';
import { SecondsTicker, formatClock, readingSeconds, serverTime } from './scoresheet-timing';

describe('scoresheet-timing', () => {
  it('liest Zeiten ohne Zone als UTC, mit Zone wie angegeben', () => {
    expect(serverTime('2026-09-28T08:52:00')).toBe(Date.UTC(2026, 8, 28, 8, 52, 0));
    expect(serverTime('2026-09-28T08:52:00Z')).toBe(Date.UTC(2026, 8, 28, 8, 52, 0));
    expect(serverTime('2026-09-28T10:52:00+02:00')).toBe(Date.UTC(2026, 8, 28, 8, 52, 0));
    expect(serverTime(null)).toBeNaN();
  });

  it('zählt die Sekunden seit dem Hochladen, nie negativ', () => {
    const now = Date.UTC(2026, 8, 28, 8, 52, 31);
    expect(readingSeconds('2026-09-28T08:52:00', now)).toBe(31);
    expect(readingSeconds('2026-09-28T08:53:00', now)).toBe(0);
    expect(readingSeconds(undefined, now)).toBe(0);
  });

  it('schreibt m:ss', () => {
    expect([0, 7, 60, 83, 3725].map(formatClock)).toEqual(['0:00', '0:07', '1:00', '1:23', '62:05']);
  });

  it('die Uhr läuft nur, solange sie soll', fakeAsync(() => {
    const t = new SecondsTicker();
    const start = t.now();
    t.run(true);
    tick(2000);
    expect(t.now()).toBeGreaterThanOrEqual(start + 2000);
    t.run(false);
    const stopped = t.now();
    tick(3000);
    expect(t.now()).toBe(stopped);
  }));
});
