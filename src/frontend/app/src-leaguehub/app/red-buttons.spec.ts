import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatButtonModule } from '@angular/material/button';
import { contrast, parseColor } from '@rh/testing/contrast';

/**
 * Rote Knöpfe in LeagueHub (leaguehub.scss, das Karma für LeagueHub mitlädt), in BEIDEN Modi:
 * - d1-l-mask-red-contrast: Schrift auf gefülltem Rot kommt aus `--on-red` — dunkel war Weiß auf #ff4d63 nur 3,2:1.
 * - d1-x-account-delete-warn-ignored: „Konto in RookHub löschen" (mat-flat-button color="warn") ist rot statt in der
 *   Primärfarbe, mit lesbarer Schrift und OHNE die Link-Unterstreichung der Rechtsseiten.
 * Die Knöpfe sind echte Material-Knöpfe, eingehängt in nachgebautes Markup der geteilten Masken.
 */
@Component({
  standalone: true,
  imports: [MatButtonModule],
  template: `
    <button mat-flat-button color="primary" class="mask-primary">Registrieren</button>
    <a mat-flat-button color="warn" class="delete-now" href="#">Konto in RookHub löschen</a>
    <a class="plain-link" href="#">Kurse</a>
  `,
})
class ButtonsComponent {}

describe('LeagueHub: Schrift auf Rot (--on-red) und Gefahrknopf', () => {
  let host: HTMLElement;

  function mount(page: string): HTMLElement {
    host = document.createElement('lh-root');
    host.innerHTML = `<main class="wrap"><${page}><div class="mat-mdc-card"></div></${page}></main>`;
    document.body.appendChild(host);
    const fixture = TestBed.createComponent(ButtonsComponent);
    fixture.detectChanges();
    host.querySelector('.mat-mdc-card')!.appendChild(fixture.nativeElement);
    return host;
  }

  /** Farbe von `var(name)` am Element `at` als berechnetes rgb(). */
  function probe(at: Element, name: string): string {
    const p = document.createElement('span');
    p.style.color = `var(${name})`;
    at.appendChild(p);
    const c = getComputedStyle(p).color;
    p.remove();
    return c;
  }

  beforeEach(() => TestBed.configureTestingModule({ imports: [ButtonsComponent] }));
  afterEach(() => {
    host?.remove();
    document.documentElement.classList.remove('dark-theme');
    TestBed.resetTestingModule();
  });

  for (const dark of [false, true]) {
    const mode = dark ? 'dunkel' : 'hell';

    it(`${mode}: Hauptknopf der Masken rot mit Schrift ≥ 4,5:1`, () => {
      document.documentElement.classList.toggle('dark-theme', dark);
      const el = mount('app-register');
      const s = getComputedStyle(el.querySelector('.mask-primary')!);
      expect(s.backgroundColor).toBe(probe(el, '--red'));
      expect(s.color).toBe(probe(el, '--on-red'));
      expect(contrast(parseColor(s.color), parseColor(s.backgroundColor))).toBeGreaterThanOrEqual(4.5);
    });

    it(`${mode}: „Konto löschen" rot statt Primärfarbe, lesbar, nicht unterstrichen — Links daneben schon`, () => {
      document.documentElement.classList.toggle('dark-theme', dark);
      const el = mount('app-account-deletion');
      const btn = el.querySelector('.delete-now')!;
      const s = getComputedStyle(btn);
      expect(s.backgroundColor).toBe(probe(el, '--red'));
      expect(contrast(parseColor(s.color), parseColor(s.backgroundColor))).toBeGreaterThanOrEqual(4.5);
      expect(s.textDecorationLine).toBe('none');
      expect(getComputedStyle(el.querySelector('.plain-link')!).textDecorationLine).toBe('underline');
    });

    it(`${mode}: .cta .btn-pri — Schrift auf Rot ≥ 4,5:1`, () => {
      document.documentElement.classList.toggle('dark-theme', dark);
      host = document.createElement('div');
      host.innerHTML = `<div class="cta"><a class="btn-pri" href="#">Partien hochladen</a></div>`;
      document.body.appendChild(host);
      const s = getComputedStyle(host.querySelector('.btn-pri')!);
      expect(contrast(parseColor(s.color), parseColor(s.backgroundColor))).toBeGreaterThanOrEqual(4.5);
    });
  }
});
