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
    const wide = await render(1000, `<p class="wrap lede anon" id="in">Satz</p><p class="wrap lede" id="in2">Satz</p><div class="pick-bar" id="bar"></div><div class="pick-area" id="closed">
      <header class="top" id="top"></header><form class="pick" id="form"></form><div class="stand"><label class="all-leagues" id="all">alle Ligen</label><span id="at">Stand</span>
      <div class="lineups-toggle" id="lu"><button class="btn-link">Alle Aufstellungen</button></div></div></div>`);
    expect(shown(wide, 'in')).toBeTrue();
    expect(shown(wide, 'in2')).toBeFalse();                                  // z3-l-kopf-lede-laptop: angemeldet auch breit weg
    expect(style(wide, 'top').paddingTop).toBe('20px');
    expect(shown(wide, 'bar')).toBeFalse();
    expect(shown(wide, 'form')).toBeTrue();
    // Schalter, Datenstand und Aufstellungen in einer Zeile
    expect(Math.abs(box(wide, 'all').top - box(wide, 'at').top)).toBeLessThan(12);
    expect(Math.abs(box(wide, 'lu').top - box(wide, 'at').top)).toBeLessThan(12);
  });

  it('großer Bildschirm: Bretter zweispaltig, mehrere Konto-Vorschläge eines Spielers nebeneinander (.multi, z2-l-konten-spalten)', async () => {
    const li = (id: string) => `<li class="board" id="${id}"><span class="bno">1</span></li>`;
    const doc = await render(1400, `<main class="wrap mid"><ol class="boards">${li('b1')}${li('b2')}${li('b3')}</ol>
      <article class="sugg-group multi"><lh-account-suggestions><ul class="sugg-list"><li id="s1">a</li><li id="s2">b</li></ul></lh-account-suggestions></article></main>`);
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

  // z2-l-konten-spalten: ab 1280 px zwei Spieler nebeneinander; wer mehrere Vorschläge hat, nimmt die volle Breite.
  it('Konto-Vorschläge breit: Spieler in zwei Spalten, mehrere Vorschläge über die volle Breite', async () => {
    const group = (id: string, n: number) => `<article class="sugg-group${n > 1 ? ' multi' : ''}" id="${id}"><h3>Spieler</h3>
      <lh-account-suggestions><ul class="sugg-list">${'<li>chess.com: konto · Nutzername aus dem Namen</li>'.repeat(n)}</ul></lh-account-suggestions></article>`;
    const body = `<main class="wrap mid"><section class="sugg-page"><div class="sugg-groups" id="gs">${group('a', 1)}${group('b', 1)}${group('c', 2)}</div></section></main>`;
    const doc = await render(1366, body);
    expect(box(doc, 'b').top).toBe(box(doc, 'a').top);
    expect(box(doc, 'b').left).toBeGreaterThan(box(doc, 'a').right);
    expect(box(doc, 'c').width).toBeGreaterThan(box(doc, 'gs').width - 2);
    const lis = doc.querySelectorAll('#c li');
    expect(lis[1].getBoundingClientRect().top).toBe(lis[0].getBoundingClientRect().top);
    frame!.remove();
    const phone = await render(390, body);
    expect(box(phone, 'b').top).toBeGreaterThan(box(phone, 'a').bottom - 1);
  });

  // z3-l-konten-raster-loecher: Einzelkarte vor einem Mehrfach-Spieler — die nächste Einzelkarte rückt ins Loch rechts (dense)
  it('Konto-Vorschläge breit: kein Loch neben einer Einzelkarte vor einem Mehrfach-Spieler', async () => {
    const group = (id: string, n: number) => `<article class="sugg-group${n > 1 ? ' multi' : ''}" id="${id}"><h3>Spieler</h3>
      <lh-account-suggestions><ul class="sugg-list">${'<li>chess.com: konto</li>'.repeat(n)}</ul></lh-account-suggestions></article>`;
    const doc = await render(1366, `<main class="wrap mid"><div class="sugg-groups">${group('a', 1)}${group('m', 2)}${group('b', 1)}</div></main>`);
    expect(box(doc, 'b').top).toBe(box(doc, 'a').top);
    expect(box(doc, 'm').top).toBeGreaterThan(box(doc, 'a').top);
  });

  // z3-l-verein-aktion-grundlinie: „Nachspielen" und ⋮ auf der Grundlinie des Zeilentexts
  it('Vereinspartien breit: alle Zellen einer Partie auf einer Grundlinie', async () => {
    const doc = await render(1240, `<main class="wrap mid"><div class="club-scroll"><table class="rtable club-table"><tbody>
      <tr class="game"><td id="y">2026</td><td class="acts" id="a"><div class="row-acts"><button class="btn-link">Nachspielen</button></div></td></tr>
      </tbody></table></div></main>`);
    expect(style(doc, 'y').verticalAlign).toBe('baseline');
    expect(style(doc, 'a').verticalAlign).toBe('baseline');
  });

  // z3-l-register-datenschutz-rot + z3-l-privacy-karte-rechts-leer
  it('Registrieren: Datenschutz-Link grau; Rechtsseiten-Karte endet mit der Textspalte, linksbündig', async () => {
    const doc = await render(1366, `<lh-root><main class="wrap"><app-register><div class="mat-mdc-card"><p class="privacy-note"><a id="p" href="#">Datenschutz</a></p></div></app-register>
      <app-privacy><div class="legal-container" id="lc"><div class="mat-mdc-card" id="card">Text</div></div></app-privacy></main></lh-root>
      <span id="m" style="color: var(--muted)">m</span><span id="r" style="color: var(--red)">r</span>`, true);
    expect(style(doc, 'p').color).toBe(style(doc, 'm').color);
    expect(style(doc, 'p').color).not.toBe(style(doc, 'r').color);
    expect(parseFloat(style(doc, 'card').maxWidth)).toBeLessThan(700);
    expect(style(doc, 'lc').justifyContent).toBe('flex-start');
  });

  // z2-l-register-hinweis: der Hinweis der Masken nicht im Fehlerrot, sondern in --focus
  it('Masken-Hinweis (Registrieren und Login) neutral in --focus statt Rot', async () => {
    const doc = await render(1000, `<style>.site-note, .auth-required { border-left: 3px solid gray; }</style>
      <lh-root><app-register><div class="mat-mdc-card"><p class="site-note" id="n">Hinweis</p></div></app-register>
      <app-login><div class="mat-mdc-card"><p class="auth-required" id="l">Hinweis</p></div></app-login></lh-root>
      <span id="f" style="color: var(--focus)">f</span><span id="r" style="color: var(--red)">r</span>`, true);
    for (const id of ['n', 'l']) {
      expect(style(doc, id).borderLeftColor).toBe(style(doc, 'f').color);
      expect(style(doc, id).borderLeftColor).not.toBe(style(doc, 'r').color);
    }
  });

  // z2-l-verein-steuerzeilen: breit keine eigene Zählerzeile; am Handy bleibt sie.
  it('Vereinspartien: breit Anzahl/PGN neben der Überschrift und Hinzufügen in der Suchzeile, am Handy die Zählerzeile', async () => {
    const body = `<main class="wrap"><section class="club-intro"><div class="club-head" id="hd"><h2 id="h2">Vereinspartien</h2>
      <span class="small muted cg-wide" id="hc">170 Partien</span><button class="btn-link pgn-dl cg-wide" id="hp">PGN herunterladen</button></div></section>
      <form class="club-search" id="fs"><input type="search" /><button class="btn-sec">Suchen</button><a class="btn-pri search-add cg-wide" id="sa">Partien hinzufügen</a></form>
      <div class="stand club-stand" id="st"><span class="cg-narrow" id="sc">170 Partien</span><button class="btn-link pgn-dl cg-narrow">PGN herunterladen</button>
      <a class="btn-pri add-games cg-narrow" id="ad"><span class="add-plus">+</span><span class="add-label">Partien hinzufügen</span><span class="add-short">Hinzufügen</span></a>
      <span class="update-msg" role="status"></span></div></main>`;
    const doc = await render(1366, body);
    expect(shown(doc, 'st')).toBeFalse();
    expect(shown(doc, 'hc')).toBeTrue();
    expect(box(doc, 'hp').top).toBeLessThan(box(doc, 'h2').bottom);
    expect(box(doc, 'sa').top).toBeLessThan(box(doc, 'fs').top + 10);         // in der Suchzeile
    frame!.remove();
    const phone = await render(390, body);
    expect(shown(phone, 'st')).toBeTrue();
    expect(shown(phone, 'sc')).toBeTrue();
    expect(shown(phone, 'hc')).toBeFalse();
    expect(shown(phone, 'sa')).toBeFalse();
  });

  // z2-l-korrigieren-kopfzeile: „Speichern ↓" rechtsbündig, nicht am Ende von 70ch
  it('Korrigieren: „Speichern ↓" steht breit ganz rechts in der Rücklink-Zeile', async () => {
    const doc = await render(1366, `<main class="wrap"><section class="club-intro" id="ci"><p class="back-row"><a class="back-link">← Vereinspartien</a>
      <button type="button" class="btn-link to-save" id="ts">Speichern ↓</button></p>
      <div class="scan-head"><h2 id="h">Vereinspartie korrigieren</h2><p class="muted game-title" id="gt">Schwaz – Gegner (2026) · 0–1</p></div></section></main>`);
    expect(box(doc, 'ci').right - box(doc, 'ts').right).toBeLessThan(4);
    expect(box(doc, 'gt').top).toBeLessThan(box(doc, 'h').bottom);           // Partietitel in der Zeile der Überschrift
  });

  // Falz-Runde 16.10.: am Handy begann das Brett bei 50 % — Rücklink-Zeile entfällt, Kopf in zwei Zeilen
  it('Korrigieren am Handy: „←" vor der Überschrift, „Speichern ↓" in der Zeile des Partietitels', async () => {
    const doc = await render(390, `<main class="wrap"><section class="club-intro" id="ci"><p class="back-row"><a class="back-link" id="bl">← <span class="back-lbl">Vereinspartien</span></a>
      <button type="button" class="btn-link to-save" id="ts">Speichern ↓</button></p>
      <div class="scan-head"><h2 id="h">Vereinspartie korrigieren</h2><button type="button" class="chk-btn" id="i">i</button>
      <p class="muted game-title" id="gt">Schwaz – Frolik, Michael (2026) · 0-1</p></div></section></main>`);
    const bl = box(doc, 'bl'), h = box(doc, 'h'), gt = box(doc, 'gt'), ts = box(doc, 'ts');
    expect(Math.abs((bl.top + bl.bottom) / 2 - (h.top + h.bottom) / 2)).toBeLessThan(6);   // Pfeil in der Titelzeile
    expect(bl.width).toBeLessThan(40);                                                     // nur der Pfeil sichtbar
    expect(ts.top).toBeGreaterThanOrEqual(h.bottom - 2);                                   // „Speichern ↓" unter dem Titel …
    expect(ts.top).toBeLessThan(gt.bottom);                                                // … neben dem Partietitel
    expect(box(doc, 'ci').height).toBeLessThan(110);                                       // vorher eigene Rücklink-Zeile + Titel + Partietitel
    expect(doc.getElementById('bl')!.textContent).toContain('Vereinspartien');            // Ziel für Vorleser
    // d3-l-korrigieren-mobil-kopf-schmal: der Partietitel steht UNTER der Überschrift, nicht als 5-zeilige Spalte daneben.
    expect(gt.top).toBeGreaterThanOrEqual(h.bottom - 2);
    expect(gt.width).toBeGreaterThan(200);
    expect(Math.abs(ts.top - gt.top)).withContext('„Speichern ↓" in der Zeile des Partietitels').toBeLessThan(12);
  });


  // Falz-Runde 16.10.: /verein am Handy 41,6 % Vorlauf → engere Abstände zwischen Satz-, Such- und Zählerzeile
  it('Vereinspartien am Handy: Satz-, Such- und Zählerzeile mit 8 px Abstand', async () => {
    const doc = await render(390, `<main class="wrap"><section class="club-intro"><p class="muted info-line" id="il">„Schwaz“ steht für ein Mitglied ohne Namen.</p></section>
      <form class="club-search" id="cs"><input type="search"><button class="btn-sec">Suchen</button></form>
      <div class="stand club-stand" id="st"><span>170 Partien</span><a class="btn-pri add-games" id="ag"><span class="add-plus">+</span><span class="add-label">Partien hinzufügen</span><span class="add-short">Hinzufügen</span></a></div></main>`);
    expect(style(doc, 'il').marginBottom).toBe('8px');
    expect(style(doc, 'cs').marginBottom).toBe('8px');
    expect(style(doc, 'st').marginTop).toBe('0px');
    expect(style(doc, 'st').marginBottom).toBe('10px');
    expect(box(doc, 'ag').height).toBeGreaterThanOrEqual(44);                             // Tippziel bleibt 44 px
  });

  // z2-l-uebertragungen-meta: am Handy „Zeitraum · Ort" unter dem Titel, die Spalten fallen weg
  it('Übertragungen: am Handy Meta-Zeile statt Zeitraum-/Ort-Spalte', async () => {
    const body = `<main class="wrap"><table class="rtable bc-list"><tbody><tr><td id="t"><a>28th Bavarian International Open</a>
      <span class="bc-meta muted small" id="m">01.11.–09.11.2025 · Bad Wiessee</span></td>
      <td class="muted nowrap hide-s" id="d">01.11.–09.11.2025</td><td class="muted hide-s">Bad Wiessee</td><td class="num">29</td></tr></tbody></table></main>`;
    const phone = await render(390, body);
    expect(shown(phone, 'm')).toBeTrue();
    expect(shown(phone, 'd')).toBeFalse();
    frame!.remove();
    const wide = await render(1000, body);
    expect(shown(wide, 'm')).toBeFalse();
    expect(shown(wide, 'd')).toBeTrue();
  });

  it('Teilen-Link am Handy: Aufforderung höchstens 160 px, Knöpfe nebeneinander', async () => {
    const doc = await render(390, `<main class="wrap"><section class="cta" id="cta"><div class="cta-head"><h2>In der Runde gespielt? Partien hochladen</h2>
      <button class="chk-btn">i</button></div><div class="cta-actions"><a class="btn-pri" id="a1">Formular fotografieren</a><a class="btn-sec" id="a2">PGN</a></div></section>
      <p class="stand share-stand" id="st">Nur zum Ansehen<span class="desk-only"> · Stand der Daten: 27.09.2026 21:00</span> · <span class="desk-only">Link </span>gültig bis 14.11.2026</p></main>`);
    expect(box(doc, 'cta').height).toBeLessThanOrEqual(160);
    expect(box(doc, 'a2').top).toBe(box(doc, 'a1').top);
    expect(box(doc, 'st').height).toBeLessThan(30);
  });

  // u1-l-fuss-schwebt: auf kurzen Seiten hing die Fußzeile mitten im Bild — jetzt unten am Fenster (wie ClubHub)
  it('kurze Seite: Fußzeile an der Fensterunterkante, Inhalt und Fuß behalten ihre Breite', async () => {
    const doc = await render(1400, `<lh-root id="r"><header class="top"><div class="wrap top-row">LeagueHub</div></header>
      <lh-claim-prompt class="wrap"></lh-claim-prompt><main class="wrap" id="m"><p>Nicht freigeschaltet</p></main>
      <footer class="wrap foot" id="f"><span>v1</span><a>Impressum</a></footer></lh-root>`);
    expect(Math.abs(box(doc, 'f').bottom - doc.defaultView!.innerHeight)).toBeLessThan(1);
    expect(box(doc, 'm').width).toBe(780);
    expect(box(doc, 'f').width).toBe(780);
    expect(box(doc, 'f').left).toBe(box(doc, 'm').left);
  });

  it('lange Seite: Fußzeile folgt dem Inhalt (kein Überlappen)', async () => {
    const doc = await render(1400, `<lh-root><main class="wrap" id="m"><div style="height:1500px"></div></main>
      <footer class="wrap foot" id="f"><span>v1</span></footer></lh-root>`);
    expect(box(doc, 'f').top).toBeGreaterThanOrEqual(box(doc, 'm').bottom);
  });

  // u1-l-prognose-bretter: ab 2400 px die mittleren Seiten auf die zweite Seitenbreite, die Bretter vierspaltig; darunter
  // unverändert (1 240 px, zwei Spalten)
  it('Prognose: ab 2400 px vier Bretter nebeneinander auf 1 880 px, darunter zwei auf 1 240 px', async () => {
    const boards = Array.from({ length: 8 }, (_, i) => `<li class="board" id="b${i + 1}"><span class="bno">${i + 1}</span></li>`).join('');
    const body = `<main class="wrap mid" id="m"><ol class="boards">${boards}</ol></main>`;
    const ultra = await render(2560, body);
    expect(box(ultra, 'm').width).toBe(1880);
    expect(box(ultra, 'b4').top).toBe(box(ultra, 'b1').top);
    expect(box(ultra, 'b5').top).toBeGreaterThan(box(ultra, 'b1').top);
    frame!.remove();
    const desk = await render(1920, body);
    expect(box(desk, 'm').width).toBe(1240);
    expect(box(desk, 'b2').top).toBe(box(desk, 'b1').top);
    expect(box(desk, 'b3').top).toBeGreaterThan(box(desk, 'b1').top);
  });
});
