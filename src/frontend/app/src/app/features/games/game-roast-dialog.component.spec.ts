import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { GameRoastDialogComponent } from './game-roast-dialog.component';

describe('GameRoastDialogComponent', () => {
  function setup(data: object = { gameId: 7, shareUrl: 'https://x/g/abc' }) {
    TestBed.configureTestingModule({
      imports: [GameRoastDialogComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close: () => undefined } },
      ],
    });
    const fixture = TestBed.createComponent(GameRoastDialogComponent);
    fixture.detectChanges();
    return { fixture, http: TestBed.inject(HttpTestingController), el: fixture.nativeElement as HTMLElement };
  }

  it('drei Stile; Roasten schickt Stil + Sprache, zeigt den Text; ein anderer Stil hat seinen eigenen', () => {
    const { fixture, http, el } = setup();
    http.expectOne(r => r.url === '/api/games/7/roasts' && r.method === 'GET' && r.params.get('lang') === 'en')
      .flush({ available: true, hasAnalysis: true, items: [{ style: 'friendly', language: 'en', text: 'Nice try.', createdAt: '' }] });
    fixture.detectChanges();

    const styles = el.querySelectorAll('button.style');
    expect(styles.length).toBe(3);
    expect(el.querySelector('.text')!.textContent).toContain('Nice try.');

    (styles[2] as HTMLButtonElement).click();   // russisch
    fixture.detectChanges();
    expect(el.querySelector('.text')).toBeNull();

    const go = Array.from(el.querySelectorAll('button')).find(b => b.textContent!.includes('games.roast.go')) as HTMLButtonElement;
    go.click();
    const req = http.expectOne(r => r.url === '/api/games/7/roasts' && r.method === 'POST');
    expect(req.request.params.get('style')).toBe('russian');
    expect(req.request.params.get('lang')).toBe('en');
    req.flush({ style: 'russian', language: 'en', text: 'Your chess brain is an empty bunker.', createdAt: '' });
    fixture.detectChanges();
    expect(el.querySelector('.text')!.textContent).toContain('empty bunker');
  });

  it('ohne Analyse bzw. ohne Modell: ein Hinweis statt Knopf; eine Absage nennt ihren Grund', () => {
    const { fixture, http, el } = setup();
    http.expectOne('/api/games/7/roasts?lang=en').flush({ available: true, hasAnalysis: false, items: [] });
    fixture.detectChanges();
    expect(el.textContent).toContain('games.roast.noAnalysis');
    expect(el.querySelector('button.style')).toBeNull();

    fixture.componentInstance.state.set({ available: true, hasAnalysis: true, items: [] });
    fixture.componentInstance.roast();
    http.expectOne(r => r.method === 'POST').flush({ reason: 'dailyLimit' }, { status: 429, statusText: 'Too Many' });
    fixture.detectChanges();
    expect(el.querySelector('.error')!.textContent).toContain('games.roast.error.dailyLimit');
  });

  it('Partie mit diesem Roast teilen: gibt frei, kopiert /g/{token}?roast={id}, „Nicht mehr teilen" nimmt zurück', async () => {
    const { fixture, http, el } = setup({ gameId: 7, shareUrl: 'https://x/g/abc', shareToken: 'abc' });
    http.expectOne(r => r.url === '/api/games/7/roasts' && r.method === 'GET')
      .flush({ available: true, hasAnalysis: true, items: [{ id: 42, shared: false, style: 'friendly', language: 'en', text: 'Nice try.', createdAt: '' }] });
    fixture.detectChanges();
    (fixture.componentInstance as unknown as { canShare: boolean }).canShare = false;
    const write = spyOn(navigator.clipboard, 'writeText').and.returnValue(Promise.resolve());
    expect(el.querySelector('.shared')).toBeNull();

    (el.querySelector('button.share-game') as HTMLButtonElement).click();
    const req = http.expectOne(r => r.url === '/api/games/7/roasts/share' && r.method === 'POST');
    expect(req.request.params.get('style')).toBe('friendly');
    req.flush({ roastId: 42 });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(write).toHaveBeenCalledWith(`${window.location.origin}/g/abc?roast=42`);
    expect(el.querySelector('.shared')!.textContent).toContain('games.roast.sharedNote');
    expect(el.querySelector('.ok')!.textContent).toContain('games.roast.linkCopied');

    (el.querySelector('button.unshare') as HTMLButtonElement).click();
    http.expectOne(r => r.url === '/api/games/7/roasts/share' && r.method === 'DELETE').flush(null);
    fixture.detectChanges();
    expect(el.querySelector('.shared')).toBeNull();
  });

  it('ohne Teilen-Kennung kein „Partie teilen"-Knopf', () => {
    const { fixture, http, el } = setup();
    http.expectOne(r => r.method === 'GET')
      .flush({ available: true, hasAnalysis: true, items: [{ id: 1, style: 'friendly', language: 'en', text: 'x', createdAt: '' }] });
    fixture.detectChanges();
    expect(el.querySelector('button.share-game')).toBeNull();
  });
});
