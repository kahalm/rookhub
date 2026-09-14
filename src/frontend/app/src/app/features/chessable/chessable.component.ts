import { Component, ChangeDetectionStrategy } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';

/** Store-Seiten der RepCheck-Erweiterung. Firefox bewusst ohne Sprachkürzel im Pfad — AMO leitet
 *  auf die Sprache des Browsers um (ein festes `/de/` zeigte auch englischen Nutzern Deutsch). */
export const REPCHECK_CHROME_URL = 'https://chromewebstore.google.com/detail/mhddbldcaancdahlochjanpkkboaccpn';
export const REPCHECK_FIREFOX_URL = 'https://addons.mozilla.org/firefox/addon/repcheck/';

/**
 * `/chessable`: der Import über RookHub (Bearer hinterlegen, Kurse serverseitig holen) ist
 * abgeschaltet — auf PROD seit 2026-09-09 per `Chessable:Enabled=false`. Die Seite verweist nur
 * noch auf die RepCheck-Erweiterung und sagt kurz, warum. Der alte Import-Bildschirm liegt in der
 * Git-Historie (bis v0.477.1).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-chessable',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <div class="container">
      <div class="extension-note" role="note">
        <mat-icon class="note-icon">extension</mat-icon>
        <div class="note-text">
          <h1>{{ 'chessable.useExtension.title' | translate }}</h1>
          <p>{{ 'chessable.useExtension.body' | translate }}</p>
          <div class="store-links">
            <a mat-stroked-button [href]="chromeUrl" target="_blank" rel="noopener noreferrer">
              <mat-icon>open_in_new</mat-icon> Chrome
            </a>
            <a mat-stroked-button [href]="firefoxUrl" target="_blank" rel="noopener noreferrer">
              <mat-icon>open_in_new</mat-icon> Firefox
            </a>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .container { max-width: 760px; margin: 0 auto; padding: 1rem; }
    .extension-note {
      display: flex; align-items: flex-start; gap: 0.75rem;
      border: 1px solid var(--mat-sys-outline-variant, #d0d0d8);
      border-left: 5px solid var(--mat-sys-primary, #3f51b5); border-radius: 6px;
      padding: 1rem 1.1rem; margin-top: 0.75rem;
    }
    .note-icon { color: var(--mat-sys-primary, #3f51b5); flex: 0 0 auto; margin-top: 4px; }
    .note-text { flex: 1 1 auto; min-width: 0; }
    .note-text h1 { font-size: 1.35rem; margin: 0 0 0.5rem; }
    .note-text p { margin: 0 0 1rem; line-height: 1.5; }
    .store-links { display: flex; flex-wrap: wrap; gap: 0.5rem; }
  `]
})
export class ChessableComponent {
  readonly chromeUrl = REPCHECK_CHROME_URL;
  readonly firefoxUrl = REPCHECK_FIREFOX_URL;
}
