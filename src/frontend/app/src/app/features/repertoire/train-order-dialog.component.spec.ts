import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { renderAfterHttpInterceptor } from '../../core/render-after-http.interceptor';
import { SnackbarService } from '../../core/snackbar.service';
import { REPERTOIRE_OFFLINE_PREFIX } from '../../core/offline.service';
import { TrainOrderDialogData, openTrainOrderDialog } from './train-order-dialog.component';
import { LineOrder } from './repertoire-line-order';

/**
 * Der geteilte Dialog „Reihenfolge im Training“ (0.996.0) — geöffnet aus einer OnPush-Ansicht wie im Trainer; gezeichnet
 * wird nur über autoDetectChanges + whenStable (kein detectChanges nach einer Antwort), wie im Browser.
 */
@Component({ selector: 'app-order-host', standalone: true, changeDetection: ChangeDetectionStrategy.OnPush, template: '' })
class Host {
  readonly dialog = inject(MatDialog);
}

describe('TrainOrderDialogComponent', () => {
  const CONFIG_URL = '/api/repertoires/7/training/config';
  const ORDER_URL = '/api/repertoires/7/training/line-order';
  const OFFLINE_KEY = REPERTOIRE_OFFLINE_PREFIX + '7';
  const serverConfig = (lineOrder: string | null, hasOpponent = false) =>
    ({ effective: [], user: [], repertoire: null, source: 'default', lineOrder, hasOpponent });

  let http: HttpTestingController;
  let fixture: ComponentFixture<Host>;
  let snackbar: { success: jasmine.Spy; warn: jasmine.Spy };
  /** `'offen'`, solange der Dialog nicht geschlossen wurde; danach sein Ergebnis. */
  let closedWith: LineOrder | undefined | 'offen';

  async function open(data: Partial<TrainOrderDialogData> = {}): Promise<void> {
    snackbar = { success: jasmine.createSpy('success'), warn: jasmine.createSpy('warn') };
    await TestBed.configureTestingModule({
      imports: [Host],
      providers: [
        provideHttpClient(withInterceptors([renderAfterHttpInterceptor])), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: SnackbarService, useValue: snackbar },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Host);
    fixture.autoDetectChanges();
    closedWith = 'offen';
    openTrainOrderDialog(fixture.componentInstance.dialog, { repertoireId: 7, ...data }).subscribe(r => closedWith = r);
    await fixture.whenStable();
  }

  const panel = () => document.querySelector('mat-dialog-container') as HTMLElement | null;
  const radios = () => Array.from(panel()!.querySelectorAll<HTMLElement>('mat-radio-button'));
  const inputOf = (order: string) => panel()!.querySelector<HTMLInputElement>(`mat-radio-button[data-order="${order}"] input[type=radio]`)!;
  const checked = () => radios().filter(r => r.querySelector('input')!.checked).map(r => r.getAttribute('data-order'));
  const saveButton = () => panel()!.querySelector<HTMLButtonElement>('button.save');

  async function choose(order: string): Promise<void> {
    inputOf(order).click();
    await fixture.whenStable();
  }

  afterEach(() => {
    http.verify();
    TestBed.inject(MatDialog).closeAll();
    document.querySelectorAll('.cdk-overlay-container').forEach(o => o.innerHTML = '');
    localStorage.removeItem('rookhub_rep_train_freq_order');
    localStorage.removeItem(OFFLINE_KEY);
  });

  describe('Stand holen (Liste/Detailseite kennen ihn nicht)', () => {
    it('lädt die gewählte Reihenfolge vom Server und zeigt die fünf Möglichkeiten, die gewählte markiert', async () => {
      await open({ repertoireName: 'Weiß: Italienisch' });
      expect(panel()!.querySelector('mat-spinner')).withContext('vor der Antwort').not.toBeNull();
      expect(saveButton()).withContext('ohne Stand kein Speichern').toBeNull();

      http.expectOne(CONFIG_URL).flush(serverConfig('mostTrained'));
      await fixture.whenStable();

      expect(panel()!.querySelector('mat-spinner')).toBeNull();
      expect(radios().map(r => r.getAttribute('data-order')))
        .toEqual(['leastTrained', 'mostTrained', 'random', 'repertoire', 'frequency']);
      expect(checked()).toEqual(['mostTrained']);
      // je Möglichkeit Name + ein Erklärsatz
      expect(radios()[0].querySelector('.name')!.textContent).toContain('repertoire.trainOrder.option.leastTrained');
      expect(radios()[0].querySelector('.hint')!.textContent).toContain('repertoire.trainOrder.hint.leastTrained');
      expect(panel()!.querySelector('.subtitle')!.textContent).toContain('Weiß: Italienisch');
      expect(panel()!.querySelector('.scope')!.textContent).toContain('repertoire.trainOrder.scope');
      expect(panel()!.querySelector('h2')!.textContent).toContain('repertoire.trainOrder.title');
      expect(saveButton()).not.toBeNull();
    });

    it('nichts gewählt: die Vorgabe „Wenig trainierte zuerst“ ist markiert', async () => {
      await open();
      http.expectOne(CONFIG_URL).flush(serverConfig(null));
      await fixture.whenStable();
      expect(checked()).toEqual(['leastTrained']);
      expect(panel()!.querySelector('.subtitle')).toBeNull();   // ohne Namen keine leere Zeile
    });

    it('nichts gewählt, aber der alte Geräte-Schalter „Häufigste zuerst“ ist an: das gilt im Trainer — also ist es markiert', async () => {
      localStorage.setItem('rookhub_rep_train_freq_order', '1');
      await open();
      http.expectOne(CONFIG_URL).flush(serverConfig(null));
      await fixture.whenStable();
      expect(checked()).toEqual(['frequency']);
    });

    it('Abruf gescheitert: Ladefehler mit „Erneut versuchen“ statt einer Auswahl, die wie die gespeicherte aussähe', async () => {
      await open();
      http.expectOne(CONFIG_URL).flush('x', { status: 500, statusText: 'Server Error' });
      await fixture.whenStable();

      expect(panel()!.querySelector('app-load-error')).not.toBeNull();
      expect(radios().length).toBe(0);
      expect(saveButton()).toBeNull();

      (panel()!.querySelector('app-load-error button.retry') as HTMLButtonElement).click();
      await fixture.whenStable();
      http.expectOne(CONFIG_URL).flush(serverConfig('random'));
      await fixture.whenStable();
      expect(panel()!.querySelector('app-load-error')).toBeNull();
      expect(checked()).toEqual(['random']);
    });
  });

  describe('„Nach dem Gegner“ (0.999.3) — nur für ein Repertoire mit gespeichertem Gegner', () => {
    it('ohne gespeicherten Gegner: fünf Werte, „Nach dem Gegner“ fehlt', async () => {
      await open();
      http.expectOne(CONFIG_URL).flush(serverConfig(null, false));
      await fixture.whenStable();
      expect(radios().map(r => r.getAttribute('data-order'))).toEqual(['leastTrained', 'mostTrained', 'random', 'repertoire', 'frequency']);
    });

    it('mit gespeichertem Gegner (sagt der Server): sechs Werte, „Nach dem Gegner“ mit Erklärsatz — und markiert, wenn gewählt', async () => {
      await open({ repertoireName: 'Prep: Schett, Franz 2026' });
      http.expectOne(CONFIG_URL).flush(serverConfig('opponent', true));
      await fixture.whenStable();
      expect(radios().map(r => r.getAttribute('data-order')))
        .toEqual(['leastTrained', 'mostTrained', 'random', 'repertoire', 'frequency', 'opponent']);
      expect(checked()).toEqual(['opponent']);
      const last = radios()[5];
      expect(last.querySelector('.name')!.textContent).toContain('repertoire.trainOrder.option.opponent');
      expect(last.querySelector('.hint')!.textContent).toContain('repertoire.trainOrder.hint.opponent');
    });

    it('gespeichert ist „Nach dem Gegner“, aber der Gegner fehlt (inzwischen): fünf Werte, die Vorgabe ist markiert', async () => {
      await open();
      http.expectOne(CONFIG_URL).flush(serverConfig('opponent', false));
      await fixture.whenStable();
      expect(radios().length).toBe(5);
      expect(checked()).toEqual(['leastTrained']);
    });

    it('mit gespeichertem Gegner zählt der alte Geräte-Schalter nicht: nichts gewählt = die Vorgabe', async () => {
      localStorage.setItem('rookhub_rep_train_freq_order', '1');
      await open();
      http.expectOne(CONFIG_URL).flush(serverConfig(null, true));
      await fixture.whenStable();
      expect(checked()).toEqual(['leastTrained']);
    });

    it('der Trainer gibt mit, ob gegen den gespeicherten Gegner trainiert wird: dann sechs Werte ohne Abruf, und die Wahl wird gespeichert', async () => {
      await open({ current: 'leastTrained', hasOpponent: true });
      http.expectNone(CONFIG_URL);
      expect(radios().length).toBe(6);
      await choose('opponent');
      saveButton()!.click();
      await fixture.whenStable();
      const req = http.expectOne(ORDER_URL);
      expect(req.request.body).toEqual({ order: 'opponent' });
      req.flush(null);
      await fixture.whenStable();
      expect(closedWith).toBe('opponent');
    });

    it('der Trainer ohne Gegner-Sitzung: fünf Werte', async () => {
      await open({ current: 'leastTrained' });
      expect(radios().length).toBe(5);
    });
  });

  it('der Trainer gibt die geltende Reihenfolge mit: kein Abruf, sie ist markiert', async () => {
    await open({ current: 'repertoire' });
    http.expectNone(CONFIG_URL);
    expect(panel()!.querySelector('mat-spinner')).toBeNull();
    expect(checked()).toEqual(['repertoire']);
  });

  describe('Speichern', () => {
    it('schickt die Wahl an den Server, meldet Erfolg und schließt mit der Reihenfolge', async () => {
      await open({ current: 'leastTrained' });
      await choose('random');
      expect(checked()).toEqual(['random']);
      http.expectNone(ORDER_URL);                       // gespeichert wird erst mit „Speichern“

      saveButton()!.click();
      await fixture.whenStable();
      const req = http.expectOne(ORDER_URL);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ order: 'random' });
      expect(saveButton()!.disabled).withContext('während des Speicherns').toBeTrue();
      expect(closedWith).toBe('offen');

      req.flush(null);
      await fixture.whenStable();
      expect(closedWith).toBe('random');
      expect(panel()).toBeNull();
      expect(snackbar.success).toHaveBeenCalledOnceWith('repertoire.trainOrder.saved');
      expect(snackbar.warn).not.toHaveBeenCalled();
    });

    it('scheitert das Speichern: Snackbar, der Dialog bleibt offen und nichts gilt als gespeichert', async () => {
      localStorage.setItem(OFFLINE_KEY, JSON.stringify({ meta: { id: 7, name: 'R' }, pgn: '1. e4 *', states: [], config: null, lineOrder: 'leastTrained', savedAt: '' }));
      await open({ current: 'leastTrained' });
      await choose('mostTrained');
      saveButton()!.click();
      await fixture.whenStable();

      http.expectOne(ORDER_URL).flush({ message: 'Unknown line order.' }, { status: 400, statusText: 'Bad Request' });
      await fixture.whenStable();

      expect(snackbar.warn).toHaveBeenCalledOnceWith('repertoire.trainOrder.saveFailed');
      expect(snackbar.success).not.toHaveBeenCalled();
      expect(closedWith).toBe('offen');
      expect(panel()).not.toBeNull();
      expect(checked()).toEqual(['mostTrained']);       // die Wahl steht noch da — erneut versuchen geht
      expect(saveButton()!.disabled).toBeFalse();
      expect(JSON.parse(localStorage.getItem(OFFLINE_KEY)!).lineOrder).toBe('leastTrained');
    });

    it('zieht die Reihenfolge in einer vorhandenen Offline-Kopie nach (offline gilt dann dasselbe)', async () => {
      localStorage.setItem(OFFLINE_KEY, JSON.stringify({ meta: { id: 7, name: 'R' }, pgn: '1. e4 *', states: [], config: null, savedAt: '' }));
      await open({ current: 'leastTrained' });
      await choose('repertoire');
      saveButton()!.click();
      await fixture.whenStable();
      http.expectOne(ORDER_URL).flush(null);
      await fixture.whenStable();

      const copy = JSON.parse(localStorage.getItem(OFFLINE_KEY)!);
      expect(copy.lineOrder).toBe('repertoire');
      expect(copy.pgn).toBe('1. e4 *');
    });

    it('ohne Offline-Kopie wird keine angelegt', async () => {
      await open({ current: 'leastTrained' });
      saveButton()!.click();
      await fixture.whenStable();
      http.expectOne(ORDER_URL).flush(null);
      await fixture.whenStable();
      expect(localStorage.getItem(OFFLINE_KEY)).toBeNull();
    });
  });

  it('Abbrechen schließt ohne Ergebnis und ohne Schreiben', async () => {
    await open({ current: 'leastTrained' });
    await choose('random');
    (panel()!.querySelector('button[mat-dialog-close]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(closedWith).toBeUndefined();
    http.expectNone(ORDER_URL);
  });

  /** Am Handy ist der Dialog 94 % von 390 px breit. Gemessen mit den ECHTEN Texten (Schlüssel wären ein Wort ohne Umbruch). */
  for (const lang of ['de', 'hu', 'en', 'hr']) {
    it(`Handy (366 px, ${lang}): nichts läuft waagerecht heraus, jede Wahl ist ein Tippziel von mindestens 44 px`, async () => {
      // mit gespeichertem Gegner: alle SECHS Werte (der längste Fall)
      await open({ current: 'leastTrained', hasOpponent: true, repertoireName: 'Ein sehr langer Repertoirename, der in der schmalen Fläche sicher nicht in eine Zeile passt' });
      const translate = TestBed.inject(TranslateService);
      const res = await fetch(`/i18n/${lang}.json`).then(r => r.ok ? r : fetch(`/base/i18n/${lang}.json`));
      translate.setTranslation(lang, await res.json());
      translate.use(lang);
      const pane = document.querySelector('.cdk-overlay-pane') as HTMLElement;
      pane.style.width = '366px';
      pane.style.maxWidth = '366px';
      await fixture.whenStable();

      const content = panel()!.querySelector('mat-dialog-content') as HTMLElement;
      expect(radios().length).toBe(6);
      expect(panel()!.textContent).withContext('echte Texte geladen, kein Schlüssel sichtbar').not.toContain('repertoire.trainOrder');
      expect(content.scrollWidth).withContext('Inhalt läuft waagerecht über').toBeLessThanOrEqual(content.clientWidth + 1);
      const right = content.getBoundingClientRect().right;
      for (const r of radios()) {
        const box = r.getBoundingClientRect();
        expect(box.height).withContext(`${r.getAttribute('data-order')}: Tippziel`).toBeGreaterThanOrEqual(44);
        expect(box.right).withContext(`${r.getAttribute('data-order')}: ragt rechts heraus`).toBeLessThanOrEqual(right + 1);
        const hint = r.querySelector('.hint') as HTMLElement;
        expect(hint.scrollWidth).withContext(`${r.getAttribute('data-order')}: Erklärsatz abgeschnitten`).toBeLessThanOrEqual(hint.clientWidth + 1);
      }
      const subtitle = panel()!.querySelector('.subtitle') as HTMLElement;
      expect(subtitle.scrollWidth).withContext('langer Name: eine Zeile mit „…“').toBeGreaterThan(subtitle.clientWidth);
      expect(subtitle.getBoundingClientRect().right).toBeLessThanOrEqual(right + 1);
      const actions = panel()!.querySelector('mat-dialog-actions') as HTMLElement;
      expect(actions.scrollWidth).toBeLessThanOrEqual(actions.clientWidth + 1);
    });
  }

  it('per Tastatur bedienbar: EINE Optionsgruppe aus nativen Radio-Feldern mit Namen (Pfeiltasten wechseln)', async () => {
    await open({ current: 'mostTrained' });
    const group = panel()!.querySelector('mat-radio-group')!;
    expect(group.getAttribute('role')).toBe('radiogroup');
    expect(group.getAttribute('aria-label')).toBe('repertoire.trainOrder.title');
    const inputs = Array.from(panel()!.querySelectorAll<HTMLInputElement>('input[type=radio]'));
    expect(inputs.length).toBe(5);
    expect(new Set(inputs.map(i => i.name)).size).withContext('ein gemeinsamer Name = eine Gruppe').toBe(1);
    expect(inputs.filter(i => i.checked).length).toBe(1);
    // Titel + Auswahl + zwei Knöpfe: der Dialog hält den Fokus (Material), Speichern ist ein echter Knopf
    expect(saveButton()!.tagName).toBe('BUTTON');
  });
});
