/**
 * UI-Sweep 2026-10-10: Gestaltungs-Funde, die nur mit den GLOBALEN Regeln (styles.scss + leaguehub.scss) und echter
 * Viewport-Breite prüfbar sind — gemessen wie in `touch-targets.spec.ts` in einem iframe mit denselben Styles.
 */
describe('LeagueHub: Gestaltung nach dem UI-Sweep 2026-10-10', () => {
  let frame: HTMLIFrameElement | undefined;

  function pageStyles(): string {
    return Array.from(document.querySelectorAll('link[rel="stylesheet"], style')).map(n =>
      n instanceof HTMLLinkElement ? `<link rel="stylesheet" href="${n.href}">` : `<style>${n.textContent}</style>`).join('\n');
  }

  async function render(width: number, body: string, dark = false): Promise<Document> {
    frame = document.createElement('iframe');
    frame.style.width = `${width}px`;
    frame.style.height = '900px';
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

  const box = (doc: Document, id: string) => doc.getElementById(id)!.getBoundingClientRect();
  const style = (doc: Document, id: string) => doc.defaultView!.getComputedStyle(doc.getElementById(id)!);
  /** Zeilen, über die der Text des ersten Textknotens läuft. */
  const lines = (doc: Document, id: string) => {
    const r = doc.createRange();
    r.selectNodeContents(doc.getElementById(id)!.firstChild!);
    return new Set(Array.from(r.getClientRects()).map(x => Math.round(x.top))).size;
  };

  // l-upload-box: im dunklen Modus war der Kasten hell (umgekehrte Farben) — jetzt Kartenfläche mit rotem Balken links.
  it('Aufforderung zum Hochladen: dunkle Kartenfläche mit rotem Balken links, nicht hell', async () => {
    const doc = await render(1000, `<section class="cta" id="cta"><h2 id="h">Hast du gegen Spieler aus der Liga gespielt?</h2>
      <p id="p">Text</p></section><article class="fixture" id="fx"></article>`, true);
    const cta = style(doc, 'cta');
    expect(cta.backgroundColor).toBe(style(doc, 'fx').backgroundColor);
    expect(cta.borderLeftWidth).toBe('4px');
    const [r, g, b] = cta.backgroundColor.match(/\d+/g)!.map(Number);
    expect(r + g + b).withContext('dunkle Fläche').toBeLessThan(200);
    expect(style(doc, 'h').color).not.toBe(style(doc, 'p').color);         // Überschrift hell, Text gedämpft
  });

  // l-fixture-head: die Paarung stand als 13-px-Grauzeile da (eine fremde .match-Regel) — jetzt eine Überschrift.
  it('Begegnung: Paarung als Überschrift, die Abgleich-Zeile der Formular-Prüfung bleibt klein', async () => {
    const doc = await render(1000, `<article class="fixture"><h2 class="match" id="m">Schwaz – Spg Kufstein/Wörgl</h2>
      <p class="when">Runde 3 · Sa 07.11.2026</p><p class="venue" id="v">📍 Vereinslokal, Hubert-Danzl-Straße 1, Schwaz</p></article>
      <span class="match" id="note">nicht erkannt</span>`);
    expect(parseFloat(style(doc, 'm').fontSize)).toBeGreaterThanOrEqual(18);
    expect(Number(style(doc, 'm').fontWeight)).toBeGreaterThanOrEqual(600);
    expect(style(doc, 'note').fontSize).toBe('13px');
    expect(box(doc, 'v').height).toBeLessThan(40);                           // ganze Zeile, nicht abgeschnitten
  });

  // l-names-mobile: Name einzeilig, darunter „2302 · 259 Partien", Prozent rechts.
  it('am Handy: Kandidatenname einzeilig, Elo und Partien klein darunter', async () => {
    const doc = await render(390, `<div class="cands" style="width:300px"><div class="cand first">
      <span class="name" id="n"><button type="button" class="pl" id="plb"><span class="pl-t">Neuschmied-Oberhauser, Siegfried Maximilian</span></button>
        <span class="g muted" id="g">(259)</span></span>
      <span class="elo" id="elo">2302</span><span class="meta" id="meta">2302 · 259 Partien</span>
      <span class="pct" id="pct">40 %</span><span class="bar"><i style="width:40%"></i></span></div></div>`);
    expect(style(doc, 'g').display).toBe('none');
    expect(style(doc, 'elo').display).toBe('none');
    expect(style(doc, 'meta').display).toBe('block');
    expect(box(doc, 'n').height).toBeLessThan(30);                           // eine Zeile
    expect(box(doc, 'meta').top).toBeGreaterThanOrEqual(box(doc, 'n').bottom - 1);
    expect(box(doc, 'pct').left).toBeGreaterThan(box(doc, 'n').right - 1);
    const pl = box(doc, 'plb');                                             // die Trefferfläche bleibt (UX-069)
    expect(doc.elementFromPoint(pl.left + 10, pl.top - 5)?.closest('#plb')).not.toBeNull();
  });

  // l-nav-mobile: Konto-Knopf statt Name + Abmelden, Reiter in EINER Zeile, je ≥ 44 px.
  it('am Handy: ein Konto-Knopf im Kopf, die Reiter in einer scrollbaren Zeile', async () => {
    const tabs = ['Prognosen', 'Vereinspartien', 'Partien hinzufügen', 'Konto-Vorschläge', 'Übertragungen', 'Vereine'];
    const doc = await render(390, `<header class="top"><div class="wrap top-row"><a class="brand">LeagueHub</a><nav class="account">
      <span class="who desk" id="who">patrik</span><button class="btn-sec desk" id="out">Abmelden</button>
      <button class="btn-sec acct-btn mob" id="acct">👤 ▾</button></nav></div>
      <nav class="wrap tabs" id="tabs">${tabs.map((t, i) => `<a id="t${i}">${t}</a>`).join('')}</nav></header>`);
    expect(style(doc, 'who').display).toBe('none');
    expect(style(doc, 'out').display).toBe('none');
    expect(style(doc, 'acct').display).not.toBe('none');
    const tops = tabs.map((_, i) => Math.round(box(doc, `t${i}`).top));
    expect(new Set(tops).size).withContext('eine Zeile').toBe(1);
    for (let i = 0; i < tabs.length; i++) expect(box(doc, `t${i}`).height).toBeGreaterThanOrEqual(44);
    const nav = doc.getElementById('tabs')!;
    expect(nav.scrollWidth).toBeGreaterThan(nav.clientWidth);              // waagrecht scrollbar statt umbrechen
    expect(doc.documentElement.scrollWidth).toBeLessThanOrEqual(390);
  });

  // l-footer: Version und Links auf einer Grundlinie.
  it('Fußzeile: Version und Links auf derselben Grundlinie', async () => {
    for (const w of [390, 1000]) {
      const doc = await render(w, `<footer class="wrap foot"><span id="v">v0.740.0</span><a href="#" id="i">Impressum</a>
        <a href="#" id="d">Datenschutz</a></footer>`);
      const range = (id: string) => { const r = doc.createRange(); r.selectNodeContents(doc.getElementById(id)!); return r.getBoundingClientRect(); };
      expect(Math.abs(range('v').bottom - range('i').bottom)).withContext(`${w} px`).toBeLessThan(2);
      expect(style(doc, 'v').fontSize).toBe('12px');
      frame!.remove();
    }
  });

  // l-account-btns: drei gleich breite Knöpfe; z1-l-konten-knoepfe: am Handy 44 px hoch, 8 px Abstand.
  it('Konto-Vorschläge: drei gleich breite Knöpfe ≥ 44 px am Handy', async () => {
    const doc = await render(390, `<div class="sugg-actions"><button class="btn-sec sa-btn" id="a">Unsicher</button>
      <button class="btn-pri sa-btn" id="b">Gesichert</button><button class="btn-sec sa-btn sa-reject" id="c">Verwerfen</button></div>`);
    const w = ['a', 'b', 'c'].map(id => Math.round(box(doc, id).width));
    expect(Math.max(...w) - Math.min(...w)).toBeLessThanOrEqual(1);
    for (const id of ['a', 'b', 'c']) expect(box(doc, id).height).toBeGreaterThanOrEqual(44);
    expect(box(doc, 'b').left - box(doc, 'a').right).toBeGreaterThanOrEqual(8);
  });

  // l-club-table / l-club-mobile: breit einzeilige Namen und Eröffnung mit „…", am Handy eine Karte je Partie.
  it('Vereinspartien: breit Tabelle mit einzeiligen Namen, am Handy Karte mit Aktionen rechts', async () => {
    const row = `<div class="roster-scroll club-scroll"><table class="rtable club-table"><thead><tr><th>Jahr</th><th>Weiß</th>
      <th>Schwarz</th><th>Ergebnis</th><th class="hide-s">Eröffnung</th><th class="acts"></th></tr></thead><tbody>
      <tr class="game"><td class="num">2026</td><td class="side" id="w">Mitteregger, Gottfried <span class="small">1840</span></td>
      <td class="side">Erlacher, Herbert</td><td class="num">½</td>
      <td class="hide-s small opening" id="o">Caro-Kann-Verteidigung: Vorstoßvariante, Kurzvariante mit 4.Sf3 und 5.Le2 und noch viel mehr</td>
      <td class="acts"></td>
      <td class="card-cell" id="card"><div class="gc-head"><span class="gc-names"><span class="gc-side">Mitteregger, Gottfried</span>
        <span class="gc-vs">–</span><span class="gc-side">Erlacher, Herbert</span></span><b class="gc-result" id="res">½</b></div>
        <div class="gc-meta muted small">2026 · Caro-Kann</div>
        <div class="row-acts gc-acts" id="acts"><button class="btn-link" id="view">Nachspielen</button><button class="btn-link more-btn">⋮</button></div>
      </td></tr></tbody></table></div>`;
    let doc = await render(1240, `<main class="wrap mid">${row}</main>`);
    expect(style(doc, 'card').display).toBe('none');
    expect(lines(doc, 'w')).toBe(1);
    expect(style(doc, 'o').textOverflow).toBe('ellipsis');
    expect(lines(doc, 'o')).toBe(1);
    frame!.remove();

    doc = await render(390, `<main class="wrap">${row}</main>`);
    expect(style(doc, 'card').display).toBe('block');
    expect(style(doc, 'w').display).toBe('none');
    const card = box(doc, 'card');
    expect(card.right - box(doc, 'res').right).toBeLessThan(12);            // Ergebnis rechts
    expect(card.right - box(doc, 'acts').right).toBeLessThan(12);           // Aktionen rechtsbündig
    const scroll = doc.querySelector('.club-scroll') as HTMLElement;
    expect(scroll.scrollWidth).toBeLessThanOrEqual(scroll.clientWidth);
  });

  // l-clubs-table: Region schmal, Quelle als graue zweite Zeile.
  it('Vereine: Name einzeilig, Quelle der Region als zweite Zeile', async () => {
    const doc = await render(1000, `<table class="rtable clubs-tbl"><tbody><tr><td class="club-name" id="n">SK Weilheim</td>
      <td class="hide-s nowrap">Weilheim</td><td class="hide-s region">Bayern<span class="region-src" id="src">Ligamanager + Schachkreis Zugspitze</span></td>
      <td class="num">1</td></tr></tbody></table>`);
    expect(lines(doc, 'n')).toBe(1);
    expect(style(doc, 'src').display).toBe('block');
  });

  // l-filepicker: das native Feld ist unsichtbar, sichtbar ist der deutsche Knopf.
  it('Dateiauswahl: natives Feld unsichtbar, „Datei wählen“ sichtbar', async () => {
    const doc = await render(1000, `<label class="field">PGN-Datei<input type="file" class="file-native" id="f">
      <span class="file-pick"><span class="btn-sec file-btn" id="b">📎 Datei wählen</span><span class="file-name">Keine Datei gewählt</span></span></label>`);
    expect(box(doc, 'f').width).toBeLessThanOrEqual(1);
    expect(box(doc, 'b').width).toBeGreaterThan(40);
  });

  // ── UI-Sweep 2026-10-11, Runde d2 „Erster Blick und Scrollen" ─────────────────────────────────────────────────────
  const shown = (doc: Document, id: string) => style(doc, id).display !== 'none';

  it('am Handy: Werbesatz nur abgemeldet; Auswahl der Prognosen zugeklappt bis „Ändern"', async () => {
    const doc = await render(390, `<p class="wrap lede anon" id="anon">Satz</p><p class="wrap lede" id="in">Satz</p>
      <div class="pick-bar" id="bar"><span class="pick-sum">Landesliga · R2 · Testdorf</span><button class="btn-sec pick-change">Ändern ▾</button></div>
      <div class="pick-area" id="closed"><form class="pick" id="form"></form><div class="stand"><span id="at">Stand</span>
        <div class="lineups-toggle" id="lu"><button class="btn-link">Alle Aufstellungen</button></div></div></div>
      <div class="pick-area open" id="open"><form class="pick" id="form2"></form></div>`);
    expect(shown(doc, 'anon')).toBeTrue();
    expect(shown(doc, 'in')).toBeFalse();
    expect(shown(doc, 'bar')).toBeTrue();
    // z1-l-prognosen-vorlauf-laptop: zugeklappt bleibt nur „Alle Aufstellungen" aus der Datenstand-Zeile
    expect(shown(doc, 'form')).toBeFalse();
    expect(shown(doc, 'at')).toBeFalse();
    expect(shown(doc, 'lu')).toBeTrue();
    expect(shown(doc, 'open')).toBeTrue();
    expect(shown(doc, 'form2')).toBeTrue();
    const wide = await render(1000, `<p class="wrap lede" id="in">Satz</p><div class="pick-bar" id="bar"></div><div class="pick-area" id="closed">
      <form class="pick" id="form"></form><div class="stand"><label class="all-leagues" id="all">alle Ligen</label><span id="at">Stand</span>
      <div class="lineups-toggle" id="lu"><button class="btn-link">Alle Aufstellungen</button></div></div></div>`);
    expect(shown(wide, 'in')).toBeTrue();
    expect(shown(wide, 'bar')).toBeFalse();
    expect(shown(wide, 'form')).toBeTrue();
    // Schalter, Datenstand und Aufstellungen in einer Zeile
    expect(Math.abs(box(wide, 'all').top - box(wide, 'at').top)).toBeLessThan(12);
    expect(Math.abs(box(wide, 'lu').top - box(wide, 'at').top)).toBeLessThan(12);
  });

  it('großer Bildschirm: Bretter zweispaltig, Konto-Vorschläge eines Spielers nebeneinander', async () => {
    const li = (id: string) => `<li class="board" id="${id}"><span class="bno">1</span></li>`;
    const doc = await render(1400, `<main class="wrap mid"><ol class="boards">${li('b1')}${li('b2')}${li('b3')}</ol>
      <article class="sugg-group"><lh-account-suggestions><ul class="sugg-list"><li id="s1">a</li><li id="s2">b</li></ul></lh-account-suggestions></article></main>`);
    expect(box(doc, 'b2').top).toBe(box(doc, 'b1').top);
    expect(box(doc, 'b3').top).toBeGreaterThan(box(doc, 'b1').top);
    expect(box(doc, 's2').top).toBe(box(doc, 's1').top);
    const laptop = await render(1000, `<main class="wrap"><ol class="boards">${li('b1')}${li('b2')}</ol></main>`);
    expect(box(laptop, 'b2').top).toBeGreaterThan(box(laptop, 'b1').top);
  });

  it('lange Tabellen: Kopf klebt; Vereinspartien und Übertragungen ohne eigenen Scroll-Wrapper ab 641 px', async () => {
    const doc = await render(1000, `<div class="roster-scroll club-scroll" id="w1"><table class="rtable"><thead><tr><th id="th">Jahr</th></tr></thead></table></div>
      <div class="roster-scroll bc-scroll" id="w2"></div><div class="roster-scroll" id="w3"></div>`);
    expect(style(doc, 'th').position).toBe('sticky');
    // z1-l-uebertragungen-kopf: die Unterlinie klebt als Schatten mit
    expect(style(doc, 'th').boxShadow).toContain('inset');
    expect(style(doc, 'w1').overflowX).toBe('visible');
    expect(style(doc, 'w2').overflowX).toBe('visible');
    expect(style(doc, 'w3').overflowX).toBe('auto');
    // Übertragungen bleiben am Handy eine Tabelle — auch dort ohne Scroll-Wrapper, damit der Kopf klebt
    const mobile = await render(390, `<div class="roster-scroll bc-scroll" id="w2"></div><div class="roster-scroll club-scroll" id="w1"></div>`);
    expect(style(mobile, 'w2').overflowX).toBe('visible');
    expect(style(mobile, 'w1').overflowX).toBe('auto');
  });

  // z1-l-farbquadrat: das Farbzeichen je Brett ist ein rundes Steinchen, kein Kästchen
  it('Begegnung: Farbe des Gegners als rundes Steinchen', async () => {
    const doc = await render(390, `<ol class="boards"><li class="board"><span class="bno">1</span><span class="sq w" id="sq"></span></li></ol>`);
    expect(style(doc, 'sq').borderTopLeftRadius).toBe('50%');
  });

  // z1-l-hinzufuegen-hinweisschrift: der Hinweis im Feld ist Textschrift, nicht fett wie die Beschriftung
  it('Partien hinzufügen: Hinweis im Feld in normaler Schrift', async () => {
    const doc = await render(1000, `<label class="field" id="f">… oder eine ChessBase-Datenbank<input type="file">
      <span class="small muted" id="h">Alle Dateien der Datenbank auswählen …</span></label>`);
    expect(Number(style(doc, 'h').fontWeight)).toBe(400);
    expect(Number(style(doc, 'f').fontWeight)).toBeGreaterThanOrEqual(600);
  });

  // z1-l-korrigieren-luecke: die Zugliste schließt mit dem Spaltabstand an die Zugkarte an, auch neben einem hohen Brett
  it('Korrigieren: keine Lücke zwischen Zugkarte und Zugliste neben dem Brett', async () => {
    const doc = await render(1000, `<div class="scan-wrap"><div class="scan-layout">
      <section class="panel scan-board" style="height:600px"></section>
      <section class="panel scan-check" id="chk" style="height:150px"></section>
      <section class="panel scan-moves" id="mv" style="height:100px"></section></div></div>`);
    expect(box(doc, 'mv').top - box(doc, 'chk').bottom).toBeLessThanOrEqual(16);
  });

  // z1-l-verein-plus: „+ Hinzufügen" am Handy, nur „+" erst ≤ 380 px (dann groß)
  it('Vereinspartien: Hinzufügen-Knopf am Handy mit Kurzbeschriftung, ≤ 380 px nur „+"', async () => {
    const btn = `<div class="stand"><a class="btn-pri add-games" id="a"><span class="add-plus" id="p">+</span><span class="add-label" id="l">Partien hinzufügen</span><span class="add-short" id="s">Hinzufügen</span></a></div>`;
    const wide = await render(1000, btn);
    expect(shown(wide, 'p')).toBeFalse();
    expect(shown(wide, 's')).toBeFalse();
    expect(box(wide, 'l').width).toBeGreaterThan(40);
    const phone = await render(390, btn);
    expect(shown(phone, 'p')).toBeTrue();
    expect(shown(phone, 's')).toBeTrue();
    expect(box(phone, 'l').width).toBeLessThanOrEqual(1);
    expect(box(phone, 'a').height).toBeGreaterThanOrEqual(44);
    const small = await render(360, btn);
    expect(shown(small, 's')).toBeFalse();
    expect(style(small, 'a').fontSize).toBe('22px');
    expect(box(small, 'a').height).toBeGreaterThanOrEqual(44);
  });

  it('Teilen-Link am Handy: Aufforderung höchstens 160 px, Knöpfe nebeneinander', async () => {
    const doc = await render(390, `<main class="wrap"><section class="cta" id="cta"><div class="cta-head"><h2>Gegen die Liga gespielt? Partien hochladen</h2>
      <button class="chk-btn">i</button></div><div class="cta-actions"><a class="btn-pri" id="a1">Formular fotografieren</a><a class="btn-sec" id="a2">PGN</a></div></section>
      <p class="stand share-stand" id="st">Nur zum Ansehen<span class="desk-only"> · Stand der Daten: 27.09.2026 21:00</span> · <span class="desk-only">Link </span>gültig bis 14.11.2026</p></main>`);
    expect(box(doc, 'cta').height).toBeLessThanOrEqual(160);
    expect(box(doc, 'a2').top).toBe(box(doc, 'a1').top);
    expect(box(doc, 'st').height).toBeLessThan(30);
  });
});
