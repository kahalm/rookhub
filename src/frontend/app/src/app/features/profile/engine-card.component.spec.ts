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

  it('shows no engine list when no token is stored (and does not query engines)', () => {
    fixture.detectChanges();
    http.expectOne('/api/engine/credentials').flush({ hasCredentials: false, maskedToken: null });
    http.expectNone('/api/engine/external');
    expect(component.hasCredentials).toBeFalse();
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

  it('saves the chosen background engine via PUT /api/engine/background', () => {
    fixture.detectChanges();
    http.expectOne('/api/engine/credentials').flush({ hasCredentials: true, maskedToken: '****abcd' });
    http.expectOne('/api/engine/external').flush({
      hasCredentials: true, tokenInvalid: false, backgroundEngineId: null,
      engines: [{ id: 'eei_a', name: 'Live', maxThreads: 8, maxHash: 512 }, { id: 'eei_b', name: 'Hintergrund', maxThreads: 8, maxHash: 8192 }],
    });
    expect(component.backgroundEngineId).toBeNull();

    component.backgroundEngineId = 'eei_b';
    component.saveBackground();
    const req = http.expectOne('/api/engine/background');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ engineId: 'eei_b' });
    req.flush({ backgroundEngineId: 'eei_b' });
    expect(component.backgroundEngineId).toBe('eei_b');
  });

  it('flags a rejected token instead of showing an empty list', () => {
    fixture.detectChanges();
    http.expectOne('/api/engine/credentials').flush({ hasCredentials: true, maskedToken: '****dead' });
    http.expectOne('/api/engine/external').flush({ hasCredentials: true, tokenInvalid: true, engines: [] });

    expect(component.tokenInvalid).toBeTrue();
  });

  it('saves a token, clears the input and reloads the engines', () => {
    fixture.detectChanges();
    http.expectOne('/api/engine/credentials').flush({ hasCredentials: false, maskedToken: null });

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

  it('resets the state after deleting the token', () => {
    fixture.detectChanges();
    http.expectOne('/api/engine/credentials').flush({ hasCredentials: true, maskedToken: '****abcd' });
    http.expectOne('/api/engine/external').flush({
      hasCredentials: true, tokenInvalid: false,
      engines: [{ id: 'eei_a', name: 'SF', maxThreads: 2, maxHash: 64 }],
    });

    component.remove();
    http.expectOne(r => r.url === '/api/engine/credentials' && r.method === 'DELETE').flush(null);

    expect(component.hasCredentials).toBeFalse();
    expect(component.engines.length).toBe(0);
    expect(component.maskedToken).toBeNull();
  });
});
