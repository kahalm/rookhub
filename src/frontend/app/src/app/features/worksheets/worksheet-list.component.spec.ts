import { ChangeDetectorRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { WorksheetListComponent } from './worksheet-list.component';
import { WorksheetService, WorksheetSummary } from './worksheet.service';

const summary = (over: Partial<WorksheetSummary> = {}): WorksheetSummary => ({
  id: 1, name: 'Blatt', isClipboard: false, perPage: 6, itemCount: 3, themes: [],
  shareToken: null, createdAt: '', updatedAt: '', ...over,
});

describe('WorksheetListComponent', () => {
  function make(list: WorksheetSummary[]): WorksheetListComponent {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: WorksheetService, useValue: { list: () => of(list) } },
        { provide: Router, useValue: { navigate: jasmine.createSpy('navigate') } },
        { provide: SnackbarService, useValue: { warn: () => {} } },
        { provide: TranslateService, useValue: { instant: (k: string) => k } },
        { provide: ChangeDetectorRef, useValue: { markForCheck: () => {} } },
      ],
    });
    const c = TestBed.runInInjectionContext(() => new WorksheetListComponent());
    c.ngOnInit();
    return c;
  }

  it('die Zwischenablage steht nicht zwischen den benannten Blättern', () => {
    const c = make([summary({ id: 9, isClipboard: true, name: '' }), summary({ id: 1 })]);

    expect(c.clipboard!.id).toBe(9);
    expect(c.named.map(s => s.id)).toEqual([1]);
  });

  it('die Filterzeile zeigt jedes vergebene Thema, häufigste zuerst', () => {
    const c = make([
      summary({ id: 1, themes: ['fork', 'pin'] }),
      summary({ id: 2, themes: ['fork'] }),
      summary({ id: 3, isClipboard: true, themes: [] }),
    ]);

    expect(c.allThemes).toEqual(['fork', 'pin']);
  });

  it('ein Thema dampft die Liste ein, ein zweiter Klick hebt den Filter auf', () => {
    const c = make([summary({ id: 1, themes: ['fork'] }), summary({ id: 2, themes: ['pin'] })]);

    c.toggleThemeFilter('fork');
    expect(c.named.map(s => s.id)).toEqual([1]);

    c.toggleThemeFilter('fork');
    expect(c.named.map(s => s.id)).toEqual([1, 2]);
  });

  it('die Schreibweise entscheidet beim Filtern nicht', () => {
    const c = make([summary({ id: 1, themes: ['Gabel'] })]);

    c.toggleThemeFilter('Gabel');
    c.themeFilter = 'gabel';

    expect(c.named.map(s => s.id)).toEqual([1]);
  });
});
