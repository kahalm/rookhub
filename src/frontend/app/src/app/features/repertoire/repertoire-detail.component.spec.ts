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
  it('lässt die Linienliste nur so hoch wie ihren Inhalt, mit Rahmen als Token', () => {
    expect(css).toMatch(/\.side-panel--fit[^{]*\{ ?height: auto; max-height:/);
    expect(css).not.toContain('#e0e0e0');
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

  it('lines: kein Abfragen-Knopf im Kopf (steht in der Linienleiste)', async () => {
    const el = await render('lines');
    expect(el.querySelector('.detail-header .train-btn')).toBeNull();
    expect(el.querySelector('.header-actions mat-button-toggle-group')).not.toBeNull();
  });
});
