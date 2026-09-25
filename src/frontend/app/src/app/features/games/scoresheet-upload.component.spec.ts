import { TestBed, fakeAsync, tick, discardPeriodicTasks } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { SCAN_POLL_MS, ScoresheetUploadComponent } from './scoresheet-upload.component';

describe('ScoresheetUploadComponent', () => {
  async function setup() {
    await TestBed.configureTestingModule({
      imports: [ScoresheetUploadComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    return { fixture: TestBed.createComponent(ScoresheetUploadComponent), http: TestBed.inject(HttpTestingController) };
  }

  const status = (available = true) => ({
    available, dailyLimit: 20, usedToday: 2,
    languages: [{ code: 'de', name: 'Deutsch', pieces: 'K D T L S' }],
  });
  const scan = (s: string, extra: object = {}) => ({
    id: 7, status: s, notationLanguage: 'de', createdAt: '2026-09-25T10:00:00Z', rounds: 1,
    moveCount: 0, uncertainCount: 0, unresolvedCount: 0, ...extra,
  });

  it('without an AI key it says so and offers no upload', async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges();
    http.expectOne('/api/scoresheets/status').flush(status(false));
    http.expectOne(r => r.url.startsWith('/api/scoresheets?')).flush([]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.notice')).toBeTruthy();
    expect(el.querySelector('.upload')).toBeNull();
  });

  it('uploads the photo with the chosen language, then polls until the game is there', fakeAsync(async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges();
    http.expectOne('/api/scoresheets/status').flush(status());
    http.expectOne(r => r.url.startsWith('/api/scoresheets?')).flush([]);
    fixture.detectChanges();

    const c = fixture.componentInstance;
    const file = new File([new Uint8Array([1, 2, 3])], 'sheet.jpg', { type: 'image/jpeg' });
    c.onFile({ target: { files: [file], value: '' } } as unknown as Event);
    c.language = 'de';
    c.upload();

    const post = http.expectOne({ method: 'POST', url: '/api/scoresheets' });
    const body = post.request.body as FormData;
    expect((body.get('file') as File).name).toBe('sheet.jpg');
    expect(body.get('language')).toBe('de');
    post.flush(scan('pending'));
    http.expectOne('/api/scoresheets/status').flush(status());
    fixture.detectChanges();
    expect(c.current()?.status).toBe('pending');

    tick(SCAN_POLL_MS);
    http.expectOne('/api/scoresheets/7').flush(scan('running'));
    tick(SCAN_POLL_MS);
    http.expectOne('/api/scoresheets/7').flush(scan('done', { savedGameId: 42, moveCount: 66, uncertainCount: 2 }));
    http.expectOne(r => r.url.startsWith('/api/scoresheets?')).flush([]);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const review = el.querySelector('a[href="/games/42/edit"]');
    expect(review).toBeTruthy();
    // Danach wird nicht weiter nachgefragt.
    tick(SCAN_POLL_MS * 2);
    http.expectNone('/api/scoresheets/7');
    discardPeriodicTasks();
  }));

  it('an exhausted budget disables reading and says why', async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges();
    http.expectOne('/api/scoresheets/status').flush({ ...status(), blocked: 'userDailyBudget', budgetUsedPercent: 100 });
    http.expectOne(r => r.url.startsWith('/api/scoresheets?')).flush([]);
    fixture.detectChanges();
    expect(fixture.componentInstance.canRead()).toBeFalse();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.upload .error')?.textContent).toContain('scoresheet.error.userDailyBudget');
  });

  it('a refused upload shows the reason', async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges();
    http.expectOne('/api/scoresheets/status').flush(status());
    http.expectOne(r => r.url.startsWith('/api/scoresheets?')).flush([]);
    const c = fixture.componentInstance;
    c.onFile({ target: { files: [new File([new Uint8Array([1])], 'a.jpg', { type: 'image/jpeg' })], value: '' } } as unknown as Event);
    c.upload();
    http.expectOne({ method: 'POST', url: '/api/scoresheets' })
      .flush({ reason: 'dailyLimit' }, { status: 400, statusText: 'Bad Request' });
    expect(c.error()).toBe('scoresheet.error.dailyLimit');
    expect(c.uploading()).toBeFalse();
  });

  it('coming back while a scoresheet is still being read follows it again', fakeAsync(async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges();
    http.expectOne('/api/scoresheets/status').flush(status());
    http.expectOne(r => r.url.startsWith('/api/scoresheets?')).flush([scan('running')]);
    expect(fixture.componentInstance.current()?.id).toBe(7);
    tick(SCAN_POLL_MS);
    http.expectOne('/api/scoresheets/7').flush(scan('failed', { error: 'noMoves' }));
    http.expectOne(r => r.url.startsWith('/api/scoresheets?')).flush([scan('failed', { error: 'noMoves' })]);
    discardPeriodicTasks();
  }));
});
