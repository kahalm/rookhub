import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { FooterPresenceService } from '@rh/shared/app-footer/footer-presence';
import { ClubHubAppComponent } from './app.component';
import { AuthService } from '@rh/core/auth.service';
import { SessionStore } from '@rh/core/session-store';
import { LinkStateStore } from './core/link-state.store';

describe('ClubHubAppComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [ClubHubAppComponent],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideTranslateService({ fallbackLang: 'de' })],
  }));

  // Die Fusszeile zeigt Impressum und Datenschutz immer; die Anmeldemaske wiederholte sie darunter (UI-Sweep 2026-10-10, x-login-legal).
  it('meldet die Fusszeile als immer sichtbar, die Anmeldemaske laesst ihre Rechtslinks weg', () => {
    const f = TestBed.createComponent(ClubHubAppComponent);
    expect(TestBed.inject(FooterPresenceService).presence()).toBe('always');
    f.detectChanges();
    const links = Array.from((f.nativeElement as HTMLElement).querySelectorAll('footer a')).map(a => a.getAttribute('href'));
    expect(links).toEqual(jasmine.arrayContaining(['/impressum', '/privacy']));
  });

  // UI-Sweep 2026-10-11 (r2-c-footer-link)
  it('Fußzeile: „Konto verknüpfen" ohne Club-Recht, „Verknüpfung (Lena)" verknüpft, für Trainer/Leitung gar nicht', () => {
    const f = TestBed.createComponent(ClubHubAppComponent);
    const auth = TestBed.inject(AuthService);
    const store = TestBed.inject(LinkStateStore);
    const link = () => (f.nativeElement as HTMLElement).querySelector('footer a[href="/verknuepfen"]');
    f.detectChanges();
    expect(link()!.textContent!.trim()).toBe('Konto verknüpfen');
    store.set({ linked: true, firstName: 'Lena' });
    f.detectChanges();
    expect(link()!.textContent!.trim()).toBe('Verknüpfung (Lena)');
    // z3-c-fuss-umbruch: alle Links in EINER Gruppe neben der Version (bricht geschlossen um, nie „Datenschutz" allein).
    const group = (f.nativeElement as HTMLElement).querySelector('footer > .foot-links')!;
    expect(Array.from(group.querySelectorAll('a')).map(a => a.getAttribute('href')))
      .toEqual(['/verknuepfen', '/impressum', '/privacy']);
    spyOn(auth, 'has').and.callFake((p: string) => p === 'club.trainer');
    TestBed.inject(SessionStore).set({ token: 't', username: 'tina', userId: 5, isAdmin: false });
    f.detectChanges();
    expect(link()).toBeNull();
  });

  // UI-Sweep d2-c-kartei-lead / d2-c-attendance-lead: die Werbezeile nur ohne Club-Zugang
  it('zeigt die Werbezeile nur ohne Club-Zugang', () => {
    const f = TestBed.createComponent(ClubHubAppComponent);
    const auth = TestBed.inject(AuthService);
    const lede = () => (f.nativeElement as HTMLElement).querySelector('.lede');
    f.detectChanges();
    expect(lede()).not.toBeNull();
    spyOn(auth, 'has').and.callFake((p: string) => p === 'club.trainer');
    TestBed.inject(SessionStore).set({ token: 't', username: 'tina', userId: 5, isAdmin: false });
    f.detectChanges();
    expect(lede()).toBeNull();
  });
});
