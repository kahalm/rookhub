import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnDestroy, Output, signal } from '@angular/core';
import { ClubClient } from '../../core/club-api.service';
import { RosterPerson } from '../../core/club.models';

/**
 * Spieler suchen beim Korrigieren eines Namens (Wunsch 2026-09-28): standardmäßig unter den Personen der Liga
 * (Meldelisten aller Saisonen), per Häkchen über alle Spieler der Megabase. Ein Treffer bringt Namen und FIDE-ID mit
 * (`picked`); der getippte Text geht laufend nach außen (`textChange`), damit die Seite ihn selbst abgleichen kann.
 */
@Component({
  selector: 'lh-player-search',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="psearch">
      <input [value]="text" (input)="onInput($any($event.target).value)" (keydown.enter)="$event.preventDefault(); enter.emit()"
             [attr.aria-label]="label" [placeholder]="placeholder" maxlength="120" autocomplete="off" />
      <label class="psearch-all"><input type="checkbox" [checked]="all()" (change)="setAll($any($event.target).checked)" />
        Alle Spieler der Megabase durchsuchen</label>
      @if (open() && results().length) {
        <ul class="psearch-results" role="listbox" [attr.aria-label]="'Treffer für ' + label">
          @for (p of results(); track p.name + (p.fide ?? '') + (p.source ?? '')) {
            <li><button type="button" (click)="pick(p)">
              <span class="psearch-name">{{ p.name }}</span>
              <span class="small muted">{{ detail(p) }}</span>
              <span class="badge" [class.ok]="p.league !== false && !p.club" [class.warn]="p.club">{{ tag(p) }}</span>
            </button></li>
          }
        </ul>
      } @else if (open() && searched() && !loading()) {
        <p class="small muted">Keine Treffer{{ all() ? '' : ' in der Liga — Häkchen setzen, um die Megabase zu durchsuchen' }}.</p>
      }
    </div>
  `,
})
export class PlayerSearchComponent implements OnDestroy {
  @Input({ required: true }) client!: ClubClient;
  @Input() text = '';
  @Input() label = 'Spieler';
  @Input() placeholder = 'Nachname, Vorname';
  @Output() textChange = new EventEmitter<string>();
  @Output() picked = new EventEmitter<RosterPerson>();
  @Output() enter = new EventEmitter<void>();

  readonly all = signal(false);
  readonly results = signal<RosterPerson[]>([]);
  readonly loading = signal(false);
  readonly searched = signal(false);
  readonly open = signal(false);
  private timer: ReturnType<typeof setTimeout> | null = null;
  private seq = 0;

  ngOnDestroy(): void {
    if (this.timer) clearTimeout(this.timer);
  }

  onInput(value: string): void {
    this.text = value;
    this.textChange.emit(value);
    this.open.set(true);
    this.schedule();
  }

  setAll(on: boolean): void {
    this.all.set(on);
    this.open.set(true);
    this.schedule(0);
  }

  pick(p: RosterPerson): void {
    this.text = p.name;
    this.open.set(false);
    this.picked.emit(p);
  }

  detail(p: RosterPerson): string {
    const parts: string[] = [];
    if (p.teams.length) parts.push(p.teams.join(', '));
    if (p.fide) parts.push(`FIDE ${p.fide}`);
    if (p.source === 'mega') {
      if (p.games) parts.push(`${p.games} Partien`);
      if (p.lastYear) parts.push(`bis ${p.lastYear}`);
      if (p.maxElo) parts.push(`Elo bis ${p.maxElo}`);
    }
    return parts.join(' · ');
  }

  tag(p: RosterPerson): string {
    if (p.club) return 'Schwaz';
    return p.league === false ? 'Megabase' : 'Ligaspieler';
  }

  private schedule(ms = 300): void {
    if (this.timer) clearTimeout(this.timer);
    const q = this.text.trim();
    this.timer = setTimeout(async () => {
      if (q.length < 2) { this.results.set([]); this.searched.set(false); return; }
      const my = ++this.seq;
      this.loading.set(true);
      try {
        const r = await this.client.players(q, this.all());
        if (my === this.seq) { this.results.set(r); this.searched.set(true); }
      } catch { /* Suche ist Beiwerk — der Name lässt sich trotzdem tippen */ }
      finally { if (my === this.seq) this.loading.set(false); }
    }, ms);
  }
}
