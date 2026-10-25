import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { RepertoireDetailComponent } from './repertoire-detail.component';
import { RepertoireExplorerService } from './repertoire-explorer.service';
import { Subject, of } from 'rxjs';

describe('RepertoireDetailComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [RepertoireDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(RepertoireDetailComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });
});

describe('RepertoireDetailComponent Linien-Download', () => {
  const PGN = '[Event "Rep"]\n[White "1A | 2.Sf3"]\n[Black "1) Kapitel"]\n[Result "*"]\n\n'
    + '1. e4 e6 {[%cal Gd7d5][%alt c5 e5]Französisch} 2. Nf3 d5 3. e5 (3. Nc3 Nf6 {Steinitz}) c5 *\n';

  async function make() {
    await TestBed.configureTestingModule({
      imports: [RepertoireDetailComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const comp = TestBed.createComponent(RepertoireDetailComponent).componentInstance;
    comp.repertoire = { id: 13, name: 'Martinović Französisch' } as any;
    comp.viewerService.loadPgn(PGN);
    let saved: Blob | undefined;
    spyOn(URL, 'createObjectURL').and.callFake((b: Blob | MediaSource) => { saved = b as Blob; return 'blob:test'; });
    spyOn(URL, 'revokeObjectURL');
    const click = spyOn(HTMLAnchorElement.prototype, 'click');
    return { comp, click, text: () => saved!.text() };
  }

  it('speichert den Originaltext der Linie — mit Varianten, ohne [%alt]', async () => {
    const { comp, click, text } = await make();

    comp.onDownloadLine(comp.viewerService.lines[0]);

    expect(await text()).toBe('[Event "Rep"]\n[White "1A | 2.Sf3"]\n[Black "1) Kapitel"]\n[Result "*"]\n\n'
      + '1. e4 e6 {[%cal Gd7d5]Französisch} 2. Nf3 d5 3. e5 (3. Nc3 Nf6 {Steinitz}) c5 *\n');
    expect((click.calls.mostRecent().object as HTMLAnchorElement).download)
      .toBe('Martinović_Französisch_1_Kapitel_1A_2_Sf3.pgn');
  });

  it('ohne Originaltext baut sie die Linie aus den geparsten Zügen', async () => {
    const { comp, text } = await make();
    comp.viewerService.rawGames = [];

    comp.onDownloadLine(comp.viewerService.lines[0]);

    const pgn = await text();
    expect(pgn).toContain('1. e4 e6');
    expect(pgn).not.toContain('[%alt');
  });
});

describe('RepertoireDetailComponent Kommentar-Vorschau', () => {
  const PGN = '[Event "Rep"]\n[Result "*"]\n\n1. e4 e6 2. Nf3 d5 *\n';

  async function make() {
    await TestBed.configureTestingModule({
      imports: [RepertoireDetailComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const comp = TestBed.createComponent(RepertoireDetailComponent).componentInstance;
    comp.mode = 'lines';
    comp.viewerService.loadPgn(PGN);
    comp.viewerService.selectLine(0);
    comp.viewerService.goToMove(1);
    return comp;
  }

  it('zeigt die Stellung des angeklickten Zugs auf dem Brett', async () => {
    const comp = await make();
    comp.onCommentMovePreview({ fen: 'PREVIEW-FEN', from: 'd2', to: 'd4' });
    comp.ngDoCheck();
    expect(comp.boardFen).toBe('PREVIEW-FEN');
    expect(comp.boardLastMove).toEqual(['d2', 'd4']);

    comp.exitCommentPreview();
    expect(comp.boardFen).toBe(comp.viewerService.currentFen);
  });

  it('beendet die Vorschau, sobald sich die Stellung ändert', async () => {
    const comp = await make();
    comp.onCommentMovePreview({ fen: 'PREVIEW-FEN', from: 'd2', to: 'd4' });

    comp.viewerService.goForward();
    comp.ngDoCheck();

    expect(comp.commentPreview).toBeNull();
    expect(comp.boardFen).toBe(comp.viewerService.currentFen);
  });

  it('ignoriert Stücke ohne Stellung', async () => {
    const comp = await make();
    comp.onCommentMovePreview({ fen: undefined });
    expect(comp.commentPreview).toBeNull();
  });
});

describe('RepertoireDetailComponent Beliebtheit im Baum', () => {
  const PGN = '[Event "a"]\n\n1. e4 c5 2. Nf3 d6 *\n\n[Event "b"]\n\n1. d4 d5 *\n';
  const SETTINGS = { source: 'local', database: 'masters', ratings: [1800], speeds: ['blitz'], thresholdPercent: 1 };
  const STATS = (total: number) => ({ status: 'ok', retryAfterSeconds: null, source: 'local', database: 'masters',
    total, white: 0, draws: 0, black: 0, opening: null, eco: null, moves: [] });

  async function make(position: jasmine.Spy) {
    await TestBed.configureTestingModule({
      imports: [RepertoireDetailComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: RepertoireExplorerService, useValue: { effectiveSettings: () => of(SETTINGS), position } },
      ],
    }).compileComponents();
    const comp = TestBed.createComponent(RepertoireDetailComponent).componentInstance;
    comp.mode = 'tree';
    comp.treeService.buildTree(PGN);
    return comp;
  }

  it('asks for the statistics of the tree position on screen, with the hole finder selection', async () => {
    const position = jasmine.createSpy('position').and.returnValue(of(STATS(100)));
    const comp = await make(position);

    comp.loadTreePopularity();
    expect(position.calls.mostRecent().args[0]).toBe(comp.treeService.currentFen);
    expect(position.calls.mostRecent().args[1]).toEqual(SETTINGS);
    expect(comp.treePopularity()!.total).toBe(100);

    comp.treeService.selectChild('e4');
    comp.loadTreePopularity();
    expect(position.calls.mostRecent().args[0]).toContain('4P3');

    // Zurück zur Grundstellung: aus dem Speicher.
    comp.treeService.goToRoot();
    comp.loadTreePopularity();
    expect(position).toHaveBeenCalledTimes(2);
    expect(comp.treePopularity()!.total).toBe(100);
  });

  it('a late answer for a position already left is dropped', async () => {
    const slow = new Subject<any>();
    const position = jasmine.createSpy('position').and.returnValues(slow, of(STATS(7)));
    const comp = await make(position);

    comp.loadTreePopularity();
    comp.treeService.selectChild('d4');
    comp.loadTreePopularity();
    slow.next(STATS(999));

    expect(comp.treePopularity()!.total).toBe(7);
  });

  it('a missing token or a rate limit becomes a note', async () => {
    const position = jasmine.createSpy('position').and.returnValue(of({ ...STATS(0), status: 'tokenMissing' }));
    const comp = await make(position);
    comp.loadTreePopularity();
    expect(comp.treePopNote()).toBe('repertoire.tree.popToken');
    expect(comp.treePopularity()).toBeNull();
  });

  it('outside the tree it asks nothing', async () => {
    const position = jasmine.createSpy('position').and.returnValue(of(STATS(1)));
    const comp = await make(position);
    comp.mode = 'lines';
    comp.loadTreePopularity();
    expect(position).not.toHaveBeenCalled();
  });
});

describe('RepertoireDetailComponent ?trainColor= (Trainings-Repertoire, 2026-10-07)', () => {
  const PGN = '[Event "Rep"]\n[Black "Najdorf"]\n\n1. e4 c5 2. Nf3 d6 *\n\n[Event "Rep"]\n[Black "Offen"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 *\n';

  async function make() {
    await TestBed.configureTestingModule({
      imports: [RepertoireDetailComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const comp = TestBed.createComponent(RepertoireDetailComponent).componentInstance;
    comp.id = 77;
    comp.viewerService.loadPgn(PGN);
    comp.trainableGames = comp.viewerService.games;
    return comp;
  }

  afterEach(() => localStorage.removeItem('rookhub_rep_train_chaptercolor_77'));

  it('legt die Farbe einmal für jedes Kapitel fest — im eigenen Repertoire', async () => {
    const comp = await make();
    comp.repertoire = { id: 77, isOwner: true } as any;
    (comp as any).pendingTrainColor = 'w';
    (comp as any).applyTrainColor();
    expect(JSON.parse(localStorage.getItem('rookhub_rep_train_chaptercolor_77')!)).toEqual({ Najdorf: 'w', Offen: 'w' });
    expect((comp as any).pendingTrainColor).toBeNull();
  });

  it('in einem geteilten (fremden) Repertoire nicht', async () => {
    const comp = await make();
    comp.repertoire = { id: 77, isOwner: false } as any;
    (comp as any).pendingTrainColor = 'b';
    (comp as any).applyTrainColor();
    expect(localStorage.getItem('rookhub_rep_train_chaptercolor_77')).toBeNull();
  });
});

/** UI-Sweep z1 (z1-rk-rep-brett-gross): Brett wächst mit der Höhe, Linienkasten nur so hoch wie sein Inhalt. */
describe('RepertoireDetailComponent Layout (z1)', () => {
  const css = ((RepertoireDetailComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n').replace(/\s+/g, ' ');
  it('bindet die Brettgröße an die Fensterhöhe (400–640 px)', () => {
    expect(css).toMatch(/--(%NS%)?board-size: clamp\(400px, calc\(100vh - 300px\), 640px\)/);
    expect(css).toMatch(/\.board-section[^{]*\{ ?width: var\(--(%NS%)?board-size\)/);
  });
  // u1-rk-repertoire-brett: bei 3440 × 1440 blieb das Brett bei 640 px in 1 240 px Breite. Karma läuft 1 400 px breit.
  it('ab 2400 px Breitbild-Breite und Brett bis 1 040 px in Fensterhöhe; darunter unverändert 640 px', () => {
    const start = css.indexOf('@media (min-width: 2400px)');
    expect(start).withContext('Breitbild-Media-Query fehlt').toBeGreaterThan(-1);
    const wide = css.slice(start, css.indexOf('} }', start) + 3);
    expect(wide).toMatch(/max-width: min\(var\(--(%NS%)?page-max-width-wide, 1880px\), 96vw\)/);
    expect(wide).toMatch(/--(%NS%)?board-size: clamp\(400px, calc\(100vh - 300px\), 1040px\)/);
    expect(css.slice(0, start)).not.toContain('1040px');
  });
  it('lässt die Linienliste nur so hoch wie ihren Inhalt, mit Rahmen als Token', () => {
    expect(css).toMatch(/\.side-panel--fit[^{]*\{ ?height: auto; max-height:/);
    expect(css).not.toContain('#e0e0e0');
  });

  /** Inhalt des ersten `@media (max-width: 899.98px)`-Blocks (Karma läuft 1 400 px breit, die Regeln greifen nie). */
  function narrow(): string {
    const start = css.indexOf('@media (max-width: 899.98px)');
    expect(start).toBeGreaterThan(-1);
    return css.slice(start, css.indexOf('} }', start) + 3);
  }

  // a1-rk-linienliste-scrollkasten: Panel mindestens Brett + Hinweiszeile hoch; einspaltig kein Kasten im Kasten.
  it('Panel ab 900 px mindestens so hoch wie Brett + Hinweis; darunter läuft die Liste mit der Seite', () => {
    expect(css).toMatch(/\.side-panel(\[[^\]]*\])? ?\{[^}]*height: max\((calc\()?var\(--(%NS%)?board-size\) \+ 56px\)?, (calc\()?100vh - 260px\)?\)/);
    expect(css).toMatch(/\.side-panel--fit[^{]*\{[^}]*max-height: max\((calc\()?var\(--(%NS%)?board-size\) \+ 56px/);
    expect(css).not.toContain('max(440px');
    expect(narrow()).toMatch(/\.side-panel--fit[^{]*\{ ?height: auto; max-height: none; overflow: visible/);
  });

  it('Weiterscroll-Hinweis: Schatten unten, am Listenende von einer Abdeckung in Seitenfarbe verdeckt', () => {
    const fit = /\.side-panel--fit[^{]*\{([^}]*)\}/.exec(css)![1];
    expect(fit).toMatch(/var\(--(%NS%)?mat-sys-surface\)/);
    expect(fit).toMatch(/no-repeat local/);
    expect(fit).toMatch(/no-repeat scroll/);
  });

  // a1-rk-tablet-brett-400: einspaltig bis 640 px statt fest 400 px.
  it('einspaltig Brett bis 640 px (höchstens Fensterhöhe − 480 px), Filterleiste nie breiter', () => {
    const n = narrow();
    // d4-rk-repertoire-brett-winzig: der Höhenterm hat eine Untergrenze von 300 px (Boox 480 × 640 ergab sonst 160 px).
    expect(n).toMatch(/\.board-section[^{]*\{ ?width: 100%; max-width: min\(100%, 640px, max\(300px, (calc\()?100vh - 480px\)?\)\)/);
    expect(n).not.toContain('max-width: 400px');
    expect(n).toMatch(/\.filter-bar[^{]*\{ ?max-width: 100%/);
  });

  // a1-rk-holes-handy-brett-vor-werkzeug
  it('einspaltig: im Lochfinder ohne Loch steht das Werkzeug vor dem Brett', () => {
    expect(narrow()).toMatch(/\.viewer-layout\.holes-idle[^{]* \.side-panel[^{]*\{ ?order: -1/);
  });

  // Falz-Runde 16.10.: Baum am Tablet 73 % Vorlauf (640-px-Brett darüber) → 600–899 px nebeneinander.
  it('Baum-Modus 600–899 px: Brett und Baum nebeneinander; am Handy Titel einzeilig', () => {
    const start = css.indexOf('@media (min-width: 600px) and (max-width: 899.98px)');
    expect(start).withContext('Tablet-Regel für den Baum fehlt').toBeGreaterThan(-1);
    const tab = css.slice(start, css.indexOf('} }', start) + 3);
    expect(tab).toMatch(/\.viewer-layout--tree[^{]*\{ ?flex-direction: row/);
    expect(tab).toMatch(/\.viewer-layout--tree[^{]* \.board-section[^{]*\{ ?width: min\(56%, (calc\()?100vh - 300px\)?\)/);
    expect(css).toMatch(/\.header-info[^{]* h2[^{]*\{[^}]*white-space: nowrap;[^}]*text-overflow: ellipsis/);
  });

  // a1-rk-repertoire-edit-breite
  it('Bearbeiten hat keinen eigenen 800-px-Deckel mehr', () => {
    expect(css).not.toContain('max-width: 800px');
  });
});

describe('RepertoireDetailComponent Lochfinder-Layout (a1)', () => {
  async function make() {
    await TestBed.configureTestingModule({
      imports: [RepertoireDetailComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    return TestBed.createComponent(RepertoireDetailComponent).componentInstance;
  }

  // a1-rk-holes-karte-leer-hoch
  it('Panel so hoch wie der Inhalt: Linienliste und Lochfinder vor der Suche; mit Suche feste Höhe', async () => {
    const comp = await make();
    comp.mode = 'lines';
    expect(comp.sidePanelFits).toBeTrue();
    comp.mode = 'tree';
    expect(comp.sidePanelFits).toBeFalse();
    comp.mode = 'holes';
    expect(comp.sidePanelFits).toBeTrue();
    comp.holesSearchActive = true;
    expect(comp.sidePanelFits).toBeFalse();
  });

  it('Moduswechsel verwirft Lochfinder-Ergebnis und gewähltes Loch', async () => {
    const comp = await make();
    comp.mode = 'holes';
    comp.holesSearchActive = true;
    comp.holeView = { fen: '8/8/8/8/8/8/8/8 w - - 0 1', lastMove: ['e2', 'e4'] };
    comp.setMode('tree');
    comp.setMode('holes');
    expect(comp.holesSearchActive).toBeFalse();
    expect(comp.holeView).toBeNull();
  });
});

describe('RepertoireDetailComponent Kopf', () => {
  async function render(mode: 'lines' | 'tree' | 'holes'): Promise<HTMLElement> {
    await TestBed.configureTestingModule({
      imports: [RepertoireDetailComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(RepertoireDetailComponent);
    const comp = fixture.componentInstance;
    fixture.detectChanges();   // ngOnInit; die Lade-Anfrage bleibt unbeantwortet — der Kopf wird direkt befüllt
    comp.loading = false;
    comp.repertoire = { id: 7, name: 'Testrepertoire', description: null, isOwner: true, files: [] } as any;
    comp.mode = mode;
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  // UI-Sweep 2026-10-12 (v1-rk-repertoire-abfragen-knopf-wandert): „Abfragen“ steht in Baum/Lochfinder rechts neben
  // der Moduswahl, nicht als dritte Zeile im Titelstapel — der Kopf ist in allen Modi gleich hoch.
  for (const mode of ['tree', 'holes'] as const) {
    it(`${mode}: Abfragen-Knopf in der rechten Kopfgruppe, nicht unter dem Untertitel`, async () => {
      const el = await render(mode);
      expect(el.querySelector('.header-actions .train-btn')).not.toBeNull();
      expect(el.querySelector('.header-actions mat-button-toggle-group')).not.toBeNull();
      expect(el.querySelector('.header-info .train-btn')).toBeNull();
    });
  }

  it('Baum-Modus markiert das Layout (.viewer-layout--tree), der ganze Name steht im title des Titels', async () => {
    const el = await render('tree');
    expect(el.querySelector('.viewer-layout.viewer-layout--tree')).not.toBeNull();
    expect(el.querySelector('.header-info h2')!.getAttribute('title')).toBe('Testrepertoire');
  });
  it('Linien-Modus ohne Baum-Klasse', async () => {
    const el = await render('lines');
    expect(el.querySelector('.viewer-layout--tree')).toBeNull();
  });

  /** Regeln innerhalb `@media (max-width: …)` — Karma läuft mit 1400 px, Handy-Regeln greifen im Test also nie;
   *  geprüft wird, dass sie DA sind und was sie setzen (Muster `app-footer.component.spec.ts`). */
  function narrowRules(maxWidth: string, ...parts: string[]): CSSStyleRule[] {
    const out: CSSStyleRule[] = [];
    for (const sheet of Array.from(document.styleSheets)) {
      let rules: CSSRuleList;
      try { rules = sheet.cssRules; } catch { continue; }
      for (const r of Array.from(rules)) {
        if (!(r instanceof CSSMediaRule) || r.conditionText.replace(/\s/g, '') !== `(max-width:${maxWidth})`) continue;
        for (const inner of Array.from(r.cssRules)) {
          if (inner instanceof CSSStyleRule && inner.selectorText.split(',').some(sel => parts.every(p => sel.includes(p)))) {
            out.push(inner);
          }
        }
      }
    }
    return out;
  }

  // Nachrunde v1-rk-repertoire-abfragen-knopf-wandert (Handy): Abfragen + Moduswahl in EINER Zeile, rechtsbündig,
  // Abfragen am Handy als Symbolknopf mit Namen im aria-label.
  it('tree: Abfragen trägt seinen Namen als aria-label, die Beschriftung ist ein eigenes Stück', async () => {
    const el = await render('tree');
    const btn = el.querySelector('.header-actions .train-btn') as HTMLElement;
    expect(btn.getAttribute('aria-label')).toBe('repertoireTrainer.modeQuiz');
    expect(btn.getAttribute('title')).toBe('repertoireTrainer.modeQuiz');
    expect(btn.querySelector('.train-label')?.textContent?.trim()).toBe('repertoireTrainer.modeQuiz');
  });

  it('Handy: Kopfaktionen umbrechen nicht und stehen rechts; Abfragen-Beschriftung unter 600 px aus', async () => {
    await render('tree');
    const actions = narrowRules('899.98px', '.header-actions');
    expect(actions.some(r => r.style.flexWrap === 'nowrap' && r.style.marginLeft === 'auto')).toBeTrue();
    const label = narrowRules('599.98px', '.train-label');
    expect(label.some(r => r.style.display === 'none')).toBeTrue();
  });

  it('Abfragen und Moduswahl stehen in derselben Zeile, der Kopf ist in Liste und Baum gleich hoch', async () => {
    const heights: number[] = [];
    for (const mode of ['lines', 'tree'] as const) {
      TestBed.resetTestingModule();
      const el = await render(mode);
      document.body.appendChild(el);
      try {
        const head = el.querySelector('.detail-header') as HTMLElement;
        heights.push(Math.round(head.getBoundingClientRect().height));
        if (mode === 'tree') {
          const b = (el.querySelector('.train-btn') as HTMLElement).getBoundingClientRect();
          const g = (el.querySelector('mat-button-toggle-group') as HTMLElement).getBoundingClientRect();
          // gleiche Zeile: senkrechte Mitten fast gleich
          expect(Math.abs((b.top + b.bottom) / 2 - (g.top + g.bottom) / 2)).toBeLessThan(3);
        }
      } finally {
        el.remove();
      }
    }
    expect(heights[0]).toBe(heights[1]);
  });

  // a1-rk-holes-zwei-hauptknoepfe: im Lochfinder ist „Löcher suchen“ der einzige Primärknopf.
  it('holes: Abfragen als Rahmenknopf, im Baum gefüllt; Lochfinder-Werkzeug vorn, solange kein Loch gewählt ist', async () => {
    const holes = await render('holes');
    const hb = holes.querySelector('.header-actions .train-btn') as HTMLElement;
    expect(hb.hasAttribute('mat-stroked-button')).toBeTrue();
    expect(hb.hasAttribute('mat-flat-button')).toBeFalse();
    expect(holes.querySelector('.viewer-layout')!.classList).toContain('holes-idle');
    TestBed.resetTestingModule();
    const tree = await render('tree');
    expect((tree.querySelector('.header-actions .train-btn') as HTMLElement).hasAttribute('mat-flat-button')).toBeTrue();
    expect(tree.querySelector('.viewer-layout')!.classList).not.toContain('holes-idle');
  });

  it('lines: kein Abfragen-Knopf im Kopf (steht in der Linienleiste)', async () => {
    const el = await render('lines');
    expect(el.querySelector('.detail-header .train-btn')).toBeNull();
    expect(el.querySelector('.header-actions mat-button-toggle-group')).not.toBeNull();
  });
});

/** b1-rk-repertoire-filterbrett-fen-start: Endspiel-Repertoire (Linien ab einer FEN) — das Filterbrett steht in deren
 *  Ausgangsstellung statt in der Grundstellung (von dort traf der Filter nie eine Linie). */
describe('RepertoireDetailComponent Stellungsfilter ab FEN', () => {
  const FEN = '4k3/8/3K4/3P4/8/8/8/8 w - - 0 1';
  const game = (moves: string, fen = FEN) => `[Event "End"]\n[SetUp "1"]\n[FEN "${fen}"]\n[Result "*"]\n\n${moves} *\n`;

  async function make(pgn: string) {
    await TestBed.configureTestingModule({
      imports: [RepertoireDetailComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const comp = TestBed.createComponent(RepertoireDetailComponent).componentInstance;
    comp.mode = 'lines';
    comp.viewerService.loadPgn(pgn);
    (comp as any).recomputeFilter();
    return comp;
  }

  it('Brett zeigt die gemeinsame Start-FEN, Filterzüge treffen die Linien, Zugnummern ab der FEN', async () => {
    const comp = await make(game('1. Kc6 Kd8 2. d6') + '\n' + game('1. Ke6 Kd8 2. d6'));
    expect(comp.boardFen).toBe(FEN);
    expect(comp.filterBoardActive).toBeTrue();
    comp.onFilterMove({ san: 'Ke6', from: 'd6', to: 'e6', fen: '4k3/8/4K3/3P4/8/8/8/8 b - - 1 1' } as any);
    expect(comp.visibleLines.length).toBe(1);
    expect(comp.filterMovesText).toBe('1. Ke6');
    comp.resetFilter();
    expect(comp.boardFen).toBe(FEN);
  });

  it('Linien aus verschiedenen Startstellungen: kein Filter, Brett zeigt die erste Startstellung', async () => {
    const other = '4k3/8/3K4/8/3P4/8/8/8 w - - 0 1';
    const comp = await make(game('1. Kc6 Kd8') + '\n' + game('1. Ke6 Kd8', other));
    expect(comp.filterBoardActive).toBeFalse();
    expect(comp.boardFen).toBe(FEN);
  });

  it('normales Repertoire bleibt bei der Grundstellung', async () => {
    const comp = await make('[Event "Rep"]\n[Result "*"]\n\n1. e4 e5 *\n');
    expect(comp.filterBoardActive).toBeTrue();
    expect(comp.boardFen.startsWith('rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w')).toBeTrue();
  });
});

/** b1-rk-keine-beschreibung: der Detailkopf trägt keinen Untertitel „Keine Beschreibung“. */
describe('RepertoireDetailComponent Kopf ohne Beschreibung', () => {
  async function render(description: string | null) {
    await TestBed.configureTestingModule({
      imports: [RepertoireDetailComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: RepertoireExplorerService, useValue: { sources: () => of(null), run: () => of(null), effectiveSettings: () => of(null) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(RepertoireDetailComponent);
    const comp = fixture.componentInstance;
    fixture.detectChanges();   // ngOnInit (lädt per HttpClientTesting — bleibt offen)
    comp.loading = false;
    comp.mode = 'edit';
    comp.repertoire = { id: 1, name: 'Endspiel', description, files: [], isOwner: true } as any;
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('ohne Beschreibung kein Untertitel', async () => {
    const el = await render(null);
    expect(el.querySelector('.header-info h2')?.textContent).toContain('Endspiel');
    expect(el.querySelector('.header-info .subtitle')).toBeNull();
    expect(el.textContent).not.toContain('noDescription');
  });

  it('mit Beschreibung steht sie als Untertitel', async () => {
    const el = await render('Bauernendspiele');
    expect(el.querySelector('.header-info .subtitle')?.textContent).toContain('Bauernendspiele');
  });
});

/** d3-rk-schwarz-repertoire-brett-nicht-gedreht: Brett aus Sicht der Kapitelfarbe, wie im Trainer. */
describe('RepertoireDetailComponent Brett-Ausrichtung (d3)', () => {
  // Najdorf endet mit dem Schwarz-Zug d6 → Schwarz-Kapitel; „Weiss“ endet mit Bb5 → Weiß-Kapitel.
  const BLACK = '[Event "Rep"]\n[Black "Najdorf"]\n\n1. e4 c5 2. Nf3 d6 *\n';
  const WHITE = '[Event "Rep"]\n[Black "Weiss"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 *\n';

  async function make(pgn: string) {
    await TestBed.configureTestingModule({
      imports: [RepertoireDetailComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const comp = TestBed.createComponent(RepertoireDetailComponent).componentInstance;
    comp.id = 78;
    comp.mode = 'lines';
    comp.viewerService.loadPgn(pgn);
    comp.trainableGames = comp.viewerService.games;
    comp.refreshChapterColors();
    return comp;
  }

  afterEach(() => localStorage.removeItem('rookhub_rep_train_chaptercolor_78'));

  it('reines Schwarz-Repertoire: Brett gedreht, auch ohne gewählte Linie', async () => {
    const comp = await make(BLACK);
    expect(comp.boardFlipped).toBeTrue();
  });

  it('gemischt: ohne Linie Weiß unten, mit gewählter Schwarz-Linie gedreht', async () => {
    const comp = await make(WHITE + '\n' + BLACK);
    expect(comp.boardFlipped).toBeFalse();
    const black = comp.viewerService.lines.findIndex(l => l.chapter === 'Najdorf');
    comp.viewerService.selectLine(black);
    expect(comp.boardFlipped).toBeTrue();
    comp.viewerService.selectLine(comp.viewerService.lines.findIndex(l => l.chapter === 'Weiss'));
    expect(comp.boardFlipped).toBeFalse();
  });

  it('eine eigene Farb-Festlegung im Linien-Panel dreht das Brett mit', async () => {
    const comp = await make(BLACK);
    localStorage.setItem('rookhub_rep_train_chaptercolor_78', JSON.stringify({ Najdorf: 'w' }));
    comp.refreshChapterColors();
    expect(comp.boardFlipped).toBeFalse();
  });
});

describe('RepertoireDetailComponent Neues Repertoire aus Auswahl', () => {
  const PGN = '[Event "Rep"]\n[White "A1"]\n[Black "Kapitel A"]\n[Result "*"]\n\n1. e4 e5 {[%alt c5]Offen} 2. Nf3 *\n\n'
    + '[Event "Rep"]\n[White "B1"]\n[Black "Kapitel B"]\n[Result "*"]\n\n1. d4 d5 (1... Nf6 2. c4) 2. c4 *\n\n'
    + '[Event "Rep"]\n[White "A2"]\n[Black "Kapitel A"]\n[Result "*"]\n\n1. e4 c5 2. Nf3 *\n';

  async function make(result: unknown) {
    const { RepertoireService } = await import('../../core/repertoire.service');
    const { MatDialog } = await import('@angular/material/dialog');
    const create = jasmine.createSpy('create').and.returnValue(of({ id: 77 }));
    const uploadFile = jasmine.createSpy('uploadFile').and.returnValue(of({}));
    const open = jasmine.createSpy('open').and.returnValue({ afterClosed: () => of(result) });
    await TestBed.configureTestingModule({
      imports: [RepertoireDetailComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: RepertoireService, useValue: { create, uploadFile } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(RepertoireDetailComponent);
    const comp = fixture.componentInstance;
    (comp as any).dialog = { open };
    comp.repertoire = { id: 13, name: 'Quelle', kind: 1, useForExtension: false } as any;
    comp.id = 13;
    comp.viewerService.loadPgn(PGN);
    return { comp, create, uploadFile, open, MatDialog };
  }

  it('legt ein privates Repertoire mit den Originalabschnitten der Auswahl an', async () => {
    const { comp, create, uploadFile, open } = await make({ name: 'Nur offen', gameIndices: [2, 0] });

    comp.onCreateSubset(['k-egal']);

    const data = open.calls.mostRecent().args[1].data;
    expect(data.repertoireName).toBe('Quelle');
    expect(data.lines.length).toBe(3);
    expect(data.markedKeys).toEqual(['k-egal']);
    expect(create).toHaveBeenCalledWith(jasmine.objectContaining({ name: 'Nur offen', isPublic: false, kind: 1, useForExtension: false }));
    const form = uploadFile.calls.mostRecent().args[1] as FormData;
    expect(uploadFile.calls.mostRecent().args[0]).toBe(77);
    const text = await (form.get('file') as File).text();
    expect(text).toBe('[Event "Rep"]\n[White "A1"]\n[Black "Kapitel A"]\n[Result "*"]\n\n1. e4 e5 {[%alt c5]Offen} 2. Nf3 *\n\n'
      + '[Event "Rep"]\n[White "A2"]\n[Black "Kapitel A"]\n[Result "*"]\n\n1. e4 c5 2. Nf3 *\n');
    expect(text).not.toContain('d4');
  });

  it('abgebrochen: nichts angelegt', async () => {
    const { comp, create } = await make(undefined);
    comp.onCreateSubset([]);
    expect(create).not.toHaveBeenCalled();
  });
});

describe('RepertoireDetailComponent Baum: Pfeile + Ziehen', () => {
  const PGN = '[Event "Rep"]\n[Black "A"]\n\n1. e4 e5 2. Nf3 *\n\n[Event "Rep"]\n[Black "A"]\n\n1. e4 c5 *\n\n[Event "Rep"]\n[Black "B"]\n\n1. d4 d5 *\n';

  async function make() {
    localStorage.removeItem('rh.repertoire.treeArrows');
    await TestBed.configureTestingModule({
      imports: [RepertoireDetailComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const comp = TestBed.createComponent(RepertoireDetailComponent).componentInstance;
    comp.viewerService.loadPgn(PGN);
    comp.treeService.buildTreeFromGames(comp.viewerService.games);
    comp.mode = 'tree';
    spyOn(comp, 'loadTreePopularity');
    return comp;
  }

  it('Pfeile nur mit Schalter oder im Brett-Vollbild — dann je Fortsetzung einer, stabil zwischen Prüfläufen', async () => {
    const comp = await make();
    expect(comp.boardArrows).toEqual([]);
    comp.boardFullscreen = true;
    expect(comp.boardArrows.map(a => a.from + a.to).sort()).toEqual(['d2d4', 'e2e4']);
    expect(comp.boardArrows).toBe(comp.boardArrows);
    comp.boardFullscreen = false;
    comp.toggleTreeArrows();
    expect(comp.boardArrows.length).toBe(2);
    expect(localStorage.getItem('rh.repertoire.treeArrows')).toBe('1');
    comp.mode = 'lines';
    expect(comp.boardArrows).toEqual([]);
  });

  it('Zurück: Knopf nur im Brett-Vollbild des Baums, geht einen Zug zurück; Pfeil links ebenso', async () => {
    const comp = await make();
    const fixture = TestBed.createComponent(RepertoireDetailComponent);
    const c = fixture.componentInstance;
    fixture.detectChanges();                                 // ngOnInit (lädt ohne Route nichts, setzt loading)
    c.viewerService.loadPgn(PGN);
    c.treeService.buildTreeFromGames(c.viewerService.games);
    c.repertoire = { id: 1, name: 'R', files: [], isOwner: true } as any;
    c.loading = false;
    c.mode = 'tree';
    spyOn(c, 'loadTreePopularity');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.tree-fs-back')).toBeNull();
    c.boardFullscreen = true;
    c.treeService.selectChild('e4');
    c.treeService.selectChild('c5');
    fixture.componentRef.changeDetectorRef.markForCheck();   // OnPush: im echten Ablauf markiert das Output-Ereignis
    fixture.detectChanges();
    const back = el.querySelector<HTMLButtonElement>('.tree-fs-back')!;
    expect(back).not.toBeNull();
    expect(back.closest('.cb-fs-host')).withContext('liegt im Vollbild-Teilbaum des Bretts').not.toBeNull();
    back.click();
    expect(c.treeService.breadcrumbs.map(b => b.san)).toEqual(['e4']);
    c.onKeyDown(new KeyboardEvent('keydown', { key: 'ArrowLeft' }));
    expect(c.treeService.breadcrumbs.length).toBe(0);
    fixture.componentRef.changeDetectorRef.markForCheck();
    fixture.detectChanges();
    expect(el.querySelector<HTMLButtonElement>('.tree-fs-back')!.disabled).toBeTrue();
    c.treeBack();
    expect(c.treeService.breadcrumbs.length).toBe(0);
    void comp;
  });

  it('Zug auf dem Brett folgt der Fortsetzung; ein fremder Zug springt zurück', async () => {
    const comp = await make();
    const resync = jasmine.createSpy('resync');
    spyOn(comp, 'board').and.returnValue({ resync } as any);
    comp.onBoardMove({ from: 'e2', to: 'e4', san: 'e4', fen: '' });
    expect(comp.treeService.breadcrumbs.map(b => b.san)).toEqual(['e4']);
    expect(comp.loadTreePopularity).toHaveBeenCalled();
    comp.onBoardMove({ from: 'g8', to: 'f6', san: 'Nf6', fen: '' });
    expect(resync).toHaveBeenCalled();
    expect(comp.treeService.breadcrumbs.map(b => b.san)).toEqual(['e4']);
    comp.onBoardMove({ from: 'c7', to: 'c5', san: 'c5', fen: '' });
    expect(comp.treeService.breadcrumbs.map(b => b.san)).toEqual(['e4', 'c5']);
  });
});
