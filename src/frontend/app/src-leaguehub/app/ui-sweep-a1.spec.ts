import { contrast, parseColor } from '@rh/testing/contrast';

/**
 * UI-Sweep a1 (2026-10-13, LeagueHub): Funde, die nur mit den GLOBALEN Regeln (styles.scss + leaguehub.scss) und echter
 * Fensterbreite prüfbar sind — Muster wie `ui-sweep-layout.spec.ts`: ein iframe mit denselben Styles, so breit wie nötig
 * (auch 2 560 px für die Breitbild-Stufe, Karma selbst ist nur 1 400 px breit).
 */
describe('LeagueHub: Gestaltung nach dem UI-Sweep a1', () => {
  let frame: HTMLIFrameElement | undefined;

  function pageStyles(): string {
    return Array.from(document.querySelectorAll('link[rel="stylesheet"], style')).map(n =>
      n instanceof HTMLLinkElement ? `<link rel="stylesheet" href="${n.href}">` : `<style>${n.textContent}</style>`).join('\n');
  }

  async function render(width: number, body: string, dark = false, height = 900): Promise<Document> {
    frame?.remove();
    frame = document.createElement('iframe');
    frame.style.width = `${width}px`;
    frame.style.height = `${height}px`;
    document.body.appendChild(frame);
    const doc = frame.contentDocument!;
    const loaded = new Promise(r => frame!.addEventListener('load', r, { once: true }));
    doc.open();
    doc.write(`<!doctype html><html${dark ? ' class="dark-theme"' : ''}><head>${pageStyles()}</head><body>${body}</body></html>`);
    doc.close();
    await loaded;
    return doc;
  }

  afterEach(() => { frame?.remove(); frame = undefined; });

  const el = (doc: Document, id: string) => doc.getElementById(id)!;
  const box = (doc: Document, id: string) => el(doc, id).getBoundingClientRect();
  const style = (doc: Document, id: string) => doc.defaultView!.getComputedStyle(el(doc, id));
  const ratio = (doc: Document, fgId: string, bgId: string, prop: 'color' | 'backgroundColor' = 'color') =>
    contrast(parseColor(style(doc, fgId)[prop]), parseColor(style(doc, bgId).backgroundColor));

  for (const dark of [false, true]) {
    const mode = dark ? 'dunkel' : 'hell';

    // a1-l-partien-pruefen-gesperrt: opacity .45 gab hell ≈ 2,6:1 — jetzt feste Farben
    it(`${mode}: gesperrte Knöpfe ohne Deckkraft, Schrift ≥ 4,5:1`, async () => {
      const doc = await render(800, `<main class="wrap"><div class="panel" id="p">
        <button class="btn-pri" id="pri" disabled>Partien prüfen</button>
        <button class="btn-sec" id="sec" disabled>Laden</button></div></main>`, dark);
      expect(style(doc, 'pri').opacity).toBe('1');
      expect(style(doc, 'sec').opacity).toBe('1');
      expect(ratio(doc, 'pri', 'pri')).toBeGreaterThanOrEqual(4.5);
      expect(ratio(doc, 'sec', 'p')).toBeGreaterThanOrEqual(4.5);
    });

    // a1-l-link-browserfarbe: Links ohne Klasse in --ink statt Browserblau (#0000ee) bzw. Lila
    it(`${mode}: Links ohne Klasse in Tinte, Bereichsregeln (Fuß) behalten ihre Farbe`, async () => {
      const doc = await render(800, `<lh-root><main class="wrap"><table class="rtable"><tr><td>
        <a id="a" href="https://lichess.org/broadcast/x" target="_blank">Schach Tirol Open 2026</a></td></tr></table>
        <span id="ink" style="color: var(--ink)">x</span></main>
        <footer class="wrap foot" id="f"><a id="fa" href="#">Impressum</a></footer></lh-root>`, dark);
      expect(style(doc, 'a').color).not.toBe('rgb(0, 0, 238)');
      expect(style(doc, 'a').color).toBe(style(doc, 'ink').color);
      expect(style(doc, 'fa').color).toBe(style(doc, 'f').color);
    });

    // a1-l-passwort-mail-link-rot: Kontaktadresse unter „Link senden" grau wie beim Registrieren, nicht Fehlerrot
    it(`${mode}: „Passwort vergessen": Mail-Adresse grau, lesbar`, async () => {
      const doc = await render(800, `<lh-root><main class="wrap"><app-forgot-password><div class="mat-mdc-card" id="c" style="background: var(--surface)">
        <p class="no-email" id="ne">Schreib uns: <a id="m" href="mailto:x@y">rookhub@oberschm.id</a></p>
        <span id="muted" style="color: var(--muted)">x</span><span id="red" style="color: var(--red)">x</span>
        </div></app-forgot-password></main></lh-root>`, dark);
      expect(style(doc, 'm').color).toBe(style(doc, 'muted').color);
      expect(style(doc, 'm').color).not.toBe(style(doc, 'red').color);
      expect(style(doc, 'ne').opacity).toBe('1');
      expect(ratio(doc, 'm', 'c')).toBeGreaterThanOrEqual(4.5);
    });
  }

  // a1-l-korrigieren-ultrawide: Formular prüfen/korrigieren ab 2400 px auf 1 880 px, das Brett bis zur Fensterhöhe
  it('Formular-Korrektur: ab 2400 px breite Seite und größeres Brett, darunter unverändert', async () => {
    const body = `<main class="wrap wide" id="m"><div class="scan-wrap"><div class="scan-layout with-photo">
      <section class="scan-photo panel">Foto</section>
      <section class="scan-board" id="sb"><app-chess-board id="b" style="display:block;aspect-ratio:1"></app-chess-board></section>
      <section class="scan-check panel">Prüfen</section><section class="scan-moves panel">Züge</section></div></div></main>`;
    const ultra = await render(3440, body, false, 1300);
    expect(box(ultra, 'm').width).toBe(1880);
    const board = box(ultra, 'b').width;
    expect(board).toBeGreaterThan(1300 * 0.62 + 50);          // größer als die alte Grenze 62vh
    expect(board).toBeLessThanOrEqual(1300 - 240 + 1);        // höchstens bis zur Fensterhöhe
    const desk = await render(1920, body, false, 1300);
    expect(box(desk, 'm').width).toBe(1440);
    expect(box(desk, 'b').width).toBeLessThanOrEqual(1300 * 0.62 + 1);
  });

  // a1-l-prognose-ultrawide-6-bretter: 6 Bretter ab 2400 px als 3 + 3, 8 Bretter weiter 4 + 4
  it('Prognose ab 2400 px: 6 Bretter in zwei vollen Dreierreihen, 8 in zwei Viererreihen', async () => {
    const boards = (n: number) => `<main class="wrap mid"><ol class="boards">${Array.from({ length: n }, (_, i) =>
      `<li class="board" id="b${i + 1}"><span class="bno">${i + 1}</span></li>`).join('')}</ol></main>`;
    const six = await render(2560, boards(6));
    expect(box(six, 'b3').top).toBe(box(six, 'b1').top);
    expect(box(six, 'b4').top).toBeGreaterThan(box(six, 'b1').top);
    expect(box(six, 'b6').top).toBe(box(six, 'b4').top);
    const eight = await render(2560, boards(8));
    expect(box(eight, 'b4').top).toBe(box(eight, 'b1').top);
    expect(box(eight, 'b5').top).toBeGreaterThan(box(eight, 'b1').top);
  });

  // a1-l-uebertragungen-ultrawide: die vierspaltige Tabelle nicht über 1 880 px ziehen
  it('Übertragungen ab 2400 px: Tabelle so breit wie darunter, linksbündig', async () => {
    const body = `<main class="wrap mid" id="m"><table class="rtable bc-list" id="t"><tr><td>Schach Tirol Open 2026</td>
      <td>22.08.–29.08.2026</td><td>Innsbruck</td><td class="num">88</td></tr></table></main>`;
    const ultra = await render(2560, body);
    expect(box(ultra, 't').width).toBeLessThanOrEqual(1208);
    expect(box(ultra, 't').left).toBe(box(ultra, 'm').left + 16);
    const desk = await render(1920, body);
    expect(box(desk, 't').width).toBe(1208);
  });

  // a1-l-vereinspartien-tablet-eroeffnung: am Tablet behielt die Eröffnung nur ≈ 55 px („1.d4 Sf…")
  it('Vereinspartien am Tablet: Eröffnung bleibt lesbar breit, nichts läuft quer', async () => {
    const row = (i: number) => `<tr class="game"><td class="num">2026</td>
      <td class="side"><button type="button" class="pl" id="w${i}">Baumgartner-Hollenstein, Armin Mag.</button><span class="small" id="e${i}"> 1904</span></td>
      <td class="side"><span class="anon">Schwaz</span></td><td class="num">1–0</td>
      <td class="hide-s small opening" id="o${i}">1.d4 Sf6 2.c4 e6 3.Sc3 Lb4 4.Dc2 O-O</td><td class="num hide-s small">46</td>
      <td class="acts"><button class="btn-link">Nachspielen</button></td><td class="card-cell">Karte</td></tr>`;
    const doc = await render(820, `<main class="wrap mid"><div class="roster-scroll club-scroll" id="s"><table class="rtable club-table">
      <tbody>${row(1)}${row(2)}</tbody></table></div></main>`);
    expect(box(doc, 'o1').width).toBeGreaterThan(110);
    // Nachprüfung a1: die Elo kürzte mit („Name … .“) — sie bleibt ganz sichtbar (seit b1-l-vereinspartien-tablet-namen
    // kürzt auch der Name nicht mehr, s. ui-sweep-b1.spec.ts).
    expect(style(doc, 'e1').textOverflow).not.toBe('ellipsis');
    const td = (el(doc, 'e1').parentElement as HTMLElement).getBoundingClientRect();
    expect(box(doc, 'e1').right).toBeLessThanOrEqual(td.right + 0.5);
    expect(box(doc, 'e1').width).toBeGreaterThan(20);
    const s = el(doc, 's');
    expect(s.scrollWidth).toBeLessThanOrEqual(s.clientWidth);
  });

  // a1-l-vereine-einleitung-info: der Satz läuft über die Breite, das ⓘ steht am Satzende statt als eigene Spalte
  it('Vereine: Einleitungssatz in einer Zeile, ⓘ direkt dahinter', async () => {
    const doc = await render(1400, `<main class="wrap mid"><section class="clubs-page">
      <p class="muted clubs-help info-line" id="p"><span id="t">Jeder Verein hat eigene Partien, Formulare und Teilen-Links — kein Verein sieht die eines anderen.</span>
      <button type="button" class="chk-btn" id="i">i</button></p></section></main>`);
    expect(style(doc, 'p').display).toBe('block');
    const t = box(doc, 't'), i = box(doc, 'i');
    expect(t.height).toBeLessThan(40);                                   // eine Zeile
    expect(i.left - t.right).toBeLessThan(16);                           // ⓘ am Satzende
    expect(Math.abs((i.top + i.bottom) / 2 - (t.top + t.bottom) / 2)).toBeLessThan(8);
  });

  // a1-l-konten-mehrfachgruppe: Mehrfach-Spieler als umrahmte Fläche, Karten gleich hoch, Knöpfe auf einer Höhe
  it('Konto-Vorschläge: Mehrfach-Gruppe umrahmt, Knöpfe der Karten auf einer Höhe', async () => {
    const card = (id: string, extra: string) => `<li><div class="acc-row"><a href="#">Lichess: ${id}</a></div>
      <p class="small">${extra}</p><div class="sugg-actions" id="${id}"><button class="btn-sec sa-btn">Unsicher</button>
      <button class="btn-pri sa-btn">Gesichert</button><button class="btn-sec sa-btn sa-reject">Verwerfen</button></div></li>`;
    const doc = await render(1400, `<main class="wrap mid"><div class="sugg-groups">
      <article class="sugg-group multi" id="g"><h3><button class="btn-link sugg-name">Werner, Holger</button>
        <span class="muted small">SG Post/Süd Regensburg 1</span><span class="muted small sugg-count" id="c">2 Konten</span></h3>
        <lh-account-suggestions><ul class="sugg-list">${card('a1', 'kurz')}${card('a2', 'lang '.repeat(80))}</ul></lh-account-suggestions>
      </article></div></main>`);
    expect(style(doc, 'g').borderTopWidth).toBe('1px');
    expect(doc.defaultView!.getComputedStyle(el(doc, 'c'), '::before').content).toContain('·');
    expect(Math.abs(box(doc, 'a1').top - box(doc, 'a2').top)).toBeLessThan(1);
  });

  // a1-l-formular-wartezeit: Nachsatz in Textgröße, Strich als ::before
  it('Formular wartet: Nachsatz in Textgröße mit vorangestelltem Strich', async () => {
    const doc = await render(800, `<main class="wrap"><p class="muted" id="p">Das Formular wartet aufs Einlesen …
      <b class="scan-clock">0:07</b> <span class="scan-why" id="w">der Leser schaut alle 5 Minuten nach neuen Formularen.</span></p></main>`);
    expect(style(doc, 'w').fontSize).toBe(style(doc, 'p').fontSize);
    expect(doc.defaultView!.getComputedStyle(el(doc, 'w'), '::before').content).toContain('–');
    // Nachprüfung a1: Angular entfernt den Leerraum vor dem Span — der Strich bringt sein Leerzeichen davor selbst mit.
    expect(doc.defaultView!.getComputedStyle(el(doc, 'w'), '::before').content).toBe('"\u00a0–\u00a0"');
  });
});
