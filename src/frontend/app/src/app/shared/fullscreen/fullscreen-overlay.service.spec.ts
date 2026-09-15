import { Injector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { createOverlayRef } from '@angular/cdk/overlay';
import { FullscreenOverlayService, provideFullscreenSafeOverlays } from './fullscreen-overlay.service';

/**
 * Das Umhängen des Containers taugt nur für KLASSISCHE Overlays: ein offenes Popover schließt der
 * Browser beim Umhängen still. Die Kontrolle ohne Provider zeigt, dass der Test tatsächlich beißt.
 */
describe('provideFullscreenSafeOverlays', () => {
  function hostPopoverAttr(): string | null {
    const ref = createOverlayRef(TestBed.inject(Injector));
    const attr = ref.hostElement.getAttribute('popover');
    ref.dispose();
    return attr;
  }

  it('ohne den Provider öffnet die CDK Overlays als Popover (Kontrolle)', () => {
    TestBed.configureTestingModule({});
    expect(hostPopoverAttr()).toBe('manual');
  });

  it('mit dem Provider sind Overlays klassisch — ohne Popover-Attribut', () => {
    TestBed.configureTestingModule({ providers: [provideFullscreenSafeOverlays()] });
    expect(hostPopoverAttr()).toBeNull();
  });
});

describe('FullscreenOverlayService', () => {
  let container: HTMLElement;
  let host: HTMLElement;
  let svc: FullscreenOverlayService;

  beforeEach(() => {
    container = document.createElement('div');
    container.className = 'cdk-overlay-container';
    document.body.appendChild(container);
    host = document.createElement('div');
    document.body.appendChild(host);
    svc = new FullscreenOverlayService({ getContainerElement: () => container } as any);
  });

  afterEach(() => {
    container.remove();
    host.remove();
  });

  it('starts at the body (no fullscreen)', () => {
    expect(container.parentElement).toBe(document.body);
  });

  it('moves the overlay container into the fullscreen element', () => {
    svc.sync(host);
    expect(container.parentElement).toBe(host);
  });

  it('moves it back to the body when fullscreen ends', () => {
    svc.sync(host);
    svc.sync(null);
    expect(container.parentElement).toBe(document.body);
  });

  it('leaves it at the body for document/html/body fullscreen (app fullscreen)', () => {
    svc.sync(document.documentElement);
    expect(container.parentElement).toBe(document.body);
    svc.sync(document.body);
    expect(container.parentElement).toBe(document.body);
  });

  it('re-attaches a detached container (fullscreen element was destroyed)', () => {
    svc.sync(host);
    host.remove();                              // Navigation aus dem Vollbild heraus
    expect(container.isConnected).toBeFalse();

    svc.sync(null);

    expect(container.parentElement).toBe(document.body);
    expect(container.isConnected).toBeTrue();
  });

  it('does not move the container when it is already in the right place', () => {
    const spy = spyOn(document.body, 'appendChild').and.callThrough();
    svc.sync(host);
    svc.sync(null);                             // zurück an den body
    spy.calls.reset();
    svc.sync(null);                             // liegt schon dort → kein weiterer Umzug
    expect(spy).not.toHaveBeenCalled();
  });

  it('never creates the overlay container while no fullscreen was ever active', () => {
    // getContainerElement() LEGT den Container an — beim App-Start (und im Teardown) unerwünscht.
    let created = 0;
    const lazy = new FullscreenOverlayService({
      getContainerElement: () => { created++; return container; },
    } as any);

    lazy.sync(null);
    lazy.sync(document.documentElement);        // App-Vollbild zählt nicht
    lazy.ngOnDestroy();

    expect(created).toBe(0);
  });

  it('ngOnDestroy unhooks the listener and parks the container at the body', () => {
    svc.sync(host);
    svc.ngOnDestroy();
    expect(container.parentElement).toBe(document.body);
  });
});
