import { TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { SwUpdate } from '@angular/service-worker';
import { provideTranslateService } from '@ngx-translate/core';
import { AppUpdateService } from '@rh/core/app-update.service';
import { NotFoundComponent } from '@rh/shared/not-found/not-found.component';

@Component({ standalone: true, template: '' })
class StubComponent {}

/**
 * LeagueHub registriert keinen Service Worker, nutzt aber RookHubs Nicht-gefunden-Seite ('**'). Ohne `SwUpdate`-Anbieter
 * darf sie nicht an einer fehlenden Injektion scheitern: Stufe 1 (einmal neu laden) entfällt, Stufe 2 (version.json)
 * läuft, und „Aktualisieren“ ist ein einfacher Reload.
 */
describe('LeagueHub: Nicht-gefunden-Seite ohne Service Worker', () => {
  let reload: jasmine.Spy;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [NotFoundComponent],
      providers: [provideRouter([{ path: '', component: StubComponent }]), provideHttpClient(), provideHttpClientTesting(),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'de' })],
    });
    reload = spyOn(AppUpdateService.prototype as unknown as { reloadApp: () => void }, 'reloadApp');
  });

  async function render(body: unknown, status = 200) {
    spyOn(window, 'fetch').and.callFake(() => Promise.resolve(new Response(JSON.stringify(body), { status })));
    const fixture = TestBed.createComponent(NotFoundComponent);
    const el = fixture.nativeElement as HTMLElement;
    for (let i = 0; i < 50; i++) {
      fixture.detectChanges();
      if (!el.querySelector('mat-spinner')) break;
      await new Promise(r => setTimeout(r, 0));
    }
    fixture.detectChanges();
    return { fixture, el };
  }

  it('es gibt keinen SwUpdate-Anbieter, die Seite zeigt trotzdem ihren Text', async () => {
    expect(TestBed.inject(SwUpdate, null)).toBeNull();
    const { el } = await render({}, 404);
    expect(el.querySelector('h1')?.textContent).toContain('app.notFound.title');
    expect(el.textContent).toContain('app.notFound.text');
  });

  it('Server neuer → Hinweis, „Aktualisieren“ lädt einfach neu', async () => {
    const { el, fixture } = await render({ version: '99999.0.0' });
    expect(el.querySelector('.nf-update')).not.toBeNull();
    el.querySelector<HTMLButtonElement>('button.nf-reload')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(reload).toHaveBeenCalledTimes(1);
  });
});
