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
    // Hauptknopf wie .btn (z1-c-login-btn): 44 px hoch, 16 px fett.
    expect(cs.getPropertyValue('--mat-button-filled-container-height').trim()).toBe('44px');
    expect(cs.getPropertyValue('--mat-button-filled-label-text-size').trim()).toBe('16px');
    expect(cs.getPropertyValue('--mat-button-filled-label-text-weight').trim()).toBe('700');
    expect(cs.borderTopStyle).toBe('solid');
    // Der Hinweis-Kasten trägt die Primärfarbe als Kante statt Hellblau.
    const probe = document.createElement('span');
    probe.style.color = 'var(--primary)';
    card.appendChild(probe);
    expect(getComputedStyle(card.querySelector('.auth-required')!).borderLeftColor).toBe(getComputedStyle(probe).color);
  });

  // UI-Sweep 2026-10-11 (d2-c-auth-centered): die 80vh-Zentrierung liess am Handy 240–280 px leer über der Karte.
  it('Anmelde-Masken: keine 80vh-Zentrierung — die Karte steht direkt unter dem Kopf', () => {
    const el = mount(`<ch-root><main class="wrap"><app-forgot-password><div class="auth-container">
      <div class="mat-mdc-card"></div></div></app-forgot-password></main></ch-root>`);
    const box = getComputedStyle(el.querySelector('.auth-container')!);
    expect(box.minHeight).toBe('0px');
    expect(box.paddingTop).toBe('8px');
  });

  // UI-Sweep 2026-10-11 (d2-c-legal-line): Karte 660 px statt 760 (vorher 90–95 Zeichen). Runde z2
  // (z2-rk-legal-zeilenlaenge): 660 px ergaben bei 17 px noch ~80 Zeichen — jetzt Textspalte 33em + Polster, ≤ ~75.
  it('Rechtsseiten: Karte = 33em-Textspalte + 32 px Polster, höchstens ~75 Zeichen je Zeile', () => {
    const el = mount(`<ch-root><main class="wrap"><app-privacy><div class="legal-container"><div class="mat-mdc-card">
      </div></div></app-privacy></main></ch-root>`);
    const card = getComputedStyle(el.querySelector('.mat-mdc-card')!);
    const fs = parseFloat(card.fontSize);
    const max = parseFloat(card.maxWidth);
    expect(max).toBeCloseTo(33 * fs + 32, 0);
    expect(max).toBeLessThan(660);
    // z3-c-recht-kartenkante: dieselbe Einheit wie die Textspalte (16-px-Material-Schrift) — 560 px, rechts wie links 16 px.
    expect(fs).toBe(16);
    expect(max).toBe(560);
    const ctx = document.createElement('canvas').getContext('2d')!;
    ctx.font = `${card.fontWeight} ${card.fontSize} ${card.fontFamily}`;
    const sample = 'Der Knopf unten öffnet in deinem Profil die Karte „Konto löschen“ – bist du nicht angemeldet, meldest du dich zuerst an.';
    expect((max - 32) / (ctx.measureText(sample).width / sample.length)).withContext('Zeichen je Zeile').toBeLessThanOrEqual(76);
  });

  // UI-Sweep 2026-10-12 (z3-c-gruppen-karte-polster): der Absatz unter dem Gruppennamen bringt keinen Standardrand mit.
  it('Gruppenkarte: Infozeile ohne Rand unten — Polster oben und unten gleich', () => {
    const el = mount(`<ul class="groups"><li class="group"><div><h2>Anfänger</h2><p class="kid-meta">Freitag</p></div></li></ul>`);
    const meta = getComputedStyle(el.querySelector('.kid-meta')!);
    expect(meta.marginTop).toBe('4px');
    expect(meta.marginBottom).toBe('0px');
  });

  // UI-Sweep 2026-10-12 (z3-c-fuss-umbruch): die Links bilden eine Gruppe mit demselben Abstand wie die Fußzeile.
  it('Fußzeile: Linkgruppe umbricht geschlossen und übernimmt den Abstand der Fußzeile', () => {
    const el = mount(`<footer class="wrap foot"><span>v1</span><span class="foot-links"><a>Impressum</a><a>Datenschutz</a></span></footer>`);
    const foot = getComputedStyle(el.querySelector('.foot')!);
    const links = getComputedStyle(el.querySelector('.foot-links')!);
    expect(links.display).toBe('flex');
    expect(links.flexWrap).toBe('wrap');
    expect(links.columnGap).toBe(foot.columnGap);
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
  /** Viewport des Test-Dokuments (Karma-iframe) schmal stellen; Rückgabe = zurücksetzen (Muster turnier-navbar.spec). */
  async function narrow(width: number): Promise<() => void> {
    const frame = window.frameElement as HTMLElement | null;
    if (!frame) pending('Karma läuft nicht im iframe — der Viewport lässt sich nicht verstellen');
    const prev = frame!.style.width;
    frame!.style.width = `${width}px`;
    await new Promise<void>(r => requestAnimationFrame(() => r()));
    return () => { frame!.style.width = prev; };
  }

  // UI-Sweep d2-c-form-save-bar: am Handy EINE Reihe Knöpfe statt zwei (111 px)
  it('Speichern-Leiste beim Anlegen am Handy: eine Reihe, höchstens 64 px, „+ Weiteres" kurz', async () => {
    const restore = await narrow(390);
    try {
      mount(`<main class="wrap"><form class="sheet form"><input><div class="actions form-save">
        <button class="btn primary">Kind anlegen</button>
        <button class="btn save-next" aria-label="Weiteres Kind anlegen"><span class="long">Weiteres Kind anlegen</span><span class="short">+ Weiteres</span></button>
        <button class="btn cancel">Abbrechen</button></div></form></main>`);
      const bar = host.querySelector<HTMLElement>('.form-save')!;
      expect(bar.getBoundingClientRect().height).toBeLessThanOrEqual(64);
      const tops = Array.from(bar.querySelectorAll<HTMLElement>('.btn')).map(b => Math.round(b.getBoundingClientRect().top));
      expect(new Set(tops).size).withContext('alle Knöpfe in einer Reihe').toBe(1);
      expect(getComputedStyle(bar.querySelector('.long')!).display).toBe('none');
      expect(bar.scrollWidth).toBeLessThanOrEqual(bar.clientWidth + 1);
      const pad = parseFloat(getComputedStyle(document.documentElement).scrollPaddingBottom);
      expect(pad).toBeGreaterThanOrEqual(bar.getBoundingClientRect().height);
    } finally {
      restore();
    }
  });

  // UI-Sweep d2-c-kartei-lead: Suche, Gruppenchips und Archiv in einer Zeile (ab 641 px)
  it('Kartei: Suche, Chips und Archiv in einer Zeile, das Label „Suchen" nur für Vorleser', () => {
    mount(`<main class="wrap" style="width: 900px"><div class="tools with-chips">
      <label class="field search"><span class="sr-only">Suchen</span><input type="search" placeholder="Name suchen"></label>
      <div class="filter-chips"><button class="chip-btn">Alle</button><button class="chip-btn">Anfänger <span class="n">2</span></button></div>
      <label class="check"><input type="checkbox"> Archiv</label></div></main>`);
    const label = host.querySelector<HTMLElement>('.sr-only')!;
    expect(label.getBoundingClientRect().height).toBeLessThanOrEqual(1);
    if (window.innerWidth > 640) {
      const mid = (sel: string) => { const r = host.querySelector(sel)!.getBoundingClientRect(); return (r.top + r.bottom) / 2; };
      expect(Math.abs(mid('.filter-chips') - mid('.search input'))).toBeLessThan(12);
      expect(Math.abs(mid('.check') - mid('.search input'))).toBeLessThan(12);
    }
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
        // z2-c-loeschknopf-schrift: Beschriftung wie .btn
        const label = getComputedStyle(btn.querySelector('.mdc-button__label') ?? btn);
        expect(parseFloat(label.fontSize)).toBeGreaterThanOrEqual(16);
        expect(Number(label.fontWeight)).toBeGreaterThanOrEqual(700);
      } finally {
        document.documentElement.classList.remove('dark-theme');
        TestBed.resetTestingModule();
      }
    });
  }
});
