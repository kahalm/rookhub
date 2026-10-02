import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatButtonModule } from '@angular/material/button';

/**
 * Codereview W5 UX-052: Im M3-Thema wurde `mat-raised-button color="primary"` zum „protected"-Knopf (Fläche =
 * surface, `color` wirkungslos) — die Hauptaktion ging als Pille ohne Füllung unter, der deaktivierte Knopf war
 * grau GEFÜLLT und wirkte kräftiger. `src/styles.scss` gibt genau dieser Kombination die gefüllte Primärfarbe.
 * Karma lädt die globalen Styles (angular.json, test.styles), deshalb lässt sich das am gerenderten Knopf messen.
 */
@Component({
  standalone: true,
  imports: [MatButtonModule],
  template: `
    <span class="probe-primary" style="background-color: var(--mat-sys-primary); color: var(--mat-sys-on-primary)">x</span>
    <span class="probe-error" style="background-color: var(--mat-sys-error)">x</span>
    <span class="probe-surface" style="background-color: var(--mat-sys-surface)">x</span>
    <button mat-raised-button color="primary" class="primary">Los</button>
    <button mat-raised-button color="primary" class="primary-disabled" disabled>Los</button>
    <a mat-raised-button color="primary" class="primary-link" href="#">Los</a>
    <button mat-raised-button color="warn" class="warn">Löschen</button>
    <button mat-raised-button class="plain">Neutral</button>
  `,
})
class HostComponent {}

describe('Primärknöpfe im M3-Thema (styles.scss)', () => {
  let el: HTMLElement;
  const bg = (sel: string) => getComputedStyle(el.querySelector(sel)!).backgroundColor;
  const fg = (sel: string) => getComputedStyle(el.querySelector(sel)!).color;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HostComponent] });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    el = fixture.nativeElement;
    document.body.appendChild(el);   // getComputedStyle braucht ein Element im Dokument
  });

  afterEach(() => { el.remove(); TestBed.resetTestingModule(); });

  it('füllt mat-raised-button color="primary" mit der Primärfarbe — als Knopf und als Link', () => {
    expect(bg('.probe-primary')).not.toBe(bg('.probe-surface'));   // Prüfung trägt: Thema ist geladen
    expect(bg('.primary')).toBe(bg('.probe-primary'));
    expect(fg('.primary')).toBe(fg('.probe-primary'));
    expect(bg('.primary-link')).toBe(bg('.probe-primary'));
  });

  it('lässt den deaktivierten Knopf NICHT in der Primärfarbe — er bleibt schwächer als der aktive', () => {
    expect(bg('.primary-disabled')).not.toBe(bg('.probe-primary'));
  });

  it('füllt color="warn" mit der Fehlerfarbe und lässt raised ohne Farbe beim M3-Knopf', () => {
    expect(bg('.warn')).toBe(bg('.probe-error'));
    expect(bg('.plain')).toBe(bg('.probe-surface'));
  });
});
