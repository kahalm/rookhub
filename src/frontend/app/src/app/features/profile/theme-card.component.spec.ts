import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { ThemeCardComponent } from './theme-card.component';
import { EINK_STORAGE_KEY, setEinkActive } from '../../core/eink';

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
