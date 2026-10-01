import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';
import { firstValueFrom } from 'rxjs';
import { hasClubAccess } from '../../core/club-access';
import { ClubApiService, apiErrorText } from '../../core/club-api.service';
import { Group, GroupInput, MemberRow, Status } from '../../core/club.models';
import { WEEKDAYS, longDate, nameHead, nameTail, shortDate } from '../../core/club-format';
import { scheduleText } from './groups-page.component';
import { SessionPhotosComponent } from '../../shared/session-photos.component';

/** Zeichen einer Zelle der Anwesenheitstabelle: ✓ da, – gefehlt, · nicht erfasst. */
export function statusMark(status: Status | null): string {
  return status === 'present' ? '✓' : status === 'absent' ? '–' : '·';
}

/**
 * Eine Trainingsgruppe: die Anwesenheitstabelle (Kinder × die jüngsten Einheiten, älteste links), darunter das
 * Trainingstagebuch (je Einheit Thema und „was wurde gemacht", neueste zuerst), der Weg zur Anwesenheitsliste, Kinder
 * dazunehmen oder herausnehmen. Die Leitung ändert hier außerdem die Gruppe selbst und teilt
 * Trainer zu — über den Benutzernamen; ClubHub öffnet sich für das Konto erst mit einer Rolle, die `club.trainer` trägt.
 */
