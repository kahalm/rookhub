import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { LocaleService } from '../../core/locale.service';

/** Anzeigename einer Sprache: die Eigenbezeichnung aus der Sprachauswahl der App, sonst das Kürzel. */
export function languageName(code: string | null | undefined, locale: LocaleService): string {
  if (!code) return '';
  return locale.languages.find(l => l.code === code)?.label ?? code.toUpperCase();
}

/**
 * Sprachwahl der Kurs-Kommentare (Stufe C, 0.549.0) — ein kleiner Menü-Knopf für die Kursseite und
 * die Köpfe von Solver, Durchsehen und Kalkulation. Unsichtbar, solange der Kurs nur EINE Sprache hat.
 *
 * Die Quelle (erste Sprache der Liste) heißt „Original"; die Namen kommen aus der Sprachauswahl der
 * App (`LocaleService.languages`). Die Komponente merkt sich nichts — Speichern und Neuladen macht
 * die Seite im `picked`-Handler (über `CourseLanguageService`).
 */
@Component({
  selector: 'app-course-lang-picker',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule, TranslatePipe],
  template: `
    @if (languages().length > 1) {
      <button mat-button type="button" class="clp-btn" [matMenuTriggerFor]="langMenu"
              [matTooltip]="'courses.lang.label' | translate"
              [attr.aria-label]="'courses.lang.label' | translate">
        <mat-icon>translate</mat-icon>
        <span class="clp-current">{{ isSource(active()) ? ('courses.lang.original' | translate) : name(active()) }}</span>
        <mat-icon class="clp-caret">arrow_drop_down</mat-icon>
      </button>
      <mat-menu #langMenu="matMenu">
        @for (code of languages(); track code) {
          <button mat-menu-item type="button" [class.clp-active]="code === active()" (click)="pick(code)">
            @if (code === active()) { <mat-icon>check</mat-icon> } @else { <mat-icon></mat-icon> }
            @if (isSource(code)) {
              <span>{{ 'courses.lang.original' | translate }}@if (sourceName()) { <span class="clp-sub"> · {{ sourceName() }}</span> }</span>
            } @else {
              <span>{{ name(code) }}</span>
            }
          </button>
        }
      </mat-menu>
    }
  `,
  styles: [`
    :host { display: inline-flex; }
    .clp-btn { min-width: 0; padding: 0 6px; }
    .clp-btn mat-icon { margin-right: 4px; }
    .clp-caret { margin: 0 -4px 0 0 !important; }
    .clp-current { max-width: 12em; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .clp-sub { color: color-mix(in srgb, currentColor 55%, transparent); }
    .clp-active { font-weight: 600; }
  `],
})
export class CourseLangPickerComponent {
  private readonly locale = inject(LocaleService);

  /** Sprachen des Kurses, die QUELLE zuerst. */
  readonly languages = input<readonly string[]>([]);
  /** Die wirksame Sprache (Kürzel; die Quelle = „Original"). */
  readonly value = input<string | null>(null);
  readonly picked = output<string>();

  readonly active = computed(() => {
    const langs = this.languages();
    const v = this.value();
    return v && langs.includes(v) ? v : (langs[0] ?? null);
  });

  /** Name der Quelle hinter „Original" — nicht bei `und` (nicht bestimmbar). */
  readonly sourceName = computed(() => {
    const src = this.languages()[0];
    return src && src !== 'und' ? this.name(src) : '';
  });

  isSource(code: string | null): boolean {
    return !!code && code === this.languages()[0];
  }

  name(code: string | null): string {
    return languageName(code, this.locale);
  }

  pick(code: string): void {
    if (code !== this.active()) this.picked.emit(code);
  }
}

/**
 * „maschinell übersetzt" im Kommentar — ein Klick schaltet den Kurs aufs Original. Ohne bekannte
 * Quelle (`clickable=false`) nur Text.
 */
@Component({
  selector: 'app-machine-note',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconModule, TranslatePipe],
  template: `
    @if (clickable()) {
      <button type="button" class="mn" (click)="original.emit()"
              [attr.title]="'courses.lang.machineHint' | translate"
              [attr.aria-label]="'courses.lang.machineHint' | translate">
        <mat-icon inline>translate</mat-icon> {{ 'courses.lang.machine' | translate }}
      </button>
    } @else {
      <span class="mn mn--static"><mat-icon inline>translate</mat-icon> {{ 'courses.lang.machine' | translate }}</span>
    }
  `,
  styles: [`
    :host { display: inline-block; }
    .mn {
      font: inherit; font-size: 0.78em; font-style: italic; background: none; border: 0; padding: 0;
      color: color-mix(in srgb, currentColor 60%, transparent); cursor: pointer;
      display: inline-flex; align-items: center; gap: 3px;
    }
    button.mn:hover { text-decoration: underline; }
    .mn--static { cursor: default; }
  `],
})
export class MachineNoteComponent {
  readonly clickable = input(true);
  readonly original = output<void>();
}
