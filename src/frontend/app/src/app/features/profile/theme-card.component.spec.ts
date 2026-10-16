import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { ThemeCardComponent } from './theme-card.component';
import { EINK_STORAGE_KEY, setEinkActive } from '../../core/eink';
import { UI_SCALE_STORAGE_KEY, currentUiScale, resetUiScaleForTests } from '../../core/ui-scale';
import { SnackbarService } from '../../core/snackbar.service';

describe('ThemeCardComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [ThemeCardComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ThemeCardComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });
});

describe('ThemeCardComponent Breite', () => {
  it('ist nur so breit wie die drei Segmente (inline-flex statt volle Breite)', async () => {
    await TestBed.configureTestingModule({
      imports: [ThemeCardComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(ThemeCardComponent);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    const group = (fixture.nativeElement as HTMLElement).querySelector('.theme-toggle') as HTMLElement;
    expect(getComputedStyle(group).display).toBe('inline-flex');
    expect(group.querySelectorAll('mat-button-toggle').length).toBe(3);
    fixture.nativeElement.remove();
  });
});

describe('ThemeCardComponent E-Ink-Schalter', () => {
  let saved: string | null;
  beforeEach(() => { saved = localStorage.getItem(EINK_STORAGE_KEY); localStorage.removeItem(EINK_STORAGE_KEY); });
  afterEach(() => {
    if (saved === null) localStorage.removeItem(EINK_STORAGE_KEY); else localStorage.setItem(EINK_STORAGE_KEY, saved);
    setEinkActive(false);
  });

  it('schaltet den Modus sofort, speichert je Gerät, und erklärt, dass er hell erzwingt', async () => {
    await TestBed.configureTestingModule({
      imports: [ThemeCardComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(ThemeCardComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('profile.theme.einkHint');
    expect(el.querySelector('.theme-eink-note')).toBeNull();
    const toggle = el.querySelector('.eink-toggle button[role="switch"]') as HTMLButtonElement;
    expect(toggle).withContext('Schalter vorhanden').not.toBeNull();
    toggle.click();
    fixture.detectChanges();
    expect(document.documentElement.classList.contains('eink')).toBeTrue();
    expect(localStorage.getItem(EINK_STORAGE_KEY)).toBe('1');
    expect(el.querySelector('.theme-eink-note')).not.toBeNull();
    toggle.click();
    fixture.detectChanges();
    expect(document.documentElement.classList.contains('eink')).toBeFalse();
    expect(localStorage.getItem(EINK_STORAGE_KEY)).toBe('0');
  });
});

describe('ThemeCardComponent UI-Größe', () => {
  let saved: string | null;
  beforeEach(() => { saved = localStorage.getItem(UI_SCALE_STORAGE_KEY); localStorage.removeItem(UI_SCALE_STORAGE_KEY); });
  afterEach(() => {
    resetUiScaleForTests();
    if (saved === null) localStorage.removeItem(UI_SCALE_STORAGE_KEY); else localStorage.setItem(UI_SCALE_STORAGE_KEY, saved);
  });

  async function create() {
    await TestBed.configureTestingModule({
      imports: [ThemeCardComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(ThemeCardComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('zeigt die fünf Stufen, 100 % gewählt; eine Wahl wirkt sofort und wird je Gerät gespeichert', async () => {
    const fixture = await create();
    const el: HTMLElement = fixture.nativeElement;
    const toggles = [...el.querySelectorAll('.ui-scale-toggle mat-button-toggle')];
    expect(toggles.length).toBe(5);
    expect(el.querySelector('.ui-scale-toggle .mat-button-toggle-checked button')?.textContent).toContain('profile.theme.uiScaleValue');
    expect(toggles[0].classList).toContain('mat-button-toggle-checked');
    // Falz-Runde 16.10.: ohne Häkchen — die fünf Stufen passen am Handy in eine Zeile.
    expect(el.querySelector('.ui-scale-toggle .mat-button-toggle-checkbox-wrapper')).toBeNull();
    (toggles[4].querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(localStorage.getItem(UI_SCALE_STORAGE_KEY)).toBe('200');
    expect(currentUiScale()).toBe(200);
    expect(toggles[4].classList).toContain('mat-button-toggle-checked');
    (toggles[0].querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(localStorage.getItem(UI_SCALE_STORAGE_KEY)).toBeNull();
    expect(currentUiScale()).toBe(100);
  });

  it('sagt es, wenn der Browser nicht sicher skalieren kann', async () => {
    const fixture = await create();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.ui-scale-note')).toBeNull();
    spyOn(Element.prototype, 'getBoundingClientRect').and.returnValue({ width: 10 } as DOMRect);
    fixture.componentInstance.setUiScale(175);
    fixture.detectChanges();
    expect(el.querySelector('.ui-scale-note')?.textContent).toContain('profile.theme.uiScaleUnsupported');
  });

  it('zeigt den Gerätebefund (Weg, Layout-/Bildschirmbreite) zur Fehlersuche am Tablet', async () => {
    const fixture = await create();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.ui-scale-diag')?.textContent).toContain('profile.theme.uiScaleDiag');
    fixture.componentInstance.setUiScale(150);
    fixture.detectChanges();
    expect(fixture.componentInstance.uiScale.diagnostics().mode).toBe('zoom');
  });

  it('warnt, wenn das Speichern scheitert (gilt dann nur bis zum Neuladen)', async () => {
    const fixture = await create();
    const snackbar = TestBed.inject(SnackbarService);
    const warn = spyOn(snackbar, 'warn');
    spyOn(Storage.prototype, 'setItem').and.throwError('QuotaExceededError');
    fixture.componentInstance.setUiScale(150);
    expect(warn).toHaveBeenCalledWith('profile.theme.uiScaleNotSaved');
    expect(currentUiScale()).toBe(150);
  });
});
