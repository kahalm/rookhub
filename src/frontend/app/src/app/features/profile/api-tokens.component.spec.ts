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
import { AuthService } from '../../core/auth.service';

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
    expect(c.scopeLabel('diagrams')).toBe('profile.tokens.scope.diagrams');
    expect(c.scopeLabel('toString')).toBe('toString');   // kein Treffer über den Prototyp
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

  // UI-Sweep 2026-10-11 (r2-rk-tokens-loading): nach der HTTP-Antwort stand dauerhaft „Wird geladen…“ —
  // Angular 22 zeichnet ohne Signal/Markierung nach einer fetch-Antwort nicht neu. Hier OHNE händisches
  // detectChanges nach dem flush: nur der Scheduler darf die Liste bzw. den Leerhinweis zeichnen.
  it('renders the empty hint and the token table after the response without a manual detectChanges', async () => {
    await TestBed.configureTestingModule({
      imports: [ApiTokensComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ApiTokensComponent);
    fixture.autoDetectChanges();
    await fixture.whenStable();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('common.loading');

    http.expectOne('/api/profile/tokens').flush([]);
    await fixture.whenStable();
    expect(el.textContent).not.toContain('common.loading');
    // UI-Sweep z1 (z1-rk-profile-tokens): leise linksbündige Zeile statt zentrierter .empty-state.
    expect(el.querySelector('.tokens-empty')?.textContent).toContain('profile.tokens.empty');
    expect(el.querySelector('.empty-state')).toBeNull();

    fixture.componentInstance.load();
    http.expectOne('/api/profile/tokens').flush([
      { id: 7, name: 'Laptop', prefix: 'rkh_a1b2', scope: 'extension', createdAt: '2026-10-01T10:00:00Z', lastUsedAt: null, expiresAt: null },
    ]);
    await fixture.whenStable();
    const rows = el.querySelectorAll('.tokens-table tbody tr');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain('Laptop');
    expect(el.querySelector('.tokens-empty')).toBeNull();
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
    // b1-rk-profil-tokens-engine-textwand: die Bereiche-Erklärung steht hinter dem (?) an der Überschrift.
    expect(el.querySelector('.tokens-section h4 app-help-hint button')?.getAttribute('aria-label'))
      .toBe('profile.tokens.subtitleHelp');
    expect(el.querySelector('.tokens-section > button.tokens-create')).not.toBeNull();
  });

  // P-14 (25.10.2026): kein „Nie" mehr — der Server befristet jeden Token auf höchstens 365 Tage, die Auswahl
  // beginnt deshalb bei der Höchstdauer und schickt immer eine Zahl.
  it('starts at 365 days, offers no "never" and always sends a number', async () => {
    await TestBed.configureTestingModule({
      imports: [CreateTokenDialogComponent],
      providers: [
        { provide: AuthService, useValue: { has: () => false } },
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: MAT_DIALOG_DATA, useValue: {} },
      ],
    }).compileComponents();
    const dialog = TestBed.createComponent(CreateTokenDialogComponent).componentInstance;
    const closed: unknown[] = [];
    spyOn(dialog.dialogRef, 'close').and.callFake((r?: unknown) => { closed.push(r); });

    expect(dialog.expiresIn).toBe(365);
    dialog.name = 'devrechner';
    dialog.scope = 'engine';
    dialog.submit();
    expect(closed[0]).toEqual({ name: 'devrechner', expiresInDays: 365, scope: 'engine' });

    dialog.expiresIn = 30;
    dialog.submit();
    expect(closed[1]).toEqual({ name: 'devrechner', expiresInDays: 30, scope: 'engine' });
  });

  async function renderCreateDialog() {
    await TestBed.configureTestingModule({
      imports: [CreateTokenDialogComponent],
      providers: [
        { provide: AuthService, useValue: { has: () => false } },
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: MAT_DIALOG_DATA, useValue: {} },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(CreateTokenDialogComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  // d7-rk-gesperrter-knopf-ohne-grund: gesperrtes „Erstellen“ sagt, dass der Name fehlt (DESIGN 5.4).
  it('d7: ohne Namen steht der Grund neben dem gesperrten „Erstellen“, mit Namen nicht mehr', async () => {
    const { fixture, el } = await renderCreateDialog();
    const create = () => el.querySelector('mat-dialog-actions button[color="primary"]') as HTMLButtonElement;
    expect(create().disabled).toBeTrue();
    expect(el.querySelector('mat-dialog-actions .why')?.textContent).toContain('profile.tokens.dialog.needName');

    fixture.componentInstance.name = '   ';
    fixture.detectChanges();
    expect(create().disabled).withContext('nur Leerzeichen ist kein Name').toBeTrue();

    fixture.componentInstance.name = 'Laptop';
    fixture.detectChanges();
    expect(create().disabled).toBeFalse();
    expect(el.querySelector('mat-dialog-actions .why')).toBeNull();
  });

  // d7-rk-token-bereich-abgeschnitten: geschlossen zeigt das Bereichsfeld nur den Kurznamen, die Liste den vollen Text.
  it('d7: das Bereichsfeld zeigt geschlossen den Kurznamen statt des langen Optionstexts', async () => {
    const { fixture, el } = await renderCreateDialog();
    const trigger = () => el.querySelectorAll('mat-select')[0].querySelector('.mat-mdc-select-value') as HTMLElement;
    expect(trigger().textContent!.trim()).toBe('profile.tokens.dialog.scopeExtensionShort');

    fixture.componentInstance.scope = 'engine';
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(trigger().textContent!.trim()).toBe('profile.tokens.dialog.scopeEngineShort');
  });

  // 0.870.0: Diagramm-Worker-Token (externe Diagramm-Erkennung) nur für die Bibliotheksverwaltung (ebooks.manage).
  for (const allowed of [false, true]) {
    it(`offers the diagrams scope only with ebooks.manage (${allowed})`, async () => {
      const asked: string[] = [];
      await TestBed.configureTestingModule({
        imports: [CreateTokenDialogComponent],
        providers: [
          { provide: AuthService, useValue: { has: (p: string) => { asked.push(p); return allowed; } } },
          provideNoopAnimations(),
          provideTranslateService({ fallbackLang: 'en' }),
          { provide: MatDialogRef, useValue: { close: () => {} } },
          { provide: MAT_DIALOG_DATA, useValue: {} },
        ],
      }).compileComponents();
      const fixture = TestBed.createComponent(CreateTokenDialogComponent);
      fixture.detectChanges();
      expect(asked).toContain('ebooks.manage');
      expect(fixture.componentInstance.canDiagrams).toBe(allowed);
      fixture.componentInstance.scope = 'diagrams';
      fixture.detectChanges();
      expect(fixture.componentInstance.scopeShort[fixture.componentInstance.scope]).toBe('profile.tokens.dialog.scopeDiagramsShort');
    });
  }
});
