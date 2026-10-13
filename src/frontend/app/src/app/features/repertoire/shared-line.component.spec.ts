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

/** UI-Sweep a1: Datum am Handy nie umbrechen (a1-rk-geteilte-linie-datum-handy), Tablet hochkant stapeln
 *  (a1-rk-geteilt-recon-tablet), ab 2400 px Brett bis Fensterhöhe (a1-rk-geteilt-brett-ultrawide). */
describe('SharedLineComponent Layout', () => {
  const css = ((SharedLineComponent as any).ɵcmp.styles as string[]).join('\n').replace(/%NS%/g, '').replace(/\s+/g, '')
    .replace(/\[_ngcontent-%COMP%\]/g, '');
  const block = (q: string) => {
    const at = css.indexOf(`@media(${q})`);
    expect(at).withContext(q).toBeGreaterThan(-1);
    const next = css.indexOf('@media', at + 6);
    return css.slice(at, next > -1 ? next : undefined);
  };
  it('Datum ohne Umbruch, Trenner „·“ vor dem Datum; am Handy eine Zeile mit gekürztem Namen', () => {
    const base = css.slice(0, css.indexOf('@media'));
    expect(base).toContain('.date{white-space:nowrap;}');
    expect(base).toMatch(/\.rep\+\.date::before\{content:["']·["'];/);
    const phone = block('max-width:768px');
    expect(phone).toContain('.meta{flex-wrap:nowrap;}');
    expect(phone).toContain('.rep{min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}');
  });
  it('stapelt unter 900 px, Brett höchstens bis zur Fensterhöhe', () => {
    const tab = block('max-width:899.98px');
    expect(tab).toContain('.body{flex-direction:column;');
    expect(tab).toMatch(/\.board-wrap\{width:min\(100%,max\(360px,(calc\()?100vh-260px\)?\)\);\}/);
  });
  it('ab 2400 px Brett bis Fensterhöhe (höchstens 1080 px)', () => {
    expect(block('min-width:2400px')).toContain('--board-size:clamp(360px,calc(100vh-260px),1080px)');
  });
});
