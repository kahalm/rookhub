import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { EngineCardComponent } from './engine-card.component';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';

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

  // UI-Sweep 2026-10-11 (d2-rk-engine-token-align): Token-Feld ohne reservierten Hinweisbereich — Knopf auf einer Linie.
  it('token field sizes its subscript dynamically', () => {
    fixture.detectChanges();
    http.expectOne('/api/engine/credentials').flush({ hasCredentials: false, maskedToken: null });
    http.expectOne('/api/engine/external').flush({ hasCredentials: false, tokenInvalid: false, engines: [] });
    fixture.detectChanges();
    const field = (fixture.nativeElement as HTMLElement).querySelector('.token-field');
    expect(field?.querySelector('.mat-mdc-form-field-subscript-dynamic-size')).not.toBeNull();
  });

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
    spyOn(TestBed.inject(ConfirmService), 'ask').and.returnValue(of(true));
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
    spyOn(TestBed.inject(ConfirmService), 'ask').and.returnValue(of(false));
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

  // UI-Sweep d3 (d3-rk-profil-lichess-speichern-gesperrt-ohne-grund): kein grundlos gesperrter Knopf — leer geklickt
  // nennt ein Hinweis den Grund, das Feld bekommt den Fokus, gesendet wird nichts; Tippen raeumt den Hinweis ab.
  it('leeres Token: Speichern bleibt klickbar, zeigt den Grund und sendet nichts', () => {
    fixture.detectChanges();
    http.expectOne('/api/engine/credentials').flush({ hasCredentials: false, maskedToken: null });
    http.expectOne('/api/engine/external').flush({ hasCredentials: false, tokenInvalid: false, engines: [] });
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const btn = [...el.querySelectorAll('button')].find(b => b.textContent?.includes('common.save')) as HTMLButtonElement;
    expect(btn.disabled).toBeFalse();
    expect(el.querySelector('.token-missing')).toBeNull();

    btn.click();
    fixture.detectChanges();
    http.expectNone(r => r.url === '/api/engine/credentials' && r.method === 'POST');
    expect(el.querySelector('.token-missing')?.textContent).toContain('profile.engine.tokenMissing');
    expect(document.activeElement).toBe(el.querySelector('.token-field input'));

    const input = el.querySelector('.token-field input') as HTMLInputElement;
    input.value = 'lip_x';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(el.querySelector('.token-missing')).toBeNull();
  });

  it('resets the state after deleting the token — direct engines stay', () => {
    spyOn(TestBed.inject(ConfirmService), 'ask').and.returnValue(of(true));
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

  // F5-016: der Token lässt sich nicht wiederherstellen (nur maskiert gespeichert) → erst nach Rückfrage.
  it('keeps the token when the confirmation is declined', () => {
    const ask = spyOn(TestBed.inject(ConfirmService), 'ask').and.returnValue(of(false));
    fixture.detectChanges();
    http.expectOne('/api/engine/credentials').flush({ hasCredentials: true, maskedToken: '****abcd' });
    http.expectOne('/api/engine/external').flush({ hasCredentials: true, tokenInvalid: false, engines: [] });

    component.remove();

    expect(ask).toHaveBeenCalledWith('profile.engine.removeTokenConfirm');
    http.expectNone(r => r.url === '/api/engine/credentials' && r.method === 'DELETE');
    expect(component.hasCredentials).toBeTrue();
  });

  // UI-Sweep 2026-10-11 (r2-rk-engine-jargon): Adresse der Seite zum Kopieren statt einer nackten Variablen.
  it('shows the own address and copies it to the clipboard', async () => {
    const write = jasmine.createSpy('writeText').and.returnValue(Promise.resolve());
    spyOnProperty(navigator, 'clipboard', 'get').and.returnValue({ writeText: write } as any);
    fixture.detectChanges();
    http.expectOne('/api/engine/credentials').flush({ hasCredentials: false, maskedToken: null });
    http.expectOne('/api/engine/external').flush({ hasCredentials: false, tokenInvalid: false, engines: [] });
    fixture.detectChanges();
    // b1: Adresse/Anleitung stehen hinter „Einrichten“ (zu Beginn zugeklappt).
    (fixture.nativeElement.querySelector('.setup-toggle') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(component.rookhubUrl).toBe(window.location.origin);
    expect(fixture.nativeElement.querySelector('.direct-url code').textContent).toContain(window.location.origin);
    component.copyUrl();
    expect(write).toHaveBeenCalledWith(window.location.origin);
  });

  // UI-Sweep b1 (b1-rk-profil-tokens-engine-textwand): ein Satz sichtbar, die Einrichtung zugeklappt.
  it('keeps the direct setup collapsed behind „Einrichten“ until opened', () => {
    fixture.detectChanges();
    http.expectOne('/api/engine/credentials').flush({ hasCredentials: false, maskedToken: null });
    http.expectOne('/api/engine/external').flush({ hasCredentials: false, tokenInvalid: false, engines: [] });
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    const toggle = el.querySelector('.setup-toggle') as HTMLButtonElement;
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(el.querySelector('.direct-setup')).toBeNull();
    expect(el.textContent).not.toContain('profile.engine.directHint');
    toggle.click();
    fixture.detectChanges();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(el.querySelector('.direct-setup .direct-url')).not.toBeNull();
    expect(el.querySelector('.direct-setup')!.textContent).toContain('profile.engine.directHint');
  });

  // UI-Sweep b1 (b1-rk-profil-lichess-token-feld-schmal): am Handy volle Breite wie die übrigen Profilfelder.
  it('token field spans the full width on phones (< 600 px)', () => {
    const css = (((EngineCardComponent as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? [])
      .join('\n').replace(/%NS%/g, '').replace(/\[_ngcontent-%COMP%\]/g, '').replace(/\s+/g, ' ');
    expect(css).toMatch(/@media \(max-width: ?599\.98px\) ?\{ ?\.token-field, ?\.bg-field ?\{ ?width: ?100%/);
    expect(css).toMatch(/\.token-field ?\{ ?width: ?320px/);
  });
});
