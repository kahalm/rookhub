import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';
import { HandoffService } from '@rh/core/handoff.service';

/**
 * Ein Link auf eine Seite in RookHub — dort, wo die Turnierseite auf RookHub verweist („Passwort
 * aendern", „Freunde hinzufuegen", „die uebrige Verwaltung"). Bis dahin stand an diesen Stellen nur
 * der Satz, und wer RookHub nicht kannte, fand nicht hin (Codereview UX-079).
 *
 * <p>Ein echter Link (`href`, Strg/Mittelklick oeffnet einen neuen Tab); der gewoehnliche Klick
 * springt ueber {@link HandoffService.jumpToAccountHome} und nimmt die Anmeldung mit (Einmal-Code),
 * wie „Zu RookHub" in der Kopfzeile. Ohne bekannte RookHub-Adresse (Zugriff ueber IP oder
 * localhost) faellt der Link weg — ein geratener waere schlimmer als keiner, und die Saetze, an
 * denen er steht, nennen RookHub schon.</p>
 */
@Component({
  selector: 'trn-rookhub-link',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Ohne Adresse ganz weg statt toter Text: die Saetze drumherum nennen RookHub ohnehin.
  host: { '[hidden]': '!href' },
  template: `<a class="rh-link" [attr.href]="href" (click)="open($event)"><ng-content /></a>`,
  styles: [`
    /* Inline-Padding vergroessert die Trefferflaeche am Handy, ohne die Zeilenhoehe zu aendern. */
    .rh-link { padding: 6px 0; color: var(--mat-sys-primary); text-decoration: underline; text-underline-offset: 2px; }
  `],
})
export class RookHubLinkComponent {
  private readonly handoff = inject(HandoffService);

  /** Pfad in RookHub, ohne fuehrenden Schraegstrich (z. B. `profile`, `friends`). */
  @Input({ required: true }) path!: string;

  get href(): string | null {
    const base = this.handoff.accountHomeUrl;
    return base ? `${base}/${this.path}` : null;
  }

  open(e: MouseEvent): void {
    if (!this.href || e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;
    e.preventDefault();
    void this.handoff.jumpToAccountHome(this.path);
  }
}
