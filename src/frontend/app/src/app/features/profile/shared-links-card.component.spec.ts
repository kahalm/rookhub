import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { SnackbarService } from '../../core/snackbar.service';
import { SharedLinksCardComponent } from './shared-links-card.component';

// Codereview A2-006 (entschieden 25.10.2026): eigene /l/-Links auflisten und widerrufen.
describe('SharedLinksCardComponent', () => {
  let http: HttpTestingController;
  let confirmAnswer = true;
  let snack: jasmine.SpyObj<SnackbarService>;
  const lines = [
    { id: 1, shareToken: 'tokA', title: 'Najdorf', repertoireName: 'Sizilianisch', repertoireId: 3, createdAt: '2026-10-20T10:00:00Z' },
    { id: 2, shareToken: 'tokB', title: null, repertoireName: null, repertoireId: null, createdAt: '2026-10-21T10:00:00Z' },
  ];

  beforeEach(() => {
    confirmAnswer = true;
    snack = jasmine.createSpyObj<SnackbarService>('SnackbarService', ['success', 'warn']);
    TestBed.configureTestingModule({
      imports: [SharedLinksCardComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: ConfirmService, useValue: { ask: () => of(confirmAnswer) } },
        { provide: SnackbarService, useValue: snack },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  function render() {
    const fixture = TestBed.createComponent(SharedLinksCardComponent);
    fixture.detectChanges();
    http.expectOne({ url: '/api/repertoires/shared-lines', method: 'GET' }).flush(lines);
    fixture.detectChanges();
    return fixture;
  }

  it('listet die eigenen Linien-Links mit Öffnen-Link; ohne Titel steht der Ersatz', () => {
    const el: HTMLElement = render().nativeElement;
    const rows = el.querySelectorAll('li.row');
    expect(rows.length).toBe(2);
    expect(rows[0].querySelector('.title')?.textContent?.trim()).toBe('Najdorf');
    expect(rows[0].querySelector('a.open')?.getAttribute('href')).toBe('/l/tokA');
    expect(rows[1].querySelector('.title')?.textContent?.trim()).toBe('—');
  });

  it('widerruft nach Rückfrage und nimmt die Zeile heraus; Abbruch tut nichts', () => {
    const fixture = render();
    const el: HTMLElement = fixture.nativeElement;

    confirmAnswer = false;
    (el.querySelector('li.row button.revoke') as HTMLButtonElement).click();
    http.expectNone('/api/repertoires/shared-lines/1');

    confirmAnswer = true;
    (el.querySelector('li.row button.revoke') as HTMLButtonElement).click();
    http.expectOne({ url: '/api/repertoires/shared-lines/1', method: 'DELETE' }).flush(null);
    fixture.detectChanges();
    expect(el.querySelectorAll('li.row').length).toBe(1);
    expect(snack.success).toHaveBeenCalledWith('profile.sharedLinks.revoked');
  });

  it('leere Liste und Ladefehler zeigen einen Hinweis', () => {
    const fixture = TestBed.createComponent(SharedLinksCardComponent);
    fixture.detectChanges();
    http.expectOne('/api/repertoires/shared-lines').flush([]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.empty')).not.toBeNull();

    const failing = TestBed.createComponent(SharedLinksCardComponent);
    failing.detectChanges();
    http.expectOne('/api/repertoires/shared-lines').flush('x', { status: 500, statusText: 'err' });
    failing.detectChanges();
    expect(failing.nativeElement.querySelector('.error')).not.toBeNull();
  });
});