@Component({
  selector: 'ch-group-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, SessionPhotosComponent],
  template: `
    @if (!allowed) {
      <section class="gate"><h1>Nicht freigeschaltet</h1><p>Die Gruppen sehen die Trainer und die Leitung des Vereins.</p></section>
    } @else if (group(); as g) {
      <div class="page-head">
        <div>
          <h1>{{ g.name }}</h1>
          <p class="kid-meta">
            @if (schedule(g); as s) { <span>{{ s }}</span> }
            @if (g.trainers.length) { <span>Zugriff: {{ trainerNames() }}</span> }
            @if (g.archived) { <span class="chip">im Archiv</span> }
          </p>
        </div>
        <div class="actions">
          <a class="btn primary" [routerLink]="['/gruppen', g.id, 'anwesenheit']">Anwesenheit abhaken</a>
          <a class="btn" routerLink="/kind/neu" [queryParams]="{ gruppe: g.id }">Kind anlegen</a>
        </div>
      </div>

      @if (!g.members.length && !g.coaches.length) {
        <div class="empty"><p>Noch kein Kind in der Gruppe. Leg eines an oder nimm eines aus der Kartei dazu.</p></div>
      } @else {
        <div class="matrix-scroll">
          <table class="matrix">
            <thead>
              <tr>
                <th class="name" scope="col">Kind</th>
                @for (s of g.sessions; track s.id) {
                  <th scope="col"><a [routerLink]="['/gruppen', g.id, 'anwesenheit']" [queryParams]="{ datum: s.date }" [title]="s.topic ?? ''">{{ short(s.date) }}</a></th>
                }
                <th scope="col">da</th>
              </tr>
            </thead>
            <tbody>
              @for (m of g.members; track m.id) {
                <tr>
                  <th class="name" scope="row"><a [routerLink]="['/kind', m.id]"><b>{{ head(m) }}</b>{{ tail(m) }}</a></th>
                  @for (st of m.statuses; track $index) {
                    <td><span class="cell" [class]="st ?? 'none'">{{ mark(st) }}</span></td>
                  }
                  <td class="rate">{{ m.recorded ? m.present + ' von ' + m.recorded : '' }}</td>
                </tr>
              }
              @if (g.coaches.length) {
                <tr class="coach-head"><th class="name" scope="rowgroup" [attr.colspan]="g.sessions.length + 2">Trainer</th></tr>
                @for (m of g.coaches; track m.id) {
                  <tr class="coach">
                    <th class="name" scope="row"><a [routerLink]="['/kind', m.id]"><b>{{ head(m) }}</b>{{ tail(m) }}</a></th>
                    @for (st of m.statuses; track $index) {
                      <td><span class="cell" [class]="st ?? 'none'">{{ mark(st) }}</span></td>
                    }
                    <td class="rate">{{ m.recorded ? m.present + ' von ' + m.recorded : '' }}</td>
                  </tr>
                }
              }
            </tbody>
          </table>
        </div>
        @if (g.sessions.length) {
          <p class="legend">
            <span><span class="cell present">✓</span>da</span>
            <span><span class="cell absent">–</span>gefehlt</span>
            <span>Ein Datum antippen, um die Einheit zu ändern.</span>
          </p>
        } @else {
          <p class="muted small">Noch keine Einheit erfasst — mit „Anwesenheit abhaken" entsteht die erste.</p>
        }
      }

      @if (diary().length) {
        <section class="diary mt">
          <h2>Was wurde gemacht?</h2>
          <ul class="notes">
            @for (s of diary(); track s.id) {
              <li>
                <p><a [routerLink]="['/gruppen', g.id, 'anwesenheit']" [queryParams]="{ datum: s.date }">{{ long(s.date) }}</a>
                  <span class="muted small">{{ s.present }} von {{ s.present + s.absent }} da</span></p>
                @if (s.topic) { <p class="topic">{{ s.topic }}</p> }
                @if (s.notes) { <p class="pre muted">{{ s.notes }}</p> }
                @if (!s.topic && !s.notes) { <p class="muted small">Kein Thema eingetragen.</p> }
                @if (s.photos.length) { <ch-session-photos [sessionId]="s.id" [photos]="s.photos" /> }
              </li>
            }
          </ul>
        </section>
      }

      <p class="err status-line" role="alert">{{ error() ?? '' }}</p>

      <details class="sheet manage-kids mt" (toggle)="onKidsToggle($any($event.target).open)">
        <summary><h3 class="inline">Kinder der Gruppe verwalten</h3></summary>
        <section>
          @if (candidates().length) {
            <div class="note-form">
              <label class="field"><span>Kind aus der Kartei dazunehmen</span>
                <select class="add-kid" (change)="pick.set(+$any($event.target).value || null)">
                  <option value="">auswählen …</option>
                  @for (c of candidates(); track c.id) { <option [value]="c.id" [selected]="c.id === pick()">{{ head(c) }}{{ tail(c) }}</option> }
                </select></label>
              <button type="button" class="btn" [disabled]="busy() || !pick()" (click)="addKid()">Dazunehmen</button>
            </div>
          } @else if (allLoaded()) { <p class="muted">Alle Kinder, die du siehst, sind schon in der Gruppe.</p> }
          @if (g.members.length) {
            <ul class="notes">
              @for (m of g.members; track m.id) {
                <li>{{ head(m) }}{{ tail(m) }}
                  <button type="button" class="btn-link danger" [disabled]="busy()" (click)="removeKid(m.id, m.firstName)">aus der Gruppe nehmen</button></li>
              }
            </ul>
          }
        </section>
      </details>

      @if (g.canManage) {
        <details class="sheet manage-group mt">
          <summary><h3 class="inline">Gruppe ändern</h3></summary>
          <section class="form">
            <div class="grid-2">
              <label class="field"><span>Name der Gruppe</span>
                <input name="name" autocomplete="off" maxlength="80" [value]="form().name" (input)="patch({ name: $any($event.target).value })"></label>
              <label class="field"><span>Trainingstag</span>
                <select name="weekday" (change)="patch({ weekday: +$any($event.target).value || null })">
                  <option value="" [selected]="form().weekday === null">kein fester Tag</option>
                  @for (d of weekdays; track $index) { <option [value]="$index + 1" [selected]="form().weekday === $index + 1">{{ d }}</option> }
                </select></label>
            </div>
            <label class="field"><span>Zeit und Ort</span>
              <input name="schedule" autocomplete="off" maxlength="200" [value]="form().schedule ?? ''" (input)="patch({ schedule: $any($event.target).value })"></label>
            <label class="check"><input type="checkbox" [checked]="form().archived" (change)="patch({ archived: $any($event.target).checked })"> Im Archiv (trainiert nicht mehr)</label>
            <div class="actions">
              <button type="button" class="btn primary save-group" [disabled]="busy()" (click)="saveGroup()">Änderungen speichern</button>
              <button type="button" class="btn-link danger" [disabled]="busy()" (click)="deleteGroup()">Gruppe löschen</button>
            </div>
          </section>
          <section>
            <h3>Konten mit Zugriff auf die Gruppe</h3>
            @if (g.trainers.length) {
              <ul class="notes">
                @for (t of g.trainers; track t.userId) {
                  <li>{{ t.username }} <button type="button" class="btn-link danger" [disabled]="busy()" (click)="removeTrainer(t.userId)">entfernen</button></li>
                }
              </ul>
            } @else { <p class="muted">Noch kein Konto zugeteilt — die Gruppe sieht nur die Leitung.</p> }
            <div class="note-form mt">
              <label class="field"><span>Benutzername des Kontos</span>
                <input class="trainer-name" autocomplete="off" maxlength="100" [value]="trainerName()" (input)="trainerName.set($any($event.target).value)" (keydown.enter)="addTrainer()"></label>
              <button type="button" class="btn" [disabled]="busy() || !trainerName().trim()" (click)="addTrainer()">Zugriff geben</button>
            </div>
            <p class="muted small">Wer die Gruppe in ClubHub bedienen darf. Das Konto braucht zusätzlich eine Rolle mit dem Recht
              „ClubHub: Trainer" (in RookHub unter Rollen). Die Trainer als PERSONEN stehen in der Kartei und in jeder Anwesenheitsliste.</p>
          </section>
        </details>
      }
    } @else if (error()) {
      <section class="gate"><h1>Gruppe nicht gefunden</h1><p>{{ error() }}</p><p><a routerLink="/gruppen">Zu den Gruppen</a></p></section>
    } @else {
      <p class="muted">Lade …</p>
    }
  `,
})
export class GroupPageComponent implements OnInit {
  private readonly api = inject(ClubApiService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly confirm = inject(ConfirmService);

  readonly allowed = hasClubAccess(this.auth);
  readonly group = signal<Group | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly form = signal<GroupInput>({ name: '', schedule: null, weekday: null, archived: false });
  readonly trainerName = signal('');
  /** Die Kartei (soweit der Aufrufer sie sieht) — erst geladen, wenn „Kinder verwalten" aufgeklappt wird. */
  readonly all = signal<MemberRow[]>([]);
  readonly allLoaded = signal(false);
  readonly pick = signal<number | null>(null);

  readonly candidates = computed(() => {
    const inGroup = new Set(this.group()?.members.map(m => m.id) ?? []);
    return this.all().filter(m => !inGroup.has(m.id));
  });
  readonly trainerNames = computed(() => (this.group()?.trainers ?? []).map(t => t.username).join(', '));

  readonly weekdays = WEEKDAYS;
  readonly schedule = scheduleText;
  readonly short = shortDate;
  readonly mark = statusMark;
  readonly head = nameHead;
  readonly tail = nameTail;
  readonly long = longDate;
  /** Das Trainingstagebuch: die Einheiten der Tabelle, neueste zuerst. */
  readonly diary = computed(() => [...(this.group()?.sessions ?? [])].reverse());

  ngOnInit(): void {
    if (!this.allowed) return;
    const id = Number(this.route.snapshot.paramMap.get('id'));
    void this.run(async () => this.show(await this.api.group(id)), 'Die Gruppe konnte nicht geladen werden.');
  }

  private show(g: Group): void {
    this.group.set(g);
    this.form.set({ name: g.name, schedule: g.schedule ?? null, weekday: g.weekday ?? null, archived: g.archived });
  }

  patch(change: Partial<GroupInput>): void {
    this.form.update(f => ({ ...f, ...change }));
  }

  onKidsToggle(open: boolean): void {
    if (!open || this.allLoaded()) return;
    void this.api.members(false).then(rows => {
      this.all.set(rows);
      this.allLoaded.set(true);
    }, () => this.error.set('Die Kartei konnte nicht geladen werden.'));
  }

  async addKid(): Promise<void> {
    const [g, id] = [this.group(), this.pick()];
    if (!g || !id) return;
    await this.run(async () => {
      this.show(await this.api.addGroupMember(g.id, id));
      this.pick.set(null);
    }, 'Das Kind konnte nicht dazugenommen werden.');
  }

  async removeKid(memberId: number, firstName: string): Promise<void> {
    const g = this.group();
    if (!g || !(await firstValueFrom(this.confirm.ask(`${firstName} aus der Gruppe nehmen? Das Blatt und die erfasste Anwesenheit bleiben.`)))) return;
    await this.run(async () => this.show(await this.api.removeGroupMember(g.id, memberId)), 'Das hat nicht geklappt.');
  }

  async saveGroup(): Promise<void> {
    const [g, f] = [this.group(), this.form()];
    if (!g) return;
    if (!f.name.trim()) {
      this.error.set('Die Gruppe braucht einen Namen.');
      return;
    }
    await this.run(async () => this.show(await this.api.updateGroup(g.id, { ...f, name: f.name.trim(), schedule: f.schedule?.trim() || null })),
      'Die Gruppe konnte nicht gespeichert werden.');
  }

  async deleteGroup(): Promise<void> {
    const g = this.group();
    if (!g || !(await firstValueFrom(this.confirm.ask(`Die Gruppe „${g.name}“ löschen — mit allen Einheiten und der Anwesenheit? Die Kinder bleiben in der Kartei.`)))) return;
    await this.run(async () => {
      await this.api.deleteGroup(g.id);
      void this.router.navigateByUrl('/gruppen');
    }, 'Die Gruppe konnte nicht gelöscht werden.');
  }

  async addTrainer(): Promise<void> {
    const [g, name] = [this.group(), this.trainerName().trim()];
    if (!g || !name || this.busy()) return;
    await this.run(async () => {
      this.show(await this.api.addTrainer(g.id, name));
      this.trainerName.set('');
    }, 'Das Konto konnte nicht zugeteilt werden.');
  }

  async removeTrainer(userId: number): Promise<void> {
    const g = this.group();
    if (!g) return;
    await this.run(async () => this.show(await this.api.removeTrainer(g.id, userId)), 'Das hat nicht geklappt.');
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
