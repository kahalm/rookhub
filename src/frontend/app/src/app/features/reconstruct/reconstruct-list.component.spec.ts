import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { ReconstructListComponent } from './reconstruct-list.component';

describe('ReconstructListComponent', () => {
  let http: HttpTestingController;

  function create(): ReconstructListComponent {
    const fixture = TestBed.createComponent(ReconstructListComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ReconstructListComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  it('lädt die Liste beim Öffnen', () => {
    const c = create();
    http.expectOne('/api/reconstructions').flush([
      { id: 1, title: 'Runde 3', partCount: 2, knownPlies: 12, gaps: 1, updatedAt: '2026-09-20T10:00:00Z' },
    ]);
    expect(c.items().length).toBe(1);
    expect(c.loading()).toBeFalse();
  });

  it('springt nach dem Anlegen in die neue Rekonstruktion', () => {
    const c = create();
    http.expectOne('/api/reconstructions').flush([]);
    const router = TestBed.inject(Router);
    const nav = spyOn(router, 'navigate');

    c.newTitle = '  Runde 4  ';
    c.create();
    const req = http.expectOne({ url: '/api/reconstructions', method: 'POST' });
    expect(req.request.body).toEqual({ title: 'Runde 4' });   // getrimmt
    req.flush({ id: 9 });

    expect(nav).toHaveBeenCalledWith(['/reconstruct', 9]);
    expect(c.newTitle).toBe('');
  });

  it('ein leerer Titel löst gar keine Anfrage aus', () => {
    const c = create();
    http.expectOne('/api/reconstructions').flush([]);
    c.newTitle = '   ';
    c.create();
    http.expectNone({ url: '/api/reconstructions', method: 'POST' });
  });
});
