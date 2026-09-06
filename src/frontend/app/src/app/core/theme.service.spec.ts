import { ThemeService } from './theme.service';
import { sharedCookieDomain } from './partner-site';

const KEY = 'rookhub_app_theme';
const COOKIE = 'rookhub_theme';

function clearSharedCookie(): void {
  document.cookie = `${COOKIE}=; path=/; max-age=0`;
  const domain = sharedCookieDomain();
  if (domain) document.cookie = `${COOKIE}=; domain=${domain}; path=/; max-age=0`;
}

describe('ThemeService', () => {
  beforeEach(() => clearSharedCookie());

  afterEach(() => {
    localStorage.removeItem(KEY);
    clearSharedCookie();
    document.documentElement.classList.remove('dark-theme');
  });

  it('verwendet Dark als Default, wenn nichts gespeichert ist', () => {
    localStorage.removeItem(KEY);
    const svc = new ThemeService();
    expect(svc.preference).toBe('dark');
    expect(svc.isDark).toBeTrue();
    expect(document.documentElement.classList.contains('dark-theme')).toBeTrue();
  });

  it('eine gespeicherte Wahl hat Vorrang vor dem Default', () => {
    localStorage.setItem(KEY, 'light');
    const svc = new ThemeService();
    expect(svc.preference).toBe('light');
    expect(svc.isDark).toBeFalse();
  });

  it('ignoriert einen ungültigen gespeicherten Wert (Fallback Dark)', () => {
    localStorage.setItem(KEY, 'banana');
    const svc = new ThemeService();
    expect(svc.preference).toBe('dark');
  });

  it('setPreference persistiert und schaltet die dark-theme-Klasse', () => {
    const svc = new ThemeService();
    svc.setPreference('light');
    expect(localStorage.getItem(KEY)).toBe('light');
    expect(document.documentElement.classList.contains('dark-theme')).toBeFalse();

    svc.setPreference('dark');
    expect(document.documentElement.classList.contains('dark-theme')).toBeTrue();
  });

  it('toggle durchläuft system → light → dark → system', () => {
    const svc = new ThemeService();
    svc.setPreference('system');
    svc.toggle();
    expect(svc.preference).toBe('light');
    svc.toggle();
    expect(svc.preference).toBe('dark');
    svc.toggle();
    expect(svc.preference).toBe('system');
  });

  // --- geteilt zwischen RookHub und der Turnierseite -------------------------
  // Beide liegen auf eigenen Origins und teilen den localStorage NICHT. Der Modus wandert
  // deshalb zusaetzlich als Cookie auf der gemeinsamen Elterndomaene.

  it('nimmt den Modus aus dem geteilten Cookie', () => {
    document.cookie = `${COOKIE}=light; path=/`;
    expect(new ThemeService().preference).toBe('light');
  });

  it('das Cookie sticht den geraetelokalen Wert — es ist die juengere Wahl', () => {
    localStorage.setItem(KEY, 'dark');
    document.cookie = `${COOKIE}=light; path=/`;
    expect(new ThemeService().preference).toBe('light');
  });

  it('ignoriert einen unsinnigen Cookie-Wert', () => {
    document.cookie = `${COOKIE}=neon; path=/`;
    localStorage.setItem(KEY, 'light');
    expect(new ThemeService().preference).toBe('light');
  });

  it('schreibt eine Aenderung auch ins geteilte Cookie', () => {
    new ThemeService().setPreference('light');
    // Auf einem Host ohne gemeinsame Elterndomaene (karma laeuft auf localhost) wird bewusst
    // KEIN Cookie gesetzt, statt still ins Leere zu schreiben.
    if (sharedCookieDomain()) expect(document.cookie).toContain(`${COOKIE}=light`);
    expect(localStorage.getItem(KEY)).toBe('light');
  });
});

describe('sharedCookieDomain', () => {
  it('nennt die gemeinsame Elterndomaene beider Seiten', () => {
    expect(sharedCookieDomain('rookhub-dev.oberschmid.homes')).toBe('.oberschmid.homes');
    expect(sharedCookieDomain('turnier.oberschmid.homes')).toBe('.oberschmid.homes');
  });

  it('gibt nichts zurueck, wo es keinen gemeinsamen Elternteil gibt', () => {
    expect(sharedCookieDomain('localhost')).toBeNull();
    expect(sharedCookieDomain('10.24.13.6')).toBeNull();
    expect(sharedCookieDomain('fremde-seite.example.com')).toBeNull();
  });
});
