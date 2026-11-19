import { clickCenter, clickFreeSpot, expectTileNotClickable, expectWholeTileClickable, freeSpot, insideRoundedBox, tileHitProblems } from './tile-hit';

/**
 * Baustein „ganze Kachel klickbar“ (`src/_tile.scss`, DESIGN 5.2) und seine Testhilfe — an echtem Markup mit den
 * globalen Styles des Testlaufs: Material-artiger Knopf (`position: relative`), gekürzter Titel (`overflow: hidden`),
 * Nebenelemente vor und hinter dem Hauptziel, Link im Fließtext, gesperrter Nebenknopf.
 */
describe('rh-tile: ganze Kachel klickbar', () => {
  let host: HTMLElement;

  function mount(html: string): HTMLElement {
    host = document.createElement('div');
    host.style.cssText = 'width: 360px; padding: 24px;';
    host.innerHTML = html;
    document.body.appendChild(host);
    return host;
  }

  // `.matlike-icon`: Materials Symbolknopf setzt `z-index: 0` per Klassenregel — die Kachel muss ihn trotzdem heben.
  const CARD = (tileClass: string, targetAttrs = '') => `
    <style>.matlike-icon { position: relative; z-index: 0; }</style>
    <div class="card ${tileClass}" style="position: relative; display: flex; flex-direction: column; gap: 8px; padding: 16px; border-radius: 12px; background: #ddd;">
      <div style="position: relative; overflow: hidden; white-space: nowrap;"><h3 class="title" style="margin: 0;">Prep: Schett, Franz 2026 mit sehr langem Namen</h3></div>
      <p class="text" style="margin: 0;">Trainingslinien gegen Schett, siehe <a class="inline" href="#hilfe">Hilfe</a>.</p>
      <div style="display: flex; gap: 8px; align-items: center;">
        <button type="button" class="before" style="position: relative;">Trainieren</button>
        <button type="button" class="open rh-tile-target" style="position: relative; overflow: visible;" ${targetAttrs}>Öffnen</button>
        <span style="flex: 1 1 auto;"></span>
        <button type="button" class="off" disabled>Offline</button>
        <button type="button" class="menu matlike-icon" style="width: 40px; height: 40px;">⋮</button>
      </div>
    </div>`;

  afterEach(() => { host?.remove(); document.documentElement.classList.remove('eink'); });

  it('Fläche, Ecken und Textstellen treffen das Hauptziel, jedes Nebenelement sich selbst', () => {
    const el = mount(CARD('rh-tile'));
    expectWholeTileClickable(el.querySelector('.card')!, el.querySelector('.open')!);
  });

  it('Klick auf die leere Fläche löst das Hauptziel aus, Klick auf ein Nebenelement nur dieses', () => {
    const el = mount(CARD('rh-tile'));
    const clicks: string[] = [];
    el.querySelectorAll('button, a').forEach(b => b.addEventListener('click', e => { e.preventDefault(); clicks.push((b as HTMLElement).className.split(' ')[0]); }));
    clickFreeSpot(el.querySelector('.card')!, el.querySelector('.open')!);
    clickCenter(el.querySelector('.title')!);
    expect(clicks).toEqual(['open', 'open']);
    clickCenter(el.querySelector('.before')!);
    clickCenter(el.querySelector('.menu')!);
    clickCenter(el.querySelector('.inline')!);
    expect(clicks).toEqual(['open', 'open', 'before', 'menu', 'inline']);
    // Gesperrter Nebenknopf: der Klick landet auf ihm (und verpufft), nicht auf dem Hauptziel dahinter.
    expect(clickCenter(el.querySelector('.off')!)).toBe(el.querySelector('.off'));
    expect(clicks.length).toBe(5);
  });

  it('die Testhilfe ist keine Dekoration: ohne den Baustein meldet sie die Fläche', () => {
    const el = mount(CARD(''));
    const problems = tileHitProblems(el.querySelector('.card')!, el.querySelector('.open')!);
    expect(problems.some(p => p.startsWith('Ecke oben links'))).withContext(problems.join('\n')).toBeTrue();
    expect(problems.some(p => p.startsWith('Text „Prep: Schett'))).toBeTrue();
    expect(problems).toContain('kein Punkt außerhalb des Knopfs/Links trifft das Hauptziel <button.open.rh-tile-target>');
    expect(() => freeSpot(el.querySelector('.card')!, el.querySelector('.open')!)).toThrowError(/keine freie Stelle/);
  });

  it('die Testhilfe meldet ein Nebenelement, das unter der Fläche liegt', () => {
    const el = mount(CARD('rh-tile'));
    (el.querySelector('.before') as HTMLElement).style.zIndex = '-1';
    const problems = tileHitProblems(el.querySelector('.card')!, el.querySelector('.open')!);
    expect(problems).toEqual(['Mitte des Nebenelements <button.before> trifft <button.open.rh-tile-target>']);
    (el.querySelector('.before') as HTMLElement).style.zIndex = '';
    expect(tileHitProblems(el.querySelector('.card')!, el.querySelector('.open')!)).toEqual([]);
  });

  it('gesperrtes Hauptziel: die Fläche ist nicht klickbar', () => {
    for (const attrs of ['disabled', 'aria-disabled="true"']) {
      const el = mount(CARD('rh-tile', attrs));
      expectTileNotClickable(el.querySelector('.card')!, el.querySelector('.open')!);
      host.remove();
    }
  });

  it('ohne rh-tile (Bearbeiten-Modus) bleibt nur der Knopf selbst klickbar — und nie das ganze Fenster', () => {
    const el = mount(CARD(''));
    const target = el.querySelector('.open')!;
    expectTileNotClickable(el.querySelector('.card')!, target);
    for (const [x, y] of [[5, 5], [window.innerWidth - 5, 5], [5, window.innerHeight - 5], [window.innerWidth / 2, window.innerHeight - 5]]) {
      expect(document.elementFromPoint(x, y)).not.toBe(target);
    }
    expect(getComputedStyle(target, '::after').content).toBe('none');
  });

  it('eine Zeile als Kachel in einer Sammel-Karte: die Fläche endet an der Zeile', () => {
    const el = mount(`
      <div class="collect" style="padding: 16px; background: #ddd;">
        <div class="row rh-tile" style="display: flex; flex-direction: column; gap: 6px; padding: 4px 0;">
          <a class="name rh-tile-target" href="#kurs" style="overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">Kurs mit einem langen Namen</a>
          <div style="display: flex; gap: 8px;"><button type="button" class="seq">Der Reihe nach</button><button type="button" class="rnd">Zufällig</button></div>
        </div>
        <button type="button" class="all" style="margin-top: 12px;">Alle Kurse</button>
      </div>`);
    const row = el.querySelector('.row')!, name = el.querySelector('.name')!;
    expectWholeTileClickable(row, name);
    const collect = el.querySelector('.collect')!.getBoundingClientRect();
    expect(document.elementFromPoint(collect.left + 4, collect.top + 4)).toBe(el.querySelector('.collect'));
    expect(clickCenter(el.querySelector('.all')!)).toBe(el.querySelector('.all'));
  });

  // CI 19.11. (Chrome 157, lokal 149 grün): Chrome wertet gerundetes Abschneiden (overflow + border-radius) beim
  // Treffertest inzwischen auch für die fest positionierte Fläche aus. 2 px in der Ecke einer solchen Karte liegt NEBEN
  // der Karte (dahinter das Raster bzw. der Rahmen der Liste) — die Messpunkte müssen in der sichtbaren Form liegen.
  describe('gerundete, abschneidende Kacheln', () => {
    const ROUND = (tileClass: string) => `
      <div class="grid" style="padding: 12px; background: #bbb;">
        <div class="card ${tileClass}" style="position: relative; overflow: hidden; display: flex; flex-direction: column; gap: 8px; padding: 16px; border-radius: 12px; background: #ddd;">
          <a class="open rh-tile-target" href="#buch">Mein System</a>
          <p class="text" style="margin: 0;">Aaron Nimzowitsch</p>
          <div style="display: flex;"><span style="flex: 1 1 auto;"></span><button type="button" class="menu">⋮</button></div>
        </div>
      </div>`;
    const LIST = `
      <ul class="cards" style="list-style: none; margin: 0; padding: 0; background: #ddd; border: 1px solid #555; border-radius: 10px; overflow: hidden;">
        <li class="kid rh-tile" style="display: grid; grid-template-columns: minmax(0, 1fr) auto; align-items: center; padding: 10px 14px;">
          <a class="kid-main rh-tile-target" href="#kind" style="display: flex; align-items: center; gap: 12px; padding: 2px 0;">Anna Auer</a>
          <a class="tel" href="tel:0660">0660 111</a>
        </li>
      </ul>`;

    it('Karte mit overflow: hidden und 12 px Rundung: die ganze sichtbare Form trifft das Hauptziel', () => {
      const el = mount(ROUND('rh-tile'));
      expectWholeTileClickable(el.querySelector('.card')!, el.querySelector('.open')!);
    });

    it('Zeile in einer gerundeten, abschneidenden Liste (ClubHub-Kartei): ebenso', () => {
      const el = mount(LIST);
      expectWholeTileClickable(el.querySelector('.kid')!, el.querySelector('.kid-main')!);
    });

    it('die Eckpunkte liegen im Eckbogen, nicht daneben — und ohne den Baustein melden sie die Karte weiter', () => {
      const el = mount(ROUND(''));
      const card = el.querySelector('.card')!, box = card.getBoundingClientRect();
      const problems = tileHitProblems(card, el.querySelector('.open')!);
      const corner = problems.find(p => p.startsWith('Ecke oben links'))!;
      expect(corner).withContext(problems.join('\n')).toContain('trifft <div.card>');   // die Karte selbst, nicht das Raster dahinter
      const [x, y] = /\((\d+)\/(\d+)\)/.exec(corner)!.slice(1).map(Number);
      // 12 px Rundung: auf der Diagonale liegt der Bogen 3,5 px von der Ecke; der Messpunkt sitzt knapp dahinter.
      expect(x - box.left).toBeGreaterThan(3.5);
      expect(x - box.left).toBeLessThan(8);
      expect(Math.abs((x - box.left) - (y - box.top))).toBeLessThan(1.5);
      expect(problems.filter(p => p.startsWith('Ecke')).length).toBe(4);
    });

    it('eine eckige Kachel wird weiter 2 px vom Rand gemessen: ein dicker Rahmen ohne --rh-tile-inset fällt auf', () => {
      const el = mount(CARD('rh-tile'));
      const card = el.querySelector<HTMLElement>('.card')!;
      card.style.borderRadius = '0';
      card.style.borderLeft = '4px solid #333';
      const problems = tileHitProblems(card, el.querySelector('.open')!);
      expect(problems.some(p => p.startsWith('Ecke oben links') && p.includes('trifft <div.card.rh-tile>'))).withContext(problems.join('\n')).toBeTrue();
      card.style.setProperty('--rh-tile-inset', '0 0 0 -4px');
      expect(tileHitProblems(card, el.querySelector('.open')!)).toEqual([]);
    });

    it('stark gerundet (Pille, Kreis): gemessen wird in der Form, und es bleibt der Großteil des Rasters', () => {
      const el = mount(`
        <div style="display: flex; gap: 24px; padding: 12px; background: #bbb;">
          <div class="pill rh-tile" style="overflow: hidden; border-radius: 999px; padding: 14px 28px; background: #ddd;"><a class="rh-tile-target" href="#p">Stufe 3</a></div>
          <div class="disc rh-tile" style="overflow: hidden; border-radius: 50%; width: 96px; height: 96px; display: grid; place-items: center; background: #ddd;"><button type="button" class="rh-tile-target">★</button></div>
        </div>`);
      for (const tile of Array.from(el.querySelectorAll('.rh-tile'))) expectWholeTileClickable(tile, tile.querySelector('.rh-tile-target')!);
    });

    it('insideRoundedBox: Eck-Quadrate zählen nach dem Abstand zum Bogenmittelpunkt', () => {
      const box = { left: 100, top: 100, right: 300, bottom: 200, radii: [12, 12, 0, 12] as [number, number, number, number] };
      expect(insideRoundedBox(box, 102, 102)).toBeFalse();          // 2 px in der gerundeten Ecke: außerhalb des Bogens
      expect(insideRoundedBox(box, 104, 104)).toBeTrue();           // (12 − 4)·√2 = 11,3 < 12
      expect(insideRoundedBox(box, 104, 104, 2)).toBeFalse();       // mit 2 px Abstand zum Rand nicht mehr
      expect(insideRoundedBox(box, 106, 106, 2)).toBeTrue();
      expect(insideRoundedBox(box, 298, 198)).toBeTrue();           // eckige Ecke unten rechts
      expect(insideRoundedBox(box, 298, 102)).toBeFalse();          // oben rechts gerundet
      expect(insideRoundedBox(box, 200, 101)).toBeTrue();           // Kantenmitte
      expect(insideRoundedBox(box, 200, 99)).toBeFalse();
      expect(insideRoundedBox(box, 200, 101, 2)).toBeFalse();
    });
  });

  it('die Kachel verschiebt nichts: gleiche Maße mit und ohne Baustein, kein Seiten-Scroll', () => {
    const plain = mount(CARD('')).querySelector('.card')!.getBoundingClientRect();
    const pageWidth = document.documentElement.scrollWidth;
    host.remove();
    const el = mount(CARD('rh-tile'));
    const tile = el.querySelector('.card')!.getBoundingClientRect();
    expect([Math.round(tile.width), Math.round(tile.height)]).toEqual([Math.round(plain.width), Math.round(plain.height)]);
    expect(document.documentElement.scrollWidth).toBe(pageWidth);
  });

  it('Fokusring um die ganze Kachel, sobald das Hauptziel den Tastaturfokus hat — E-Ink 3 px schwarz', () => {
    const el = mount(CARD('rh-tile'));
    const tile = el.querySelector<HTMLElement>('.card')!, target = el.querySelector<HTMLElement>('.open')!;
    expect(getComputedStyle(tile).outlineStyle).toBe('none');
    target.focus();
    if (!target.matches(':focus-visible')) {
      // Skript-Fokus ohne vorherige Tastatureingabe gilt je nach Browserzustand nicht als :focus-visible — dann wenigstens
      // die Regeln selbst prüfen statt den Fall zu überspringen.
      const rules = Array.from(document.styleSheets).flatMap(sheet => { try { return Array.from(sheet.cssRules); } catch { return []; } })
        .flatMap(r => 'cssRules' in r ? [r, ...Array.from((r as CSSGroupingRule).cssRules)] : [r]).map(r => r.cssText);
      expect(rules.some(t => t.startsWith('.rh-tile:has(.rh-tile-target:focus-visible)') && t.includes('outline:'))).toBeTrue();
      expect(rules.some(t => t.startsWith('.rh-tile .rh-tile-target:focus-visible') && t.includes('outline: none'))).toBeTrue();
      expect(rules.some(t => t.startsWith('html.eink .rh-tile:has(.rh-tile-target:focus-visible)') && t.includes('3px'))).toBeTrue();
      return;
    }
    expect(getComputedStyle(tile).outlineStyle).toBe('solid');
    expect(getComputedStyle(tile).outlineWidth).toBe('2px');
    expect(getComputedStyle(target).outlineStyle).withContext('kein zweiter Ring am Hauptziel').toBe('none');
    document.documentElement.classList.add('eink');
    expect(getComputedStyle(tile).outlineWidth).toBe('3px');
    expect(getComputedStyle(tile).outlineColor).toBe('rgb(0, 0, 0)');
    expect(getComputedStyle(target).outlineStyle).toBe('none');
    // Ein Nebenelement mit Fokus: Ring dort, nicht an der Kachel.
    document.documentElement.classList.remove('eink');
    el.querySelector<HTMLElement>('.menu')!.focus();
    expect(getComputedStyle(tile).outlineStyle).toBe('none');
  });
});

