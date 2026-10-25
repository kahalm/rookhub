import { ChangeDetectionStrategy, Component, input, model, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { GapResult, PartKind, ReconstructionPart } from './reconstruct.service';

/**
 * Die Lückenzeile ZWISCHEN zwei Teilen der Rekonstruktion: „Lücke schließen", Suchtiefe,
 * Vorschläge verwerfen, darunter der Stand der Suche (Zerlegung Z25, aus
 * `ReconstructDetailComponent`). Gesucht wird in der Seite — hier wird nur angezeigt und gemeldet.
 */
@Component({
  selector: 'app-gap-proposals',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatButtonToggleModule, MatIconModule, MatTooltipModule, TranslatePipe],
  template: `
    <!-- Eine Lücke ist etwas ZWISCHEN zwei Teilen — hier steht sie und hier wird gesucht. -->
    <div class="gap-row">
      <mat-icon class="gap-icon">more_horiz</mat-icon>
      <span class="muted">{{ 'reconstruct.gap.here' | translate }}</span>
      @if (target().kind === Kind.Position) {
        <!-- d5-rk-reconstruct-doppelte-aktionen: steht dieselbe Lücke ab 900 px rechts als Hauptknopf, entfällt
             der Umriss-Knopf hier (gap-close-dup, nur ab 900 px ausgeblendet — darunter fehlt der Ruhe-Block). -->
        <button mat-stroked-button class="gap-close"
                [class.gap-close-dup]="duplicate()"
                [disabled]="searching() || busy()" (click)="closeGap.emit()">
          <mat-icon>link</mat-icon> {{ 'reconstruct.gap.close' | translate }}
        </button>
        <!-- Suchtiefe als Umschalter: wählt nur die Tiefe, gesucht wird allein über „Lücke schließen"
             (z1-rk-recon-lueckentiefe). -->
        <!-- Etikett und Auswahl in EINER Hülle: am Handy brechen sie zusammen um (z2-rk-recon-halbzuege-label). -->
        <span class="gap-depth-pick">
        <span class="depth-label muted small">{{ 'reconstruct.gap.pliesLabel' | translate }}</span>
        <mat-button-toggle-group class="depth-toggle" hideSingleSelectionIndicator
                                 [value]="plies()" (change)="plies.set($event.value)"
                                 [disabled]="searching() || busy()"
                                 [attr.aria-label]="'reconstruct.gap.depth' | translate">
          @for (choice of plyChoices(); track choice) {
            <mat-button-toggle [value]="choice" [attr.aria-label]="'reconstruct.gap.plies' | translate:{ count: choice }"
                               [matTooltip]="'reconstruct.gap.plies' | translate:{ count: choice }">{{ choice }}</mat-button-toggle>
          }
        </mat-button-toggle-group>
        </span>
        @if (hasProposals()) {
          <button mat-button [disabled]="busy()" (click)="discard.emit()">
            <mat-icon>close</mat-icon> {{ 'reconstruct.gap.discard' | translate }}
          </button>
        }
      } @else {
        <span class="muted small">{{ 'reconstruct.gap.needPosition' | translate }}</span>
      }
    </div>
    @if (searchingHere()) {
      <p class="muted small gap-note">{{ 'reconstruct.gap.searching' | translate }}</p>
    } @else if (messageKey(); as key) {
      <p class="muted small gap-note">
        {{ key | translate:{ count: result()!.maxPlies, depth: result()!.deepestSearched } }}
      </p>
    }
  `,
  styles: [`
    /* Kein eigener Kasten: Zeile und Hinweis bleiben Geschwister in der Teileliste wie vor der Zerlegung. */
    :host { display: contents; }
    .muted { opacity: 0.75; }
    .gap-note { margin: -0.2rem 0 0.5rem 1rem; }
    /* d5-rk-reconstruct-doppelte-aktionen: ab 900 px steht „Lücke schließen“ für die erste Lücke als Hauptknopf im
       Ruhe-Block rechts — der Umriss-Knopf derselben Lücke in der Teileliste entfällt dann (nie dieselbe Aktion doppelt). */
    @media (min-width: 900px) {
      .gap-row .gap-close-dup { display: none; }
    }
    /* Eine Lücke ist etwas ZWISCHEN zwei Teilen — deshalb steht sie auch dort und nicht in einer
       Karte. Vorher war sie nirgends zu sehen: man musste wissen, dass der Knopf an der Stellung
       DANACH hängt. */
    .gap-row {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      flex-wrap: wrap;
      margin: 0.15rem 0 0.4rem 0.5rem;
      padding-left: 0.5rem;
      border-left: 3px dashed color-mix(in srgb, var(--rh-warn) 60%, transparent);

      .gap-icon { opacity: 0.6; }
      /* Etikett „Halbzüge" + Auswahl als ein Eintrag — brechen gemeinsam um (z2-rk-recon-halbzuege-label). */
      .gap-depth-pick { display: inline-flex; align-items: center; gap: 6px; }
      .small { font-size: 0.85rem; }
    }
    /* Suchtiefe als kompakte Umschaltgruppe (2 · 4 · … · 12), passt mit „Lücke schließen" in eine Zeile. */
    .depth-toggle {
      --mat-button-toggle-height: 32px;
      font-size: 0.85rem;
      ::ng-deep .mat-button-toggle-label-content { padding: 0 9px; line-height: 32px; }
    }
    /* d3-rk-recon-halbzuege-tippziel-klein: am Handy 44 px hohe Segmente (DESIGN 2 Grundsatz 5) — sechs zu je ~44 px plus
       „Halbzüge" passen in 390 px. */
    @media (max-width: 599.98px) {
      .depth-toggle {
        --mat-button-toggle-height: 44px;
        ::ng-deep .mat-button-toggle-label-content { padding: 0 12px; line-height: 44px; min-width: 20px; }
      }
    }
  `],
})
export class GapProposalsComponent {
  readonly Kind = PartKind;

  /** Das Teil, dem die Lücke gehört (das Ziel der Suche). */
  readonly target = input.required<ReconstructionPart>();
  /** Steht dieselbe Lücke ab 900 px rechts als Hauptknopf? Dann ist der Knopf hier das Duplikat. */
  readonly duplicate = input(false);
  /** Läuft irgendeine Lückensuche (sperrt alle Knöpfe)? */
  readonly searching = input(false);
  /** Läuft die Suche für GENAU diese Lücke? */
  readonly searchingHere = input(false);
  readonly busy = input(false);
  /** Suchtiefe in Halbzügen (zweiseitig: die Seite sucht damit). */
  readonly plies = model(4);
  readonly plyChoices = input<readonly number[]>([]);
  readonly hasProposals = input(false);
  /** Satz zum leeren Ergebnis dieser Lücke (`null` = nichts zu sagen). */
  readonly messageKey = input<string | null>(null);
  readonly result = input<GapResult | null>(null);

  readonly closeGap = output<void>();
  readonly discard = output<void>();
}
