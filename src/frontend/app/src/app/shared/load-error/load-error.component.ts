import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';

/**
 * Ladefehler einer Seite/Liste: Meldung + „Erneut versuchen". Steht dort, wo sonst der Leerzustand
 * („noch keine …") stuende — ein gescheitertes Laden darf nicht wie „es gibt nichts" aussehen.
 */
@Component({
  selector: 'app-load-error',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <div class="load-error" role="alert">
      <mat-icon class="icon">cloud_off</mat-icon>
      <span class="msg">{{ messageKey | translate }}</span>
      <button mat-stroked-button type="button" class="retry" (click)="retry.emit()">
        <mat-icon>refresh</mat-icon> {{ 'common.retry' | translate }}
      </button>
    </div>
  `,
  styles: [`
    .load-error { display: flex; align-items: center; flex-wrap: wrap; gap: 8px 12px; padding: 16px 0; }
    .icon { opacity: 0.6; }
    .msg { flex: 1 1 200px; }
  `]
})
export class LoadErrorComponent {
  /** i18n-Schluessel der Meldung. */
  @Input() messageKey = 'common.loadFailed';
  @Output() retry = new EventEmitter<void>();
}
