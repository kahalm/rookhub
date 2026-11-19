import { RepertoireTrainerComponent } from './repertoire-trainer.component';

/**
 * Kopfleiste des Trainers zwischen Handy und Tablet quer (Sichtprüfung der 0.996.0, behoben in 0.999.1): bei
 * 600–768 px passen Zurück, Name, Moduswahl, Reihenfolge-Knopf und ⋮ nicht in eine Zeile — der Name (flex 1,
 * min-width 0) gab allen Platz ab und stand als „S…“ da (gemessen bei 620 px: 28 px für den Namen). Dort gehen
 * Reihenfolge-Knopf und ⋮ gemeinsam in eine zweite Zeile. Karma läuft 1 400 px breit, deshalb in einem iframe mit
 * ECHTER Breite (Muster `shared/navbar/nav-edge.spec.ts`) und mit den kompilierten Komponentenstilen; die
 * Bedienelemente sind Platzhalter in den am Build gemessenen Größen.
 */
describe('RepertoireTrainerComponent Kopfleiste bei 600–768 px', () => {
  let frame: HTMLIFrameElement | undefined;
  afterEach(() => { frame?.remove(); frame = undefined; });

  const SCOPE = 'rt';
  function componentCss(): string {
    const css = ((RepertoireTrainerComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n')
      .replace(/%COMP%/g, SCOPE).replace(/%NS%/g, '');
    const leftover = /%[A-Z_]+%/.exec(css);
    if (leftover) throw new Error(`Platzhalter ${leftover[0]} im Stiltext nicht aufgelöst`);
    return css;
  }

  type Parts = { order?: boolean; more?: 'normal' | 'solo' | false };

  /** Die Leiste wie im Trainer: Zurück (98 px), Name, Moduswahl (152 px), Reihenfolge-Knopf (229 px = längste deutsche
   *  Beschriftung), ⋮ (40 px). */
  async function render(width: number, parts: Parts = {}): Promise<Document> {
    const { order = true, more = 'normal' } = parts;
    frame?.remove();
    frame = document.createElement('iframe');
    frame.style.width = `${width}px`;
    frame.style.height = '400px';
    frame.style.border = '0';
    document.body.appendChild(frame);
    const doc = frame.contentDocument!;
    const loaded = new Promise(r => frame!.addEventListener('load', r, { once: true }));
    // `.stub` steht VOR den Komponentenstilen und ist schwächer als jede ihrer Regeln (die Moduswahl bekommt am Handy
    // ihr `flex: 1 1 100%`, das ⋮ sein `margin-left: auto`) — die Platzhalter geben nur Größe und „schrumpft nicht“ vor.
    doc.open();
    doc.write(`<!doctype html><html><head><style>
        :root { --page-max-width: 1240px; } body { margin: 0; } * { box-sizing: border-box; }
        .stub { display: block; flex: none; border: 0; padding: 0; }
        ${componentCss()}
      </style></head><body><div class="trainer"><div class="bar" id="bar">
        <a class="stub" id="back" style="width:98px;height:40px"></a>
        <span class="title" id="title"><span class="rep-name" id="name">Schwarz: Caro-Kann (ui-sweep)</span></span>
        <div class="stub mode-toggle" id="mode" style="min-width:152px;height:42px"></div>
        ${order ? '<button class="stub order-btn" id="order" style="width:229px;height:40px"></button>' : ''}
        ${more ? `<button class="stub more${more === 'solo' ? ' more-solo' : ''}" id="more" style="width:40px;height:40px"></button>` : ''}
      </div></div></body></html>`);
    doc.close();
    await loaded;
    doc.querySelectorAll('body *').forEach(e => e.setAttribute(`_ngcontent-${SCOPE}`, ''));
    return doc;
  }

  const rect = (doc: Document, id: string) => doc.getElementById(id)!.getBoundingClientRect();
  const middle = (r: DOMRect) => r.top + r.height / 2;

  for (const width of [600, 620, 700, 768]) {
    it(`${width} px: Reihenfolge-Knopf und ⋮ stehen gemeinsam in der zweiten Zeile, der Name behält die erste`, async () => {
      const doc = await render(width);
      const [bar, title, mode, order, more] = ['bar', 'title', 'mode', 'order', 'more'].map(id => rect(doc, id));

      // erste Zeile: Zurück, Name, Moduswahl — der Name füllt, was übrig ist (statt auf „S…“ zu schrumpfen)
      expect(Math.abs(middle(title) - middle(mode))).withContext('Name und Moduswahl in einer Zeile').toBeLessThanOrEqual(2);
      expect(title.width).withContext('Platz für den Namen').toBeGreaterThanOrEqual(bar.width - 98 - 152 - 2 * 12 - 1);
      expect(title.width).withContext('vorher blieben dem Namen hier 28–190 px').toBeGreaterThan(250);
      // zweite Zeile: Knopf links, ⋮ rechts — beide zusammen, keiner allein
      expect(order.top).withContext('Knopf unter der ersten Zeile').toBeGreaterThanOrEqual(mode.bottom);
      expect(Math.abs(middle(order) - middle(more))).withContext('Knopf und ⋮ in einer Zeile').toBeLessThanOrEqual(1);
      expect(Math.abs(order.left - bar.left)).toBeLessThanOrEqual(1);
      expect(Math.abs(more.right - bar.right)).toBeLessThanOrEqual(1);
      // die Umbruch-Leerzeile hat keine Höhe: Abstand wie am Handy (8 px), nicht doppelt
      expect(Math.round(order.top - mode.bottom)).withContext('Zeilenabstand').toBe(8);
      expect(doc.documentElement.scrollWidth).toBeLessThanOrEqual(doc.documentElement.clientWidth);
    });
  }

  for (const width of [769, 900, 1240]) {
    it(`${width} px: alles in einer Zeile (wie bisher)`, async () => {
      const doc = await render(width);
      const [bar, title, mode, order, more] = ['bar', 'title', 'mode', 'order', 'more'].map(id => rect(doc, id));
      for (const r of [title, order, more]) expect(Math.abs(middle(r) - middle(mode))).toBeLessThanOrEqual(2);
      expect(bar.height).toBeLessThanOrEqual(44);
      expect(more.left).toBeGreaterThan(order.right);
      expect(title.width).withContext('Name bei ' + width).toBeGreaterThanOrEqual(140);
    });
  }

  it('700 px, Leerzustand/Gegner-Sitzung (kein Knopf, ⋮ mit more-solo): eine Zeile, keine leere zweite', async () => {
    const doc = await render(700, { order: false, more: 'solo' });
    const [bar, mode, more] = ['bar', 'mode', 'more'].map(id => rect(doc, id));
    expect(Math.abs(middle(more) - middle(mode))).toBeLessThanOrEqual(2);
    expect(bar.height).withContext('keine Leerzeile').toBeLessThanOrEqual(44);
  });

  it('700 px, während des ersten Ladens (Knopf noch nicht da): das ⋮ steht schon rechts in der zweiten Zeile — kein Sprung, wenn der Knopf kommt', async () => {
    const loading = await render(700, { order: false });
    const moreLoading = rect(loading, 'more');
    const modeLoading = rect(loading, 'mode');
    expect(moreLoading.top).toBeGreaterThanOrEqual(modeLoading.bottom);
    const loadedDoc = await render(700);
    const moreLoaded = rect(loadedDoc, 'more');
    expect(Math.round(moreLoading.left)).toBe(Math.round(moreLoaded.left));
    expect(Math.round(moreLoading.top)).toBe(Math.round(moreLoaded.top));
  });

  it('700 px, offline (weder Knopf noch ⋮): eine Zeile', async () => {
    const doc = await render(700, { order: false, more: false });
    expect(rect(doc, 'bar').height).toBeLessThanOrEqual(44);
  });

  it('390 px (Handy) bleibt, wie es war: Moduswahl in eigener Zeile, darunter Knopf links und ⋮ rechts', async () => {
    const doc = await render(390);
    const [bar, title, mode, order, more] = ['bar', 'title', 'mode', 'order', 'more'].map(id => rect(doc, id));
    expect(mode.top).toBeGreaterThanOrEqual(title.bottom - 1);
    expect(order.top).toBeGreaterThanOrEqual(mode.bottom);
    expect(Math.abs(middle(order) - middle(more))).toBeLessThanOrEqual(1);
    expect(Math.abs(more.right - bar.right)).toBeLessThanOrEqual(1);
    expect(Math.round(order.top - mode.bottom)).toBe(8);
  });
});
