import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslateModule } from '@ngx-translate/core';
import { VizCardComponent } from './viz-card.component';
import { PuzzleRatingCardComponent } from './puzzle-rating-card.component';

/**
 * OnPush-Regression für die präsentationalen Puzzle-Display-Cards: bei reiner Input-Bindung
 * (Eltern rebinden je CD) muss eine geänderte Eingabe weiterhin neu rendern, und Klick-Outputs
 * müssen feuern. Bestätigt, dass das Umstellen auf OnPush das Verhalten nicht bricht.
 */
// Host nutzt Default-CD (kein OnPush): so propagiert ein direkter Input-Wechsel + detectChanges
// an die OnPush-Kinder — getestet wird damit die Re-Render-Reaktion der KINDER auf Input-Änderung.
@Component({
  standalone: true,
  imports: [VizCardComponent, PuzzleRatingCardComponent],
  template: `
    <app-viz-card
      [visualizationMode]="vizLevel"
      [vizPiecesHidden]="true"
      (vizShowClicked)="vizClicks = vizClicks + 1"></app-viz-card>
    <app-puzzle-rating-card
      [levelText]="levelText"
      [ratingParams]="{ rating: rating }"
      [shareKey]="'puzzles.share'"
      (shareClicked)="shareClicks = shareClicks + 1"></app-puzzle-rating-card>
  `,
})
class HostComponent {
  vizLevel = 1;
  levelText = 'Easy';
  rating = 1500;
  vizClicks = 0;
  shareClicks = 0;
}

describe('Display cards (OnPush)', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HostComponent, TranslateModule.forRoot()],
    }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('viz-card and puzzle-rating-card are OnPush', () => {
    expect((VizCardComponent as any).ɵcmp.onPush).toBeTrue();
    expect((PuzzleRatingCardComponent as any).ɵcmp.onPush).toBeTrue();
  });

  it('re-renders the viz level badge when the bound input changes', () => {
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.viz-level-badge')?.textContent).toContain('1');

    host.vizLevel = 4;
    fixture.detectChanges();
    expect(el.querySelector('.viz-level-badge')?.textContent).toContain('4');
  });

  it('emits the viz show + share outputs on click (OnPush does not swallow events)', () => {
    const el = fixture.nativeElement as HTMLElement;
    (el.querySelector('.viz-show-btn') as HTMLButtonElement).click();
    (el.querySelector('.prc-share') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(host.vizClicks).toBe(1);
    expect(host.shareClicks).toBe(1);
  });
});
