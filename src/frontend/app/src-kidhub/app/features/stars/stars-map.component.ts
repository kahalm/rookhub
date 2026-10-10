import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { STAR_STAGES } from '../../core/kids-stars';
import { KidsStarsStore } from '../../core/kids-stars.store';
import { KID_BACK } from '../../shared/kids-layout';
import { pieceGlyph } from './stars-play.component';

/** Die Stufen der Sternenjagd: Figur und Zahl der Sterne; offen ist Stufe 1 und jede nach einer geschafften. */
@Component({
  selector: 'kid-stars-map',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslatePipe],
  template: `
    <header class="head">
      <a class="back" routerLink="/">← {{ 'kids.back' | translate }}</a>
      <h1>{{ 'kids.stars.title' | translate }}</h1>
      <span class="count">✅ {{ store.done() }}/{{ store.total }}</span>
    </header>
    <p class="intro">{{ 'kids.stars.intro' | translate }}</p>
    <p class="free"><a class="free-btn" routerLink="/stars/free">🎲 {{ 'kids.stars.free.title' | translate }}</a>
      <span>{{ 'kids.stars.free.hint' | translate }}</span></p>
    <ol class="grid">
      @for (s of stages; track s.stage) {
        <li>
          @if (store.isOpen(s.stage)) {
            <a class="stage" [class.done]="store.isDone(s.stage)" [class.current]="s.stage === store.current()"
               [routerLink]="['/stars', s.stage]"
               [attr.aria-label]="('kids.stars.stage' | translate: { stage: s.stage }) + ' – ' + ('kids.stars.piece.' + s.piece | translate)">
              <span class="num">{{ s.stage }}</span>
              <span class="glyph" aria-hidden="true">{{ glyph(s.piece) }}</span>
              <span class="name">{{ 'kids.stars.piece.' + s.piece | translate }}</span>
              <span class="meta">{{ 'kids.stars.starRange' | translate: { min: s.counts[0], max: s.counts[s.counts.length - 1] } }}</span>
            </a>
          } @else {
            <span class="stage locked" [attr.aria-label]="('kids.stars.stage' | translate: { stage: s.stage }) + ' – ' + ('kids.levels.locked' | translate)">
              <span class="num">{{ s.stage }}</span>
              <span class="glyph" aria-hidden="true">🔒</span>
              <span class="meta">{{ 'kids.stars.starRange' | translate: { min: s.counts[0], max: s.counts[s.counts.length - 1] } }}</span>
            </span>
          }
        </li>
      }
    </ol>
  `,
  styles: [KID_BACK, `
    :host { display: block; max-width: 980px; margin: 0 auto; padding: 16px; }
    .head { display: flex; align-items: center; gap: 12px; margin-bottom: 8px; }
    .head h1 { flex: 1; margin: 0; font-size: 1.9rem; color: var(--kid-title); text-align: center; }
    .count { font-size: 1.15rem; font-weight: 800; white-space: nowrap; }
    .intro { text-align: center; font-size: 1.15rem; margin: 0 0 16px; }
    .free { display: flex; flex-wrap: wrap; gap: 8px 14px; align-items: center; justify-content: center; margin: 0 0 18px; }
    .free-btn {
      display: inline-flex; align-items: center; min-height: 44px; padding: 8px 20px; border-radius: 999px;
      background: #e8e0ff; color: inherit; text-decoration: none; font-size: 1.2rem; font-weight: 800;
      box-shadow: 0 4px 0 var(--kid-shadow);
    }
    .grid { list-style: none; margin: 0; padding: 0; display: grid; gap: 14px;
            grid-template-columns: repeat(auto-fill, minmax(132px, 1fr)); }
    .stage {
      position: relative; display: flex; flex-direction: column; align-items: center; gap: 2px;
      padding: 14px 8px 10px; border-radius: 24px; text-decoration: none; color: inherit;
      background: var(--kid-card); box-shadow: 0 5px 0 var(--kid-shadow); min-height: 138px; box-sizing: border-box;
    }
    a.stage:hover { transform: translateY(-2px); }
    .stage.done { background: var(--kid-good-bg); }
    .stage.current { outline: 4px solid var(--kid-green-strong); outline-offset: -4px; }
    .stage.locked { opacity: .55; }
    .num { position: absolute; top: 8px; left: 12px; font-weight: 800; opacity: .7; }
    .glyph { font-size: 2.8rem; line-height: 1.1; }
    .name { font-size: 1.1rem; font-weight: 800; }
    .meta { font-size: .95rem; opacity: .85; }
  `],
})
export class StarsMapComponent {
  readonly store = inject(KidsStarsStore);
  readonly stages = STAR_STAGES;
  readonly glyph = pieceGlyph;
}
