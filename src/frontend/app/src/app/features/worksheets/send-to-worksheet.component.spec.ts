import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { SendToWorksheetComponent } from './send-to-worksheet.component';
import { WorksheetSummary } from './worksheet.service';

const summary = (over: Partial<WorksheetSummary> = {}): WorksheetSummary => ({
  id: 1, name: '', isClipboard: false, perPage: 6, itemCount: 0, themes: [], shareToken: null,
  createdAt: '', updatedAt: '', ...over,
});

/**
 * Mit dem ECHTEN WorksheetService (nur HTTP gestubbt): geprüft wird, was der Nutzer im aufgeklappten
 * Menü sieht — Zwischenablage zuerst, dann die benannten Blätter — und dass erneutes Aufklappen die
 * Liste aus dem Service-Gedächtnis nimmt statt jedes Mal anzufragen.
 */
describe('SendToWorksheetComponent', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SendToWorksheetComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: Router, useValue: { navigate: jasmine.createSpy('navigate') } },
        { provide: SnackbarService, useValue: { warn: () => {}, info: () => {} } },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    document.querySelectorAll('.cdk-overlay-container').forEach(c => c.innerHTML = '');
    TestBed.resetTestingModule();
  });

  function render() {
    const fixture = TestBed.createComponent(SendToWorksheetComponent);
    fixture.componentRef.setInput('asButton', true);   // eigenständiger Knopf: kein umgebendes <mat-menu> nötig
    fixture.detectChanges();
    const picked: (number | null)[] = [];
    fixture.componentInstance.pick.subscribe(v => picked.push(v));
    const trigger = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    const items = () => Array.from(document.querySelectorAll<HTMLButtonElement>('.cdk-overlay-container [mat-menu-item]'));
    const openMenu = () => { trigger.click(); fixture.detectChanges(); };
    return { fixture, picked, items, openMenu };
  }

  it('lädt die Ziele erst beim Aufklappen: Zwischenablage zuerst, dann die benannten Blätter ohne die Ablage', () => {
    const { fixture, items, openMenu, picked } = render();
    http.expectNone('/api/worksheets');                 // nur die Kurs-Seite geöffnet: keine Anfrage

    openMenu();
    http.expectOne('/api/worksheets').flush([
      summary({ id: 9, isClipboard: true, itemCount: 4 }),
      summary({ id: 1, name: 'Taktik', itemCount: 3 }),
      summary({ id: 2, name: 'Endspiele', itemCount: 0 }),
    ]);
    fixture.detectChanges();

    const labels = items().map(b => b.textContent!.replace(/\s+/g, ' ').trim());
    expect(labels.length).toBe(3);
    expect(labels[0]).toContain('worksheets.clipboard');
    expect(labels[1]).toContain('Taktik');
    expect(labels[1]).toContain('3');
    expect(labels[2]).toContain('Endspiele');

    items()[0].click();
    expect(picked).toEqual([null]);                     // null = Zwischenablage
  });

  it('nimmt beim zweiten Aufklappen die Liste aus dem Gedächtnis und meldet die Blatt-ID', () => {
    const { fixture, items, openMenu, picked } = render();
    openMenu();
    http.expectOne('/api/worksheets').flush([summary({ id: 7, name: 'Taktik', itemCount: 1 })]);
    fixture.detectChanges();
    items()[1].click();                                 // schließt das Menü
    fixture.detectChanges();

    openMenu();                                         // http.verify() meldet eine zweite Anfrage
    fixture.detectChanges();
    expect(items().length).toBe(2);
    items()[1].click();

    expect(picked).toEqual([7, 7]);
  });

  it('ohne Zielliste (Fehler) bleibt die Zwischenablage wählbar', () => {
    const { fixture, items, openMenu, picked } = render();
    openMenu();
    http.expectOne('/api/worksheets').flush('x', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(items().length).toBe(1);
    expect(fixture.componentInstance.named).toEqual([]);
    items()[0].click();
    expect(picked).toEqual([null]);
  });
});
