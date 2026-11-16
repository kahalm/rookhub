import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { TrainerCommentsComponent, plyLabel } from './trainer-comments.component';
import { TrainingGameComment, TrainingVideosService } from '../training-videos/training-videos.service';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const AFTER_E4 = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1';
const AFTER_E5 = 'rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2';

function comment(p: Partial<TrainingGameComment>): TrainingGameComment {
  return {
    fenIndex: 1, matchedBy: 'move', lessonId: 3, date: '2026-10-27', segmentId: 1, startSec: 730, endSec: null, fen: AFTER_E4,
    move: 'e4', moveKind: 'played', comment: 'Guter Start', variations: null, tags: [], hasVideo: true, ...p,
  };
}

/** OnPush-Host wie die Partieseite: die Stellung kommt als Eingabe (Muster admin-tabs-onpush.spec). */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TrainerCommentsComponent],
  template: `<app-trainer-comments [gameId]="7" [fenIndex]="idx()" [fens]="fens" [sans]="sans" (goTo)="jumped = $event" />`,
})
class HostComponent {
  readonly idx = signal(1);
  readonly fens = [START, AFTER_E4, AFTER_E5];
  readonly sans = ['e4', 'e5'];
  jumped: number | null = null;
}

describe('TrainerCommentsComponent', () => {
  let items: TrainingGameComment[];
  let calls: number[];

  async function render() {
    calls = [];
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [
        provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        { provide: TrainingVideosService, useValue: { gameComments: (id: number) => { calls.push(id); return of({ items, unmatched: 0 }); } } },
      ],
    });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.autoDetectChanges();
    await fixture.whenStable();
    return fixture;
  }

  it('zeigt nur die Kommentare der Stellung auf dem Brett und zieht beim Zugwechsel mit', async () => {
    items = [comment({ segmentId: 1, fenIndex: 1, comment: 'Zu e4' }), comment({ segmentId: 2, fenIndex: 2, comment: 'Zu e5', move: 'e5' })];
    const fixture = await render();
    const el: HTMLElement = fixture.nativeElement;
    expect(calls).toEqual([7]);
    expect(el.querySelectorAll('.tc-card').length).toBe(1);
    expect(el.querySelector('.tc-card .text')!.textContent).toContain('Zu e4');
    fixture.componentInstance.idx.set(2);
    await fixture.whenStable();
    expect(el.querySelectorAll('.tc-card').length).toBe(1);
    expect(el.querySelector('.tc-card .text')!.textContent).toContain('Zu e5');
    fixture.componentInstance.idx.set(0);
    await fixture.whenStable();
    expect(el.querySelectorAll('.tc-card').length).toBe(0);
    // Sprungzeile bleibt: beide kommentierten Züge, Klick springt auf currentMoveIndex = fenIndex − 1
    const marks = el.querySelectorAll<HTMLButtonElement>('.mark');
    expect([...marks].map(m => m.textContent!.trim())).toEqual(['1.e4', '1…e5']);
    marks[1].click();
    expect(fixture.componentInstance.jumped).toBe(1);
  });

  it('rendert ohne Kommentare gar nichts', async () => {
    items = [];
    const fixture = await render();
    expect((fixture.nativeElement as HTMLElement).querySelector('app-trainer-comments')!.children.length).toBe(0);
  });

  it('Nebenvariante bekommt ein eigenes Brett; ohne Video kein Video-Link', async () => {
    items = [comment({ matchedBy: 'variation', fen: AFTER_E5, hasVideo: false })];
    const fixture = await render();
    const card = (fixture.nativeElement as HTMLElement).querySelector('.tc-card')!;
    expect(card.querySelector('app-chess-board')).not.toBeNull();
    expect(card.querySelector('.tc-video')).toBeNull();
    expect(card.querySelector('.tc-drill')!.getAttribute('href')).toBe('/training-videos/3/drill?seg=1');
  });

  it('plyLabel: Zugnummer aus der FEN, Weiß mit Punkt, Schwarz mit …', () => {
    expect(plyLabel([START, AFTER_E4, AFTER_E5], ['e4', 'e5'], 1, 'en')).toBe('1.e4');
    expect(plyLabel([START, AFTER_E4, AFTER_E5], ['e4', 'e5'], 2, 'en')).toBe('1…e5');
    expect(plyLabel([START], [], 0, 'en')).toBeNull();
  });
});
