import { DestroyRef, NgZone } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { Subject } from 'rxjs';
import { NavbarComponent } from './navbar.component';
import { measureContentEdge, trackNavEdge } from './nav-edge';

/**
 * UI-Sweep b1 (b1-rk-kopfleiste-ultrawide-flucht, DESIGN 4.1): ab 2400 px fluchtet der Kopfinhalt mit der Inhaltskante
 * der JEWEILIGEN Seite (Logo-Kante = Inhalt-Kante ± 16 px). Karma läuft 1 400 px breit — geprüft wird deshalb in einem
 * iframe mit echter Breite (Muster `src-leaguehub/app/ui-sweep-b1.spec.ts`): Messung, Nachführen und die gerenderte
 * Kopfleiste mit der Polster-Regel aus dem kompilierten Navbar-CSS.
 */
describe('Kopfleiste fluchtet ab 2400 px mit der Inhaltskante (b1-rk-kopfleiste-ultrawide-flucht)', () => {
  let frame: HTMLIFrameElement | undefined;
  const destroyers: (() => void)[] = [];
  const zone = { runOutsideAngular: <T>(f: () => T) => f() } as unknown as NgZone;
  const destroyRef = { onDestroy: (f: () => void) => { destroyers.push(f); return () => {}; } } as unknown as DestroyRef;

  /** Padding-Regel der Navbar ab 2400 px, wie sie Angular kompiliert hat (ohne Kapselungs-Platzhalter). */
  function navbarPadding(): string {
    const css = ((NavbarComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n').replace(/\s+/g, ' ');
    const wide = css.slice(css.indexOf('@media (min-width: 2400px)'));
    return /mat-toolbar[^{]*\{ ?padding-inline: ([^;}]+)/.exec(wide)![1].replace(/%NS%/g, '');
  }

  async function render(width: number, page: string): Promise<Document> {
    frame?.remove();
    frame = document.createElement('iframe');
    frame.style.width = `${width}px`;
    frame.style.height = '900px';
    document.body.appendChild(frame);
    const doc = frame.contentDocument!;
    const loaded = new Promise(r => frame!.addEventListener('load', r, { once: true }));
    doc.open();
    doc.write(`<!doctype html><html><head><style>
        body { margin: 0; } * { box-sizing: border-box; }
        .tb { display: flex; height: 64px; }
        @media (min-width: 2400px) { .tb { padding-inline: ${navbarPadding()}; } }
      </style></head><body><div id="app">
        <app-navbar id="nav" style="display:block"><div class="tb" id="tb"><a id="logo">RookHub</a>
          <span style="flex:1"></span><button id="menu">☰</button></div></app-navbar>
        <main id="main"><router-outlet></router-outlet>${page}</main>
      </div></body></html>`);
    doc.close();
    await loaded;
    return doc;
  }

  const frames = (n = 3) => new Promise<void>(r => {
    const step = (k: number) => k <= 0 ? r() : frame!.contentWindow!.requestAnimationFrame(() => step(k - 1));
    step(n);
  });

  afterEach(() => { destroyers.splice(0).forEach(f => f()); frame?.remove(); frame = undefined; });

  const box = (doc: Document, id: string) => doc.getElementById(id)!.getBoundingClientRect();

  // Seitenformen aus dem Fund: Prep 760, Lese-/Rekonstruktionsseiten 1240, /l + /r ≈ 1 600, Partieseite fit-content ≈ 2 510.
  const PAGES: [string, string, number][] = [
    ['Spielervorbereitung (760 px)', `<app-prep-search><div id="c" style="max-width:760px;margin:0 auto;height:300px"></div></app-prep-search>`, 760],
    ['Seitenbreite (1 240 px)', `<app-reconstruct-list><div id="c" style="max-width:min(1240px,96vw);margin:0 auto;height:300px"></div></app-reconstruct-list>`, 1240],
    ['geteilte Linie (≈ 1 600 px)', `<app-shared-line><section><div id="c" style="max-width:1600px;margin:0 auto;height:300px"></div></section></app-shared-line>`, 1600],
    ['Partieseite (fit-content ≈ 2 510 px)', `<app-shared-game><div style="display:flex;justify-content:center"><div id="c" style="width:2510px;height:300px"></div></div></app-shared-game>`, 2510],
  ];

  for (const [name, page, width] of PAGES) {
    it(`${name}: Logo und ☰ fluchten bei 2 560 px mit der Inhaltskante (± 16 px)`, async () => {
      const doc = await render(2560, page);
      expect(measureContentEdge(doc.getElementById('main'))).toBe(width);
      trackNavEdge(doc.getElementById('nav')!, { events: new Subject() } as unknown as Router, zone, destroyRef);
      await frames();
      expect(doc.getElementById('nav')!.style.getPropertyValue('--nav-edge')).toBe(`${width}px`);
      const c = box(doc, 'c');
      expect(Math.abs(box(doc, 'logo').left - c.left - 16)).toBeLessThanOrEqual(1);
      expect(Math.abs(c.right - box(doc, 'menu').right - 16)).toBeLessThanOrEqual(1);
    });
  }

  it('Seite wechselt (Navigation, Inhalt tauscht sich aus): die Kante zieht nach', async () => {
    const doc = await render(2560, PAGES[0][1]);
    const events = new Subject<unknown>();
    trackNavEdge(doc.getElementById('nav')!, { events } as unknown as Router, zone, destroyRef);
    await frames();
    expect(doc.getElementById('nav')!.style.getPropertyValue('--nav-edge')).toBe('760px');
    doc.querySelector('app-prep-search')!.outerHTML = PAGES[2][1];
    events.next(new NavigationEnd(1, '/l/x', '/l/x'));
    await frames();
    expect(doc.getElementById('nav')!.style.getPropertyValue('--nav-edge')).toBe('1600px');
    expect(Math.abs(box(doc, 'logo').left - box(doc, 'c').left - 16)).toBeLessThanOrEqual(1);
  });

  it('randloser Inhalt oder nur ein Ladekreis: keine Variable — Rückfall --page-max-width-wide (1 880 px)', async () => {
    const doc = await render(2560, `<app-analysis><div style="height:300px"></div></app-analysis>`);
    expect(measureContentEdge(doc.getElementById('main'))).toBeNull();
    const spin = await render(2560, `<app-x><div style="width:48px;height:48px;margin:0 auto"></div></app-x>`);
    expect(measureContentEdge(spin.getElementById('main'))).toBeNull();
    trackNavEdge(spin.getElementById('nav')!, { events: new Subject() } as unknown as Router, zone, destroyRef);
    await frames();
    expect(spin.getElementById('nav')!.style.getPropertyValue('--nav-edge')).toBe('');
    // 1 880 px mittig bei 2 560 px: Logo bei 340 + 16.
    expect(Math.round(box(spin, 'logo').left)).toBe((2560 - 1880) / 2 + 16);
  });

  it('nicht mittiger Inhalt: der Kasten reicht bis zur äußeren Kante (Kopf ragt nie in den Inhalt)', async () => {
    const doc = await render(2560, `<app-x><div id="c" style="width:1200px;margin-left:300px;height:300px"></div></app-x>`);
    // links 300 px Abstand, rechts 1 060 px → symmetrischer Kasten 2 560 − 2·300.
    expect(measureContentEdge(doc.getElementById('main'))).toBe(1960);
  });

  it('unter 2400 px: nichts gemessen, keine Variable, Polster unverändert', async () => {
    const doc = await render(1920, PAGES[0][1]);
    trackNavEdge(doc.getElementById('nav')!, { events: new Subject() } as unknown as Router, zone, destroyRef);
    await frames();
    expect(doc.getElementById('nav')!.style.getPropertyValue('--nav-edge')).toBe('');
    expect(doc.defaultView!.getComputedStyle(doc.getElementById('tb')!).paddingLeft).toBe('0px');
  });

  it('Fenster wird schmaler als 2400 px: Variable fällt weg', async () => {
    const doc = await render(2560, PAGES[0][1]);
    trackNavEdge(doc.getElementById('nav')!, { events: new Subject() } as unknown as Router, zone, destroyRef);
    await frames();
    expect(doc.getElementById('nav')!.style.getPropertyValue('--nav-edge')).toBe('760px');
    frame!.style.width = '1920px';
    await frames(4);
    expect(doc.getElementById('nav')!.style.getPropertyValue('--nav-edge')).toBe('');
  });
});
