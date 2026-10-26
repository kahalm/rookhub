import { CalcLocalMode } from './calc-local-mode';
import { CALC_NOTICE_PREFIX } from './calc-local.util';
import { CalcBackend, CalculationService } from './calculation.service';

describe('CalcLocalMode', () => {
  const server = {} as CalcBackend;
  const api = {} as CalculationService;
  afterEach(() => { try { localStorage.removeItem(CALC_NOTICE_PREFIX + '991'); } catch { /* ignore */ } });

  it('angemeldet bleibt der Server die Quelle, ohne Hinweis', () => {
    const m = new CalcLocalMode();
    m.enter(api, 991, true);
    expect(m.localOnly).toBeFalse();
    expect(m.backend(server)).toBe(server);
    expect(m.showNotice).toBeFalse();
  });

  it('anonym: Gerätespeicher, Hinweis wegklickbar und gemerkt', () => {
    const m = new CalcLocalMode();
    m.enter(api, 991, false);
    expect(m.localOnly).toBeTrue();
    expect(m.backend(server)).not.toBe(server);
    expect(m.showNotice).toBeTrue();
    m.dismissNotice(991);
    expect(m.showNotice).toBeFalse();
    const again = new CalcLocalMode();
    again.enter(api, 991, false);
    expect(again.noticeDismissed).toBeTrue();
  });

  it('die Warnung kommt mit jedem neuen Fehlschlag zurück', () => {
    const m = new CalcLocalMode();
    m.enter(api, 991, false);
    m.setSaveFailed(true);
    expect(m.showWarning).toBeTrue();
    expect(m.showNotice).toBeFalse();
    m.dismissWarning();
    expect(m.showWarning).toBeFalse();
    m.setSaveFailed(true);
    expect(m.showWarning).toBeTrue();
    m.setSaveFailed(false);
    expect(m.showWarning).toBeFalse();
  });
});
