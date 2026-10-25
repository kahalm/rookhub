import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ScanExport } from './scan-export';

/** Knopfzeile „Die geprüfte Partie": PGN herunterladen/kopieren, zu „Meine Partien", „In RookHub öffnen". */
@Component({
  selector: 'lh-scan-export-actions',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [':host { display: contents; }'],
  template: `
    <div class="actions pgn-actions">
      <span class="muted small">Die geprüfte Partie:</span>
      <button type="button" class="btn-sec" (click)="exp().downloadPgn()">PGN herunterladen</button>
      <button type="button" class="btn-sec" (click)="exp().copyPgn()">PGN kopieren</button>
      @if (loggedIn()) {
        <button type="button" class="btn-sec" [disabled]="exp().adding() || !!exp().myGameId()" (click)="exp().addToMyGames()">
          {{ exp().adding() ? 'Speichere …' : 'Zu meinen Partien hinzufügen' }}</button>
      }
      @if (exp().myGameId(); as id) {
        @if (rookHub()) { <a class="small" [href]="rookHub() + '/games/' + id" target="_blank" rel="noopener">In RookHub öffnen</a> }
      }
      <span class="update-msg" [class.err]="exp().pgnMsg()?.err" role="status">{{ exp().pgnMsg()?.text ?? '' }}</span>
    </div>
  `,
})
export class ScanExportActionsComponent {
  readonly exp = input.required<ScanExport>();
  readonly loggedIn = input(false);
  /** Adresse von RookHub für „In RookHub öffnen" — leer = Link entfällt. */
  readonly rookHub = input<string | null | undefined>(null);
}
