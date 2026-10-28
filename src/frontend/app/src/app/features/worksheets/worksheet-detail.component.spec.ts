import { ChangeDetectorRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { BehaviorSubject, of, throwError } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { WorksheetDetailComponent } from './worksheet-detail.component';
import { Worksheet, WorksheetService } from './worksheet.service';

const item = (id: number, sourceThemes = '') => ({
  id, sortOrder: id, fen: `8/8/8/8/8/8/8/8 w - - 0 ${id}`, orientation: 'white' as const,
  heading: '', text: '', solutionMoves: '', sourceThemes, source: 'Book' as const, sourceId: id, bookId: 1,
});

const sheet = (over: Partial<Worksheet> = {}): Worksheet => ({
  id: 4, name: 'Mittwoch', isClipboard: false, perPage: 6, itemCount: 3, shareToken: null, themes: [],
  createdAt: '', updatedAt: '', items: [item(1), item(2), item(3)], ...over,
});

describe('WorksheetDetailComponent', () => {
  let worksheets: any;
  let router: { navigate: jasmine.Spy };
  /** Die Route ist ein STROM: „Als Blatt speichern" wechselt die Id, ohne die Komponente neu zu bauen. */
  let params: BehaviorSubject<any>;
  /** Antwort der Rückfrage (ConfirmService) — je Test umstellbar, Vorgabe „ja". */
  let confirmAnswer: boolean;

  const paramMap = (id: string) => ({ get: (k: string) => (k === 'id' ? id : null) });

  function make(loaded: Worksheet = sheet()): WorksheetDetailComponent {
    confirmAnswer = true;
    worksheets = {
      get: jasmine.createSpy('get').and.returnValue(of(loaded)),
      reorder: jasmine.createSpy('reorder').and.callFake((_id: number, ids: number[]) =>
        of({ ...loaded, items: ids.map(i => item(i)) })),
      updateItem: jasmine.createSpy('updateItem').and.returnValue(of(item(1))),
      removeItem: jasmine.createSpy('removeItem').and.returnValue(of(undefined)),
      update: jasmine.createSpy('update').and.callFake((_id: number, patch: any) =>
        of({ ...loaded, ...patch, themes: patch.themes ?? loaded.themes })),
      clear: jasmine.createSpy('clear').and.returnValue(of({ ...loaded, items: [] })),
      remove: jasmine.createSpy('remove').and.returnValue(of(undefined)),
      addItems: jasmine.createSpy('addItems').and.returnValue(of({ added: 1 })),
      saveClipboardAs: jasmine.createSpy('saveClipboardAs').and.returnValue(of({ ...loaded, id: 9 })),
      share: jasmine.createSpy('share').and.returnValue(of('Ux7f2K')),
      unshare: jasmine.createSpy('unshare').and.returnValue(of(undefined)),
      shareUrl: (token: string) => `https://rookhub.test/w/${token}`,
    };
    router = { navigate: jasmine.createSpy('navigate') };
    params = new BehaviorSubject<any>(paramMap('4'));
    TestBed.resetTestingModule();   // mehrere Blätter je Test (Zwischenablage vs. benanntes Blatt)
    TestBed.configureTestingModule({
      providers: [
        { provide: WorksheetService, useValue: worksheets },
        { provide: Router, useValue: router },
        { provide: ActivatedRoute, useValue: { paramMap: params, snapshot: { paramMap: paramMap('4') } } },
        { provide: SnackbarService, useValue: { warn: () => {}, info: () => {} } },
        { provide: TranslateService, useValue: { instant: (k: string) => k } },
        { provide: ChangeDetectorRef, useValue: { markForCheck: () => {} } },
        { provide: ConfirmService, useValue: { ask: () => of(confirmAnswer) } },
      ],
    });
    const c = TestBed.runInInjectionContext(() => new WorksheetDetailComponent());
    c.ngOnInit();
    return c;
  }

  it('lädt das Blatt seiner Route', () => {
    const c = make();
    expect(worksheets.get).toHaveBeenCalledWith(4);
    expect(c.sheet!.items.length).toBe(3);
  });

  it('ein Wechsel der Adresse lädt das neue Blatt — die Komponente bleibt dieselbe', () => {
    const c = make();
    expect(worksheets.get).toHaveBeenCalledWith(4);

    params.next(paramMap('9'));

    expect(worksheets.get).toHaveBeenCalledWith(9);
    expect(c.id).toBe(9);
  });

  it('beim Blattwechsel bleiben keine Entwürfe des vorigen stehen', () => {
    const c = make(sheet({ isClipboard: true, name: '' }));
    c.saveName = 'Mittwoch';
    c.newFen = '8/8/8/8/8/8/8/8 w - - 0 1';
    c.newTheme = 'fork';

    params.next(paramMap('9'));

    expect(c.saveName).toBe('');
    expect(c.newFen).toBe('');
    expect(c.newTheme).toBe('');
  });

  it('Ziehen sortiert um und schreibt die neue Reihenfolge weg', () => {
    const c = make();
    c.drop({ previousIndex: 0, currentIndex: 2 } as any);
    expect(worksheets.reorder).toHaveBeenCalledWith(4, [2, 3, 1]);
  });

  it('Ziehen an dieselbe Stelle schreibt nichts', () => {
    const c = make();
    c.drop({ previousIndex: 1, currentIndex: 1 } as any);
    expect(worksheets.reorder).not.toHaveBeenCalled();
  });

  it('hoch/runter bewegt dasselbe ohne Maus, am Rand passiert nichts', () => {
    const c = make();
    c.move(2, -1);
    expect(worksheets.reorder).toHaveBeenCalledWith(4, [1, 3, 2]);

    worksheets.reorder.calls.reset();
    c.move(0, -1);
    expect(worksheets.reorder).not.toHaveBeenCalled();
  });

  it('Überschrift und Begleittext gehen beim Verlassen des Feldes zum Server', () => {
    const c = make();
    const first = c.sheet!.items[0];
    first.heading = 'Grundreihe';
    first.text = 'Wie verteidigt Schwarz?';
    c.saveItem(first);
    expect(worksheets.updateItem).toHaveBeenCalledWith(4, 1, { heading: 'Grundreihe', text: 'Wie verteidigt Schwarz?' });
  });

  it('Brett drehen wirkt sofort und fällt bei Serverfehler zurück', () => {
    const c = make();
    const first = c.sheet!.items[0];
    worksheets.updateItem.and.returnValue(throwError(() => new Error('nope')));

    c.flip(first);

    expect(first.orientation).toBe('white');   // zurückgerollt
  });

  it('entfernte Aufgaben verschwinden aus der Liste', () => {
    const c = make();
    c.removeItem(c.sheet!.items[1]);
    expect(c.sheet!.items.map(i => i.id)).toEqual([1, 3]);
    expect(c.sheet!.itemCount).toBe(2);
  });

  it('die Zwischenablage heißt übersetzt, ein Blatt nach seinem Namen', () => {
    expect(make().title).toBe('Mittwoch');
    expect(make(sheet({ isClipboard: true, name: '' })).title).toBe('worksheets.clipboard');
  });

  it('die Zwischenablage lässt sich nicht umbenennen', () => {
    const c = make(sheet({ isClipboard: true, name: '' }));
    c.startRename();
    expect(c.renaming).toBeFalse();
  });

  it('gesichert wird die Ablage unter Namen und Dichte — danach steht man im neuen Blatt', () => {
    const c = make(sheet({ isClipboard: true, name: '', perPage: 2 }));
    c.saveName = '  Mittwochstraining  ';
    c.saveAsSheet();
    expect(worksheets.saveClipboardAs).toHaveBeenCalledWith('Mittwochstraining', 2);
    expect(router.navigate).toHaveBeenCalledWith(['/worksheets', 9]);
  });

  it('schlägt die Themen der Aufgaben vor, häufigste zuerst', () => {
    const c = make(sheet({
      items: [item(1, 'fork backRankMate'), item(2, 'fork'), item(3, 'pin')],
    }));

    expect(c.themeSuggestions.map(s => s.theme)).toEqual(['fork', 'backRankMate', 'pin']);
    expect(c.themeSuggestions[0].count).toBe(2);
  });

  it('ein bereits gesetztes Thema wird nicht nochmal vorgeschlagen', () => {
    const c = make(sheet({ themes: ['fork'], items: [item(1, 'fork pin')] }));

    expect(c.themeSuggestions.map(s => s.theme)).toEqual(['pin']);
  });

  it('ein Thema hinzufügen schreibt die Liste weg und leert das Eingabefeld', () => {
    const c = make();
    c.newTheme = '  Turmendspiel  ';

    c.addTheme(c.newTheme);

    expect(worksheets.update).toHaveBeenCalledWith(4, { themes: ['Turmendspiel'] });
    expect(c.newTheme).toBe('');
  });

  it('dasselbe Thema in anderer Schreibweise kommt nicht zweimal aufs Blatt', () => {
    const c = make(sheet({ themes: ['Gabel'] }));

    c.addTheme('gabel');

    expect(worksheets.update).not.toHaveBeenCalled();
    expect(c.themes).toEqual(['Gabel']);
  });

  it('ein Thema entfernen schickt die verbleibende Liste', () => {
    const c = make(sheet({ themes: ['fork', 'pin'] }));

    c.removeTheme('fork');

    expect(worksheets.update).toHaveBeenCalledWith(4, { themes: ['pin'] });
  });

  it('Teilen einschalten holt das Token und baut die Adresse für den QR-Code', () => {
    const c = make();
    c.toggleShare(true);
    expect(worksheets.share).toHaveBeenCalledWith(4);
    expect(c.sheet!.shareToken).toBe('Ux7f2K');
    expect(c.shareUrl).toBe('https://rookhub.test/w/Ux7f2K');
  });

  it('Teilen beenden fragt nach — und ein Nein lässt den Link stehen', () => {
    const c = make(sheet({ shareToken: 'Ux7f2K' }));
    confirmAnswer = false;

    c.toggleShare(false);

    expect(worksheets.unshare).not.toHaveBeenCalled();
    expect(c.sheet!.shareToken).toBe('Ux7f2K');
  });

  it('bestätigtes Beenden nimmt den Link weg', () => {
    const c = make(sheet({ shareToken: 'Ux7f2K' }));

    c.toggleShare(false);

    expect(worksheets.unshare).toHaveBeenCalledWith(4);
    expect(c.sheet!.shareToken).toBeNull();
    expect(c.shareUrl).toBe('');
  });

  // mat-slide-toggle kippt beim Klick SELBST um; bleibt der gebundene Wert gleich, schreibt Angular
  // [checked] nicht neu. Die Attrappe steht deshalb schon auf dem neuen Wert, wie der echte Schalter.
  it('Abbrechen beim Beenden stellt den Schalter zurück auf „an" — der Link ist ja noch öffentlich', () => {
    const c = make(sheet({ shareToken: 'Ux7f2K' }));
    confirmAnswer = false;
    const toggle = { checked: false };

    c.toggleShare(false, toggle);

    expect(toggle.checked).toBeTrue();
    expect(c.sheet!.shareToken).toBe('Ux7f2K');
  });

  it('gescheitertes Teilen stellt den Schalter zurück auf „aus"', () => {
    const c = make();
    worksheets.share.and.returnValue(throwError(() => new Error('500')));
    const toggle = { checked: true };

    c.toggleShare(true, toggle);

    expect(toggle.checked).toBeFalse();
    expect(c.sheet!.shareToken).toBeNull();
  });

  it('gescheitertes Beenden stellt den Schalter zurück auf „an"', () => {
    const c = make(sheet({ shareToken: 'Ux7f2K' }));
    worksheets.unshare.and.returnValue(throwError(() => new Error('500')));
    const toggle = { checked: false };

    c.toggleShare(false, toggle);

    expect(toggle.checked).toBeTrue();
    expect(c.sheet!.shareToken).toBe('Ux7f2K');
  });

  it('eine Stellung von Hand kommt als FEN mit passender Ausrichtung', () => {
    const c = make();
    c.newFen = ' 8/8/8/8/8/8/8/8 b - - 0 1 ';
    c.addFen();
    expect(worksheets.addItems).toHaveBeenCalledWith(4, [{ fen: '8/8/8/8/8/8/8/8 b - - 0 1', orientation: 'black' }]);
  });
});

// d2-rk-ws-detail-lead: Themen- und Teilen-Karte standen vor allen Stellungen (Handy: keine einzige sichtbar). Jetzt EINE
// Zeile „Themen: … ＋ Thema · Teilen · Link kopieren · QR ▾"; Feld, Vorschläge und QR-Code klappen erst auf Tipp auf.
describe('WorksheetDetailComponent Themen + Teilen als eine Zeile', () => {
  async function render(loaded: Worksheet) {
    const worksheets = {
      get: () => of(loaded),
      shareUrl: (token: string) => `https://rookhub.test/w/${token}`,
    };
    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({
      imports: [WorksheetDetailComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: WorksheetService, useValue: worksheets },
        { provide: ActivatedRoute, useValue: { paramMap: of({ get: (k: string) => (k === 'id' ? '4' : null) }),
          snapshot: { paramMap: { get: () => '4' } } } },
        { provide: SnackbarService, useValue: { warn: () => {}, info: () => {} } },
        { provide: ConfirmService, useValue: { ask: () => of(true) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(WorksheetDetailComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('zeigt Themen und Teilen als Zeile vor der Liste; Feld und QR erst nach Tipp', async () => {
    const fixture = await render(sheet({ shareToken: 'Ux7f2K', themes: ['Gabel'] }));
    const el = fixture.nativeElement as HTMLElement;
    const row = el.querySelector('.wd-meta-row') as HTMLElement;
    expect(row).not.toBeNull();
    expect(row.textContent).toContain('Gabel');
    expect(row.compareDocumentPosition(el.querySelector('.wd-list')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(el.querySelector('.wd-themes')).withContext('Themen-Feld zu').toBeNull();
    expect(el.querySelector('app-qr-code')).withContext('QR zu').toBeNull();
    // d6-rk-blatt-qr-karte-fliesstext: die Erklärung steht hinter dem ? der Teilen-Zeile; die kurze Lösungs-Warnung bleibt
    // sichtbar, solange das Blatt geteilt ist — auch bei zugeklapptem QR-Code (die Lösungen hängen am Link).
    expect(el.querySelector('.wd-warn')?.textContent).withContext('Lösungs-Warnung sichtbar').toContain('worksheets.share.solutionWarning');
    expect(row.querySelector('app-help-hint.wd-share-help')).not.toBeNull();

    (el.querySelector('.wd-theme-toggle') as HTMLButtonElement).click();
    (el.querySelector('.wd-qr-toggle') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelector('.wd-themes input')).not.toBeNull();
    expect(el.querySelector('app-qr-code')).not.toBeNull();
    // QR-Karte: kein Fließtext-Absatz mehr; die Warnung steht genau einmal, unter der Karte.
    const card = el.querySelector('.wd-share') as HTMLElement;
    expect(card.querySelectorAll('.wd-share-lines p').length).toBe(0);
    expect(el.querySelectorAll('.wd-warn').length).toBe(1);
    expect(card.compareDocumentPosition(el.querySelector('.wd-warn')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  // 0.808.x · Boox 200 % Falz: im niedrigen Fenster stehen Drucken und Link kopieren nur als Symbol — der Name muss
  // deshalb im aria-label/title bleiben, die Beschriftung in einem eigenen Span (den die Abfrage ausblendet).
  it('Drucken und Link kopieren tragen ihren Namen im aria-label, die Beschriftung im ausblendbaren Span', async () => {
    const fixture = await render(sheet({ shareToken: 'Ux7f2K' }));
    const el = fixture.nativeElement as HTMLElement;
    for (const sel of ['.wd-print', '.wd-copy']) {
      const b = el.querySelector(sel) as HTMLElement;
      expect(b).withContext(sel).not.toBeNull();
      expect(b.getAttribute('aria-label')).withContext(sel).toBeTruthy();
      expect(b.getAttribute('title')).withContext(sel).toBe(b.getAttribute('aria-label'));
      expect(b.querySelector('mat-icon')).withContext(sel).not.toBeNull();
      expect(b.querySelector('.wd-btn-label')?.textContent?.trim()).withContext(sel).toBe(b.getAttribute('aria-label')!);
    }
  });

  it('d5-rk-blattbrett-koordinaten-winzig: Vorschaubretter im Editor ohne (winzige) Koordinaten', async () => {
    const fixture = await render(sheet());
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('app-flashcard-board.wd-board').length).toBe(3);
    expect(el.querySelector('app-flashcard-board.wd-board text.fb-coord')).toBeNull();
    expect(el.querySelector('app-flashcard-board.wd-board svg')!.getAttribute('viewBox')).toBe('0 0 8 8');
  });

  it('ungeteilt: kein Kopieren, kein QR, keine Warnung', async () => {
    const fixture = await render(sheet());
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.wd-qr-toggle')).toBeNull();
    expect(el.querySelector('.wd-warn')).toBeNull();
    expect(el.querySelector('.wd-share-toggle')).not.toBeNull();
  });

  it('a1-rk-ws-hinzufuegen-gesperrt: leeres FEN-Feld nennt den Grund für den gesperrten Knopf darunter', async () => {
    const fixture = await render(sheet());
    const el = fixture.nativeElement as HTMLElement;
    const add = el.querySelector('.wd-add') as HTMLElement;
    expect(add.querySelector('button[disabled]')).withContext('Knopf gesperrt').not.toBeNull();
    expect(add.querySelector('.wd-add-hint')?.textContent).toContain('worksheets.detail.addFenHint');

    fixture.componentInstance.newFen = '8/8/8/8/8/8/8/8 w - - 0 1';
    fixture.detectChanges();
    expect(add.querySelector('.wd-add-hint')).withContext('mit FEN kein Hinweis').toBeNull();
  });
});

/** UI-Sweep z3 (z3-rk-ws-detail-vorschau): am Handy Brett 140 px, Knöpfe 2-spaltig daneben, Felder darunter. */
describe('WorksheetDetailComponent Handy-Karte', () => {
  const css = ((WorksheetDetailComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n').replace(/\s+/g, ' ');
  it('setzt unter 600 px Brett 140 px, Knöpfe neben das Brett und die Felder darunter', () => {
    const phone = css.slice(css.indexOf('@media (max-width: 599.98px)'));
    expect(phone).toMatch(/\.wd-board[^{]*\{ ?width: 140px/);
    expect(phone).toMatch(/\.wd-item-actions[^{]*\{ ?order: 1;[^}]*display: grid; grid-template-columns: repeat\(2, auto\)/);
    expect(phone).toMatch(/\.wd-fields[^{]*\{ ?order: 2; flex-basis: 100%/);
  });
  // Falz-Runde 16.10.: Vorlauf 40,5 % → Dichte-Zeile enger, Lösungs-Hinweis kleiner
  it('unter 600 px Dichte-Zeile mit 0.25rem Abstand und Lösungs-Hinweis in 0.8rem', () => {
    const phone = css.slice(css.indexOf('@media (max-width: 599.98px)'));
    expect(phone).toMatch(/\.wd-density[^{]*\{ ?margin: 0\.25rem 0/);
    expect(phone).toMatch(/\.wd-warn[^{]*\{ ?font-size: 0\.8rem/);
  });
});

/** UI-Sweep a1 (a1-rk-ws-detail-bretter-klein): Diagramme 168 px, Tablet 140 px (nie kleiner als am Handy), ab 2400 px 220 px. */
describe('WorksheetDetailComponent Diagrammgröße', () => {
  const css = ((WorksheetDetailComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n').replace(/%NS%/g, '').replace(/\s+/g, ' ');
  const block = (q: string) => { const i = css.indexOf(q); return i < 0 ? '' : css.slice(i, css.indexOf('@media', i + 6) >>> 0); };

  it('Grundgröße 168 px statt 132 px', () => {
    expect(css).toMatch(/\.wd-board[^{]*\{ ?width: 168px/);
    expect(css).not.toContain('width: 132px');
  });

  it('Tablet (≤ 768 px) 140 px statt 88 px', () => {
    expect(block('@media (max-width: 768px)')).toMatch(/\.wd-board[^{]*\{ ?width: 140px/);
    expect(css).not.toContain('width: 88px');
  });

  it('ab 2400 px breite Spalte und 220-px-Diagramme', () => {
    const wide = block('@media (min-width: 2400px)');
    expect(wide).toMatch(/\.wd-page[^{]*\{ ?max-width: min\(var\(--page-max-width-wide, 1880px\), 96vw\)/);
    expect(wide).toMatch(/\.wd-board[^{]*\{ ?width: 220px/);
  });
});
