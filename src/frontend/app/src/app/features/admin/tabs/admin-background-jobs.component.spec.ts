import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { AdminBackgroundJobsComponent, BACKGROUND_JOBS_URL, BackgroundJobsPause } from './admin-background-jobs.component';
import { SnackbarService } from '../../../core/snackbar.service';

describe('AdminBackgroundJobsComponent', () => {
  let fixture: ComponentFixture<AdminBackgroundJobsComponent>;
  let http: HttpTestingController;
  let snack: jasmine.SpyObj<SnackbarService>;

  const running: BackgroundJobsPause = {
    paused: false, since: null, by: null, forcedByConfig: false, pausedInDb: false,
    services: [
      { name: 'HeartbeatService', pausable: false, lastTick: null },
      { name: 'AnalysisJobWorker', pausable: true, lastTick: '2026-10-20T12:00:00Z' },
    ],
  };
  const paused: BackgroundJobsPause = {
    ...running, paused: true, pausedInDb: true, since: '2026-10-20T12:30:00Z', by: 'kahalm',
  };

  const el = () => fixture.nativeElement as HTMLElement;
  const q = (id: string) => el().querySelector(`[data-testid="${id}"]`);

  async function render(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(() => {
    snack = jasmine.createSpyObj('SnackbarService', ['success', 'warn']);
    TestBed.configureTestingModule({
      imports: [AdminBackgroundJobsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: SnackbarService, useValue: snack }],
    });
    fixture = TestBed.createComponent(AdminBackgroundJobsComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('zeigt „läuft" und die Dienste, pausierbare zuerst', async () => {
    fixture.detectChanges();
    http.expectOne(BACKGROUND_JOBS_URL).flush(running);
    await render();

    expect(q('pause-status')!.textContent).toContain('admin.jobs.statusRunning');
    expect(q('pause-since')).toBeNull();
    expect(q('pause-forced')).toBeNull();
    const rows = Array.from(el().querySelectorAll('[data-testid="job-service"]'));
    expect(rows.map(r => r.querySelector('.name')!.textContent!.trim())).toEqual(['AnalysisJobWorker', 'HeartbeatService']);
    expect(rows[0].textContent).toContain('admin.jobs.pausable');
    expect(rows[0].textContent).toContain('admin.jobs.lastTick');
    expect(rows[1].textContent).toContain('admin.jobs.notPausable');
    expect(rows[1].textContent).toContain('admin.jobs.noTick');
  });

  it('pausiert über den Schalter (PUT) und zeigt danach seit wann und wer', async () => {
    fixture.detectChanges();
    http.expectOne(BACKGROUND_JOBS_URL).flush(running);
    await render();

    const input = el().querySelector('[data-testid="pause-toggle"] button, [data-testid="pause-toggle"] input') as HTMLElement;
    input.click();
    const req = http.expectOne(BACKGROUND_JOBS_URL);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ paused: true });
    req.flush(paused);
    await render();

    expect(q('pause-status')!.textContent).toContain('admin.jobs.statusPaused');
    expect(q('pause-since')).not.toBeNull();
    expect(snack.success).toHaveBeenCalledWith('admin.jobs.pausedNow');
  });

  it('meldet einen gescheiterten Wechsel und lässt den Stand stehen', async () => {
    fixture.detectChanges();
    http.expectOne(BACKGROUND_JOBS_URL).flush(running);
    await render();

    const input = el().querySelector('[data-testid="pause-toggle"] button, [data-testid="pause-toggle"] input') as HTMLElement;
    input.click();
    http.expectOne(BACKGROUND_JOBS_URL).flush('nope', { status: 500, statusText: 'err' });
    await render();

    expect(snack.warn).toHaveBeenCalledWith('admin.jobs.saveError');
    expect(fixture.componentInstance.state()!.paused).toBeFalse();
    expect(q('pause-status')!.textContent).toContain('admin.jobs.statusRunning');
  });

  it('weist auf die per Konfiguration erzwungene Pause hin', async () => {
    fixture.detectChanges();
    http.expectOne(BACKGROUND_JOBS_URL).flush({ ...running, paused: true, forcedByConfig: true });
    await render();

    expect(q('pause-status')!.textContent).toContain('admin.jobs.statusPaused');
    expect(q('pause-forced')!.textContent).toContain('admin.jobs.forced');
    expect(q('pause-since')).toBeNull();          // nicht per Schalter pausiert → kein „seit … von …"
  });

  it('zeigt beim gescheiterten Laden den Fehlerzustand mit „Erneut versuchen"', async () => {
    fixture.detectChanges();
    http.expectOne(BACKGROUND_JOBS_URL).flush('x', { status: 500, statusText: 'err' });
    await render();

    expect(el().querySelector('app-load-error')).not.toBeNull();
    expect(el().querySelector('[data-testid="pause-toggle"]')).toBeNull();
  });
});
