import { ChangeDetectorRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { WorksheetDetailComponent } from './worksheet-detail.component';
import { Worksheet, WorksheetService } from './worksheet.service';

const item = (id: number) => ({
  id, sortOrder: id, fen: `8/8/8/8/8/8/8/8 w - - 0 ${id}`, orientation: 'white' as const,
  heading: '', text: '', source: 'Book' as const, sourceId: id, bookId: 1,
});

const sheet = (over: Partial<Worksheet> = {}): Worksheet => ({
  id: 4, name: 'Mittwoch', isClipboard: false, perPage: 6, itemCount: 3,
  createdAt: '', updatedAt: '', items: [item(1), item(2), item(3)], ...over,
});

describe('WorksheetDetailComponent', () => {
  let worksheets: any;
  let router: { navigate: jasmine.Spy };

  function make(loaded: Worksheet = sheet()): WorksheetDetailComponent {
    worksheets = {
      get: jasmine.createSpy('get').and.returnValue(of(loaded)),
      reorder: jasmine.createSpy('reorder').and.callFake((_id: number, ids: number[]) =>
        of({ ...loaded, items: ids.map(i => item(i)) })),
      updateItem: jasmine.createSpy('updateItem').and.returnValue(of(item(1))),
      removeItem: jasmine.createSpy('removeItem').and.returnValue(of(undefined)),
      update: jasmine.createSpy('update').and.returnValue(of(loaded)),
      clear: jasmine.createSpy('clear').and.returnValue(of({ ...loaded, items: [] })),
      remove: jasmine.createSpy('remove').and.returnValue(of(undefined)),
      addItems: jasmine.createSpy('addItems').and.returnValue(of({ added: 1 })),
      saveClipboardAs: jasmine.createSpy('saveClipboardAs').and.returnValue(of({ ...loaded, id: 9 })),
    };
    router = { navigate: jasmine.createSpy('navigate') };
    TestBed.resetTestingModule();   // mehrere Blätter je Test (Zwischenablage vs. benanntes Blatt)
    TestBed.configureTestingModule({
      providers: [
        { provide: WorksheetService, useValue: worksheets },
        { provide: Router, useValue: router },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => '4' } } } },
        { provide: SnackbarService, useValue: { warn: () => {}, info: () => {} } },
        { provide: TranslateService, useValue: { instant: (k: string) => k } },
        { provide: ChangeDetectorRef, useValue: { markForCheck: () => {} } },
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

  it('eine Stellung von Hand kommt als FEN mit passender Ausrichtung', () => {
    const c = make();
    c.newFen = ' 8/8/8/8/8/8/8/8 b - - 0 1 ';
    c.addFen();
    expect(worksheets.addItems).toHaveBeenCalledWith(4, [{ fen: '8/8/8/8/8/8/8/8 b - - 0 1', orientation: 'black' }]);
  });
});
