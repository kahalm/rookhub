import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { hasClubAccess } from '../../core/club-access';
import { ClubApiService, apiErrorText } from '../../core/club-api.service';
import { Group, SessionDetail, Status } from '../../core/club.models';
import { longDate, trainingDate } from '../../core/club-format';

/** „7 von 12 da" */
export function tallyText(present: number, total: number): string {
  return `${present} von ${total} da`;
}

/**
 * Die Anwesenheitsliste (Wunsch 2026-09-30: „am Freitag abhaken, wer da ist"). Sie geht für den jüngsten Trainingstag der
 * Gruppe auf — am Freitag für heute, am Montag darauf für den vergangenen Freitag —, jede Zeile ist EIN großer Tipp: da
 * oder nicht. Daneben „entschuldigt". Beim Speichern gilt, wer nicht abgehakt ist, als gefehlt; so stimmt die Quote.
 *
 * Je Gruppe und Tag gibt es eine Einheit, ein Speichern ersetzt sie. Deshalb holt die Seite vor dem ersten Tipp, was für
 * den Tag schon erfasst ist (`sessionByDate`) — eine leer geöffnete Liste überschriebe sonst die erfasste.
 */
@Component({
  selector: 'ch-attendance-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    @if (!allowed) {
      <section class="gate"><h1>Nicht freigeschaltet</h1><p>Die Anwesenheit erfassen die Trainer und die Leitung des Vereins.</p></section>
    } @else if (group(); as g) {
      <h1>Wer ist da?</h1>
      <p class="kid-meta"><a [routerLink]="['/gruppen', g.id]">{{ g.name }}</a><span>{{ long(date()) }}</span></p>
      <div class="roll-head">
        <label class="field"><span>Tag der Einheit</span>
          <input type="date" name="date" [value]="date()" (change)="changeDate($any($event.target).value)"></label>
        <label class="field"><span>Thema</span>
          <input name="topic" autocomplete="off" maxlength="200" placeholder="z. B. Gabel und Spieß" [value]="topic()" (input)="topic.set($any($event.target).value)"></label>
      </div>
      @if (session()) { <p class="muted small existing">Für diesen Tag ist schon eine Einheit erfasst — du änderst sie.</p> }

      @if (!g.members.length) {
        <div class="empty">
          <p>In dieser Gruppe ist noch kein Kind.</p>
          <a class="btn primary" routerLink="/kind/neu" [queryParams]="{ gruppe: g.id }">Kind anlegen</a>
        </div>
      } @else {
        <div class="tally">
          <strong aria-live="polite">{{ tally() }}</strong>
          <button type="button" class="btn slim all-present" [disabled]="loading()" (click)="allPresent()">Alle da</button>
        </div>
        <ul class="roll">
          @for (m of g.members; track m.id) {
            <li [class.present]="marks()[m.id] === 'present'" [class.excused]="marks()[m.id] === 'excused'">
              <button type="button" class="tick" [attr.aria-pressed]="marks()[m.id] === 'present'" [disabled]="loading()" (click)="toggle(m.id)">
                <span class="box" aria-hidden="true"><svg viewBox="0 0 24 24"><path d="M5 12.5l4.5 4.5L19 7.5" /></svg></span>
                <span><b>{{ m.lastName }}</b> {{ m.firstName }}</span>
              </button>
              <button type="button" class="excuse" [attr.aria-pressed]="marks()[m.id] === 'excused'" [disabled]="loading()" (click)="toggleExcused(m.id)">entschuldigt</button>
            </li>
          }
        </ul>
        <div class="save-bar">
          <button type="button" class="btn primary save" [disabled]="busy() || loading()" (click)="save()">Anwesenheit speichern</button>
          <span class="ok" role="status">{{ saved() ?? '' }}</span>
          <span class="err" role="alert">{{ error() ?? '' }}</span>
          @if (session(); as s) { <button type="button" class="btn-link danger" [disabled]="busy()" (click)="remove(s)">Einheit löschen</button> }
        </div>
      }
    } @else if (error()) {
      <section class="gate"><h1>Gruppe nicht gefunden</h1><p>{{ error() }}</p><p><a routerLink="/gruppen">Zu den Gruppen</a></p></section>
    } @else {
      <p class="muted">Lade …</p>
    }
  `,
})
export class AttendancePageComponent implements OnInit {
  private readonly api = inject(ClubApiService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly allowed = hasClubAccess(this.auth);
  readonly group = signal<Group | null>(null);
  readonly date = signal('');
  readonly session = signal<SessionDetail | null>(null);
  /** Kind → Status; ohne Eintrag gilt es als nicht da. */
  readonly marks = signal<Record<number, Status>>({});
  readonly topic = signal('');
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly saved = signal<string | null>(null);
  /** „Heute" — als Feld, damit ein Test den Wochentag festlegen kann. */
  now: () => Date = () => new Date();
  /** Zählt die Tageswechsel — eine Antwort für einen inzwischen verlassenen Tag wird verworfen. */
  private epoch = 0;

  readonly present = computed(() => (this.group()?.members ?? []).filter(m => this.marks()[m.id] === 'present').length);
  readonly tally = computed(() => tallyText(this.present(), this.group()?.members.length ?? 0));
  readonly long = longDate;

  ngOnInit(): void {
    if (!this.allowed) return;
    void this.init(Number(this.route.snapshot.paramMap.get('id')), this.route.snapshot.queryParamMap.get('datum'));
  }

  private async init(groupId: number, wanted: string | null): Promise<void> {
    try {
      const g = await this.api.group(groupId);
      this.group.set(g);
      await this.loadDate(wanted && /^\d{4}-\d{2}-\d{2}$/.test(wanted) ? wanted : trainingDate(g.weekday, this.now()));
    } catch (err) {
      this.error.set(apiErrorText(err, 'Die Gruppe konnte nicht geladen werden.'));
    }
  }

  changeDate(value: string): void {
    if (value && value !== this.date()) void this.loadDate(value);
  }

  /** Was für diesen Tag schon erfasst ist — solange das lädt, ist die Liste gesperrt. */
  private async loadDate(date: string): Promise<void> {
    const g = this.group();
    if (!g) return;
    const mine = ++this.epoch;
    this.date.set(date);
    this.loading.set(true);
    this.saved.set(null);
    this.error.set(null);
    try {
      const s = await this.api.sessionByDate(g.id, date);
      if (mine !== this.epoch) return;
      this.apply(s);
    } catch (err) {
      if (mine !== this.epoch) return;
      this.apply(null);
      this.error.set(apiErrorText(err, 'Der Stand für diesen Tag konnte nicht geladen werden.'));
    } finally {
      if (mine === this.epoch) this.loading.set(false);
    }
  }

  private apply(s: SessionDetail | null): void {
    this.session.set(s);
    this.topic.set(s?.topic ?? '');
    const marks: Record<number, Status> = {};
    for (const a of s?.attendance ?? []) if (a.status) marks[a.memberId] = a.status;
    this.marks.set(marks);
  }

  /** Ein Tipp auf die Zeile: da ↔ nicht da (ein „entschuldigt" wird zu „da"). */
  toggle(memberId: number): void {
    this.mark(memberId, this.marks()[memberId] === 'present' ? 'absent' : 'present');
  }

  toggleExcused(memberId: number): void {
    this.mark(memberId, this.marks()[memberId] === 'excused' ? 'absent' : 'excused');
  }

  private mark(memberId: number, status: Status): void {
    this.marks.update(m => ({ ...m, [memberId]: status }));
    this.saved.set(null);
  }

  allPresent(): void {
    const marks: Record<number, Status> = {};
    for (const m of this.group()?.members ?? []) marks[m.id] = 'present';
    this.marks.set(marks);
    this.saved.set(null);
  }

  async save(): Promise<void> {
    const g = this.group();
    if (!g || this.loading()) return;
    this.busy.set(true);
    this.error.set(null);
    this.saved.set(null);
    try {
      const s = await this.api.saveSession(g.id, {
        date: this.date(), topic: this.topic().trim() || null, notes: this.session()?.notes ?? null,
        // Jedes Kind der Gruppe bekommt einen Eintrag: wer nicht abgehakt ist, hat gefehlt.
        attendance: g.members.map(m => ({ memberId: m.id, status: this.marks()[m.id] ?? 'absent' })),
      });
      this.apply(s);
      this.saved.set(`Gespeichert: ${tallyText(s.present, g.members.length)}.`);
    } catch (err) {
      this.error.set(apiErrorText(err, 'Speichern hat nicht geklappt — die Liste ist noch nicht gesichert.'));
    } finally {
      this.busy.set(false);
    }
  }

  async remove(s: SessionDetail): Promise<void> {
    const g = this.group();
    if (!g || !confirm(`Die Einheit vom ${longDate(s.date)} samt Anwesenheit löschen?`)) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.deleteSession(s.id);
      void this.router.navigate(['/gruppen', g.id]);
    } catch (err) {
      this.error.set(apiErrorText(err, 'Löschen hat nicht geklappt.'));
    } finally {
      this.busy.set(false);
    }
  }
}
