import { VisibilityStopwatch } from './visibility-stopwatch';

function setVisibility(state: 'visible' | 'hidden'): void {
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => state });
  document.dispatchEvent(new Event('visibilitychange'));
}

describe('VisibilityStopwatch', () => {
  beforeEach(() => {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date(0));
    setVisibility('visible');
  });

  afterEach(() => {
    setVisibility('visible');
    jasmine.clock().uninstall();
  });

  it('counts elapsed time while the tab is visible', () => {
    const sw = new VisibilityStopwatch();
    sw.start();

    jasmine.clock().tick(5000);
    expect(sw.elapsedSeconds).toBe(5);

    sw.stop();
  });

  it('does NOT count time while the tab is hidden, and resumes when visible again', () => {
    const sw = new VisibilityStopwatch();
    sw.start();

    jasmine.clock().tick(4000);        // 4s sichtbar
    setVisibility('hidden');
    jasmine.clock().tick(60000);       // 60s im Hintergrund → zählt NICHT
    expect(sw.elapsedSeconds).toBe(4);

    setVisibility('visible');
    jasmine.clock().tick(3000);        // 3s sichtbar
    expect(sw.elapsedSeconds).toBe(7);

    expect(sw.stop()).toBe(7);
  });

  it('starts already paused if the tab is hidden at start()', () => {
    setVisibility('hidden');
    const sw = new VisibilityStopwatch();
    sw.start();

    jasmine.clock().tick(10000);
    expect(sw.elapsedSeconds).toBe(0);

    setVisibility('visible');
    jasmine.clock().tick(2000);
    expect(sw.elapsedSeconds).toBe(2);
    sw.stop();
  });

  it('resumes from an initial seconds value (Endless session restore)', () => {
    const sw = new VisibilityStopwatch();
    sw.start(120);                     // Lauf bei 2:00 fortgesetzt

    jasmine.clock().tick(5000);
    expect(sw.elapsedSeconds).toBe(125);
    sw.stop();
  });

  it('stop() detaches the listener so later visibility changes are ignored', () => {
    const sw = new VisibilityStopwatch();
    sw.start();
    jasmine.clock().tick(3000);
    const final = sw.stop();
    expect(final).toBe(3);

    // Nach stop(): Sichtbarkeitswechsel + Zeit dürfen den Wert nicht mehr verändern.
    setVisibility('hidden');
    setVisibility('visible');
    jasmine.clock().tick(10000);
    expect(sw.elapsedSeconds).toBe(3);
  });

  it('repeated start() does not stack listeners (re-arm)', () => {
    const sw = new VisibilityStopwatch();
    sw.start();
    jasmine.clock().tick(2000);
    sw.start();                        // re-arm → akkumulierte Zeit zurückgesetzt
    expect(sw.elapsedSeconds).toBe(0);

    // Genau EIN aktiver Listener: hidden → 0 weiter, visible → wieder zählen.
    setVisibility('hidden');
    jasmine.clock().tick(5000);
    expect(sw.elapsedSeconds).toBe(0);
    setVisibility('visible');
    jasmine.clock().tick(1000);
    expect(sw.elapsedSeconds).toBe(1);
    sw.stop();
  });
});
