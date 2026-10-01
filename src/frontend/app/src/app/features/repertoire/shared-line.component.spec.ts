import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { SharedLineComponent } from './shared-line.component';

describe('SharedLineComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [SharedLineComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(SharedLineComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('arrow keys typed into an input or an open menu do not step the line', async () => {
    await TestBed.configureTestingModule({
      imports: [SharedLineComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const c = TestBed.createComponent(SharedLineComponent).componentInstance;
    const step = spyOn(c.service, 'goForward');
    const press = (target: EventTarget) => {
      const e = new KeyboardEvent('keydown', { key: 'ArrowRight', cancelable: true });
      Object.defineProperty(e, 'target', { value: target });
      c.onKeyDown(e);
    };
    const overlay = document.createElement('div');
    overlay.className = 'cdk-overlay-container';
    const item = document.createElement('button');
    overlay.appendChild(item);
    document.body.appendChild(overlay);
    try {
      press(item);
      press(document.createElement('input'));
      expect(step).not.toHaveBeenCalled();
    } finally {
      overlay.remove();
    }
    press(document.body);
    expect(step).toHaveBeenCalledTimes(1);
  });
});
