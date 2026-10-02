import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatSelect, MatSelectChange } from '@angular/material/select';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { MaiaEngineService, MaiaStatus } from './maia-engine.service';
import { MaiaSparringCardComponent } from './maia-sparring-card.component';

const flush = () => new Promise<void>(r => setTimeout(r));

/** Promise, das der Test von außen auflöst. */
function deferred<T>() {
  let resolve!: (v: T) => void;
  const promise = new Promise<T>(r => { resolve = r; });
  return { promise, resolve };
}

/** Der Dienst zum Anfassen: Status/Fortschritt/Fehler stellt der Test, prepare()/download() löst er selbst auf. */
class FakeMaia {
  status = signal<MaiaStatus>('idle');
  progress = signal(0);
  error = signal<'unavailable' | 'failed' | null>(null);
  canStore = signal(true);
  prepareCalls: { resolve: (ok: boolean) => void }[] = [];
  downloadCalls: { resolve: (ok: boolean) => void }[] = [];
  prepare = jasmine.createSpy('prepare').and.callFake(() => {
    const d = deferred<boolean>(); this.prepareCalls.push(d); return d.promise;
  });
  download = jasmine.createSpy('download').and.callFake(() => {
    const d = deferred<boolean>(); this.downloadCalls.push(d); return d.promise;
  });
}