/**
 * Verwendung in den Quelltexten aller Oberflächen (karma.conf.js liefert sie aus): ein Hauptziel steht in einer Kachel,
 * und die Kachel selbst trägt kein `(click)` — der Klick gehört dem echten Link/Knopf (DESIGN 5.2).
 */
describe('rh-tile: Verwendung in den Quelltexten', () => {
  type Served = { __karma__?: { files?: Record<string, unknown> } };
  /** Hauptziel in dieser Datei, die Kachel stellt der Aufrufer (Woodpecker-Inhalt in der Dashboard-Kachel). */
  const TILE_FROM_PARENT = ['src/app/features/dashboard/woodpecker-tile.component.ts'];

  const stripComments = (src: string) => src.replace(/<!--[\s\S]*?-->/g, '').replace(/\/\*[\s\S]*?\*\//g, '');
  const tags = (src: string) => stripComments(src).match(/<[a-zA-Z][^<>]*>/g) ?? [];
  const isTile = (tag: string) => /class="[^"]*\brh-tile\b(?!-)[^"]*"|\[class\.rh-tile\]=/.test(tag);
  const isTarget = (tag: string) => /class="[^"]*\brh-tile-target\b[^"]*"|\[class\.rh-tile-target\]=/.test(tag);

  it('der Sucher erkennt Kachel und Hauptziel, auch als Klassenbindung', () => {
    const src = '<mat-card class="rep-card rh-tile"><a class="x rh-tile-target" routerLink="/a">A</a>'
      + '<li [class.rh-tile]="!editing"><button [class.rh-tile-target]="first">B</button><span class="rh-tile-above"></span>';
    expect(tags(src).filter(isTile).length).toBe(2);
    expect(tags(src).filter(isTarget).length).toBe(2);
  });

  it('jedes Hauptziel hat seine Kachel in derselben Datei, keine Kachel trägt (click)', async () => {
    const served = (globalThis as Served).__karma__?.files ?? {};
    const urls = Object.keys(served).filter(u => /^\/base\/src[\w-]*\/.+\.(ts|html)$/.test(u) && !u.endsWith('.spec.ts'));
    expect(urls.length).withContext('Quelltexte nicht ausgeliefert (karma.conf.js files)').toBeGreaterThan(500);
    const texts = await Promise.all(urls.map(async url => [url.replace('/base/', ''), await (await fetch(url)).text()] as const));
    const problems: string[] = [];
    let used = 0;
    for (const [file, text] of texts) {
      const all = tags(text);
      const tiles = all.filter(isTile), targets = all.filter(isTarget);
      used += targets.length;
      if (targets.length && !tiles.length && !TILE_FROM_PARENT.includes(file)) problems.push(`${file}: rh-tile-target ohne rh-tile`);
      for (const t of tiles) if (/\(click\)=/.test(t)) problems.push(`${file}: (click) an der Kachel ${t.slice(0, 80)}`);
      for (const t of targets) if (!/^<(a|button)[\s>]/.test(t)) problems.push(`${file}: Hauptziel ist kein <a>/<button>: ${t.slice(0, 80)}`);
    }
    expect(problems).toEqual([]);
    expect(used).withContext('der Baustein wird benutzt').toBeGreaterThan(5);
  }, 60000);
});
