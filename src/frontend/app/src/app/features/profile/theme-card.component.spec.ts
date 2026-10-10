import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { ThemeCardComponent } from './theme-card.component';

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
