import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { ApiTokensComponent, CreateTokenDialogComponent } from './api-tokens.component';
import { MatDialog } from '@angular/material/dialog';
import { HttpTestingController } from '@angular/common/http/testing';
import { Subject, of } from 'rxjs';

describe('ApiTokensComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [ApiTokensComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: MAT_DIALOG_DATA, useValue: {} },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ApiTokensComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('offers the engine scope and labels the scope column', async () => {
    await TestBed.configureTestingModule({
      imports: [ApiTokensComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: MAT_DIALOG_DATA, useValue: {} },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ApiTokensComponent);
    const c = fixture.componentInstance;
    expect(c.scopeLabel('engine')).toBe('profile.tokens.scope.engine');
    expect(c.scopeLabel('extension')).toBe('profile.tokens.scope.extension');
    expect(c.scopeLabel('future')).toBe('future');   // unbekannter Bereich steht roh da

    const dialog = TestBed.createComponent(CreateTokenDialogComponent).componentInstance;
    expect(dialog.scope).toBe('extension');   // Vorgabe wie bisher
  });

  it('sends the chosen scope when creating a token', async () => {
    await TestBed.configureTestingModule({
      imports: [ApiTokensComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const http = TestBed.inject(HttpTestingController);
    const afterClosed = new Subject<unknown>();
    const fixture = TestBed.createComponent(ApiTokensComponent);
    // Der eigenständige Baustein bekommt SEINEN MatDialog (MatDialogModule in den imports) — nicht den der Wurzel.
    const dialogs = (fixture.componentInstance as unknown as { dialog: MatDialog }).dialog;
    spyOn(dialogs, 'open').and.returnValues(
      { afterClosed: () => afterClosed } as never,
      { afterClosed: () => of(undefined) } as never,
    );
    fixture.detectChanges();
    http.expectOne('/api/profile/tokens').flush([]);

    fixture.componentInstance.openCreateDialog();
    afterClosed.next({ name: 'Engine-Provider', expiresInDays: null, scope: 'engine' });
    const post = http.expectOne(r => r.url === '/api/profile/tokens' && r.method === 'POST');
    expect(post.request.body).toEqual({ name: 'Engine-Provider', expiresInDays: null, scope: 'engine' });
    post.flush({ id: 1, name: 'Engine-Provider', prefix: 'rkh_abcdefgh', scope: 'engine', createdAt: '', lastUsedAt: null, expiresAt: null, rawToken: 'rkh_x' });
    http.expectOne(r => r.url === '/api/profile/tokens' && r.method === 'GET').flush([]);
    http.verify();
  });
});
