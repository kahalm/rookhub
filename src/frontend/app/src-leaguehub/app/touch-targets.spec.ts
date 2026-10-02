/**
 * Bedienziele von LeagueHub am Handy (UX-069): mindestens 44 px hoch (Projektregel). Gemessen wurde vorher u. a.
 * „Anmelden" 83×33, die Fußzeile ≈ 21 px, „Nachspielen/Bearbeiten/Löschen" 31 px, die Züge der Formular-Korrektur 29 px
 * und „← Deine Formulare" 20 px.
 *
 * Der Karma-Browser ist 1400 px breit — die Handy-Regeln (`@media (max-width: 640px)`) griffen dort nie. Deshalb rendert
 * der Test Muster-Elemente in einem 390 px breiten iframe mit DENSELBEN globalen Regeln (styles.scss + leaguehub.scss,
 * aus `document.styleSheets` kopiert): dort ist der Viewport 390 px, und die Media-Query gilt wie am Handy.
 *
 * Die unsichtbare Trefferfläche der Spielernamen (::after) darf kein anderes Ziel überdecken: bei Überlappung gewinnt das
 * spätere Element. Mit ±11 px fingen die Namen der Vereinstabelle die oberen 9 px von „Nachspielen“/„Bearbeiten“ ab, ein
 * Tipp knapp unter einem Kandidaten/Gemeldeten öffnete die Karte des NÄCHSTEN, die Meldeliste wurde senkrecht scrollbar,
 * und gescrollte Namen der Spielerkarte malten über deren klebenden Kopf.
 */
