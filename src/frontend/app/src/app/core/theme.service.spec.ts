import { ThemeService } from './theme.service';

const KEY = 'rookhub_app_theme';

describe('ThemeService', () => {
  afterEach(() => {
    localStorage.removeItem(KEY);
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
});
