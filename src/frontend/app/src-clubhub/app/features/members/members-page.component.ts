import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { hasClubAccess } from '../../core/club-access';
import { ClubApiService } from '../../core/club-api.service';
import { GroupRow, MemberRow } from '../../core/club.models';
import { ageClass, byInitial, firstPhone, telHref, trainsToday, weekdayName } from '../../core/club-format';

/** Namenssuche über Vor- und Nachname, ohne Groß/klein und ohne Akzente („saric" findet „Šarić"). */
export function matchesName(m: Pick<MemberRow, 'firstName' | 'lastName'>, q: string): boolean {
  const fold = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
  const term = fold(q.trim());
  return !term || fold(`${m.firstName} ${m.lastName}`).includes(term) || fold(`${m.lastName} ${m.firstName}`).includes(term);
}

/**
 * Die Kartei (Wunsch 2026-09-30): alle Kinder nach Nachnamen in Registern, jede Zeile mit der ersten Telefonnummer samt
 * Hinweis („Mutter Daniela") zum Antippen — die Liste ist zugleich die Telefonliste. Oben der Textmarker-Streifen, wenn
 * heute eine Gruppe trainiert: ein Tipp zur Anwesenheitsliste. Gesucht und gefiltert wird im Browser; die Kartei eines
 * Vereins ist klein genug, um sie ganz zu laden.
 */
@Component({
  selector: 'ch-members-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    @if (!allowed) {
      <section class="gate">
        <h1>Nicht freigeschaltet</h1>
        <p>ClubHub sehen die Trainer und die Leitung des Vereins. Wenn du dazugehörst, lass dir die Rolle geben.</p>
        <p>Hast du von deinem Trainer einen Code bekommen? <a routerLink="/verknuepfen">Konto verknüpfen</a></p>
      </section>
    } @else {
      @for (g of today(); track g.id) {
        <p class="today" role="status">
          <strong>Heute ist {{ weekday(g.weekday) }}: {{ g.name }} trainiert.</strong>
          <a class="btn" [routerLink]="['/gruppen', g.id, 'anwesenheit']">Anwesenheit abhaken</a>
        </p>
      }
      <div class="page-head">
        <h1>Kartei</h1>
        <a class="btn primary" routerLink="/kind/neu">Kind anlegen</a>
      </div>
      <div class="tools">
        <label class="field search"><span>Suchen</span>
          <input type="search" placeholder="Name" [value]="q()" (input)="q.set($any($event.target).value)"></label>
        <label class="field"><span>Gruppe</span>
          <select (change)="groupId.set(+$any($event.target).value || null)">
            <option value="">alle</option>
            @for (g of groups(); track g.id) { <option [value]="g.id" [selected]="g.id === groupId()">{{ g.name }}</option> }
          </select></label>
        <label class="check"><input type="checkbox" [checked]="archived()" (change)="toggleArchived($any($event.target).checked)"> Archiv</label>
      </div>
      @if (error()) { <p class="err" role="alert">{{ error() }}</p> }
      @if (loading() && !rows().length) { <p class="muted">Lade …</p> }
      @if (!loading() && !error() && !rows().length) {
        <div class="empty">
          @if (archived()) { <p>Im Archiv liegt kein Blatt.</p> }
          @else {
            <p>Noch kein Kind in der Kartei.</p>
            <a class="btn primary" routerLink="/kind/neu">Erstes Kind anlegen</a>
          }
        </div>
      }
      @if (rows().length) {
        <p class="count">{{ countText() }}</p>
        @for (r of registers(); track r.letter) {
          <section class="register">
            <h2 class="letter" aria-hidden="true">{{ r.letter }}</h2>
            <ul class="cards">
              @for (m of r.items; track m.id) {
                <li class="kid">
                  <a class="kid-main" [routerLink]="['/kind', m.id]">
                    <span class="kid-name"><b>{{ m.lastName }}</b> {{ m.firstName }}</span>
                    <span class="kid-meta">
                      @if (m.birthYear) { <span>Jg. {{ m.birthYear }}@if (cls(m); as c) { ({{ c }}) }</span> }
                      @if (m.level) { <span>{{ m.level }}</span> }
                      @for (g of m.groups; track g.id) { <span class="chip">{{ g.name }}</span> }
                    </span>
                  </a>
                  @if (phone(m); as p) {
                    <div class="kid-call">
                      <a [href]="tel(p.value)">{{ p.value }}</a>
                      @if (p.label) { <span>{{ p.label }}</span> }
                    </div>
                  }
                </li>
              }
            </ul>
          </section>
        }
        @if (!shown().length) { <p class="muted">Kein Kind passt zu dieser Suche.</p> }
      }
    }
  `,
})
export class MembersPageComponent implements OnInit {
  private readonly api = inject(ClubApiService);
  private readonly auth = inject(AuthService);

  readonly allowed = hasClubAccess(this.auth);
  readonly rows = signal<MemberRow[]>([]);
  readonly groups = signal<GroupRow[]>([]);
  readonly q = signal('');
  readonly groupId = signal<number | null>(null);
  readonly archived = signal(false);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  /** „Heute" — als Feld, damit ein Test den Wochentag festlegen kann. */
  now: () => Date = () => new Date();

  readonly shown = computed(() => {
    const gid = this.groupId();
    return this.rows().filter(m => matchesName(m, this.q()) && (gid === null || m.groups.some(g => g.id === gid)));
  });
  readonly registers = computed(() => byInitial(this.shown()));
  readonly today = computed(() => this.groups().filter(g => !g.archived && trainsToday(g.weekday, this.now())));
  readonly countText = computed(() => {
    const [n, all] = [this.shown().length, this.rows().length];
    return n === all ? `${all} ${all === 1 ? 'Kind' : 'Kinder'}` : `${n} von ${all} ${all === 1 ? 'Kind' : 'Kindern'}`;
  });

  readonly weekday = weekdayName;
  readonly tel = telHref;

  ngOnInit(): void {
    if (this.allowed) void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const [rows, groups] = await Promise.all([this.api.members(this.archived()), this.api.groups()]);
      this.rows.set(rows);
      this.groups.set(groups);
    } catch {
      this.error.set('Die Kartei konnte nicht geladen werden.');
    } finally {
      this.loading.set(false);
    }
  }

  toggleArchived(on: boolean): void {
    this.archived.set(on);
    void this.load();
  }

  cls(m: MemberRow): string | null {
    return ageClass(m.birthYear, this.now().getFullYear());
  }

  phone(m: MemberRow) {
    return firstPhone(m.contacts);
  }
}
