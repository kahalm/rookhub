import { TestBed, fakeAsync, tick, discardPeriodicTasks } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { SnackbarService } from '../../core/snackbar.service';
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

  // d3-rk-scoresheet-hilfe-icon-abgesetzt-handy: das ⓘ gehört IN den Titel, direkt hinter das letzte Wort.
  it('puts the help hint inside the title, right after its last word', async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges();
    http.expectOne('/api/scoresheets/status').flush(status());
    http.expectOne(r => r.url.startsWith('/api/scoresheets?')).flush([]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.header > h1 > app-help-hint')).not.toBeNull();
    expect(el.querySelector('.header > app-help-hint')).toBeNull();
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
    c.setSide('black');
    c.upload();

    const post = http.expectOne({ method: 'POST', url: '/api/scoresheets' });
    const body = post.request.body as FormData;
    expect((body.get('file') as File).name).toBe('sheet.jpg');
    expect(body.get('language')).toBe('de');
    expect(body.get('side')).toBe('black');
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

  // z1-rk-scoresheet-grund: der gesperrte Hauptknopf sagt, dass erst ein Foto fehlt.
  it('without a photo the read button is disabled and a quiet line says a photo is needed', async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges();
    http.expectOne('/api/scoresheets/status').flush(status());
    http.expectOne(r => r.url.startsWith('/api/scoresheets?')).flush([]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector<HTMLButtonElement>('.actions button[color="primary"]')?.disabled).toBeTrue();
    expect(el.querySelector('.actions .need-photo')?.textContent).toContain('scoresheet.needPhoto');
    // a1-rk-scoresheet-grund-trenner: der Grund steht in einer eigenen Zeile UNTER Knopf und Budget, nicht daneben.
    const btnRect = el.querySelector('.actions button[color="primary"]')!.getBoundingClientRect();
    const needRect = el.querySelector('.actions .need-photo')!.getBoundingClientRect();
    expect(btnRect.height).withContext('im DOM gerendert').toBeGreaterThan(0);
    expect(needRect.top).toBeGreaterThanOrEqual(btnRect.bottom - 1);

    const file = new File([new Uint8Array([1, 2, 3])], 'sheet.jpg', { type: 'image/jpeg' });
    fixture.componentInstance.onFile({ target: { files: [file], value: '' } } as unknown as Event);
    fixture.detectChanges();
    expect(el.querySelector('.actions .need-photo')).toBeNull();
    expect(el.querySelector<HTMLButtonElement>('.actions button[color="primary"]')?.disabled).toBeFalse();
  });

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

  // Seit 0.568.1 eine Einlesung je 24 h: die Seite sagt, ab wann die nächste geht, statt den Knopf stumm zu sperren.
  it('with today\'s scoresheet used up it disables reading and names when the next one is possible', async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges();
    http.expectOne('/api/scoresheets/status').flush({
      ...status(), dailyLimit: 1, usedToday: 1, nextAllowedAt: '2026-09-28T18:14:00Z',
    });
    http.expectOne(r => r.url.startsWith('/api/scoresheets?')).flush([]);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    expect(c.canRead()).toBeFalse();
    expect(c.nextTime()).not.toBe('');
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.upload .error')?.textContent).toContain('scoresheet.nextAt');
  });

  // Wunsch 2026-09-29: ein Formular über mehrere Blätter — bis zu drei Fotos, in Seitenreihenfolge, als EIN Upload.
  it('takes up to three photos as pages of one scoresheet and sends them in order', async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges();
    http.expectOne('/api/scoresheets/status').flush(status());
    http.expectOne(r => r.url.startsWith('/api/scoresheets?')).flush([]);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    const pick = (name: string) => c.onFile({
      target: { files: [new File([new Uint8Array([1])], name, { type: 'image/jpeg' })], value: '' },
    } as unknown as Event);

    pick('seite1.jpg');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('scoresheet.takePage');   // nächster Knopf nimmt Seite 2
    expect(el.textContent).toContain('scoresheet.morePages');
    pick('seite2.jpg');
    pick('falsch.jpg');
    c.removePage(2);                                            // vertauscht → raus
    pick('seite3.jpg');
    pick('zuviel.jpg');                                         // mehr als drei: ignoriert
    fixture.detectChanges();
    expect(c.pages().map(p => p.file.name)).toEqual(['seite1.jpg', 'seite2.jpg', 'seite3.jpg']);
    expect(el.querySelectorAll('.page').length).toBe(3);
    expect(el.textContent).not.toContain('scoresheet.takePage'); // voll: keine weiteren Knöpfe

    c.upload();
    const post = http.expectOne({ method: 'POST', url: '/api/scoresheets' });
    const files = (post.request.body as FormData).getAll('file') as File[];
    expect(files.map(f => f.name)).toEqual(['seite1.jpg', 'seite2.jpg', 'seite3.jpg']);
    post.flush({ id: 8, status: 'pending', notationLanguage: 'de', createdAt: '2026-09-29T10:00:00Z', rounds: 0,
      moveCount: 0, uncertainCount: 0, unresolvedCount: 0, pageCount: 3 });
    http.expectOne('/api/scoresheets/status').flush(status());
    expect(c.pages().length).toBe(0);
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

  // A6-020: nginx weist einen zu großen Rumpf mit einer HTML-413 ab — ohne `reason`. Das ist „zu groß", nicht „fehlgeschlagen".
  it('a 413 from the proxy (HTML, no reason) says the photo is too large', async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges();
    http.expectOne('/api/scoresheets/status').flush(status());
    http.expectOne(r => r.url.startsWith('/api/scoresheets?')).flush([]);
    const c = fixture.componentInstance;
    c.onFile({ target: { files: [new File([new Uint8Array([1])], 'a.jpg', { type: 'image/jpeg' })], value: '' } } as unknown as Event);
    c.upload();
    http.expectOne({ method: 'POST', url: '/api/scoresheets' })
      .flush('<html><head><title>413 Request Entity Too Large</title></head></html>', { status: 413, statusText: 'Request Entity Too Large' });
    expect(c.error()).toBe('scoresheet.error.tooLarge');
    expect(c.uploading()).toBeFalse();
  });

  // Gemeldet 2026-09-28: ein Abruf, der nicht durchkam, beendete das Nachfragen — die Seite zeigte weiter „wird gelesen".
  it('a failed status request is skipped, the next one still finds the finished game', fakeAsync(async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges();
    http.expectOne('/api/scoresheets/status').flush(status());
    http.expectOne(r => r.url.startsWith('/api/scoresheets?')).flush([scan('running')]);
    tick(SCAN_POLL_MS);
    http.expectOne('/api/scoresheets/7').error(new ProgressEvent('error'));      // Funkloch
    tick(SCAN_POLL_MS);
    http.expectOne('/api/scoresheets/7').flush(scan('done', { savedGameId: 42, moveCount: 40 }));
    http.expectOne(r => r.url.startsWith('/api/scoresheets?')).flush([]);
    expect(fixture.componentInstance.current()?.status).toBe('done');
    discardPeriodicTasks();
  }));

  it('the three segments of "I played" all start with a capital letter (e1-rk-scoresheet-side-auto-case)', async () => {
    for (const lang of ['en', 'de', 'hr', 'hu']) {
      let json: { scoresheet: Record<string, string> } | undefined;
      for (const url of [`/i18n/${lang}.json`, `/base/i18n/${lang}.json`]) {
        const res = await fetch(url);
        if (res.ok) { json = await res.json(); break; }
      }
      for (const k of ['sideWhite', 'sideBlack', 'sideAuto']) {
        const v = json!.scoresheet[k];
        expect(v[0]).withContext(`${lang}: scoresheet.${k} = ${v}`).toBe(v[0].toLocaleUpperCase(lang));
      }
    }
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

// Nachlauf 0.840.0: eigene Einlesung in der Liste „Zuletzt eingelesen“ löschen (DELETE /api/scoresheets/{id}).
describe('ScoresheetUploadComponent — Einlesung löschen', () => {
  let answer: boolean;
  let snackbar: jasmine.SpyObj<SnackbarService>;
  const scan = (id: number, extra: object = {}) => ({
    id, status: 'done', notationLanguage: 'de', createdAt: '2026-09-25T10:00:00Z', rounds: 1,
    moveCount: 40, uncertainCount: 0, unresolvedCount: 0, white: 'A', black: 'B', ...extra,
  });

  async function setup() {
    answer = true;
    snackbar = jasmine.createSpyObj<SnackbarService>('SnackbarService', ['info', 'warn', 'success']);
    const confirm = { ask: jasmine.createSpy('ask').and.callFake(() => of(answer)) };
    await TestBed.configureTestingModule({
      imports: [ScoresheetUploadComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: ConfirmService, useValue: confirm },
        { provide: SnackbarService, useValue: snackbar },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ScoresheetUploadComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/scoresheets/status').flush({ available: true, dailyLimit: 20, usedToday: 2, languages: [] });
    http.expectOne(r => r.url.startsWith('/api/scoresheets?')).flush([scan(7, { savedGameId: 42 }), scan(8)]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const del = (i: number) => el.querySelectorAll<HTMLButtonElement>('.recent .row button.delete')[i];
    return { fixture, http, el, del, confirm };
  }

  it('jede Zeile hat einen benannten Löschen-Knopf; nach der Rückfrage geht DELETE, 204 nimmt die Zeile heraus', async () => {
    const { fixture, http, el, del, confirm } = await setup();
    expect(el.querySelectorAll('.recent .row').length).toBe(2);
    expect(del(0).getAttribute('aria-label')).toBe('scoresheet.delete');
    del(1).click();
    expect(confirm.ask).toHaveBeenCalledWith('scoresheet.deleteConfirm');
    const req = http.expectOne({ method: 'DELETE', url: '/api/scoresheets/8' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    expect(fixture.componentInstance.recent().map(s => s.id)).toEqual([7]);
    expect(snackbar.info).toHaveBeenCalledWith('scoresheet.deleted');
    http.verify();
  });

  it('abgebrochene Rückfrage löscht nichts', async () => {
    const { http, del } = await setup();
    answer = false;
    del(0).click();
    http.expectNone({ method: 'DELETE' });
    http.verify();
  });

  it('409 busy: die Zeile bleibt, der Hinweis sagt, dass sie noch gelesen wird', async () => {
    const { fixture, http, del } = await setup();
    del(0).click();
    http.expectOne({ method: 'DELETE', url: '/api/scoresheets/7' })
      .flush({ reason: 'busy' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(fixture.componentInstance.recent().length).toBe(2);
    expect(snackbar.warn).toHaveBeenCalledWith('scoresheet.deleteBusy');
    expect(del(0).disabled).toBeFalse();
  });

  it('404: schon weg — die Zeile geht trotzdem aus der Liste', async () => {
    const { fixture, http, del } = await setup();
    del(0).click();
    http.expectOne({ method: 'DELETE', url: '/api/scoresheets/7' }).flush(null, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(fixture.componentInstance.recent().map(s => s.id)).toEqual([8]);
    expect(snackbar.info).toHaveBeenCalledWith('scoresheet.deleteGone');
  });

  it('anderer Fehler: Zeile bleibt, Fehlerhinweis', async () => {
    const { fixture, http, del } = await setup();
    del(0).click();
    http.expectOne({ method: 'DELETE', url: '/api/scoresheets/7' }).flush(null, { status: 500, statusText: 'Error' });
    fixture.detectChanges();
    expect(fixture.componentInstance.recent().length).toBe(2);
    expect(snackbar.warn).toHaveBeenCalledWith('scoresheet.deleteFailed');
  });
});
