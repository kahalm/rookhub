import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatSelectHarness } from '@angular/material/select/testing';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { MistakesCollectionComponent } from './mistakes-collection.component';
import { GamesService, SavedGame } from './games.service';
import { MistakeJudgeService } from './mistake-judge.service';

/** UI-Sweep 2026-10-11: Grund unter „Starten", Einzahl, Material-Filter auf /games/mistakes. */
describe('MistakesCollectionComponent (UI-Sweep 2026-10-11)', () => {
  const game = (id: number, c1: string, c2: string, done: boolean): SavedGame => ({
    id, source: 'pgn', shareToken: 't' + id, moveCount: 40, createdAt: '2026-10-01T00:00:00Z',
    classifier1: c1, classifier2: c2,
    analysis: done ? { status: 'done', analyzed: 40, total: 40 } : null,
  });

  async function setup(games: SavedGame[]) {
    TestBed.configureTestingModule({
      imports: [MistakesCollectionComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: GamesService, useValue: { list: () => of(games) } },
        { provide: MistakeJudgeService, useValue: {} },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({}) } } },
      ],
    });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('de', { games: { mistakes: { collection: {
      selection: '{{games}} Partien gewählt, davon {{analysed}} analysiert',
      selectionOne: '1 Partie gewählt, davon {{analysed}} analysiert',
      noneAnalysed: 'Noch keine dieser Partien ist fertig analysiert.',
      noGames: 'Für diese Auswahl gibt es keine Partien.',
    } } } });
    translate.use('de');
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    const fixture = TestBed.createComponent(MistakesCollectionComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const loader = TestbedHarnessEnvironment.loader(fixture);
    return { fixture, el: fixture.nativeElement as HTMLElement, component: fixture.componentInstance, loader, navigate };
  }

  it('nennt unter dem gesperrten „Starten" den Grund, wenn keine Partie analysiert ist', async () => {
    const { el } = await setup([game(1, 'Landesliga', '2026/27', false)]);
    const start = el.querySelector('button.start') as HTMLButtonElement;
    expect(start.disabled).toBeTrue();
    expect(el.querySelector('.disabled-reason')?.textContent).toContain('Noch keine dieser Partien ist fertig analysiert.');
  });

  it('nennt „keine Partien", wenn die Auswahl leer ist', async () => {
    const { el } = await setup([]);
    expect(el.querySelector('.disabled-reason')?.textContent).toContain('Für diese Auswahl gibt es keine Partien.');
  });

  it('zeigt keinen Grund, sobald eine analysierte Partie da ist', async () => {
    const { el } = await setup([game(1, 'Landesliga', '2026/27', true)]);
    expect((el.querySelector('button.start') as HTMLButtonElement).disabled).toBeFalse();
    expect(el.querySelector('.disabled-reason')).toBeNull();
  });

  it('schreibt bei genau einer Partie die Einzahl', async () => {
    const { el } = await setup([game(1, 'Landesliga', '2026/27', false)]);
    expect(el.querySelector('.count')?.textContent?.trim()).toBe('1 Partie gewählt, davon 0 analysiert');
  });

  it('schreibt bei mehreren Partien die Mehrzahl', async () => {
    const { el } = await setup([game(1, 'Landesliga', '2026/27', true), game(2, 'Landesliga', '2025/26', false)]);
    expect(el.querySelector('.count')?.textContent?.trim()).toBe('2 Partien gewählt, davon 1 analysiert');
  });

  it('Filter sind mat-select in outline-Feldern; die Auswahl setzt filter1 und den URL-Parameter c1', async () => {
    const { el, component, loader, navigate } = await setup([
      game(1, 'Landesliga', '2026/27', true), game(2, 'Bundesliga', '2026/27', true),
    ]);
    expect(el.querySelectorAll('.filters select').length).toBe(0);
    expect(el.querySelectorAll('.filters mat-form-field.mat-form-field-appearance-outline').length).toBe(2);
    const select = await loader.getHarness(MatSelectHarness.with({ selector: '.filter1' }));
    await select.open();
    await select.clickOptions({ text: 'Landesliga' });
    expect(component.filter1).toBe('Landesliga');
    expect(component.selected().length).toBe(1);
    expect(navigate).toHaveBeenCalled();
    const extras = navigate.calls.mostRecent().args[1]!;
    expect(extras.queryParams).toEqual(jasmine.objectContaining({ c1: 'Landesliga' }));
  });
});
