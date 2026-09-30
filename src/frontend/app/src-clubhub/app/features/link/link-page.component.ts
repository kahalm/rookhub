import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { ClubApiService, apiErrorText } from '../../core/club-api.service';
import { LinkState } from '../../core/club.models';

/**
 * „Konto verknüpfen": hier löst ein KIND (bzw. seine Eltern) den Code ein, den der Trainer ausgegeben hat — angemeldet mit
 * dem eigenen RookHub-/KidHub-Konto. Das Einlösen ist die Einwilligung, dass der Trainer sieht, wie viel das Konto
 * trainiert; deshalb geht es nur von dieser Seite aus und lässt sich hier jederzeit wieder trennen. Die Seite braucht
 * kein Club-Recht und zeigt nichts aus der Kartei außer dem eigenen Vornamen.
 */
@Component({
  selector: 'ch-link-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="sheet">
      <header class="sheet-head"><h1>Konto verknüpfen</h1></header>
      @if (state(); as s) {
        @if (s.linked) {
          <section>
            <p class="linked">Dein Konto ist mit dem Karteiblatt von <b>{{ s.firstName }}</b> verknüpft.</p>
            <p class="muted">Deine Trainer sehen damit, wie viel du in RookHub und KidHub trainierst: Trainingsminuten, gelöste
              Puzzles, geschaffte Stufen. Einzelne Aufgaben oder Partien sehen sie nicht.</p>
            <button type="button" class="btn danger" [disabled]="busy()" (click)="unlink()">Verknüpfung trennen</button>
          </section>
        } @else {
          <form class="form" (submit)="$event.preventDefault(); redeem()">
            <p>Gib den Code ein, den du von deinem Trainer bekommen hast. Danach sieht er, wie viel du in RookHub und KidHub
              trainierst.</p>
            <label class="field"><span>Code</span>
              <input name="code" class="code-input" autocomplete="off" autocapitalize="characters" spellcheck="false" maxlength="32"
                placeholder="ABCDE-FGHJK" [value]="code()" (input)="code.set($any($event.target).value)"></label>
            <div class="actions pb">
              <button type="submit" class="btn primary" [disabled]="busy() || !code().trim()">Konto verknüpfen</button>
            </div>
          </form>
        }
      } @else if (!error()) {
        <p class="muted">Lade …</p>
      }
      <p class="err status-line" role="alert">{{ error() ?? '' }}</p>
    </section>
  `,
})
export class LinkPageComponent implements OnInit {
  private readonly api = inject(ClubApiService);
  private readonly route = inject(ActivatedRoute);

  readonly state = signal<LinkState | null>(null);
  readonly code = signal('');
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.code.set(this.route.snapshot.queryParamMap.get('code') ?? '');
    void this.run(async () => this.state.set(await this.api.linkState()), 'Der Stand konnte nicht geladen werden.');
  }

  async redeem(): Promise<void> {
    const code = this.code().trim();
    if (!code || this.busy()) return;
    await this.run(async () => this.state.set(await this.api.redeem(code)), 'Verknüpfen hat nicht geklappt.');
  }

  async unlink(): Promise<void> {
    if (!confirm('Die Verknüpfung trennen? Deine Trainer sehen deinen Trainingsstand dann nicht mehr.')) return;
    await this.run(async () => {
      await this.api.selfUnlink();
      this.state.set({ linked: false });
      this.code.set('');
    }, 'Trennen hat nicht geklappt.');
  }

  private async run(action: () => Promise<void>, fallback: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
    } catch (err) {
      this.error.set(apiErrorText(err, fallback));
    } finally {
      this.busy.set(false);
    }
  }
}
