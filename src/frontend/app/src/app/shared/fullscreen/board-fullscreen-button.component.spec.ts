import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { BoardFullscreenButtonComponent } from './board-fullscreen-button.component';

describe('BoardFullscreenButtonComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BoardFullscreenButtonComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
  });

  it('zeigt den Knopf erst, wenn ein Ziel-Element gesetzt ist', () => {
    const fixture = TestBed.createComponent(BoardFullscreenButtonComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('button')).toBeNull();

    fixture.componentRef.setInput('target', document.createElement('div'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('button')).not.toBeNull();
  });

  it('schickt das übergebene Element ins Vollbild und schluckt den Klick', () => {
    const fixture = TestBed.createComponent(BoardFullscreenButtonComponent);
    const target = document.createElement('div');
    const request = spyOn(target, 'requestFullscreen').and.returnValue(Promise.resolve());
    fixture.componentRef.setInput('target', target);
    fixture.detectChanges();

    const event = new MouseEvent('click', { cancelable: true, bubbles: true });
    const stop = spyOn(event, 'stopPropagation');
    fixture.nativeElement.querySelector('button').dispatchEvent(event);

    expect(request).toHaveBeenCalled();
    // Ohne stopPropagation zieht chessground unter dem Knopf eine Figur an.
    expect(stop).toHaveBeenCalled();
    expect(event.defaultPrevented).toBeTrue();
  });

  it('folgt dem Vollbild-Zustand (auch wenn per Esc verlassen wird)', () => {
    const fixture = TestBed.createComponent(BoardFullscreenButtonComponent);
    const target = document.createElement('div');
    let current: Element | null = null;
    spyOnProperty(document, 'fullscreenElement', 'get').and.callFake(() => current);
    fixture.componentRef.setInput('target', target);
    fixture.detectChanges();
    expect(fixture.componentInstance.active).toBeFalse();
    expect(fixture.componentInstance.label).toBe('common.fullscreen');

    current = target;
    document.dispatchEvent(new Event('fullscreenchange'));
    fixture.detectChanges();
    expect(fixture.componentInstance.active).toBeTrue();
    expect(fixture.componentInstance.label).toBe('common.fullscreenExit');
    expect(fixture.nativeElement.querySelector('mat-icon').textContent.trim()).toBe('fullscreen_exit');

    current = null;                       // Esc
    document.dispatchEvent(new Event('fullscreenchange'));
    fixture.detectChanges();
    expect(fixture.componentInstance.active).toBeFalse();
  });

  it('schwebt im App-Vollbild oben rechts statt über dem Brett Höhe zu kosten', () => {
    // Der Knopf steht normal IM Fluss (schmale Zeile über dem Brett) …
    const fixture = TestBed.createComponent(BoardFullscreenButtonComponent);
    fixture.componentRef.setInput('target', document.createElement('div'));
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const btn = host.querySelector('button') as HTMLElement;
    document.body.appendChild(host);

    expect(getComputedStyle(btn).position).toBe('static');
    expect(host.getBoundingClientRect().height).toBeGreaterThan(0);

    // … und verlässt ihn im App-Vollbild (Host-Klasse auf einem Vorfahren), damit das Brett
    // nach oben rutscht.
    const appRoot = document.createElement('div');
    appRoot.className = 'app-fullscreen';
    document.body.appendChild(appRoot);
    appRoot.appendChild(host);
    fixture.detectChanges();

    expect(getComputedStyle(btn).position).toBe('fixed');
    expect(host.getBoundingClientRect().height).toBe(0);

    appRoot.remove();
  });

  it('meldet den Ereignis-Hörer beim Zerstören ab', () => {
    const fixture = TestBed.createComponent(BoardFullscreenButtonComponent);
    fixture.componentRef.setInput('target', document.createElement('div'));
    fixture.detectChanges();

    fixture.destroy();

    // Nach dem Abmelden darf ein Vollbild-Wechsel die zerstörte Komponente nicht mehr anfassen.
    expect(() => document.dispatchEvent(new Event('fullscreenchange'))).not.toThrow();
  });
});
