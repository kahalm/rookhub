import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '@rh/core/auth.service';
import { LeagueApiService } from '../../core/league-api.service';
import { Broadcast } from '../../core/league.models';

/** „22.08.–29.08.2026" bzw. ein Tag. */
export function broadcastDates(b: Broadcast): string {
  const f = (s: string | null) => s ? `${s.slice(8, 10)}.${s.slice(5, 7)}.` : '';
  if (!b.startsAt) return '';
  const year = b.startsAt.slice(0, 4);
  return !b.endsAt || b.endsAt === b.startsAt ? `${f(b.startsAt)}${year}` : `${f(b.startsAt)}–${f(b.endsAt)}${b.endsAt.slice(0, 4)}`;
}

/** Stand in Worten: fertig, läuft, noch nicht begonnen, Fehler. */
export function broadcastStatus(b: Broadcast, now = new Date()): string {
  if (b.error) return `Fehler: ${b.error}`;
  const games = `${b.games} ${b.games === 1 ? 'Partie' : 'Partien'} mit Ligaspielern`;
  if (b.finished) return `fertig — ${games}`;
  if (!b.importedAt) return b.startsAt && new Date(b.startsAt) > now ? 'beginnt noch' : 'wird demnächst eingespielt';
  return `läuft — ${games}, alle 6 Stunden nachgeholt`;
}

/** Filter der Liste: „nur mit Ligaspielern" (mindestens eine Partie) und Suche in Name und Ort, ohne Groß/klein. */
export function filterBroadcasts(items: Broadcast[], onlyLeague: boolean, query: string): Broadcast[] {
  const q = query.trim().toLowerCase();
  return items.filter(b => (!onlyLeague || b.games > 0)
    && (!q || b.name.toLowerCase().includes(q) || (b.location ?? '').toLowerCase().includes(q)));
}

/** Absage beim Hinzufügen als Satz. */
export function broadcastErrorText(reason: string | undefined): string {
  switch (reason) {
    case 'invalidUrl': return 'Das ist kein Link auf eine Lichess-Übertragung (lichess.org/broadcast/…).';
    case 'notFound': return 'Diese Übertragung gibt es auf Lichess nicht (mehr).';
    case 'rateLimited': return 'Lichess bremst gerade — bitte in ein paar Minuten nochmal.';
    default: return 'Lichess ist gerade nicht erreichbar.';
  }
}

/**
 * Lichess-Übertragungen (0.608.0, Wunsch 2026-09-30): welche Turniere am Brett in die Spielerkarten eingespielt werden.
 * LeagueHub findet österreichische Übertragungen selbst (Lichess-Suche, täglich); ein Turnier im Ausland mit Tiroler Spielern
 * fügt ein Verwalter per Link hinzu. Zugeordnet wird über die FIDE-ID in den Partien.
 */
