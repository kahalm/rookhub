/**
 * Bedienziele von LeagueHub am Handy (UX-069): mindestens 44 px hoch (Projektregel). Gemessen wurde vorher u. a.
 * „Anmelden" 83×33, die Fußzeile ≈ 21 px, „Nachspielen/Bearbeiten/Löschen" 31 px, die Züge der Formular-Korrektur 29 px
 * und „← Deine Formulare" 20 px.
 *
 * Der Karma-Browser ist 1400 px breit — die Handy-Regeln (`@media (max-width: 640px)`) griffen dort nie. Deshalb rendert
 * der Test Muster-Elemente in einem 390 px breiten iframe mit DENSELBEN globalen Regeln (styles.scss + leaguehub.scss,
 * aus `document.styleSheets` kopiert): dort ist der Viewport 390 px, und die Media-Query gilt wie am Handy.
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

  async function render(width: number): Promise<Document> {
    frame = document.createElement('iframe');
    frame.style.width = `${width}px`;
    frame.style.height = '900px';
    document.body.appendChild(frame);
    const doc = frame.contentDocument!;
    const loaded = new Promise(r => frame.addEventListener('load', r, { once: true }));
    doc.open();
    doc.write(`<!doctype html><html><head>${pageStyles()}</head><body>
      <header class="top"><div class="wrap top-row"><nav class="account"><a class="btn-sec" id="login" href="#">Anmelden</a></nav></div></header>
      <p><a class="back-link" id="back" href="#">← Deine Formulare</a></p>
      <button type="button" class="btn-sec" id="sec">PGN herunterladen</button>
      <a class="btn-pri" id="pri" href="#">Partien hinzufügen</a>
      <div class="row-acts"><button type="button" class="btn-link" id="link">Bearbeiten</button>
        <a class="btn-link" id="alink" href="#">Analyse</a></div>
      <div class="seg" role="group"><button type="button" id="seg" aria-pressed="true">Ersetzen</button><button type="button">Davor einfügen</button></div>
      <div class="moves"><span class="no">1.</span><button type="button" class="ply" id="ply">e4</button><button type="button" class="ply">e5</button></div>
      <div class="cand"><span class="name" style="display:block;margin-top:40px"><button type="button" class="pl" id="pl">Hengl, Philip</button></span></div>
      <footer class="wrap foot"><span>v1</span><a href="#" id="imp">Impressum</a><a href="#" id="priv">Datenschutz</a></footer>
    </body></html>`);
    doc.close();
    await loaded;                                     // erst messen, wenn die Styles im iframe geladen sind
    return doc;
  }

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
    expect(doc.elementFromPoint(x, r.top - 8)).toBe(pl);
    expect(doc.elementFromPoint(x, r.bottom + 8)).toBe(pl);
  });

  it('am PC bleibt alles, wie es war (Knöpfe ≈ 34 px, Fußzeile ohne Polster)', async () => {
    const doc = await render(1000);
    expect(height(doc, 'sec')).toBeLessThan(36);
    expect(height(doc, 'imp')).toBeLessThan(30);
    expect(height(doc, 'seg')).toBeLessThan(40);
  });
});