describe('LeagueHub: Bedienziele am Handy (UX-069)', () => {
  let frame: HTMLIFrameElement;

  /** Dieselben globalen Styles wie die Seite (angular.json → test.styles): verlinkte Dateien als Link, eingebettete als
   *  Text. Bewusst NICHT über `cssRules` kopiert — Chrome serialisiert eine Kurzschreibweise mit var() (`font: … var(--body)`
   *  am body) als leere Einzelwerte, und das iframe hätte eine andere Zeilenhöhe als die Seite. */
  function pageStyles(): string {
    return Array.from(document.querySelectorAll('link[rel="stylesheet"], style')).map(n =>
      n instanceof HTMLLinkElement ? `<link rel="stylesheet" href="${n.href}">` : `<style>${n.textContent}</style>`).join('\n');
  }

  async function render(width: number, body = SAMPLES): Promise<Document> {
    frame = document.createElement('iframe');
    frame.style.width = `${width}px`;
    frame.style.height = '900px';
    document.body.appendChild(frame);
    const doc = frame.contentDocument!;
    const loaded = new Promise(r => frame.addEventListener('load', r, { once: true }));
    doc.open();
    doc.write(`<!doctype html><html><head>${pageStyles()}</head><body>${body}</body></html>`);
    doc.close();
    await loaded;                                     // erst messen, wenn die Styles im iframe geladen sind
    return doc;
  }

  /** Einzelne Muster-Ziele (Kopfzeile, Knöpfe, Umschalter, Züge, ein Spielername, Fußzeile). */
  const SAMPLES = `
      <header class="top"><div class="wrap top-row"><nav class="account"><a class="btn-sec" id="login" href="#">Anmelden</a></nav></div></header>
      <p><a class="back-link" id="back" href="#">← Deine Formulare</a></p>
      <button type="button" class="btn-sec" id="sec">PGN herunterladen</button>
      <a class="btn-pri" id="pri" href="#">Partien hinzufügen</a>
      <div class="row-acts"><button type="button" class="btn-link" id="link">Bearbeiten</button>
        <a class="btn-link" id="alink" href="#">Analyse</a></div>
      <div class="seg" role="group"><button type="button" id="seg" aria-pressed="true">Ersetzen</button><button type="button">Davor einfügen</button></div>
      <div class="moves"><span class="no">1.</span><button type="button" class="ply" id="ply">e4</button><button type="button" class="ply">e5</button></div>
      <div class="cand"><span class="name" style="display:block;margin-top:40px"><button type="button" class="pl" id="pl">Hengl, Philip</button></span></div>
      <footer class="wrap foot"><span>v1</span><a href="#" id="imp">Impressum</a><a href="#" id="priv">Datenschutz</a></footer>`;

  const plBtn = (id: string, name: string) => `<button type="button" class="pl" id="${id}">${name}</button>`;

  /** Vereinstabelle wie club-games-page (UX-035): am Handy steht unter jeder Partie eine eigene Aktionszeile, 2 px unter
   *  den Namen. Dazu die Meldeliste wie fixture-view (.rtable in .roster-scroll) und eine Kandidatenliste (.cands). */
  const LISTS = `
      <div class="roster-scroll club-scroll"><table class="rtable club-table">
        <thead><tr><th class="num">Jahr</th><th>Weiß</th><th>Schwarz</th><th class="num">Ergebnis</th><th class="hide-s">Eröffnung</th>
          <th class="num">Analyse</th><th class="acts"><span class="sr">Aktionen</span></th></tr></thead>
        <tbody>${[0, 1].map(i => `
          <tr class="game"><td class="num">2024</td><td>${plBtn(`w${i}`, 'Hengl, P.')}</td>
            <td><button type="button" class="pl unknown" id="b${i}">Unbekannt <span class="small muted">✎</span></button></td>
            <td class="num">1-0</td><td class="hide-s small">Sizilianisch</td><td class="num small">91 · 88</td><td class="acts"></td></tr>
          <tr class="acts-row"><td colspan="7"><div class="row-acts">
            <button type="button" class="btn-link" id="view${i}">Nachspielen</button><a class="btn-link" id="ana${i}" href="#">Analyse</a>
            <button type="button" class="btn-link" id="edit${i}">Bearbeiten</button><button type="button" class="btn-link" id="del${i}">Löschen</button>
          </div></td></tr>`).join('')}
        </tbody></table></div>
      <div class="roster-scroll" id="rscroll"><table class="rtable">
        <thead><tr><th class="num">Meld.</th><th>Spieler</th><th class="num">Elo</th><th class="num">spielt</th></tr></thead>
        <tbody>${['Alpha, Anton', 'Beta, Berta', 'Gamma, Gustav'].map((n, i) => `
          <tr><td class="num">${i + 1}</td><td>${plBtn(`r${i}`, n)}</td><td class="num">2000</td><td class="num"><b>50 %</b></td></tr>`).join('')}
        </tbody></table></div>
      <div class="cands">${['Hengl, Philip', 'Oberschmid, Patrik', 'Muster, Max'].map((n, i) => `
        <div class="cand${i ? '' : ' first'}"><span class="name">${plBtn(`c${i}`, n)}</span><span class="elo">2000</span>
          <span class="pct">30 %</span><span class="bar"><i style="width:30%"></i></span></div>`).join('')}
      </div>`;

  afterEach(() => frame?.remove());

  const height = (doc: Document, id: string) => doc.getElementById(id)!.getBoundingClientRect().height;

  it('bei 390 px sind Knöpfe, Links, Umschalter, Züge, Fußzeile und Rücklink mindestens 44 px hoch', async () => {
    const doc = await render(390);
    for (const id of ['login', 'back', 'sec', 'pri', 'link', 'alink', 'seg', 'ply', 'imp', 'priv']) {
      expect(height(doc, id)).withContext(id).toBeGreaterThanOrEqual(44);
    }
  });

  it('bei 390 px trifft ein Tipp knapp über oder unter einem Spielernamen noch den Namen', async () => {
    const doc = await render(390);
    const pl = doc.getElementById('pl')!;
    const r = pl.getBoundingClientRect();
    const x = r.left + r.width / 2;
    expect(doc.elementFromPoint(x, r.top - 6)).toBe(pl);
    expect(doc.elementFromPoint(x, r.bottom + 6)).toBe(pl);
  });

  /** Id des Ziels unter einem Punkt dy px von der Ober- bzw. Unterkante eines Elements (negativ = darüber). */
  const at = (doc: Document, id: string, edge: 'top' | 'bottom', dy: number) => {
    const r = doc.getElementById(id)!.getBoundingClientRect();
    return doc.elementFromPoint(r.left + Math.min(r.width / 2, 20), r[edge] + dy)?.closest('[id]')?.id;
  };

  it('bei 390 px fängt kein Spielername die Aktionen der Vereinstabelle darunter ab', async () => {
    const doc = await render(390, LISTS);
    for (const i of [0, 1]) {
      for (const a of ['view', 'ana', 'edit', 'del']) {
        expect(doc.getElementById(`${a}${i}`)!.getBoundingClientRect().height).withContext(`${a}${i}`).toBeGreaterThanOrEqual(44);
        expect(at(doc, `${a}${i}`, 'top', 2)).withContext(`2 px unter der Oberkante von ${a}${i}`).toBe(`${a}${i}`);
      }
    }
    expect(at(doc, 'b0', 'bottom', 1)).withContext('1 px unter dem Namen').toBe('b0');
  });

  it('bei 390 px trifft ein Tipp zwischen zwei Namen den näheren, nie den übernächsten', async () => {
    const doc = await render(390, LISTS);
    for (const [upper, lower] of [['r0', 'r1'], ['r1', 'r2'], ['c1', 'c2']]) {
      expect(at(doc, upper, 'bottom', 3)).withContext(`3 px unter ${upper}`).toBe(upper);
      expect(at(doc, lower, 'top', -3)).withContext(`3 px über ${lower}`).toBe(lower);
    }
  });

  it('bei 390 px ragt der letzte Name der Meldeliste nicht unter die Tabelle (kein senkrechtes Scrollen)', async () => {
    const doc = await render(390, LISTS);
    const scroll = doc.getElementById('rscroll')!;
    expect(scroll.scrollHeight).toBe(scroll.clientHeight);
  });

  it('bei 390 px liegt der klebende Kopf der Spielerkarte über gescrollten Namen', async () => {
    const doc = await render(390, `
      <dialog class="card" id="dlg"><div class="card-head" id="head"><div><h2>Hengl, Philip</h2></div>
        <button type="button" class="card-close" id="close">✕</button></div>
        <div class="card-body"><div style="height:300px"></div>${plBtn('t0', '1. e4')}<div style="height:1200px"></div></div></dialog>`);
    const dlg = doc.getElementById('dlg') as HTMLDialogElement;
    dlg.showModal();
    const head = doc.getElementById('head')!.getBoundingClientRect();
    dlg.scrollTop = doc.getElementById('t0')!.getBoundingClientRect().top - head.top - 20;   // Name liegt jetzt unter dem Kopf
    const t = doc.getElementById('t0')!.getBoundingClientRect();
    expect(t.top).toBeLessThan(doc.getElementById('head')!.getBoundingClientRect().bottom);
    expect(doc.elementFromPoint(t.left + 4, t.top + t.height / 2)?.closest('.card-head')).withContext('Tipp auf den Kopf').not.toBeNull();
    dlg.close();
  });

  it('am PC bleibt alles, wie es war (Knöpfe ≈ 34 px, Fußzeile ohne Polster)', async () => {
    const doc = await render(1000);
    expect(height(doc, 'sec')).toBeLessThan(36);
    expect(height(doc, 'imp')).toBeLessThan(30);
    expect(height(doc, 'seg')).toBeLessThan(40);
  });
});
