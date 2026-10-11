import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatButtonModule } from '@angular/material/button';
import { contrast, parseColor } from '@rh/testing/contrast';

/** Der Löschknopf der geteilten Seite „Konto löschen" als echter Material-Knopf. */
@Component({
  standalone: true,
  imports: [MatButtonModule],
  template: `<a mat-flat-button color="warn" class="delete-now" href="#">Konto in RookHub löschen</a>`,
})
class DeleteButtonComponent {}

/**
 * Regeln aus clubhub.scss (das Karma für ClubHub mitlädt), die an keiner eigenen Komponente hängen: die Karteireiter
 * der App-Hülle, RookHubs Anmelde-Maske in ClubHub-Farben und der Abstand über der klebenden Speichern-Leiste.
 * Geprüft wird an nachgebautem Markup — dieselben Klassen wie in den Vorlagen.
 */
describe('clubhub.scss', () => {
  let host: HTMLElement;

  function mount(html: string): HTMLElement {
    host = document.createElement('div');
    host.innerHTML = html;
    document.body.appendChild(host);
    return host;
  }

  afterEach(() => host?.remove());

  const rootVar = (name: string) => getComputedStyle(document.documentElement).getPropertyValue(name).trim();

  it('Karteireiter: aktiver und inaktiver haben dieselbe Schrift, dieselben Polster und dieselbe Grundlinie', () => {
    const el = mount(`<header class="top"><nav class="wrap tabs"><a class="on">Kartei</a><a>Gruppen</a></nav></header>`);
    const [on, off] = Array.from(el.querySelectorAll<HTMLElement>('.tabs a'));
    const style = (a: HTMLElement) => {
      const s = getComputedStyle(a);
      return [s.fontSize, s.fontWeight, s.fontFamily, s.paddingTop, s.paddingBottom, s.lineHeight];
    };
    expect(style(on)).toEqual(style(off));
    const textBottom = (a: HTMLElement) => {
      const range = document.createRange();
      range.selectNodeContents(a.firstChild!);
      return Math.round(range.getBoundingClientRect().bottom);
    };
    expect(textBottom(on)).toBe(textBottom(off));                                       // vorher 3 px höher
    expect(getComputedStyle(on).backgroundColor).not.toBe(getComputedStyle(off).backgroundColor);
  });

  it('Anmelde-Maske: Karte, Knopf und Fokus in ClubHub-Farben, Überschrift in Zilla Slab', () => {
    const el = mount(`<app-login><div class="auth-container"><div class="mat-mdc-card">
      <p class="auth-required">Hinweis</p></div></div></app-login>`);
    const card = el.querySelector<HTMLElement>('.mat-mdc-card')!;
    const cs = getComputedStyle(card);
    expect(cs.getPropertyValue('--mat-sys-primary').trim()).toBe(rootVar('--primary'));
    expect(cs.getPropertyValue('--mat-sys-on-primary').trim()).toBe(rootVar('--primary-ink'));
    expect(cs.getPropertyValue('--mat-card-elevated-container-color').trim()).toBe(rootVar('--card'));
    expect(cs.getPropertyValue('--mat-card-title-text-font')).toContain('Zilla Slab');
    expect(cs.getPropertyValue('--mat-button-filled-container-shape').trim()).toBe('8px');
    expect(cs.borderTopStyle).toBe('solid');
    // Der Hinweis-Kasten trägt die Primärfarbe als Kante statt Hellblau.
    const probe = document.createElement('span');
    probe.style.color = 'var(--primary)';
    card.appendChild(probe);
    expect(getComputedStyle(card.querySelector('.auth-required')!).borderLeftColor).toBe(getComputedStyle(probe).color);
  });

  it('Anmelde-Maske außerhalb der Anmeldung bleibt unberührt', () => {
    const el = mount(`<div class="mat-mdc-card"></div>`);
    expect(getComputedStyle(el.querySelector('.mat-mdc-card')!).getPropertyValue('--mat-card-title-text-font').trim()).toBe('');
  });

  it('mit klebender Speichern-Leiste scrollt ein fokussiertes Feld über die Leiste statt darunter', () => {
    mount(`<form class="sheet form"><input><div class="actions form-save"><button class="btn primary">Kind anlegen</button></div></form>`);
    const pad = parseFloat(getComputedStyle(document.documentElement).scrollPaddingBottom);
    const bar = host.querySelector<HTMLElement>('.form-save')!;
    expect(pad).toBeGreaterThanOrEqual(bar.getBoundingClientRect().height);
  });
  // UI-Sweep 2026-10-11 (r2-c-privacy-style)
  it('Datenschutzseite: ClubHub-Karte, Slab-Köpfe in Kopflinienfarbe, Links in Primärfarbe, kein Extra-Rand im .wrap', () => {
    const el = mount(`<ch-root><main class="wrap"><app-privacy><div class="legal-container"><div class="mat-mdc-card">
      <h2>Verantwortlicher</h2><a href="#x">Link</a></div></div></app-privacy></main></ch-root>`);
    const card = el.querySelector<HTMLElement>('.mat-mdc-card')!;
    const cs = getComputedStyle(card);
    expect(cs.getPropertyValue('--mat-sys-primary').trim()).toBe(rootVar('--primary'));
    expect(cs.getPropertyValue('--mat-card-title-text-font')).toContain('Zilla Slab');
    expect(getComputedStyle(el.querySelector('.legal-container')!).paddingLeft).toBe('0px');
    const probe = (v: string) => {
      const p = document.createElement('span');
      p.style.color = `var(${v})`;
      card.appendChild(p);
      return getComputedStyle(p).color;
    };
    const h2 = getComputedStyle(el.querySelector('h2')!);
    expect(h2.fontFamily).toContain('Zilla Slab');
    expect(h2.color).toBe(probe('--rule'));
    expect(getComputedStyle(el.querySelector('a')!).color).toBe(probe('--primary'));
  });

  // UI-Sweep 2026-10-11 (d1-x-account-delete-warn-ignored)
  for (const dark of [false, true]) {
    it(`Konto löschen (${dark ? 'dunkel' : 'hell'}): Knopf in --bad statt Primärgrün, Form wie .btn (Radius 8, ≥ 44 px), lesbar`, () => {
      document.documentElement.classList.toggle('dark-theme', dark);
      try {
        const el = mount(`<ch-root><main class="wrap"><app-account-deletion><div class="legal-container"><div class="mat-mdc-card">
          </div></div></app-account-deletion></main></ch-root>`);
        TestBed.configureTestingModule({ imports: [DeleteButtonComponent] });
        const fixture = TestBed.createComponent(DeleteButtonComponent);
        fixture.detectChanges();
        el.querySelector('.mat-mdc-card')!.appendChild(fixture.nativeElement);
        const btn = el.querySelector<HTMLElement>('.delete-now')!;
        const s = getComputedStyle(btn);
        const probe = document.createElement('span');
        probe.style.color = 'var(--bad)';
        el.appendChild(probe);
        expect(s.backgroundColor).toBe(getComputedStyle(probe).color);
        expect(contrast(parseColor(s.color), parseColor(s.backgroundColor))).toBeGreaterThanOrEqual(4.5);
        expect(s.borderTopLeftRadius).toBe('8px');
        expect(btn.getBoundingClientRect().height).toBeGreaterThanOrEqual(44);
      } finally {
        document.documentElement.classList.remove('dark-theme');
        TestBed.resetTestingModule();
      }
    });
  }
});
