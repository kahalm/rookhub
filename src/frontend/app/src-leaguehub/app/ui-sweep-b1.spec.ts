import { contrast, parseColor } from '@rh/testing/contrast';

/**
 * UI-Sweep b1 (2026-10-14, LeagueHub): Funde, die nur mit den GLOBALEN Regeln (styles.scss + leaguehub.scss) und echter
 * Fensterbreite prüfbar sind — Muster wie `ui-sweep-a1.spec.ts`: ein iframe mit denselben Styles, so breit wie nötig.
 * Gekapselte Komponentenregeln, gegen die eine globale Regel gewinnen muss, stehen nachgebaut als `[_ngcontent-x]` daneben.
 */
describe('LeagueHub: Gestaltung nach dem UI-Sweep b1', () => {
  let frame: HTMLIFrameElement | undefined;

  function pageStyles(): string {
    return Array.from(document.querySelectorAll('link[rel="stylesheet"], style')).map(n =>
      n instanceof HTMLLinkElement ? `<link rel="stylesheet" href="${n.href}">` : `<style>${n.textContent}</style>`).join('\n');
  }

  async function render(width: number, body: string, dark = false, extraCss = ''): Promise<Document> {
    frame?.remove();
    frame = document.createElement('iframe');
    frame.style.width = `${width}px`;
    frame.style.height = '900px';
    document.body.appendChild(frame);
    const doc = frame.contentDocument!;
    const loaded = new Promise(r => frame!.addEventListener('load', r, { once: true }));
    doc.open();
    doc.write(`<!doctype html><html${dark ? ' class="dark-theme"' : ''}><head>${pageStyles()}<style>${extraCss}</style></head>`
      + `<body>${body}</body></html>`);
    doc.close();
    await loaded;
    void doc.body.offsetWidth;            // Layout anstoßen, damit Barlow lädt — gemessen wird mit der echten Schrift
    await doc.fonts.ready;
    return doc;
  }

  afterEach(() => { frame?.remove(); frame = undefined; });

  const el = (doc: Document, id: string) => doc.getElementById(id)!;
  const box = (doc: Document, id: string) => el(doc, id).getBoundingClientRect();
  const style = (doc: Document, id: string) => doc.defaultView!.getComputedStyle(el(doc, id));

  for (const dark of [false, true]) {
    const mode = dark ? 'dunkel' : 'hell';

    // b1-l-zeilenmenue-material: Kartenfläche mit Linie statt Material-Grau, „Löschen“ rot und lesbar
    it(`${mode}: ⋮-Menü der Vereinspartien im LeagueHub-Stil, „Löschen“ rot (≥ 4,5:1)`, async () => {
      const doc = await render(800, `<div class="mat-mdc-menu-panel lh-menu" id="p" style="background: var(--mat-menu-container-color)">
        <button class="mat-mdc-menu-item" id="a" style="color: var(--mat-menu-item-label-text-color)">Analyse</button>
        <button class="mat-mdc-menu-item delete-item" id="d">Löschen</button></div>
        <span id="surface" style="background: var(--surface)">x</span><span id="red" style="color: var(--red)">x</span>
        <span id="ink" style="color: var(--ink)">x</span>`, dark);
      expect(style(doc, 'p').borderTopWidth).toBe('1px');
      expect(style(doc, 'p').backgroundColor).toBe(style(doc, 'surface').backgroundColor);
      expect(style(doc, 'a').color).toBe(style(doc, 'ink').color);
      expect(style(doc, 'd').color).toBe(style(doc, 'red').color);
      expect(contrast(parseColor(style(doc, 'd').color), parseColor(style(doc, 'p').backgroundColor))).toBeGreaterThanOrEqual(4.5);
    });

    // b1-l-passwort-mail-deckkraft: die gekapselte Regel `.no-email[_ngcontent]` (0,2,0) setzte weiter opacity .85
    it(`${mode}: „Passwort vergessen“: Hinweis mit Mail-Adresse ohne Deckkraft, Link ≥ 4,5:1`, async () => {
      const doc = await render(800, `<lh-root><main class="wrap"><app-forgot-password>
        <mat-card class="mat-mdc-card" id="c" style="background: var(--surface)">
        <p _ngcontent-x class="auth-hint no-email" id="ne">Keine E-Mail? <a _ngcontent-x id="m" href="mailto:x@y">rookhub@oberschm.id</a></p>
        <span id="muted" style="color: var(--muted)">x</span></mat-card></app-forgot-password></main></lh-root>`, dark,
        '.no-email[_ngcontent-x] { opacity: .85; } .no-email[_ngcontent-x] a[_ngcontent-x] { color: var(--mat-sys-primary); }');
      expect(style(doc, 'ne').opacity).toBe('1');
      expect(style(doc, 'm').color).toBe(style(doc, 'muted').color);
      expect(contrast(parseColor(style(doc, 'm').color), parseColor(style(doc, 'c').backgroundColor))).toBeGreaterThanOrEqual(4.5);
    });
  }

  // b1-l-haekchen-browserblau: Häkchen ohne eigene Regel in 18 px und Tinte; Bereichsregeln und Material bleiben
  it('Häkchen ohne eigene Regel: 18 px in Tinte, eigene Größen (.check, .review-table) und Material-Kästchen unberührt', async () => {
    const doc = await render(800, `<lh-root><main class="wrap">
      <label class="bc-only"><input type="checkbox" id="c1"> nur mit Ligaspielern</label>
      <label class="all-leagues"><input type="checkbox" id="c2"> alle Ligen der Region</label>
      <label class="auto-mine small"><input type="checkbox" id="c3"> meins</label>
      <label class="check"><input type="checkbox" id="k"> Brett</label>
      <input type="checkbox" class="mdc-checkbox__native-control" id="mat">
      <span id="ink" style="color: var(--ink)">x</span></main></lh-root>`);
    for (const id of ['c1', 'c2', 'c3']) {
      expect(style(doc, id).width).withContext(id).toBe('18px');
      expect(style(doc, id).accentColor).withContext(id).toBe(style(doc, 'ink').color);
    }
    expect(style(doc, 'k').width).toBe('15px');
    expect(style(doc, 'mat').width).not.toBe('18px');
  });

  // b1-l-sperrkarte-breit: Sperrkarte in der Breitbild-Spalte nicht über 1 850 px
  it('Sperrkarte: in .wrap.mid ab 2400 px höchstens 760 px, linksbündig; in der schmalen Seite unverändert', async () => {
    const body = `<main class="wrap mid" id="m"><section class="gate" id="g"><h2>Nicht freigeschaltet</h2>
      <p>Die Konto-Vorschläge sehen nur Liga-Verwalter.</p></section></main>`;
    const ultra = await render(2560, body);
    expect(box(ultra, 'g').width).toBeLessThanOrEqual(760);
    expect(box(ultra, 'g').left).toBe(box(ultra, 'm').left + 16);
    const narrow = await render(800, `<main class="wrap" id="m"><section class="gate" id="g">x</section></main>`);
    expect(box(narrow, 'g').width).toBe(748);
  });

  // b1-l-info-panel-rechts-leer: der Rahmen endet mit dem Text (68ch) statt bei 760 px
  it('ⓘ-Kasten: so breit wie sein Text, nie breiter als die Spalte', async () => {
    const text = 'Partien, die Mitglieder von SK Schwaz hochgeladen haben — sie stehen auch auf den Spielerkarten der Gegner. '.repeat(3);
    const doc = await render(1400, `<main class="wrap mid"><div class="info-panel" id="p"><p class="note" id="n">${text}</p></div></main>`);
    expect(box(doc, 'p').width).toBeLessThanOrEqual(box(doc, 'n').width + 26 + 0.5);   // 2 × 12 Polster + 2 × 1 Rand
    const phone = await render(390, `<main class="wrap"><div class="info-panel" id="p"><p class="note">${text}</p></div></main>`);
    expect(box(phone, 'p').width).toBeLessThanOrEqual(390 - 32);
  });

  // b1-l-prognose-info-tabellen-handy: Top 2/Top 3 und „eingetroffen“ lagen rechts außerhalb des Kastens
  it('Prognose-ⓘ am Handy: Treffer- und Kalibrierungstabelle ganz im Kasten, ohne waagrechtes Scrollen', async () => {
    const cells = (a: string) => `<td class="num">${a}<span class="exp">erw. 31 %</span></td>`;
    for (const w of [360, 390]) {
    const doc = await render(w, `<main class="wrap"><article class="fixture"><div class="info-panel">
      <div class="src-scroll" id="s1"><table class="src-tbl stats-tbl"><thead><tr><th></th><th class="num hide-s" id="cnt">Prognosen</th>
        <th class="num">Platz 1</th><th class="num">Top 2</th><th class="num" id="top3">Top 3</th></tr></thead><tbody>
        <tr><td>Landesliga Unterland<span class="src-sub" id="sub">R1 29 % · R2 40 % · R3 54 %</span></td><td class="num hide-s">1.044</td>
        ${cells('29 %')}${cells('40 %')}${cells('100 %')}</tr></tbody></table></div>
      <div class="src-scroll" id="s2"><table class="src-tbl stats-tbl cal-tbl"><thead><tr><th>Angabe</th><th class="num">Fälle</th>
        <th class="num">angesagt</th><th class="num" id="came">eingetroffen</th></tr></thead><tbody>
        <tr><td>20–30 %</td><td class="num">2.229</td><td class="num">24 %</td><td class="num"><b>22 %</b></td></tr></tbody></table></div>
      </div></article></main>`);
    for (const id of ['s1', 's2']) expect(el(doc, id).scrollWidth).withContext(id).toBeLessThanOrEqual(el(doc, id).clientWidth);
    expect(box(doc, 'top3').right).toBeLessThanOrEqual(box(doc, 's1').right + 0.5);
    expect(box(doc, 'came').right).toBeLessThanOrEqual(box(doc, 's2').right + 0.5);
    expect(style(doc, 'sub').display).toBe('none');
    expect(style(doc, 'cnt').display).toBe('none');
    }
    // breit bleibt die Rundenzeile unter dem Liganamen
    const wide = await render(1400, `<table class="src-tbl stats-tbl"><tr><td>L<span class="src-sub" id="sub">R1</span></td></tr></table>`);
    expect(style(wide, 'sub').display).toBe('block');
  });

  // b1-l-hinzufuegen-info-abgesetzt: ⓘ am Satzende statt als eigene Spalte am rechten Rand
  it('„Partien hinzufügen“ am Handy: Einleitung als Absatz, ⓘ direkt hinter dem Satz', async () => {
    const doc = await render(390, `<main class="wrap mid"><section class="club-intro">
      <p class="muted info-line info-inline" id="p"><span id="t">Angenommen wird jede Partie mit bekanntem Gegner. Ohne Anmeldung.</span>
      <button type="button" class="chk-btn" id="i">i</button></p></section></main>`);
    expect(style(doc, 'p').display).toBe('block');
    const rects = el(doc, 't').getClientRects();
    const last = rects[rects.length - 1], i = box(doc, 'i');
    expect(i.left - last.right).toBeLessThan(16);                                  // am Satzende, nicht am rechten Rand
    expect(Math.abs((i.top + i.bottom) / 2 - (last.top + last.bottom) / 2)).toBeLessThan(8);
  });

  // b1-l-vereinspartien-tablet-namen: am Tablet kürzten die Namen mit „…“ (Folge von a1), die Eröffnung stand ganz
  it('Vereinspartien am Tablet: Namen ungekürzt (Elo darunter), nur die Eröffnung kürzt, nichts läuft quer', async () => {
    const row = (i: number) => `<tr class="game"><td class="num">2026</td>
      <td class="side"><button type="button" class="pl" id="w${i}">Wirtenberger, Markus</button><span class="small" id="e${i}"> 1904</span></td>
      <td class="side"><span class="anon">Schwaz</span></td><td class="num">1–0</td>
      <td class="hide-s small opening" id="o${i}">1.d4 Sf6 2.c4 e6 3.Sc3 Lb4 4.Dc2 O-O 5.a3 Lxc3+ 6.Dxc3 b6</td>
      <td class="num hide-s small">46</td><td class="acts"><button class="btn-link">Nachspielen</button></td>
      <td class="card-cell">Karte</td></tr>`;
    const doc = await render(820, `<main class="wrap mid"><div class="roster-scroll club-scroll" id="s"><table class="rtable club-table">
      <tbody>${row(1)}</tbody></table></div></main>`);
    const w = el(doc, 'w1');
    expect(style(doc, 'w1').textOverflow).not.toBe('ellipsis');
    expect(w.scrollWidth).toBeLessThanOrEqual(w.clientWidth);                       // ganzer Name sichtbar
    expect(box(doc, 'e1').top).toBeGreaterThanOrEqual(box(doc, 'w1').bottom - 0.5); // Elo in der zweiten Zeile
    expect(box(doc, 'o1').width).toBeGreaterThan(110);
    expect(style(doc, 'o1').textOverflow).toBe('ellipsis');
    expect(el(doc, 's').scrollWidth).toBeLessThanOrEqual(el(doc, 's').clientWidth);
  });

  // b1-l-vereinspartien-ultrawide: Tabelle ab 2400 px 1 440 px statt 1 850 px, Suchzeile bleibt breit
  it('Vereinspartien ab 2400 px: Tabelle höchstens 1 440 px, linksbündig; darunter volle Spalte', async () => {
    const body = `<main class="wrap mid" id="m"><div class="club-search" id="q"><input></div>
      <div class="roster-scroll club-scroll"><table class="rtable club-table" id="t"><tbody><tr class="game"><td>2026</td>
      <td class="side">Frolik, Michael</td><td class="hide-s small opening">1.d4 Sf6</td><td class="acts">Nachspielen</td></tr>
      </tbody></table></div></main>`;
    const ultra = await render(2560, body);
    expect(box(ultra, 't').width).toBeLessThanOrEqual(1440);
    expect(box(ultra, 't').left).toBe(box(ultra, 'm').left + 16);
    expect(box(ultra, 'q').width).toBeGreaterThan(1800);
    const desk = await render(1920, body);
    expect(box(desk, 't').width).toBe(1208);
  });

  // b1-l-vereinspartien-ultrawide (Nachprüfung): keine Lücken mehr — Eröffnung → „Züge“ und Schwarz → Ergebnis dicht
  it('Vereinspartien ab 2400 px: Spalten nach Inhalt, keine Lücke hinter Eröffnung und Schwarz-Namen', async () => {
    const body = `<main class="wrap mid" id="m"><div class="roster-scroll club-scroll"><table class="rtable club-table" id="t"><tbody>
      <tr class="game"><td class="num">2026</td>
      <td class="side">Frolik, Michael <span class="small">1904</span></td>
      <td class="side"><span id="b">Wirtenberger, Markus</span> <span class="small">1850</span></td><td class="num" id="r">1–0</td>
      <td class="hide-s small opening" id="o"><span id="ot">1.d4 Sf6 2.c4 e6 3.Sc3 Lb4</span></td>
      <td class="num hide-s small" id="z">46</td><td class="acts">Nachspielen</td><td class="card-cell">Karte</td></tr>
      </tbody></table></div></main>`;
    const ultra = await render(2560, body);
    expect(box(ultra, 't').width).toBeLessThanOrEqual(1440);
    expect(box(ultra, 'z').left - box(ultra, 'ot').right).withContext('Eröffnung → Züge').toBeLessThan(120);
    expect(box(ultra, 'r').left - box(ultra, 'b').right).withContext('Schwarz → Ergebnis').toBeLessThan(120);
    expect(style(ultra, 'o').textOverflow).toBe('ellipsis');
    const desk = await render(1920, body);
    expect(box(desk, 't').width).withContext('1920 px wie bisher').toBe(1208);
  });

  // b1-l-korrigieren-zugliste-kasten: seit 0.816.0 in der gemeinsamen Formular-Werkbank geprüft
  // (`sheet-workbench.component.spec.ts`, „Zugliste einspaltig ohne eigenen Rollbereich").

  // b1-l-partienzahl-format: „V(3115)“ — die Klammer klebte am Namen (2 px)
  it('Prognose-Bretter: Partienzahl mit Wortabstand hinter dem Namen', async () => {
    const doc = await render(800, `<div class="cand"><span class="name"><button class="pl" id="n"><span class="pl-t">Pranav, V</span></button>
      <span class="g muted" id="g">(3.115)</span></span></div>`);
    expect(parseFloat(style(doc, 'g').marginLeft)).toBeGreaterThanOrEqual(3.5);   // .3em bei 13 px
    expect(box(doc, 'g').left - box(doc, 'n').right).toBeGreaterThanOrEqual(3.5);
  });

  // b1-l-404-knopf-material: „Zur Startseite“ als LeagueHub-Knopf, Kopf in --cond (schlägt die gekapselte Regel)
  it('Seite nicht gefunden: Kopf in Barlow Semi Condensed, Knopf-Variablen im LeagueHub-Stil', async () => {
    const doc = await render(800, `<lh-root><main class="wrap"><app-not-found><section _ngcontent-x class="not-found">
      <h1 _ngcontent-x id="h">Seite nicht gefunden</h1><p _ngcontent-x>Unter dieser Adresse gibt es nichts.</p>
      <div _ngcontent-x class="nf-actions" id="a"><a href="/">Zur Startseite</a></div></section></app-not-found>
      <span id="ink" style="color: var(--ink)">x</span></main></lh-root>`, false,
      '.not-found h1[_ngcontent-x] { font-size: 1.5rem; margin: 0 0 12px; }');
    expect(style(doc, 'h').fontSize).toBe('26px');
    expect(style(doc, 'h').fontFamily).toContain('Barlow Semi Condensed');
    const a = style(doc, 'a');
    expect(a.getPropertyValue('--mat-button-filled-container-shape').trim()).toBe('6px');
    expect(a.getPropertyValue('--mat-button-filled-container-height').trim()).toBe('44px');
  });
});
