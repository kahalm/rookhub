import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { LEGAL_SITE } from '@rh/features/legal/legal-site';
import { AccessGateComponent } from './access-gate.component';

/** UX-033: die Sperrkarte nennt Konto, fehlendes Recht und den nächsten Schritt — früher nur ein Satz ohne Link. */
describe('AccessGateComponent', () => {
  async function render(inputs: { text: string; purpose?: string; back?: { link: string; label: string } | null }, url = '/verein/neu') {
    TestBed.configureTestingModule({
      imports: [AccessGateComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { currentUser: { username: 'ux-sonde' } } },
        { provide: LEGAL_SITE, useValue: { contactEmail: 'admin@example.org', imprint: true, kind: 'leaguehub' } },
      ],
    });
    spyOnProperty(TestBed.inject(Router), 'url', 'get').and.returnValue(url);
    const f = TestBed.createComponent(AccessGateComponent);
    f.componentRef.setInput('text', inputs.text);
    if (inputs.purpose) f.componentRef.setInput('purpose', inputs.purpose);
    if (inputs.back !== undefined) f.componentRef.setInput('back', inputs.back);
    f.detectChanges();
    await f.whenStable();
    return f.nativeElement as HTMLElement;
  }
  const links = (el: HTMLElement) => Array.from(el.querySelectorAll('a'));
  const link = (el: HTMLElement, text: string) => links(el).find(a => a.textContent?.includes(text));

  it('nennt das Konto und den Grund, bietet Anfrage und Kontowechsel', async () => {
    const el = await render({ text: 'LeagueHub sehen Admins und die Vereinsgruppe von SK Schwaz.' });
    expect(el.textContent).toContain('Nicht freigeschaltet');
    expect(el.textContent).toContain('Angemeldet als ux-sonde.');
    expect(el.textContent).toContain('LeagueHub sehen Admins und die Vereinsgruppe von SK Schwaz.');

    const ask = link(el, 'Freischaltung anfragen')!.getAttribute('href')!;
    expect(ask.startsWith('mailto:admin@example.org?')).toBeTrue();
    const q = new URLSearchParams(ask.split('?')[1]);
    expect(q.get('subject')).toBe('LeagueHub-Freischaltung: ux-sonde');
    expect(q.get('body')).toContain('„ux-sonde“ für LeagueHub frei');

    // Kontowechsel ohne Abmelden (guestGuard lässt ?switch=1 durch), danach zurück auf diese Seite.
    const sw = link(el, 'Mit anderem Konto anmelden')!.getAttribute('href')!;
    expect(sw).toBe('/login?switch=1&returnUrl=%2Fverein%2Fneu');
    expect(link(el, 'Zu den Vereinspartien')).toBeUndefined();
  });

  it('Leser ohne Beitragsrecht: Zweck in der Anfrage und der Weg zurück zu den Vereinspartien', async () => {
    const el = await render({ text: 'Du kannst die Vereinspartien lesen.', purpose: 'das Hinzufügen von Vereinspartien',
      back: { link: '/verein', label: 'Zu den Vereinspartien' } });
    expect(link(el, 'Zu den Vereinspartien')!.getAttribute('href')).toBe('/verein');
    const body = new URLSearchParams(link(el, 'Freischaltung anfragen')!.getAttribute('href')!.split('?')[1]).get('body');
    expect(body).toContain('für das Hinzufügen von Vereinspartien frei');
  });
});
