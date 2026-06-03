import { Component, Input, Output, EventEmitter } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

/**
 * Wiederverwendbare Vor/Zurück-Navigation für die Lösungs-Durchsicht
 * (Standard-, Buch- und Endless-Puzzle). Reine Darstellung: die Eltern-Komponente
 * hält Index/Gesamtzahl und führt beim `prev`/`next`-Event ihre eigene Logik aus
 * (Timer stoppen, Brett aufbauen, …). Die Disabled-Logik (erster/letzter Schritt)
 * ist identisch über alle Modi und daher hier gekapselt.
 */
@Component({
  selector: 'app-review-nav',
  standalone: true,
  imports: [MatButtonModule, MatIconModule],
  template: `
    <div class="review-nav">
      <button mat-icon-button (click)="prev.emit()" [disabled]="currentIndex === 0"><mat-icon>chevron_left</mat-icon></button>
      <span class="review-counter">{{ currentIndex }} / {{ totalSteps }}</span>
      <button mat-icon-button (click)="next.emit()" [disabled]="currentIndex >= totalSteps"><mat-icon>chevron_right</mat-icon></button>
    </div>
  `,
  styles: [`
    .review-nav { display: flex; align-items: center; gap: 0.5rem; }
    .review-counter { font-variant-numeric: tabular-nums; min-width: 56px; text-align: center; }
  `],
})
export class ReviewNavComponent {
  @Input() currentIndex = 0;
  @Input() totalSteps = 0;
  @Output() prev = new EventEmitter<void>();
  @Output() next = new EventEmitter<void>();
}