@Component({
  selector: 'lh-broadcasts-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (!allowed) {
      <section class="gate">
        <h2>Nicht freigeschaltet</h2>
        <p>Die Übertragungen verwalten nur Liga-Verwalter.</p>
      </section>
    } @else {
      <section class="bc-page">
        <h2>Lichess-Übertragungen</h2>
        <p class="muted">Turniere am Brett, die auf Lichess live übertragen werden, landen in den Spielerkarten — zugeordnet über
          die FIDE-ID in jeder Partie. Österreichische Übertragungen findet LeagueHub selbst; ein Turnier im Ausland mit Tiroler
          Spielern fügst du hier per Link hinzu.</p>
        <form class="bc-add" (submit)="$event.preventDefault(); add()">
          <label class="field">Link zur Übertragung (Turnier oder Runde)
            <input type="url" autocomplete="off" placeholder="https://lichess.org/broadcast/…" [value]="link()"
                   (input)="link.set($any($event.target).value)" />
          </label>
          <button type="submit" class="btn-pri" [disabled]="adding() || !link().trim()">{{ adding() ? 'Spielt ein …' : 'Hinzufügen und einspielen' }}</button>
        </form>
        @if (note(); as n) { <p class="small" [class.err]="n.err" role="status">{{ n.text }}</p> }
        @if (error()) { <p class="err">{{ error() }}</p> }
        @if (loading() && !items().length) { <p class="muted">Lade …</p> }
        <!-- UI-Sweep 2026-10-10 (l-broadcasts): kompakte Zeilen statt hoher Karten, oben Suche und „nur mit Ligaspielern" (an) -->
        @if (items().length) {
          <div class="bc-tools">
            <label class="bc-only"><input type="checkbox" [checked]="onlyLeague()" (change)="onlyLeague.set($any($event.target).checked)" />
              nur mit Ligaspielern</label>
            <input type="search" class="bc-search" placeholder="Suchen …" aria-label="Übertragungen suchen" [value]="query()"
                   (input)="query.set($any($event.target).value)" />
            @if (hiddenCount()) { <span class="small muted">{{ hiddenCount() }} ausgeblendet</span> }
          </div>
        }
        <div class="roster-scroll"><table class="rtable bc-list">
          @if (shown().length) {
            <thead><tr><th>Übertragung</th><th>Zeitraum</th><th class="hide-s">Ort</th><th class="num">Partien</th></tr></thead>
          }
          <tbody>
            @for (b of shown(); track b.tourId) {
              <tr [attr.title]="status(b)">
                <td><a [href]="b.url" target="_blank" rel="noopener">{{ b.name }}</a>
                  @if (b.manual) { <span class="tag">per Link</span> }
                  @if (b.error || !b.finished) { <span class="bc-state small" [class.err]="!!b.error">{{ status(b) }}</span> }</td>
                <td class="muted nowrap">{{ dates(b) }}</td>
                <td class="muted hide-s">{{ b.location }}</td>
                <td class="num">{{ b.games }}</td>
              </tr>
            } @empty {
              @if (!loading() && !error()) {
                <tr><td class="muted" colspan="4">{{ items().length ? 'Keine Übertragung passt zur Auswahl.'
                  : 'Noch keine Übertragung vorgemerkt — die Suche läuft im Hintergrund.' }}</td></tr>
              }
            }
          </tbody>
        </table></div>
      </section>
    }
  `,
})
export class BroadcastsPageComponent implements OnInit {
  private readonly api = inject(LeagueApiService);
  private readonly auth = inject(AuthService);

  readonly allowed = this.auth.has('league.manage');
  readonly items = signal<Broadcast[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly link = signal('');
  readonly adding = signal(false);
  readonly note = signal<{ text: string; err: boolean } | null>(null);
  /** Vorgabe an: die vielen „0 Partien mit Ligaspielern" verdeckten die, um die es geht. */
  readonly onlyLeague = signal(true);
  readonly query = signal('');
  readonly shown = computed(() => filterBroadcasts(this.items(), this.onlyLeague(), this.query()));
  readonly hiddenCount = computed(() => this.items().length - this.shown().length);
  readonly dates = broadcastDates;
  readonly status = (b: Broadcast) => broadcastStatus(b);

  ngOnInit(): void {
    if (this.allowed) void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.items.set(await this.api.broadcasts());
    } catch {
      this.error.set('Die Übertragungen konnten nicht geladen werden.');
    } finally {
      this.loading.set(false);
    }
  }

  async add(): Promise<void> {
    this.adding.set(true);
    this.note.set(null);
    try {
      const r = await this.api.addBroadcast(this.link().trim());
      this.note.set({ text: `„${r.name}“ eingespielt: ${r.games} ${r.games === 1 ? 'Partie' : 'Partien'} mit Ligaspielern.`, err: false });
      this.link.set('');
      await this.load();
    } catch (err) {
      const reason = err instanceof HttpErrorResponse ? err.error?.reason : undefined;
      this.note.set({ text: broadcastErrorText(reason), err: true });
    } finally {
      this.adding.set(false);
    }
  }
}
