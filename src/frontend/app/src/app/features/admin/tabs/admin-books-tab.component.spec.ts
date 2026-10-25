import { ChangeDetectorRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { AdminService } from '../../../core/admin.service';
import { AuthService } from '../../../core/auth.service';
import { SnackbarService } from '../../../core/snackbar.service';
import { ConfirmService } from '../../../shared/confirm-dialog/confirm-dialog.component';
import { PromptService } from '../../../shared/prompt-dialog/prompt-dialog.component';
import { AdminGroupsStore } from '../admin-groups.store';
import { AdminBooksTabComponent } from './admin-books-tab.component';

/** Rückfrage-Doppelgänger: `answer` legt fest, was der Dialog liefert (F5-013). */
const confirmStub = { answer: true, ask: jasmine.createSpy('ask') };
/** Eingabe-Doppelgänger (F5-016): `answer` = Werte je Feld-Key, `null` = abgebrochen. */
const promptStub: { answer: Record<string, string> | null; ask: jasmine.Spy } = { answer: null, ask: jasmine.createSpy('ask') };

/** Ohne Template/ngOnInit — testet die Komponenten-Logik (aus admin.component.spec umgezogen, Zerlegung Z30). */
function make(adminOverrides: any = {}, auth: any = {}) {
  const adminService = {
    getBooks: jasmine.createSpy('getBooks').and.returnValue(of([])),
    ...adminOverrides,
  };
  const snackbar = { info: jasmine.createSpy('info') };
  TestBed.configureTestingModule({
    providers: [
      AdminGroupsStore,
      { provide: AdminService, useValue: adminService },
      { provide: AuthService, useValue: { has: () => true, isAdminLive: true, ...auth } },
      { provide: SnackbarService, useValue: snackbar },
      { provide: TranslateService, useValue: { instant: (k: string) => k } },
      { provide: ConfirmService, useValue: confirmStub },
      { provide: PromptService, useValue: promptStub },
      { provide: ChangeDetectorRef, useValue: { markForCheck: () => {} } },
    ],
  });
  const c = TestBed.runInInjectionContext(() => new AdminBooksTabComponent());
  return { c, adminService, snackbar };
}

describe('AdminBooksTabComponent', () => {
  beforeEach(() => {
    confirmStub.answer = true;
    confirmStub.ask = jasmine.createSpy('ask').and.callFake(() => of(confirmStub.answer));
    promptStub.answer = null;
    promptStub.ask = jasmine.createSpy('ask').and.callFake(() => of(promptStub.answer));
  });

  it('lädt beim Öffnen die Bücher und wendet den Filter an', () => {
    const getBooks = jasmine.createSpy('getBooks').and.returnValue(of([{ id: 1, displayName: 'A' }, { id: 2, displayName: 'B' }]));
    const { c } = make({ getBooks });
    c.bookSearch.set('b');
    c.ngOnInit();
    expect(c.books().length).toBe(2);
    expect(c.filteredBooks().map(b => b.id)).toEqual([2]);
    expect(c.booksLoading()).toBeFalse();
  });

  it('Spaltenfilter, Zurücksetzen und Suche leeren', () => {
    const { c } = make();
    c.books.set([
      { id: 1, displayName: 'Endgame', fileName: 'e.pgn', kind: 'Puzzle', puzzleCount: 50, accessGroupIds: [4] },
      { id: 2, displayName: 'Tactics', fileName: 't.pgn', kind: 'Study', puzzleCount: 500, accessGroupIds: [] },
    ] as any);

    c.bookFilters.kind = 'Puzzle';
    c.applyBookFilter();
    expect(c.filteredBooks().map(b => b.id)).toEqual([1]);
    expect(c.hasActiveBookFilters()).toBeTrue();

    c.resetBookFilters();
    expect(c.filteredBooks().length).toBe(2);
    expect(c.hasActiveBookFilters()).toBeFalse();

    c.bookSearch.set('tactics');
    c.applyBookFilter();
    expect(c.filteredBooks().map(b => b.id)).toEqual([2]);
    c.clearBookSearch();
    expect(c.bookSearch()).toBe('');
    expect(c.filteredBooks().length).toBe(2);
  });

  it('renameBook sends the new DisplayName and updates the row + filter', () => {
    const updateBook = jasmine.createSpy('updateBook').and.returnValue(of({}));
    const { c } = make({ updateBook });
    const book = { id: 7, displayName: 'Old Name', fileName: 'x.pgn', tags: null, minElo: 800, maxElo: 1200 } as any;
    c.books.set([book]);
    promptStub.answer = { name: '  New Name  ' };

    c.renameBook(book);

    expect(promptStub.ask.calls.mostRecent().args[0].fields).toEqual([{ key: 'name', label: 'admin.books.renamePrompt', value: 'Old Name' }]);
    // Nur der Name: der Endpunkt lässt fehlende Felder unverändert, auch die Elo-Spanne (N9-009).
    expect(updateBook).toHaveBeenCalledWith(7, { displayName: 'New Name' });
    expect(book.displayName).toBe('New Name');
    expect(c.filteredBooks()).toEqual([book]);
  });

  it('renameBook does nothing on cancel or unchanged name', () => {
    const updateBook = jasmine.createSpy('updateBook').and.returnValue(of({}));
    const { c } = make({ updateBook });
    const book = { id: 7, displayName: 'Same', fileName: 'x.pgn', tags: null } as any;

    promptStub.answer = null;                                           // cancelled
    c.renameBook(book);
    promptStub.answer = { name: 'Same' };                               // unchanged
    c.renameBook(book);

    expect(updateBook).not.toHaveBeenCalled();
  });

  it('editKidsTitles fragt alle KidHub-Sprachen in EINEM Dialog ab und speichert alle, ohne die Elo zu verlieren', () => {
    const updateBook = jasmine.createSpy('updateBook').and.returnValue(of({ kidsTitles: { de: 'Matt in einem Zug', en: 'Checkmate in One' } }));
    const { c } = make({ updateBook });
    const book = { id: 9, displayName: 'Learn Chess', forKids: true, minElo: null, maxElo: 1000, kidsTitles: { de: 'Alt' } } as any;
    promptStub.answer = { de: ' Matt in einem Zug ', en: 'Checkmate in One', hr: '', hu: '' };

    c.editKidsTitles(book);

    // F5-016: ein Dialog statt vier Abfragen nacheinander; vorhandener Titel vorbelegt.
    expect(promptStub.ask).toHaveBeenCalledTimes(1);
    const fields = promptStub.ask.calls.mostRecent().args[0].fields;
    expect(fields.map((f: any) => f.key)).toEqual(['de', 'en', 'hr', 'hu']);
    expect(fields[0].value).toBe('Alt');
    expect(updateBook).toHaveBeenCalledWith(9, {
      kidsTitles: { de: 'Matt in einem Zug', en: 'Checkmate in One', hr: '', hu: '' },
    });
    expect(book.kidsTitles).toEqual({ de: 'Matt in einem Zug', en: 'Checkmate in One' });
  });

  it('editKidsTitles: Abbrechen speichert nichts', () => {
    const updateBook = jasmine.createSpy('updateBook').and.returnValue(of({}));
    const { c } = make({ updateBook });
    promptStub.answer = null;

    c.editKidsTitles({ id: 9, displayName: 'x', forKids: true } as any);

    expect(updateBook).not.toHaveBeenCalled();
  });

  it('saveBook schickt ein geleertes Elo-Feld als 0 (Grenze entfernen), null hiesse unverändert', () => {
    const updateBook = jasmine.createSpy('updateBook').and.returnValue(of({}));
    const { c } = make({ updateBook });

    c.saveBook({ id: 3, displayName: 'x', kind: 'Puzzle', minElo: null, maxElo: 1800 } as any);

    expect(updateBook.calls.mostRecent().args[1]).toEqual(jasmine.objectContaining({ minElo: 0, maxElo: 1800 }));
  });

  // F5-013: „Öffentlich" und „Kinder" erst nach Rückfrage einschalten; Abbrechen stellt den Schalter zurück.
  describe('Öffentlich/Kinder', () => {
    it('Einschalten fragt nach; Abbrechen stellt zurück und speichert nicht', () => {
      confirmStub.answer = false;
      const updateBook = jasmine.createSpy('updateBook').and.returnValue(of({}));
      const { c } = make({ updateBook });
      const book = { id: 9, displayName: 'Privatkurs', isPublic: true, forKids: false } as any;   // ngModel hat schon umgestellt

      c.toggleExposure(book, 'isPublic');

      expect(confirmStub.ask).toHaveBeenCalledWith('admin.books.confirmPublic', { name: 'Privatkurs' });
      expect(book.isPublic).toBeFalse();
      expect(updateBook).not.toHaveBeenCalled();
    });

    it('Einschalten mit OK speichert; Ausschalten speichert ohne Rückfrage', () => {
      const updateBook = jasmine.createSpy('updateBook').and.returnValue(of({}));
      const { c } = make({ updateBook });

      c.toggleExposure({ id: 9, displayName: 'K', forKids: true } as any, 'forKids');
      expect(confirmStub.ask).toHaveBeenCalledWith('admin.books.confirmKids', { name: 'K' });
      expect(updateBook).toHaveBeenCalledTimes(1);

      confirmStub.ask.calls.reset();
      c.toggleExposure({ id: 9, displayName: 'K', forKids: false } as any, 'forKids');
      expect(confirmStub.ask).not.toHaveBeenCalled();
      expect(updateBook).toHaveBeenCalledTimes(2);
    });
  });

  // N9-010: private Bücher anderer Konten — Besitzer erkennbar, auch die Pools fragen nach und nennen ihn.
  describe('fremde private Bücher', () => {
    const me = { currentUser: { userId: 1 } };

    it('erkennt fremde private Bücher, nicht globale und nicht die eigenen', () => {
      const { c } = make({}, me);
      expect(c.isForeignBook({ ownerUserId: 42 } as any)).toBeTrue();
      expect(c.isForeignBook({ ownerUserId: 1 } as any)).toBeFalse();
      expect(c.isForeignBook({ ownerUserId: null } as any)).toBeFalse();
    });

    it('Tagespuzzle auf einer fremden Kopie fragt mit Besitzer nach; Abbrechen stellt zurück', () => {
      confirmStub.answer = false;
      const updateBook = jasmine.createSpy('updateBook').and.returnValue(of({}));
      const { c } = make({ updateBook }, me);
      const book = { id: 7, displayName: 'Lifetime Repertoires', ownerUserId: 42, ownerName: 'bob', forDaily: true } as any;

      c.toggleExposure(book, 'forDaily');

      expect(confirmStub.ask).toHaveBeenCalledWith('admin.books.confirmForeign', { name: 'Lifetime Repertoires', owner: 'bob' });
      expect(book.forDaily).toBeFalse();
      expect(updateBook).not.toHaveBeenCalled();
    });

    it('Pools auf globalen und eigenen Büchern speichern ohne Rückfrage', () => {
      const updateBook = jasmine.createSpy('updateBook').and.returnValue(of({}));
      const { c } = make({ updateBook }, me);

      c.toggleExposure({ id: 7, displayName: 'G', ownerUserId: null, forRandom: true } as any, 'forRandom');
      c.toggleExposure({ id: 8, displayName: 'Mein', ownerUserId: 1, forBlind: true } as any, 'forBlind');

      expect(confirmStub.ask).not.toHaveBeenCalled();
      expect(updateBook).toHaveBeenCalledTimes(2);
    });
  });

  it('die Gruppen-Auswahl kommt aus dem Store der Seite', () => {
    const { c } = make();
    TestBed.inject(AdminGroupsStore).groupOptions.set([{ id: 3, name: 'Verein', memberCount: 4 }]);
    expect(c.groupOptions().map(g => g.name)).toEqual(['Verein']);
  });
});

describe('AdminBooksTabComponent – Darstellung', () => {
  it('creates und rendert die gefilterten Bücher (OnPush, nach der Antwort ohne Klick)', async () => {
    TestBed.configureTestingModule({
      imports: [AdminBooksTabComponent],
      providers: [
        AdminGroupsStore,
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }), provideRouter([]),
        { provide: AdminService, useValue: { getBooks: () => of([
          { id: 1, displayName: 'Endgame', fileName: 'e.pgn', kind: 'Puzzle', puzzleCount: 3, accessGroupIds: [] },
        ]) } },
        { provide: AuthService, useValue: { currentUser: { userId: 1 }, has: () => true, isAdminLive: true } },
      ],
    });
    const fixture = TestBed.createComponent(AdminBooksTabComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('tr.mat-mdc-row').length).toBe(1);
    expect(el.querySelector('.book-name')!.textContent).toContain('Endgame');
    expect(el.querySelector('.book-filters')).not.toBeNull();
  });
});
