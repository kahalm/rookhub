import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { EngineCardComponent } from './engine-card.component';

describe('EngineCardComponent', () => {
  let fixture: ComponentFixture<EngineCardComponent>;
  let component: EngineCardComponent;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EngineCardComponent, NoopAnimationsModule],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();

    fixture = TestBed.createComponent(EngineCardComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // Seit dem eigenen Broker gibt es Engines auch OHNE Lichess-Token („RookHub direkt") — die Liste wird
  // deshalb immer geholt; ohne Token bleibt nur der Lichess-Teil leer.
  it('queries the engine list even without a Lichess token and shows direct engines', () => {
    fixture.detectChanges();
    http.expectOne('/api/engine/credentials').flush({ hasCredentials: false, maskedToken: null });
    http.expectOne('/api/engine/external').flush({
      hasCredentials: false, tokenInvalid: false,
      engines: [{ id: 'rhe_aaaaaaaaaaaa', name: 'Heim-PC', maxThreads: 8, maxHash: 512, source: 'rookhub', online: true }],
    });
    fixture.detectChanges();

    expect(component.hasCredentials).toBeFalse();
    expect(component.directEngines.map(e => e.id)).toEqual(['rhe_aaaaaaaaaaaa']);
    expect(component.lichessEngines).toEqual([]);
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.direct-list')?.textContent).toContain('Heim-PC');
    expect(el.querySelector('.direct-list .dot.on')).not.toBeNull();   // Online-Punkt
  });

  it('marks an offline direct engine and splits the sources', () => {
    fixture.detectChanges();
    http.expectOne('/api/engine/credentials').flush({ hasCredentials: true, maskedToken: '****abcd' });
    http.expectOne('/api/engine/external').flush({
      hasCredentials: true, tokenInvalid: false,
      engines: [
        { id: 'rhe_bbbbbbbbbbbb', name: 'Laptop', maxThreads: 4, maxHash: 256, source: 'rookhub', online: false },
        { id: 'eei_cloud', name: 'Cloud', maxThreads: 32, maxHash: 8192, source: 'lichess', online: null },
      ],
    });
    fixture.detectChanges();

    expect(component.directEngines.length).toBe(1);
    expect(component.lichessEngines.map(e => e.id)).toEqual(['eei_cloud']);
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.direct-list .dot')).not.toBeNull();
    expect(el.querySelector('.direct-list .dot.on')).toBeNull();
  });

  it('says so when Lichess is unreachable but direct engines are listed', () => {
    fixture.detectChanges();
    http.expectOne('/api/engine/credentials').flush({ hasCredentials: true, maskedToken: '****abcd' });
    http.expectOne('/api/engine/external').flush({
      hasCredentials: true, tokenInvalid: false, lichessUnreachable: true,
      engines: [{ id: 'rhe_aaaaaaaaaaaa', name: 'Heim-PC', maxThreads: 8, maxHash: 512, source: 'rookhub', online: true }],
    });
    expect(component.lichessUnreachable).toBeTrue();
  });

  it('removes a direct engine via DELETE /api/external-engine/{id} and reloads', () => {
    spyOn(window, 'confirm').and.returnValue(true);
    fixture.detectChanges();
    http.expectOne('/api/engine/credentials').flush({ hasCredentials: false, maskedToken: null });
    http.expectOne('/api/engine/external').flush({
      hasCredentials: false, tokenInvalid: false,
      engines: [{ id: 'rhe_aaaaaaaaaaaa', name: 'Heim-PC', maxThreads: 8, maxHash: 512, source: 'rookhub', online: true }],
    });

    component.removeDirect(component.directEngines[0]);
    const del = http.expectOne('/api/external-engine/rhe_aaaaaaaaaaaa');
    expect(del.request.method).toBe('DELETE');
    del.flush(null);
    http.expectOne('/api/engine/external').flush({ hasCredentials: false, tokenInvalid: false, engines: [] });
    expect(component.directEngines).toEqual([]);
  });

  it('does not delete when the confirmation is declined', () => {
    spyOn(window, 'confirm').and.returnValue(false);
    fixture.detectChanges();
    http.expectOne('/api/engine/credentials').flush({ hasCredentials: false, maskedToken: null });
    http.expectOne('/api/engine/external').flush({
      hasCredentials: false, tokenInvalid: false,
      engines: [{ id: 'rhe_aaaaaaaaaaaa', name: 'Heim-PC', maxThreads: 8, maxHash: 512, source: 'rookhub', online: true }],
    });
    component.removeDirect(component.directEngines[0]);
    http.expectNone('/api/external-engine/rhe_aaaaaaaaaaaa');
  });

  it('loads the engine list when a token is stored', () => {
    fixture.detectChanges();
    http.expectOne('/api/engine/credentials').flush({ hasCredentials: true, maskedToken: '****abcd' });
    http.expectOne('/api/engine/external').flush({
      hasCredentials: true, tokenInvalid: false,
      engines: [{ id: 'eei_a', name: 'SF Heim-PC', maxThreads: 8, maxHash: 512 }],
    });

    expect(component.engines.length).toBe(1);
    expect(component.maskedToken).toBe('****abcd');
    expect(component.tokenInvalid).toBeFalse();
  });

  // MEHRERE Hintergrund-Engines sind der Sinn der Sache: der Server rechnet je Engine EINEN
  // Auftrag, es laufen also so viele nebeneinander, wie hier ausgewaehlt sind.
  it('saves the chosen background engines via PUT /api/engine/background', () => {
    fixture.detectChanges();
    http.expectOne('/api/engine/credentials').flush({ hasCredentials: true, maskedToken: '****abcd' });
    http.expectOne('/api/engine/external').flush({
      hasCredentials: true, tokenInvalid: false, backgroundEngineIds: [],
      engines: [{ id: 'eei_a', name: 'Live', maxThreads: 8, maxHash: 512 }, { id: 'eei_b', name: 'Hintergrund', maxThreads: 8, maxHash: 8192 }],
    });
    expect(component.backgroundEngineIds).toEqual([]);

    component.backgroundEngineIds = ['eei_b', 'eei_a'];
    component.saveBackground();
    const req = http.expectOne('/api/engine/background');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ engineIds: ['eei_b', 'eei_a'] });
    req.flush({ backgroundEngineIds: ['eei_b', 'eei_a'] });
    expect(component.backgroundEngineIds).toEqual(['eei_b', 'eei_a']);
  });

  it('flags a rejected token instead of showing an empty list', () => {
    fixture.detectChanges();
    http.expectOne('/api/engine/credentials').flush({ hasCredentials: true, maskedToken: '****dead' });
    http.expectOne('/api/engine/external').flush({ hasCredentials: true, tokenInvalid: true, engines: [] });

    expect(component.tokenInvalid).toBeTrue();
  });

  it('saves a token, clears the input and reloads the engines', () => {
    fixture.detectChanges();
    http.expectOne(r => r.url === '/api/engine/credentials' && r.method === 'GET').flush({ hasCredentials: false, maskedToken: null });
    http.expectOne('/api/engine/external').flush({ hasCredentials: false, tokenInvalid: false, engines: [] });

    component.tokenInput = '  lip_tok  ';
    component.save();

    const req = http.expectOne(r => r.url === '/api/engine/credentials' && r.method === 'POST');
    expect(req.request.body).toEqual({ token: 'lip_tok' });   // getrimmt
    req.flush({ hasCredentials: true, maskedToken: '****_tok' });

    http.expectOne('/api/engine/external').flush({ hasCredentials: true, tokenInvalid: false, engines: [] });
    expect(component.tokenInput).toBe('');
    expect(component.hasCredentials).toBeTrue();
    expect(component.saving).toBeFalse();
  });

  it('resets the state after deleting the token — direct engines stay', () => {
    fixture.detectChanges();
    http.expectOne('/api/engine/credentials').flush({ hasCredentials: true, maskedToken: '****abcd' });
    http.expectOne('/api/engine/external').flush({
      hasCredentials: true, tokenInvalid: false,
      engines: [
        { id: 'eei_a', name: 'SF', maxThreads: 2, maxHash: 64 },
        { id: 'rhe_aaaaaaaaaaaa', name: 'Heim-PC', maxThreads: 8, maxHash: 512, source: 'rookhub', online: true },
      ],
    });

    component.remove();
    http.expectOne(r => r.url === '/api/engine/credentials' && r.method === 'DELETE').flush(null);
    // Nach dem Löschen wird neu geholt: die Lichess-Engine ist weg, die direkte bleibt.
    http.expectOne('/api/engine/external').flush({
      hasCredentials: false, tokenInvalid: false,
      engines: [{ id: 'rhe_aaaaaaaaaaaa', name: 'Heim-PC', maxThreads: 8, maxHash: 512, source: 'rookhub', online: true }],
    });

    expect(component.hasCredentials).toBeFalse();
    expect(component.lichessEngines.length).toBe(0);
    expect(component.directEngines.length).toBe(1);
    expect(component.maskedToken).toBeNull();
  });
});