describe('MaiaSparringCardComponent', () => {
  let fixture: ComponentFixture<MaiaSparringCardComponent>;
  let card: MaiaSparringCardComponent;
  let maia: FakeMaia;
  let started: number;
  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';
  const button = (cls: string) => (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(`button.${cls}`);
  const click = async (cls: string) => {
    const b = button(cls);
    expect(b).withContext(`Knopf .${cls}`).not.toBeNull();
    b!.click();
    fixture.detectChanges();
    await flush();
    fixture.detectChanges();
  };

  beforeEach(async () => {
    maia = new FakeMaia();
    await TestBed.configureTestingModule({
      imports: [MaiaSparringCardComponent],
      providers: [
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MaiaEngineService, useValue: maia },
      ],
    }).compileComponents();
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', { analysis: { maia: {
      downloading: 'Loading {{percent}} %', status: 'Maia {{elo}} · {{side}}', youWhite: 'WHITE', youBlack: 'BLACK',
    } } });
    translate.use('en');
    fixture = TestBed.createComponent(MaiaSparringCardComponent);
    card = fixture.componentInstance;
    started = 0;
    card.start.subscribe(() => started++);
    fixture.detectChanges();
  });

  afterEach(() => fixture.destroy());

  it('model in the cache: Start → prepare → start, without asking', async () => {
    await click('maia-start');
    expect(maia.prepare).toHaveBeenCalledTimes(1);
    expect(fixture.debugElement.query(By.directive(MatProgressBar))).not.toBeNull();   // „wird vorbereitet"
    maia.status.set('ready');
    maia.prepareCalls[0].resolve(true);
    await flush();
    fixture.detectChanges();
    expect(started).toBe(1);
    expect(maia.download).not.toHaveBeenCalled();
    expect(button('maia-start')).not.toBeNull();   // zurück in Ruhe (das Elternteil schaltet auf aktiv)
  });

  it('missing → confirmation → Download → progress → start', async () => {
    await click('maia-start');
    maia.status.set('missing');
    maia.prepareCalls[0].resolve(false);
    await flush();
    fixture.detectChanges();
    expect(text()).toContain('analysis.maia.downloadInfo');
    expect(text()).not.toContain('analysis.maia.downloadInfoNoStore');
    expect(started).toBe(0);

    await click('maia-download');
    expect(maia.download).toHaveBeenCalledTimes(1);
    maia.status.set('downloading');
    maia.progress.set(37);
    fixture.detectChanges();
    const bar = fixture.debugElement.query(By.directive(MatProgressBar)).componentInstance as MatProgressBar;
    expect(bar.mode).toBe('determinate');
    expect(bar.value).toBe(37);
    expect(text()).toContain('Loading 37 %');

    maia.status.set('loading');
    fixture.detectChanges();
    expect((fixture.debugElement.query(By.directive(MatProgressBar)).componentInstance as MatProgressBar).mode).toBe('indeterminate');

    maia.status.set('ready');
    maia.downloadCalls[0].resolve(true);
    await flush();
    expect(started).toBe(1);
  });

  it('without a usable Cache API the confirmation does not promise to keep the model', async () => {
    await click('maia-start');
    maia.canStore.set(false);
    maia.status.set('missing');
    maia.prepareCalls[0].resolve(false);
    await flush();
    fixture.detectChanges();
    expect(text()).toContain('analysis.maia.downloadInfoNoStore');
    expect(text()).not.toMatch(/analysis\.maia\.downloadInfo(?!NoStore)/);
  });

  it('Cancel on the confirmation: no download, no start', async () => {
    await click('maia-start');
    maia.status.set('missing');
    maia.prepareCalls[0].resolve(false);
    await flush();
    fixture.detectChanges();
    await click('maia-cancel');
    expect(maia.download).not.toHaveBeenCalled();
    expect(started).toBe(0);
    expect(button('maia-start')).not.toBeNull();
  });

  it('Cancel while loading: a load that finishes later does not start the sparring', async () => {
    await click('maia-start');
    await click('maia-cancel');
    maia.status.set('ready');
    maia.prepareCalls[0].resolve(true);
    await flush();
    expect(started).toBe(0);
  });

  it('leaving the page while loading: no start afterwards', async () => {
    await click('maia-start');
    fixture.destroy();
    maia.prepareCalls[0].resolve(true);
    await flush();
    expect(started).toBe(0);
  });

  it('shows the error and retries the same way (download after a failed download)', async () => {
    await click('maia-start');
    maia.status.set('missing');
    maia.prepareCalls[0].resolve(false);
    await flush();
    fixture.detectChanges();
    await click('maia-download');
    maia.status.set('error');
    maia.error.set('unavailable');
    maia.downloadCalls[0].resolve(false);
    await flush();
    fixture.detectChanges();
    expect(text()).toContain('analysis.maia.errorUnavailable');

    await click('maia-retry');
    expect(maia.download).toHaveBeenCalledTimes(2);
    expect(maia.prepare).toHaveBeenCalledTimes(1);

    maia.error.set('failed');
    maia.downloadCalls[1].resolve(false);
    await flush();
    fixture.detectChanges();
    expect(text()).toContain('analysis.maia.errorFailed');
  });

  it('retries from the cache when the failure came from prepare()', async () => {
    await click('maia-start');
    maia.status.set('error');
    maia.error.set('failed');
    maia.prepareCalls[0].resolve(false);
    await flush();
    fixture.detectChanges();
    await click('maia-retry');
    expect(maia.prepare).toHaveBeenCalledTimes(2);
    expect(maia.download).not.toHaveBeenCalled();
  });

  it('active view: status line, „Maia moves" only when Maia is to move and not thinking', () => {
    fixture.componentRef.setInput('active', true);
    fixture.componentRef.setInput('userColor', 'black');
    fixture.componentRef.setInput('elo', 1800);
    fixture.componentRef.setInput('maiaToMove', false);
    fixture.detectChanges();
    expect(text()).toContain('Maia 1800 · BLACK');
    expect(button('maia-start')).toBeNull();
    expect(button('maia-move')).toBeNull();
    expect(button('maia-switch')).not.toBeNull();
    expect(button('maia-restart')).not.toBeNull();
    expect(button('maia-stop')).not.toBeNull();

    fixture.componentRef.setInput('maiaToMove', true);
    fixture.detectChanges();
    expect(button('maia-move')).not.toBeNull();
    expect(button('maia-move')!.getAttribute('aria-label')).toBe('analysis.maia.maiaMove');

    fixture.componentRef.setInput('thinking', true);
    fixture.detectChanges();
    expect(button('maia-move')).toBeNull();
    expect(text()).toContain('analysis.maia.thinking');
  });

  it('the active buttons emit their events', () => {
    fixture.componentRef.setInput('active', true);
    fixture.componentRef.setInput('maiaToMove', true);
    fixture.detectChanges();
    const seen: string[] = [];
    card.maiaMove.subscribe(() => seen.push('move'));
    card.switchSides.subscribe(() => seen.push('switch'));
    card.restart.subscribe(() => seen.push('restart'));
    card.stop.subscribe(() => seen.push('stop'));
    for (const cls of ['maia-move', 'maia-switch', 'maia-restart', 'maia-stop']) button(cls)!.click();
    expect(seen).toEqual(['move', 'switch', 'restart', 'stop']);
  });

  it('changing the strength emits eloChange (also while sparring)', () => {
    fixture.componentRef.setInput('active', true);
    fixture.detectChanges();
    const values: number[] = [];
    card.eloChange.subscribe(v => values.push(v));
    const select = fixture.debugElement.query(By.directive(MatSelect)).componentInstance as MatSelect;
    select.selectionChange.emit({ value: 1200, source: select } as MatSelectChange);
    expect(values).toEqual([1200]);
  });

  // ----- „Partie analysieren" -----

  it('„Partie analysieren" only with showAnalyze — at rest next to Start, while sparring under the icons', () => {
    expect(button('maia-analyze')).toBeNull();
    fixture.componentRef.setInput('showAnalyze', true);
    fixture.detectChanges();
    const b = button('maia-analyze')!;
    expect(b).not.toBeNull();
    expect(b.textContent).toContain('games.analyze');
    expect(b.querySelector('mat-icon')!.textContent).toContain('insights');
    expect(b.classList).toContain('mat-mdc-outlined-button');          // nicht die farbige Hauptaktion
    expect(b.disabled).toBeFalse();
    expect(b.parentElement).toBe(button('maia-start')!.parentElement);  // in derselben Zeile wie „Starten"

    fixture.componentRef.setInput('active', true);
    fixture.detectChanges();
    expect(button('maia-start')).toBeNull();
    expect(button('maia-analyze')).not.toBeNull();
    expect(button('maia-stop')).not.toBeNull();
    // Unter den Symbolen, nicht zwischen ihnen.
    expect(button('maia-analyze')!.parentElement).not.toBe(button('maia-stop')!.parentElement);

    fixture.componentRef.setInput('showAnalyze', false);
    fixture.detectChanges();
    expect(button('maia-analyze')).toBeNull();
  });

  it('not in the confirmation, loading and error views', async () => {
    fixture.componentRef.setInput('showAnalyze', true);
    fixture.detectChanges();
    await click('maia-start');
    expect(button('maia-analyze')).withContext('lädt').toBeNull();
    maia.status.set('missing');
    maia.prepareCalls[0].resolve(false);
    await flush();
    fixture.detectChanges();
    expect(button('maia-download')).not.toBeNull();
    expect(button('maia-analyze')).withContext('Rückfrage').toBeNull();
    await click('maia-download');
    maia.status.set('error');
    maia.error.set('failed');
    maia.downloadCalls[0].resolve(false);
    await flush();
    fixture.detectChanges();
    expect(button('maia-retry')).not.toBeNull();
    expect(button('maia-analyze')).withContext('Fehler').toBeNull();
  });

  it('a click emits analyze', () => {
    fixture.componentRef.setInput('showAnalyze', true);
    fixture.detectChanges();
    let seen = 0;
    card.analyze.subscribe(() => seen++);
    button('maia-analyze')!.click();
    expect(seen).toBe(1);
  });

  it('analyzing: disabled with a small spinner instead of the icon, no second click', () => {
    fixture.componentRef.setInput('showAnalyze', true);
    fixture.componentRef.setInput('analyzing', true);
    fixture.detectChanges();
    const b = button('maia-analyze')!;
    expect(b.disabled).toBeTrue();
    expect(b.querySelector('mat-spinner')).not.toBeNull();
    expect(b.querySelector('mat-icon')).toBeNull();
    let seen = 0;
    card.analyze.subscribe(() => seen++);
    card.onAnalyze();
    expect(seen).toBe(0);
    expect(text()).not.toContain('guess.upload.noEngine');
  });

  it('no engine: disabled, the reason as a line below (a disabled button shows no tooltip) — at rest and active', () => {
    fixture.componentRef.setInput('showAnalyze', true);
    fixture.componentRef.setInput('analyzeBlocked', true);
    fixture.detectChanges();
    const check = (where: string) => {
      const b = button('maia-analyze')!;
      expect(b.disabled).withContext(where).toBeTrue();
      expect(b.getAttribute('title')).withContext(where).toBe('guess.upload.noEngine');
      const hint = (fixture.nativeElement as HTMLElement).querySelector('.maia-analyze-hint');
      expect(hint?.textContent).withContext(where).toContain('guess.upload.noEngine');
      expect(b.getAttribute('aria-describedby')).withContext(where).toBeTruthy();   // matTooltip beschreibt ihn
    };
    check('Ruhe');
    let seen = 0;
    card.analyze.subscribe(() => seen++);
    card.onAnalyze();
    expect(seen).toBe(0);

    fixture.componentRef.setInput('active', true);
    fixture.detectChanges();
    check('aktiv');

    fixture.componentRef.setInput('analyzeBlocked', false);
    fixture.detectChanges();
    expect(button('maia-analyze')!.disabled).toBeFalse();
    expect(button('maia-analyze')!.getAttribute('title')).toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('.maia-analyze-hint')).toBeNull();
  });

  it('läuft am Handy (360 px) nicht horizontal über — Ruhe, Rückfrage und aktive Ansicht', async () => {
    const host = fixture.nativeElement as HTMLElement;
    host.style.display = 'block';
    host.style.width = '360px';
    document.body.appendChild(host);
    const fits = () => {
      const cardEl = host.querySelector('mat-card') as HTMLElement;
      return cardEl.scrollWidth <= cardEl.clientWidth && host.scrollWidth <= 360;
    };
    // Mit den echten (deutschen, längsten) Texten des Analyse-Knopfs und des Hinweises.
    TestBed.inject(TranslateService).setTranslation('en', {
      games: { analyze: 'Partie analysieren' },
      guess: { upload: { noEngine: 'Gerade steht keine Engine bereit, die eingeworfene Partien rechnen könnte.' } },
    }, true);
    fixture.detectChanges();
    expect(fits()).withContext('Ruhe').toBeTrue();

    fixture.componentRef.setInput('showAnalyze', true);
    fixture.componentRef.setInput('analyzeBlocked', true);
    fixture.detectChanges();
    expect(fits()).withContext('Ruhe mit „Partie analysieren"').toBeTrue();
    fixture.componentRef.setInput('analyzeBlocked', false);
    fixture.componentRef.setInput('showAnalyze', false);
    fixture.detectChanges();

    await click('maia-start');
    maia.canStore.set(false);
    maia.status.set('missing');
    maia.prepareCalls[0].resolve(false);
    await flush();
    fixture.detectChanges();
    expect(fits()).withContext('Rückfrage').toBeTrue();

    fixture.componentRef.setInput('active', true);
    fixture.componentRef.setInput('maiaToMove', true);
    fixture.componentRef.setInput('elo', 2600);
    fixture.detectChanges();
    expect(fits()).withContext('aktiv').toBeTrue();

    fixture.componentRef.setInput('showAnalyze', true);
    fixture.componentRef.setInput('analyzing', true);
    fixture.detectChanges();
    expect(fits()).withContext('aktiv mit „Partie analysieren"').toBeTrue();
    fixture.componentRef.setInput('analyzing', false);
    fixture.componentRef.setInput('analyzeBlocked', true);
    fixture.detectChanges();
    expect(fits()).withContext('aktiv, keine Engine').toBeTrue();
    host.remove();
  });

  it('a disabled card does not start', async () => {
    fixture.componentRef.setInput('disabled', true);
    fixture.detectChanges();
    expect(button('maia-start')!.disabled).toBeTrue();
    card.onStart();
    expect(maia.prepare).not.toHaveBeenCalled();
  });
});
