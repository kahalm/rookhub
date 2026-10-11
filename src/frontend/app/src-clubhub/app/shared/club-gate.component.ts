import { ChangeDetectionStrategy, Component, OnInit, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LinkStateStore } from '../core/link-state.store';

/**
 * UI-Sweep 2026-10-11 (r2-c-gate-deadend): EINE Sperrkarte für alle Seiten, die ein Konto ohne Club-Recht nicht sieht
 * (Kartei, Gruppen, Gruppe, Anwesenheit, Karteiblatt). Vorher stand dort je Seite ein eigener Satz ohne Ausweg; jetzt
 * derselbe Text je nach Verknüpfungsstand (r2-c-start-linked) und ein Knopf zur Startseite (auf der Startseite selbst
 * nicht, `start = false`). Nur `area` sagt, worum es auf der Seite geht.
 */
@Component({
  selector: 'ch-gate',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    @if (store.state(); as s) {
      @if (s.linked) {
        <section class="gate linked-gate">
          <h1>Hallo{{ s.firstName ? ' ' + s.firstName : '' }}!</h1>
          <p>Dein Konto ist mit dem Karteiblatt von <b>{{ s.firstName }}</b> verknüpft. Deine Trainer sehen, wie viel du in
            RookHub und KidHub trainierst – einzelne Aufgaben oder Partien nicht.</p>
          <p class="muted">{{ area() }}</p>
          <p class="gate-actions">
            @if (start()) { <a class="btn primary" routerLink="/">Zur Startseite</a> }
            <a class="btn" routerLink="/verknuepfen">Verknüpfung ansehen</a>
          </p>
        </section>
      } @else {
        <section class="gate">
          <h1>Nicht freigeschaltet</h1>
          <p>{{ area() }} Wenn du Trainer bist, bitte die Vereinsleitung um Zugang.</p>
          <p>Hast du von deinem Trainer einen Code bekommen? Dann verknüpfe hier dein Konto.</p>
          <p class="gate-actions">
            @if (start()) { <a class="btn primary" routerLink="/">Zur Startseite</a> }
            <a class="btn" [class.primary]="!start()" routerLink="/verknuepfen">Konto verknüpfen</a>
          </p>
        </section>
      }
    } @else {
      <p class="muted">Lade …</p>
    }
  `,
})
export class ClubGateComponent implements OnInit {
  readonly store = inject(LinkStateStore);
  /** Wer die Seite sieht, z. B. „Die Gruppen sehen nur Trainer und die Vereinsleitung." */
  readonly area = input.required<string>();
  /** Knopf „Zur Startseite" (auf der Startseite selbst aus). */
  readonly start = input(true);

  ngOnInit(): void {
    void this.store.ensure();
  }
}
