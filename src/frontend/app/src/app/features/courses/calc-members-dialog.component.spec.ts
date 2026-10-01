import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { CalcMembersDialogComponent } from './calc-members-dialog.component';
import { CalcSeriesMember } from './calc-editions.service';

const BOOK = 12;
const URL = `/api/calc-editions/${BOOK}`;

function member(userId: number, username: string, isTester = false): CalcSeriesMember {
  return { userId, username, isTester, createdAt: '2026-09-01T10:00:00Z' };
}

/**
 * Der Dialog ist OnPush und hält seinen Zustand in Feldern. Der renderAfterHttp-Interceptor tickt nur
 * die App (`appRef.tick()`), das überspringt eine unmarkierte OnPush-Ansicht — ohne eigene Markierung
 * nach jeder HTTP-Antwort blieben Ladebalken, leere Liste und gesperrte Knöpfe stehen. Die Tests
 * arbeiten deshalb mit dem ECHTEN OnPush-Fixture: `fixture.detectChanges()` zeichnet nur neu, wenn
 * die Komponente sich selbst markiert hat.
 */
describe('CalcMembersDialogComponent (OnPush zeichnet nach HTTP neu)', () => {
  let http: HttpTestingController;
  let warn: jasmine.Spy;
  let quick: jasmine.Spy;

  function create() {
    const fixture = TestBed.createComponent(CalcMembersDialogComponent);
    fixture.detectChanges();          // erster Lauf: busy = true, Liste leer
    return fixture;
  }

  /** Die drei Ladeanfragen eines reload(): Mitglieder (kritisch) + Ausgaben/Sichten (best effort). */
  function flushReload(members: CalcSeriesMember[]): void {
    http.expectOne(`${URL}/members`).flush(members);
    http.expectOne(`${URL}/manage`).flush([
      { id: 1, bookId: BOOK, chapter: 'KW1', publishAt: '2026-09-01T00:00:00Z', released: true },
      { id: 2, bookId: BOOK, chapter: 'KW2', publishAt: '2026-09-08T00:00:00Z', released: true },
    ]);
    http.expectOne(`${URL}/views`).flush([
      { editionId: 1, chapter: 'KW1', userId: 1, username: 'anna', viewedAt: '2026-09-02T00:00:00Z' },
    ]);
  }

  const text = (el: HTMLElement) => el.textContent!.replace(/\s+/g, ' ');

  beforeEach(async () => {
    warn = jasmine.createSpy('warn');
    quick = jasmine.createSpy('quick');
    await TestBed.configureTestingModule({
      imports: [CalcMembersDialogComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MAT_DIALOG_DATA, useValue: { bookId: BOOK } },
        { provide: SnackbarService, useValue: { warn, quick } },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('zeigt die geladenen Mitglieder samt „Gesehen"-Zähler und gibt die Eingabe frei', () => {
    const fixture = create();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('mat-progress-bar')).not.toBeNull();

    flushReload([member(1, 'anna'), member(2, 'bert', true)]);
    fixture.detectChanges();

    expect(el.querySelector('mat-progress-bar')).toBeNull();
    const names = Array.from(el.querySelectorAll('.member .name')).map(n => n.textContent!.trim());
    expect(names).toEqual(['anna', 'bert']);
    // Zähler aus forkJoin (Ausgaben + Sichten): ohne Übersetzungen steht dort der Schlüssel.
    expect(el.querySelectorAll('.seen').length).toBe(2);
    expect((el.querySelector('.add-row input') as HTMLInputElement).disabled).toBeFalse();
  });

  it('zeigt „keine Mitglieder", wenn die Liste leer ist', () => {
    const fixture = create();
    flushReload([]);
    fixture.detectChanges();
    expect(text(fixture.nativeElement)).toContain('calc.series.noMembers');
  });

  it('nimmt ein hinzugefügtes Mitglied in die Liste auf und leert die Eingabe', async () => {
    const fixture = create();
    flushReload([member(1, 'anna')]);
    fixture.detectChanges();

    fixture.componentInstance.newUsername = 'carla';
    fixture.componentInstance.add();
    http.expectOne(r => r.method === 'PUT' && r.url === `${URL}/members`).flush(member(3, 'carla'));
    flushReload([member(1, 'anna'), member(3, 'carla')]);
    fixture.detectChanges();
    await fixture.whenStable();

    const el: HTMLElement = fixture.nativeElement;
    expect(quick).toHaveBeenCalled();
    const names = Array.from(el.querySelectorAll('.member .name')).map(n => n.textContent!.trim());
    expect(names).toEqual(['anna', 'carla']);
    expect((el.querySelector('.add-row input') as HTMLInputElement).value).toBe('');
  });

  it('gibt nach einem gescheiterten Laden die Eingabe wieder frei', () => {
    const fixture = create();
    http.expectOne(`${URL}/members`).flush('x', { status: 500, statusText: 'Server Error' });
    http.expectOne(`${URL}/manage`).flush([]);
    http.expectOne(`${URL}/views`).flush([]);
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(warn).toHaveBeenCalled();
    expect(el.querySelector('mat-progress-bar')).toBeNull();
    expect((el.querySelector('.add-row input') as HTMLInputElement).disabled).toBeFalse();
  });
});
