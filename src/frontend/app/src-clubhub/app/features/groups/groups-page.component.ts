import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { hasClubAccess, isClubManager } from '../../core/club-access';
import { ClubApiService, apiErrorText } from '../../core/club-api.service';
import { GroupInput, GroupRow } from '../../core/club.models';
import { WEEKDAYS, shortDate, weekdayName } from '../../core/club-format';

/** „Freitag, 17:00 Vereinsheim" — Trainingstag und freier Text, was davon da ist. */
export function scheduleText(g: Pick<GroupRow, 'weekday' | 'schedule'>): string {
  return [weekdayName(g.weekday), g.schedule?.trim()].filter(Boolean).join(', ');
}

/**
 * Die Trainingsgruppen: je Gruppe Trainingstag, Kinderzahl, Trainer, letzte Einheit — und der Knopf zur Anwesenheitsliste.
 * Ein Trainer sieht die Gruppen, denen er zugeteilt ist; anlegen darf nur die Leitung (`club.manage`).
 */
@Component({
  selector: 'ch-groups-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    @if (!allowed) {
      <section class="gate"><h1>Nicht freigeschaltet</h1><p>Die Gruppen sehen die Trainer und die Leitung des Vereins.</p></section>
    } @else {
      <div class="page-head">
        <h1>Gruppen</h1>
        @if (manager && !adding()) { <button type="button" class="btn primary add-group" (click)="adding.set(true)">Gruppe anlegen</button> }
      </div>
      @if (adding()) {
        <form class="sheet form new-group" (submit)="$event.preventDefault(); create()">
          <div class="grid-2">
            <label class="field"><span>Name der Gruppe</span>
              <input name="name" autocomplete="off" maxlength="80" placeholder="z. B. Anfänger" [value]="form().name" (input)="patch({ name: $any($event.target).value })"></label>
            <label class="field"><span>Trainingstag</span>
              <select name="weekday" (change)="patch({ weekday: +$any($event.target).value || null })">
                <option value="">kein fester Tag</option>
                @for (d of weekdays; track $index) { <option [value]="$index + 1" [selected]="form().weekday === $index + 1">{{ d }}</option> }
              </select></label>
          </div>
          <label class="field"><span>Zeit und Ort</span>
            <input name="schedule" autocomplete="off" maxlength="200" placeholder="z. B. 17:00–18:30, Vereinsheim" [value]="form().schedule ?? ''" (input)="patch({ schedule: $any($event.target).value })"></label>
          <div class="actions pb">
            <button type="submit" class="btn primary" [disabled]="busy()">Gruppe anlegen</button>
            <button type="button" class="btn" [disabled]="busy()" (click)="adding.set(false)">Abbrechen</button>
          </div>
        </form>
      }
      <p class="err status-line" role="alert">{{ error() ?? '' }}</p>
      @if (loading() && !groups().length) { <p class="muted">Lade …</p> }
      @if (!loading() && !error() && !groups().length && !adding()) {
        <div class="empty">
          @if (manager) { <p>Noch keine Gruppe. Leg die erste an — mit Trainingstag, dann wartet an dem Tag die Anwesenheitsliste.</p> }
          @else { <p>Du bist noch keiner Gruppe als Trainer zugeteilt. Das macht die Leitung.</p> }
        </div>
      }
      <ul class="groups">
        @for (g of groups(); track g.id) {
          <li class="group" [class.archived]="g.archived">
            <div>
              <h2><a [routerLink]="['/gruppen', g.id]">{{ g.name }}</a></h2>
              <p class="kid-meta">
                @if (schedule(g); as s) { <span>{{ s }}</span> }
                <span>{{ g.memberCount }} {{ g.memberCount === 1 ? 'Kind' : 'Kinder' }}</span>
                @if (g.lastSession) { <span>zuletzt {{ short(g.lastSession) }}</span> }
                @if (g.trainers.length) { <span>Zugriff: {{ trainerNames(g) }}</span> }
                @if (g.archived) { <span class="chip">im Archiv</span> }
              </p>
            </div>
            @if (!g.archived) { <a class="btn" [routerLink]="['/gruppen', g.id, 'anwesenheit']">Anwesenheit abhaken</a> }
          </li>
        }
      </ul>
    }
  `,
})
export class GroupsPageComponent implements OnInit {
  private readonly api = inject(ClubApiService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly allowed = hasClubAccess(this.auth);
  readonly manager = isClubManager(this.auth);
  readonly groups = signal<GroupRow[]>([]);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly adding = signal(false);
  readonly form = signal<GroupInput>({ name: '', schedule: null, weekday: null, archived: false });

  readonly weekdays = WEEKDAYS;
  readonly schedule = scheduleText;
  readonly short = shortDate;

  ngOnInit(): void {
    if (this.allowed) void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.groups.set(await this.api.groups());
    } catch {
      this.error.set('Die Gruppen konnten nicht geladen werden.');
    } finally {
      this.loading.set(false);
    }
  }

  patch(change: Partial<GroupInput>): void {
    this.form.update(f => ({ ...f, ...change }));
  }

  trainerNames(g: GroupRow): string {
    return g.trainers.map(t => t.username).join(', ');
  }

  async create(): Promise<void> {
    const f = this.form();
    if (!f.name.trim()) {
      this.error.set('Die Gruppe braucht einen Namen.');
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      const g = await this.api.createGroup({ ...f, name: f.name.trim(), schedule: f.schedule?.trim() || null });
      void this.router.navigate(['/gruppen', g.id]);
    } catch (err) {
      this.error.set(apiErrorText(err, 'Die Gruppe konnte nicht angelegt werden.'));
    } finally {
      this.busy.set(false);
    }
  }
}
