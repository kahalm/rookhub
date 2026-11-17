import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { TrainingLessonComponent } from './training-lesson.component';
import { nextTurn, previousTurn, sortedTurns, speakerBands, speakerName, turnIndexAt } from './training-speaker.util';
import { TrainingLesson, TrainingSpeakerTurn, TrainingVideosService } from './training-videos.service';

const TURNS: TrainingSpeakerTurn[] = [{ t: 0, sp: 'patrik' }, { t: 33, sp: 'sasa' }, { t: 90, sp: 'patrik' }];

describe('Sprecherwechsel (Hilfsfunktionen)', () => {
  it('Index, nächster und voriger Beitrag', () => {
    expect(turnIndexAt(TURNS, 0)).toBe(0);
    expect(turnIndexAt(TURNS, 32.9)).toBe(0);
    expect(turnIndexAt(TURNS, 33)).toBe(1);
    expect(nextTurn(TURNS, 10)).toEqual(TURNS[1]);
    expect(nextTurn(TURNS, 32.8)).toEqual(TURNS[2]);   // steht fast auf dem Wechsel → der übernächste
    expect(nextTurn(TURNS, 95)).toBeNull();
    expect(previousTurn(TURNS, 60)).toEqual(TURNS[1]);  // Anfang des laufenden
    expect(previousTurn(TURNS, 34)).toEqual(TURNS[0]);  // gerade erst gewechselt → voriger
    expect(previousTurn(TURNS, 1)).toEqual(TURNS[0]);
  });

  it('Namen, Ordnung und Leiste', () => {
    expect(speakerName('sasa')).toBe('Saša');
    expect(speakerName('patrik')).toBe('Patrik');
    expect(speakerName('gast')).toBe('Gast');
    expect(sortedTurns([{ t: 9, sp: 'a' }, { t: -1, sp: 'b' }, { t: 2, sp: 'b' }])).toEqual([{ t: 2, sp: 'b' }, { t: 9, sp: 'a' }]);
    expect(sortedTurns(null)).toEqual([]);
    const bands = speakerBands(TURNS, 120);
    expect(bands.map(b => Math.round(b.widthPct))).toEqual([28, 48, 25]);
    expect(speakerBands(TURNS, 0)).toEqual([]);
  });
});

describe('TrainingLessonComponent — Nächster Sprecher', () => {
  function lesson(p: Partial<TrainingLesson> = {}): TrainingLesson {
    return { id: 7, date: '2026-10-27', summary: null, durationSec: 120, hasVideo: true, ownerName: null, games: [], segments: [],
      pgns: {}, analysisPgns: {}, part: 'private', otherParts: [], speakerTurns: TURNS, ...p };
  }

  async function render(l: TrainingLesson) {
    const route = { snapshot: { paramMap: convertToParamMap({ id: '7' }), queryParamMap: convertToParamMap({}), data: {} },
      paramMap: of(convertToParamMap({ id: '7' })) };
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: route },
        { provide: TrainingVideosService, useValue: {
          get: () => of(l), accessToken: () => of('tok'), videoUrl: () => 'about:blank', forgetToken: () => undefined, shares: () => of([]) } }],
    });
    const fixture = TestBed.createComponent(TrainingLessonComponent);
    fixture.autoDetectChanges();
    await fixture.whenStable();
    return fixture;
  }

  it('zeigt Knöpfe, Leiste und Legende; „Nächster Sprecher“ springt an den nächsten Wechsel, „zurück“ an den Anfang', async () => {
    const fixture = await render(lesson());
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.tv-speaker-next')).not.toBeNull();
    expect(el.querySelector('.tv-speaker-prev')).not.toBeNull();
    expect(el.querySelectorAll('.speaker-track .band').length).toBe(3);
    expect(Array.from(el.querySelectorAll('.speaker-legend .speaker')).map(x => x.textContent!.trim())).toEqual(['Patrik', 'Saša']);

    const cmp = fixture.componentInstance;
    const seek = spyOn(cmp, 'seek');
    (el.querySelector('.tv-speaker-next') as HTMLButtonElement).click();
    expect(seek).toHaveBeenCalledWith(33, false);
    expect(cmp.currentTurn()?.sp).toBe('sasa');
    await fixture.whenStable();
    expect(el.querySelector('.speaker.now')!.textContent).toContain('Saša');

    seek.calls.reset();
    (el.querySelector('.tv-speaker-prev') as HTMLButtonElement).click();
    expect(seek).toHaveBeenCalledWith(0, false);   // Video steht noch bei 0 → Anfang des ersten Beitrags
  });

  it('ohne Sprecherwechsel kein Knopf und keine Leiste', async () => {
    const fixture = await render(lesson({ speakerTurns: [] }));
    expect(fixture.nativeElement.querySelector('.tv-speakers')).toBeNull();
  });
});
