import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { AnalyzeGameService, AnalyzeMode } from '../games/analyze-game.service';

/**
 * Profil: Vorgabe für „Partie analysieren" (0.855.0, Wunsch 2026-10-27: „eine ‚Entscheidung merken'-Box — der Standard soll
 * auch irgendwo umgestellt werden können (Profil)"). Jedes Mal fragen, immer mit oder immer ohne Engine; gespeichert je Nutzer
 * (`/api/view-state`, gilt auf allen Geräten).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-analyze-choice-card',
  standalone: true,
  imports: [MatButtonToggleModule, MatIconModule, TranslatePipe],
  template: `
    <div class="analyze-choice-section">
      <h4 id="analyze-choice-label">{{ 'profile.analyzeChoice.title' | translate }}</h4>
      <mat-button-toggle-group class="analyze-choice-toggle" aria-labelledby="analyze-choice-label" hideSingleSelectionIndicator
                               [value]="analyze.mode()" (change)="set($event.value)">
        <mat-button-toggle value="ask"><mat-icon>help_outline</mat-icon> {{ 'profile.analyzeChoice.ask' | translate }}</mat-button-toggle>
        <mat-button-toggle value="engine"><mat-icon>insights</mat-icon> {{ 'games.analyzeChoice.engine' | translate }}</mat-button-toggle>
        <mat-button-toggle value="blind"><mat-icon>visibility_off</mat-icon> {{ 'games.analyzeChoice.blind' | translate }}</mat-button-toggle>
      </mat-button-toggle-group>
      <p class="hint">{{ 'profile.analyzeChoice.hint' | translate }}</p>
    </div>
  `,
  styles: [`
    /* d6-rk-profil-ueberschrift-partie-analysieren: Abschnittstitel in der Akzentfarbe wie alle Profilkarten. */
    .analyze-choice-section h4 { margin: 0 0 0.5rem; color: var(--rh-accent); }
    /* d6-rk-profil-analysieren-umschalter-umbruch: die Pillengruppe bricht nie um (vorher flex-wrap → „Ohne Engine“ allein
       in Zeile 2, Rundung mitten in der Gruppe). Ohne Häkchen (die Wahl ist eingefärbt, wie bei der UI-Größe); am Handy
       volle Breite, drei gleich breite Segmente, Symbol über dem Wort (wie die Modus-Leiste im E-Book-Brett). */
    .analyze-choice-toggle { max-width: 100%; }
    @media (max-width: 599.98px) {
      .analyze-choice-toggle { display: flex; width: 100%; }
      .analyze-choice-toggle mat-button-toggle { flex: 1 1 0; min-width: 0; }
      .analyze-choice-toggle ::ng-deep .mat-button-toggle-label-content { display: flex; flex-direction: column;
        align-items: center; gap: 2px; line-height: 1.2; padding: 6px 4px; white-space: normal; text-align: center; }
    }
    .hint { color: var(--mat-sys-on-surface-variant); font-size: 13px; margin: 6px 0 0; }
  `],
})
export class AnalyzeChoiceCardComponent implements OnInit {
  readonly analyze = inject(AnalyzeGameService);

  ngOnInit(): void { this.analyze.loadMode().subscribe(); }

  set(mode: AnalyzeMode): void { this.analyze.setMode(mode); }
}
