import { DOT_FAVICON, TAB_BLINK_MS, TAB_MARK, TabAttention, notifySupported, pageAttended, pawnsText, requestNotifyPermission,
  resultShort, showLiveNotification } from './live-attention';

/** Eigenes Dokument mit zwei Favicons und schaltbarem `hidden`/`hasFocus`. */
function fakeDoc(): { doc: Document; setHidden: (h: boolean) => void; setFocus: (f: boolean) => void } {
  const doc = document.implementation.createHTMLDocument('Live-Bretter · Turnier');
  doc.head.insertAdjacentHTML('beforeend', '<link rel="icon" type="image/png" href="icons/icon-192.png"><link rel="icon" href="favicon.ico"><link rel="apple-touch-icon" href="x.png">');
  let hidden = true;
  let focus = false;
  Object.defineProperty(doc, 'hidden', { configurable: true, get: () => hidden });
  Object.defineProperty(doc, 'hasFocus', { configurable: true, value: () => focus });
  return { doc, setHidden: h => { hidden = h; }, setFocus: f => { focus = f; } };
}

const icons = (doc: Document) => Array.from(doc.querySelectorAll('link[rel~="icon"]')).map(l => l.getAttribute('href'));

/** Ersatz für `window.Notification` mit einstellbarer Erlaubnis; merkt sich jede Meldung. */
class FakeNotification {
  static permission: NotificationPermission = 'default';
  static answer: NotificationPermission = 'granted';
  static shown: FakeNotification[] = [];
  static requestPermission = jasmine.createSpy('requestPermission').and.callFake(async () => {
    FakeNotification.permission = FakeNotification.answer;
    return FakeNotification.answer;
  });
  onclick: (() => void) | null = null;
  closed = false;
  constructor(public title: string, public options: NotificationOptions) { FakeNotification.shown.push(this); }
  close(): void { this.closed = true; }
}

describe('live-attention', () => {
  describe('TabAttention', () => {
    beforeEach(() => jasmine.clock().install());
    afterEach(() => jasmine.clock().uninstall());

    it('Hintergrund: Titel wechselt mit dem Original, Favicons mit Punkt; Sichtbarwerden stellt beides wieder her', () => {
      const { doc, setHidden } = fakeDoc();
      const tab = new TabAttention(doc);
      tab.start('1-0 Brett 3');
      expect(doc.title).toBe(TAB_MARK + '1-0 Brett 3');
      expect(icons(doc)).toEqual([DOT_FAVICON, DOT_FAVICON]);
      expect(doc.querySelector('link[rel="apple-touch-icon"]')!.getAttribute('href')).toBe('x.png');
      jasmine.clock().tick(TAB_BLINK_MS);
      expect(doc.title).toBe('Live-Bretter · Turnier');
      jasmine.clock().tick(TAB_BLINK_MS);
      expect(doc.title).toBe('● 1-0 Brett 3');
      // weitere Meldung ersetzt nur den Text
      tab.start('Brett 2: −0.3');
      expect(doc.title).toBe('● Brett 2: −0.3');
      setHidden(false);
      doc.dispatchEvent(new Event('visibilitychange'));
      expect(doc.title).toBe('Live-Bretter · Turnier');
      expect(icons(doc)).toEqual(['icons/icon-192.png', 'favicon.ico']);
      expect(tab.active).toBeFalse();
      jasmine.clock().tick(5 * TAB_BLINK_MS);
      expect(doc.title).toBe('Live-Bretter · Turnier');
    });

    it('sichtbarer Tab: nichts; stop() (Verlassen der Seite) stellt wieder her, ein inzwischen neuer Titel gilt als Original', () => {
      const { doc, setHidden } = fakeDoc();
      const tab = new TabAttention(doc);
      setHidden(false);
      tab.start('1-0 Brett 1');
      expect(doc.title).toBe('Live-Bretter · Turnier');
      expect(tab.active).toBeFalse();
      setHidden(true);
      tab.start('1-0 Brett 1');
      doc.title = 'Runde 2 · Turnier';
      jasmine.clock().tick(TAB_BLINK_MS);
      tab.stop();
      expect(doc.title).toBe('Runde 2 · Turnier');
      expect(icons(doc)).toEqual(['icons/icon-192.png', 'favicon.ico']);
      tab.stop();   // doppelt schadet nicht
      expect(doc.title).toBe('Runde 2 · Turnier');
    });
  });

  describe('Notification', () => {
    let saved: unknown;
    beforeEach(() => {
      saved = (window as unknown as { Notification?: unknown }).Notification;
      FakeNotification.permission = 'default';
      FakeNotification.answer = 'granted';
      FakeNotification.shown = [];
      FakeNotification.requestPermission.calls.reset();
      (window as unknown as { Notification: unknown }).Notification = FakeNotification;
    });
    afterEach(() => { (window as unknown as { Notification: unknown }).Notification = saved; });

    it('Erlaubnis: erteilt, abgelehnt, schon gesperrt (ohne Nachfrage), fehlende API', async () => {
      expect(await requestNotifyPermission()).toBeTrue();
      expect(FakeNotification.requestPermission).toHaveBeenCalledTimes(1);
      expect(await requestNotifyPermission()).toBeTrue();   // schon erlaubt: keine zweite Frage
      expect(FakeNotification.requestPermission).toHaveBeenCalledTimes(1);
      FakeNotification.permission = 'default';
      FakeNotification.answer = 'denied';
      expect(await requestNotifyPermission()).toBeFalse();
      FakeNotification.requestPermission.calls.reset();
      expect(await requestNotifyPermission()).toBeFalse();
      expect(FakeNotification.requestPermission).not.toHaveBeenCalled();
      delete (window as unknown as { Notification?: unknown }).Notification;
      expect(notifySupported()).toBeFalse();
      expect(await requestNotifyPermission()).toBeFalse();
      expect(showLiveNotification('a', 'b', 't', () => undefined)).toBeNull();
    });

    it('Meldung mit tag; Klick holt das Fenster nach vorn, ruft zurück und schließt; ohne Erlaubnis keine', () => {
      const onClick = jasmine.createSpy('onClick');
      expect(showLiveNotification('Brett 3', 'A – B 1-0', 'rh-live-x', onClick)).toBeNull();
      FakeNotification.permission = 'granted';
      const focus = spyOn(window, 'focus');
      const n = showLiveNotification('Brett 3', 'A – B 1-0', 'rh-live-x', onClick) as unknown as FakeNotification;
      expect(n.title).toBe('Brett 3');
      expect(n.options.body).toBe('A – B 1-0');
      expect(n.options.tag).toBe('rh-live-x');
      n.onclick!();
      expect(focus).toHaveBeenCalled();
      expect(onClick).toHaveBeenCalled();
      expect(n.closed).toBeTrue();
    });

    it('pageAttended: nur sichtbar UND mit Fokus', () => {
      const { doc, setHidden, setFocus } = fakeDoc();
      expect(pageAttended(doc)).toBeFalse();
      setHidden(false);
      expect(pageAttended(doc)).toBeFalse();
      setFocus(true);
      expect(pageAttended(doc)).toBeTrue();
    });
  });

  it('Texte: Bewertung wie im Balken, Kappe als Matt, Remis als ½-½', () => {
    expect(pawnsText(1.4)).toBe('+1.4');
    expect(pawnsText(-0.3)).toBe('−0.3');
    expect(pawnsText(0)).toBe('0.0');
    expect(pawnsText(10)).toBe('#');
    expect(resultShort('1/2-1/2')).toBe('½-½');
    expect(resultShort('0-1')).toBe('0-1');
  });
});
