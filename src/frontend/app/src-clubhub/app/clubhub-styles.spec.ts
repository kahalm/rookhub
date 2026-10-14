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

  // a1-c-disabled-button-contrast: gesperrte Knöpfe waren pauschal 50 % durchsichtig (hell ≈ 2,4:1).
  for (const dark of [false, true]) {
    it(`gesperrte Knöpfe (${dark ? 'dunkel' : 'hell'}): volle Deckkraft, Schrift ≥ 4,5:1, Rahmen gestrichelt ≥ 3:1`, () => {
      document.documentElement.classList.toggle('dark-theme', dark);
      try {
        const el = mount(`<div class="sheet"><button class="btn" disabled>keine spätere ›</button>
          <button class="btn primary" disabled>Speichern</button><button class="btn danger" disabled>Trennen</button>
          <label class="btn slim upload disabled">Bild wählen</label></div>`);
        const card = parseColor(getComputedStyle(el.querySelector('.sheet')!).backgroundColor);
        for (const btn of Array.from(el.querySelectorAll<HTMLElement>('.btn'))) {
          const s = getComputedStyle(btn);
          const label = btn.textContent!.trim();
          expect(Number(s.opacity)).withContext(`${label}: Deckkraft`).toBe(1);
          expect(s.borderTopStyle).withContext(`${label}: Rahmen`).toBe('dashed');
          expect(contrast(parseColor(s.color), parseColor(s.backgroundColor))).withContext(`${label}: Schrift`).toBeGreaterThanOrEqual(4.5);
          expect(contrast(parseColor(s.borderTopColor), card)).withContext(`${label}: Rahmen`).toBeGreaterThanOrEqual(3);
        }
      } finally {
        document.documentElement.classList.remove('dark-theme');
      }
    });
  }

  // a1-c-groups-meta-separator: Zeitplan (Freitext), Kinderzahl und „zuletzt" liefen ohne Trenner ineinander.
  // b1-c-meta-trenner-zeilenende: als ::after (0.763.1) hing der Punkt am Zeilenende (am Handy allein in einer Zeile),
  // als gewöhnliches ::before (a1) am Zeilenanfang. Jetzt: ::before absolut in der Lücke, am Zeilenanfang weggeschnitten.
  it('Metazeile: „·" als ::before in der Lücke vor der nächsten Angabe, nicht vor Chips, nie am Zeilenende', () => {
    const el = mount(`<p class="kid-meta"><span>Freitag, 17:00–18:00 neue Mittelschule Schwaz</span><span>2 Kinder</span>
      <span>zuletzt Fr 25.09.</span><span class="chip">im Archiv</span></p>
      <p class="kid-meta att"><a href="#">Anfänger</a><span>Freitag, 25.09.2026</span></p>`);
    const before = (n: Element) => getComputedStyle(n, '::before');
    const meta = el.querySelector('.kid-meta:not(.att)')!;
    const [first, second, third, chip] = Array.from(meta.children);
    expect(before(first).content).withContext('erste Angabe ohne Trenner').toBe('none');
    expect(before(second).content).toBe('"·"');
    expect(before(third).content).toBe('"·"');
    expect(before(chip).content).withContext('vor dem Chip kein Trenner').toBe('none');
    for (const n of [first, second, third, chip]) expect(getComputedStyle(n, '::after').content).toBe('none');
    // Der Punkt nimmt keinen Platz (absolut) und sitzt links vor seiner Angabe, also in der Lücke.
    expect(before(second).position).toBe('absolute');
    expect(getComputedStyle(second).position).toBe('relative');
    expect(parseFloat(getComputedStyle(meta).columnGap)).toBeGreaterThanOrEqual(12);
    expect(getComputedStyle(meta).overflowX).toBe('clip');
    expect(before(el.querySelector('.att > span')!).content).withContext('Anwesenheit: Trenner nach dem Link').toBe('"·"');

    // Umbruch (390-px-Handy, Liste mit schmaler Spalte): die umgebrochene Angabe beginnt an der Kante der Metazeile —
    // ihr Punkt läge links außerhalb und wird vom Clip abgeschnitten; am Ende der Zeile davor steht nichts.
    (meta as HTMLElement).style.width = '260px';
    const metaLeft = meta.getBoundingClientRect().left;
    const wrapped = Array.from(meta.children).filter(n => n !== first && Math.abs(n.getBoundingClientRect().left - metaLeft) < 1);
    expect(wrapped.length).withContext('mindestens eine Angabe bricht um').toBeGreaterThan(0);
    const dotWidth = 6;                                                     // „·" in 15 px ≈ 4 px, plus 5 px Abstand
    expect(parseFloat(before(wrapped[0]).marginRight) + dotWidth).toBeLessThanOrEqual(parseFloat(getComputedStyle(meta).columnGap) + 1);
  });

  // a1-c-fold-lead-group-detail-laptop: die Knöpfe rutschten unter Titel und lange Metazeile (Vorlauf 42 %).
  it('Seitenkopf: Knöpfe bleiben rechts neben Titel und umbrechender Metazeile', () => {
    const el = mount(`<main class="wrap"><div class="page-head"><div><h1>Anfänger</h1><p class="kid-meta">
      <span>Freitag, 17:00–18:00 neue Mittelschule Schwaz, Turnsaal hinten links</span><span>Zugriff: claude-dev-trainer, tina</span></p></div>
      <div class="actions"><a class="btn primary">Anwesenheit abhaken</a><a class="btn">Kind anlegen</a></div></div></main>`);
    const left = el.querySelector('.page-head > div')!.getBoundingClientRect();
    const actions = el.querySelector('.page-head > .actions')!.getBoundingClientRect();
    expect(actions.top).withContext('eine Reihe').toBeLessThan(left.bottom);
    expect(actions.left).toBeGreaterThanOrEqual(left.right);
    expect(el.querySelector('.actions')!.getBoundingClientRect().height).withContext('Knöpfe nicht umgebrochen').toBeLessThan(60);
  });

  // a1-c-legal-card-vs-header-width: die 560-px-Karte stand mittig, Wortmarke und Reiter ≈ 150 px weiter links.
  it('Rechtsseiten: Karte linksbündig an der Spaltenkante', () => {
    const el = mount(`<ch-root><main class="wrap"><app-privacy><div class="legal-container"><div class="mat-mdc-card" style="width: 100%">
      </div></div></app-privacy></main></ch-root>`);
    const box = el.querySelector('.legal-container')!;
    expect(getComputedStyle(box).justifyContent).toBe('flex-start');
    expect(el.querySelector('.mat-mdc-card')!.getBoundingClientRect().left).toBeCloseTo(box.getBoundingClientRect().left, 0);
  });

  // a1-c-wide-matrix-ultrawide: ab 2400 px nur Seiten mit Anwesenheitsmatrix 1880 px breit, sonst 860 px; darunter unverändert.
  it('Breitbild-Stufe: Gruppenseite mit Matrix ab 2400 px breit, andere Seiten und kleinere Fenster unverändert', () => {
    const css = Array.from(document.styleSheets).map(sheet => {
      try { return Array.from(sheet.cssRules).map(r => r.cssText).join('\n'); } catch { return ''; }
    }).join('\n');
    const widthAt = (w: number, matrix: boolean) => {
      const frame = document.createElement('iframe');
      frame.style.cssText = `position: fixed; left: ${-w - 50}px; top: 0; width: ${w}px; height: 900px; border: 0;`;
      document.body.appendChild(frame);
      try {
        const doc = frame.contentDocument!;
        doc.open();
        doc.write(`<!doctype html><html><head><style>${css}</style></head><body><ch-root><main class="wrap">`
          + (matrix ? '<div class="matrix-scroll"></div><section class="diary"><p>x</p></section>' : '<p>Kartei</p>')
          + `</main></ch-root></body></html>`);
        doc.close();
        return {
          wrap: doc.querySelector('main.wrap')!.getBoundingClientRect().width,
          diary: doc.querySelector('.diary')?.getBoundingClientRect().width ?? 0,
        };
      } finally {
        frame.remove();
      }
    };
    expect(widthAt(3440, true).wrap).toBeCloseTo(1880, 0);
    expect(widthAt(3440, true).diary).withContext('Verlauf ≈ 75 Zeichen').toBeLessThanOrEqual(46 * 17 + 1);
    expect(widthAt(3440, false).wrap).withContext('Seite ohne Matrix').toBeCloseTo(860, 0);
    expect(widthAt(1920, true).wrap).withContext('1920 px wie bisher').toBeCloseTo(860, 0);
  });

  // b1-c-ultrawide-matrix-formular-gestreckt: ab 2400 px ist die Matrix nur so breit wie ihre Spalten (≥ 828 px wie
  // darunter), die Klappkästen „Kinder verwalten"/„Gruppe ändern" und die Statuszeile bleiben 828 px; unter 2400 px gleich.
  it('Breitbild-Stufe: Matrix so breit wie ihre Spalten, Formular-Klappkästen bleiben schmal', () => {
    const css = Array.from(document.styleSheets).map(sheet => {
      try { return Array.from(sheet.cssRules).map(r => r.cssText).join('\n'); } catch { return ''; }
    }).join('\n');
    const measure = (w: number, sessions: number) => {
      const frame = document.createElement('iframe');
      frame.style.cssText = `position: fixed; left: ${-w - 50}px; top: 0; width: ${w}px; height: 900px; border: 0;`;
      document.body.appendChild(frame);
      try {
        const doc = frame.contentDocument!;
        const heads = Array.from({ length: sessions }, (_, i) => `<th><a>Fr ${10 + i}.09.</a></th>`).join('');
        const cells = Array.from({ length: sessions }, () => '<td><span class="cell present">✓</span></td>').join('');
        doc.open();
        doc.write(`<!doctype html><html><head><style>${css}</style></head><body><ch-root><main class="wrap">`
          + `<div class="matrix-scroll"><table class="matrix"><thead><tr><th class="name">Kind</th>${heads}<th class="rate">da</th></tr></thead>`
          + `<tbody><tr><th class="name">Auer Anna</th>${cells}<td class="rate">1 von 1</td></tr></tbody></table></div>`
          + `<p class="err status-line">Fehler</p><details class="sheet manage-group" open><summary>Gruppe ändern</summary><section>x</section></details>`
          + `</main></ch-root></body></html>`);
        doc.close();
        const width = (sel: string) => doc.querySelector(sel)!.getBoundingClientRect().width;
        return { matrix: width('.matrix-scroll'), sheet: width('details.sheet'), status: width('.status-line'), wrap: width('main.wrap') };
      } finally {
        frame.remove();
      }
    };
    const one = measure(3440, 1);
    expect(one.wrap).toBeCloseTo(1880, 0);
    expect(one.matrix).withContext('ein Termin: nicht mehr 1 848 px').toBeCloseTo(828, 0);
    expect(one.sheet).toBeCloseTo(828, 0);
    expect(one.status).toBeLessThanOrEqual(828);
    const many = measure(3440, 40);
    expect(many.matrix).withContext('viele Termine: wächst bis zur Seitenbreite').toBeCloseTo(1848, 0);
    const laptop = measure(1920, 1);
    expect([laptop.matrix, laptop.sheet]).withContext('unter 2400 px wie bisher').toEqual([828, 828]);
  });
});
