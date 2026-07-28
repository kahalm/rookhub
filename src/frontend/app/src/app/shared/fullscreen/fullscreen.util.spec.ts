import {
  exitFullscreen, fullscreenElement, fullscreenSupported, isFullscreen, onFullscreenChange,
  requestFullscreen, toggleFullscreen,
} from './fullscreen.util';

/**
 * Die Fullscreen-API lässt sich in Karma nicht echt auslösen (braucht eine Nutzer-Gebärde), also
 * werden `requestFullscreen`/`exitFullscreen`/`document.fullscreenElement` gestubbt. Getestet wird
 * damit das, was hier wirklich Logik ist: Erkennung, Umschalt-Entscheidung und das Abmelden der
 * Ereignis-Hörer.
 */
describe('fullscreen.util', () => {
  let element: HTMLElement;
  let current: Element | null;
  const calls: string[] = [];

  beforeEach(() => {
    calls.length = 0;
    current = null;
    element = document.createElement('div');
    spyOnProperty(document, 'fullscreenElement', 'get').and.callFake(() => current);
    spyOn(element, 'requestFullscreen').and.callFake(() => {
      calls.push('request');
      current = element;
      return Promise.resolve();
    });
    spyOn(document, 'exitFullscreen').and.callFake(() => {
      calls.push('exit');
      current = null;
      return Promise.resolve();
    });
  });

  it('meldet die Unterstützung anhand der vorhandenen API', () => {
    expect(fullscreenSupported()).toBeTrue();          // Karma-Chrome kann Element-Vollbild
  });

  it('erkennt, welches Element im Vollbild ist', async () => {
    expect(isFullscreen(element)).toBeFalse();
    expect(fullscreenElement()).toBeNull();

    await requestFullscreen(element);

    expect(isFullscreen(element)).toBeTrue();
    expect(isFullscreen(document.createElement('div'))).toBeFalse();
    expect(isFullscreen(null)).toBeFalse();
  });

  it('schaltet hin und zurück', async () => {
    await toggleFullscreen(element);
    expect(calls).toEqual(['request']);

    await toggleFullscreen(element);
    expect(calls).toEqual(['request', 'exit']);
    expect(isFullscreen(element)).toBeFalse();
  });

  it('verschluckt eine Ablehnung des Browsers (der Knopf bleibt einfach wirkungslos)', async () => {
    (element.requestFullscreen as jasmine.Spy).and.returnValue(Promise.reject(new Error('denied')));
    await expectAsync(requestFullscreen(element)).toBeResolved();

    (document.exitFullscreen as jasmine.Spy).and.returnValue(Promise.reject(new Error('denied')));
    await expectAsync(exitFullscreen()).toBeResolved();
  });

  it('hört auf beide Ereignisnamen und meldet sich wieder ab', () => {
    let hits = 0;
    const off = onFullscreenChange(() => hits++);

    document.dispatchEvent(new Event('fullscreenchange'));
    document.dispatchEvent(new Event('webkitfullscreenchange'));   // Safari
    expect(hits).toBe(2);

    off();
    document.dispatchEvent(new Event('fullscreenchange'));
    expect(hits).toBe(2);
  });
});
