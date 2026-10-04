import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { AdminLeagueUploadsComponent } from './admin-league-uploads.component';
import { SnackbarService } from '../../../core/snackbar.service';
import { ConfirmService } from '../../../shared/confirm-dialog/confirm-dialog.component';
import { of } from 'rxjs';

describe('AdminLeagueUploadsComponent', () => {
  let fixture: ComponentFixture<AdminLeagueUploadsComponent>;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AdminLeagueUploadsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: SnackbarService, useValue: jasmine.createSpyObj('SnackbarService', ['success', 'warn']) },
        { provide: ConfirmService, useValue: { ask: () => of(true) } }],
    });
    fixture = TestBed.createComponent(AdminLeagueUploadsComponent);
    http = TestBed.inject(HttpTestingController);
  });

  it('listet die Stapel, anonym als Teilen-Link, und löscht nach Rückfrage', async () => {
    fixture.detectChanges();
    http.expectOne('/api/admin/league-uploads').flush([
      { id: 3, createdAt: '2026-10-04T18:00:00Z', finishedAt: '2026-10-04T18:05:00Z', user: null, viaShare: true, files: 12, bytes: 3 * 1048576, comment: 'Runde 3' },
      { id: 2, createdAt: '2026-10-04T17:00:00Z', finishedAt: null, user: 'mitglied', viaShare: false, files: 1, bytes: 1048576, comment: null },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();
    const rows = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr'));
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('admin.uploads.viaShare');
    expect(rows[0].textContent).toContain('3.0 MB');
    expect(rows[1].textContent).toContain('mitglied');
    expect(rows[1].textContent).toContain('admin.uploads.open');

    const p = fixture.componentInstance.remove(fixture.componentInstance.rows()![0]);
    await Promise.resolve();
    const req = http.expectOne('/api/admin/league-uploads/3');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
    await p;
    expect(fixture.componentInstance.rows()!.map(r => r.id)).toEqual([2]);
  });
});
