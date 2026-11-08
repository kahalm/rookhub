import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '../../../core/auth.service';
import { ClubSummaryLinkComponent } from './club-summary-link.component';

describe('ClubSummaryLinkComponent (0.913.0)', () => {
  function setup(isAdmin: boolean) {
    TestBed.configureTestingModule({
      imports: [ClubSummaryLinkComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isAdmin } }],
    });
    const f = TestBed.createComponent(ClubSummaryLinkComponent);
    f.componentRef.setInput('gameId', 236);
    f.detectChanges();
    return { f, http: TestBed.inject(HttpTestingController), el: f.nativeElement as HTMLElement };
  }
  const none = { status: 'none', markdown: null, doneAt: null, requestedAt: null, stale: false, model: null, error: null };

  it('zeigt den Link, sobald es einen Text gibt — ohne Anforder-Knopf für Nicht-Admins', () => {
    const { f, http, el } = setup(false);
    http.expectOne('/api/league/club/games/236/summary').flush({ ...none, status: 'done', markdown: '# x' });
    f.detectChanges();
    expect(el.querySelector('a.summary-link')?.getAttribute('href')).toBe('/club-games/236/summary');
    expect(el.querySelector('.summary-request')).toBeNull();
  });

  it('Admin: anfordern schickt POST und zeigt danach den Wartestand', () => {
    const { f, http, el } = setup(true);
    http.expectOne('/api/league/club/games/236/summary').flush(none);
    f.detectChanges();
    expect(el.querySelector('a.summary-link')).toBeNull();
    (el.querySelector('.summary-request') as HTMLButtonElement).click();
    const req = http.expectOne('/api/league/club/games/236/summary');
    expect(req.request.method).toBe('POST');
    req.flush({ ...none, status: 'requested' });
    f.detectChanges();
    expect(el.querySelector('.summary-wait')).not.toBeNull();
    expect(el.querySelector('.summary-request')).toBeNull();
  });
});
