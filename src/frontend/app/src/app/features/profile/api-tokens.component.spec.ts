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

  // Abschnitt im Muster der übrigen Profil-Abschnitte: Überschrift + Erklärsatz, keine eigene Karte.
  it('renders as a plain profile section (no own card), button below the hint', async () => {
    await TestBed.configureTestingModule({
      imports: [ApiTokensComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ApiTokensComponent);
    fixture.detectChanges();
    http.expectOne('/api/profile/tokens').flush([]);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('mat-card')).toBeNull();
    expect(el.querySelector('.tokens-section h4')?.textContent).toContain('profile.tokens.title');
    expect(el.querySelector('.tokens-hint')?.textContent).toContain('profile.tokens.subtitle');
    expect(el.querySelector('.tokens-section > button.tokens-create')).not.toBeNull();
  });

  // Die Ablauf-Auswahl trug fuer "Nie" den Wert null — und ein MatSelect zeigt eine Option mit dem
  // Wert null nie als gewaehlt an (`option.value != null` in _selectOptionByValue). Das Feld stand
  // deshalb leer da, auch nachdem man "Nie" angeklickt hatte. Jetzt ist "Nie" die 0; nach aussen
  // muss trotzdem null gehen, denn so heisst "kein Ablauf" in der API.
  it('closes with expiresInDays null for "never" and with the number otherwise', async () => {
    await TestBed.configureTestingModule({
      imports: [CreateTokenDialogComponent],
      providers: [
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: MAT_DIALOG_DATA, useValue: {} },
      ],
    }).compileComponents();
    const dialog = TestBed.createComponent(CreateTokenDialogComponent).componentInstance;
    const closed: unknown[] = [];
    spyOn(dialog.dialogRef, 'close').and.callFake((r?: unknown) => { closed.push(r); });

    expect(dialog.expiresIn).toBe(0);            // Vorgabe "Nie" — und sie ist waehlbar, nicht null
    dialog.name = 'devrechner';
    dialog.scope = 'engine';
    dialog.submit();
    expect(closed[0]).toEqual({ name: 'devrechner', expiresInDays: null, scope: 'engine' });

    dialog.expiresIn = 30;
    dialog.submit();
    expect(closed[1]).toEqual({ name: 'devrechner', expiresInDays: 30, scope: 'engine' });
  });
});
