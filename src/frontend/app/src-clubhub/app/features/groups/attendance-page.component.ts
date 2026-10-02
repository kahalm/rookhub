import { ChangeDetectionStrategy, Component, OnInit, WritableSignal, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';
import { firstValueFrom } from 'rxjs';
import { SessionPhotosComponent } from '../../shared/session-photos.component';
import { hasClubAccess } from '../../core/club-access';
import { ClubApiService, apiErrorText } from '../../core/club-api.service';
import { Group, SessionDetail, Status } from '../../core/club.models';
import { dateNeighbours, longDate, nameHead, nameTail, shortDate, trainingDate } from '../../core/club-format';

/** „7 von 12 da" */
export function tallyText(present: number, total: number): string {
  return `${present} von ${total} da`;
}

/**
 * Die Anwesenheitsliste (Wunsch 2026-09-30: „am Freitag abhaken, wer da ist"). Sie geht für den jüngsten Trainingstag der
 * Gruppe auf — am Freitag für heute, am Montag darauf für den vergangenen Freitag —, jede Zeile ist EIN großer Tipp: da
 * oder nicht (ein „entschuldigt" gibt es bewusst nicht). Beim Speichern gilt, wer nicht abgehakt ist, als gefehlt; so
 * stimmt die Quote. Unter den Kindern stehen die TRAINER (alle der Kartei, in jeder Gruppe) und werden genauso abgehakt.
 * Dazu das Thema der Einheit und, ausführlicher, was gemacht wurde — beides steht danach im Trainingstagebuch der Gruppe.
 *
 * Je Gruppe und Tag gibt es eine Einheit, ein Speichern ersetzt sie. Deshalb holt die Seite vor dem ersten Tipp, was für
 * den Tag schon erfasst ist (`sessionByDate`) — eine leer geöffnete Liste überschriebe sonst die erfasste.
 *
 * BLÄTTERN (Wunsch 2026-10-02): zwei Knöpfe führen zur Einheit davor und danach — über die Tage, an denen die Gruppe
 * eine Einheit hat (`sessionDates`), plus den Tag, für den die Liste von selbst aufgeht. Wer mit ungespeicherten Haken
 * blättert (oder das Datum ändert), wird vorher gefragt. Einen Knopf „Alle da" gibt es nicht mehr (Wunsch: „das gibt's
 * eigentlich nie").
 */
@Component({
  selector: 'ch-attendance-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, SessionPhotosComponent],
  template: `
    @if (!allowed) {
      <section class="gate"><h1>Nicht freigeschaltet</h1><p>Die Anwesenheit erfassen die Trainer und die Leitung des Vereins.</p></section>
    } @else if (group(); as g) {
      <h1>Wer ist da?</h1>
      <p class="kid-meta"><a [routerLink]="['/gruppen', g.id]">{{ g.name }}</a><span>{{ long(date()) }}</span></p>
      <nav class="pager" aria-label="Einheiten blättern">
        <button type="button" class="btn slim prev" [disabled]="!around().prev || busy()" (click)="changeDate(around().prev ?? '')">
          <span aria-hidden="true">‹</span> {{ around().prev ? short(around().prev!) : 'keine frühere' }}</button>
        <button type="button" class="btn slim next" [disabled]="!around().next || busy()" (click)="changeDate(around().next ?? '')">
          {{ around().next ? short(around().next!) : 'keine spätere' }} <span aria-hidden="true">›</span></button>
      </nav>
      <div class="roll-head">
        <label class="field"><span>Tag der Einheit</span>
          <input type="date" name="date" [value]="date()" (change)="changeDate($any($event.target).value, $any($event.target))"></label>
        <label class="field"><span>Thema</span>
          <input name="topic" autocomplete="off" maxlength="200" placeholder="z. B. Gabel und Spieß" [value]="topic()" (input)="edit(topic, $any($event.target).value)"></label>
      </div>
      <label class="field done"><span>Was wurde gemacht?</span>
        <textarea name="notes" maxlength="2000" rows="3" placeholder="z. B. Gabel wiederholt, Arbeitsblatt 3, zum Schluss Simultan"
          [value]="notes()" (input)="edit(notes, $any($event.target).value)"></textarea></label>
      @if (session()) { <p class="muted small existing">Für diesen Tag ist schon eine Einheit erfasst — du änderst sie.</p> }

      @if (!g.members.length && !g.coaches.length) {
        <div class="empty">
          <p>In dieser Gruppe ist noch kein Kind.</p>
          <a class="btn primary" routerLink="/kind/neu" [queryParams]="{ gruppe: g.id }">Kind anlegen</a>
        </div>
      } @else {
        <div class="tally">
          <strong aria-live="polite">{{ tally() }}</strong>
        </div>
        <ul class="roll">
          @for (m of g.members; track m.id) {
            <li [class.present]="marks()[m.id] === 'present'">
              <button type="button" class="tick" [attr.aria-pressed]="marks()[m.id] === 'present'" [disabled]="loading()" (click)="toggle(m.id)">
                <span class="box" aria-hidden="true"><svg viewBox="0 0 24 24"><path d="M5 12.5l4.5 4.5L19 7.5" /></svg></span>
                <span><b>{{ head(m) }}</b>{{ tail(m) }}</span>
              </button>
            </li>
          } @empty { <li class="roll-note muted">In dieser Gruppe ist noch kein Kind.</li> }
          @if (g.coaches.length) {
            <li class="roll-group">Trainer</li>
            @for (m of g.coaches; track m.id) {
              <li class="coach" [class.present]="marks()[m.id] === 'present'">
                <button type="button" class="tick" [attr.aria-pressed]="marks()[m.id] === 'present'" [disabled]="loading()" (click)="toggle(m.id)">
                  <span class="box" aria-hidden="true"><svg viewBox="0 0 24 24"><path d="M5 12.5l4.5 4.5L19 7.5" /></svg></span>
                  <span><b>{{ head(m) }}</b>{{ tail(m) }}</span>
                </button>
              </li>
            }
          }
        </ul>
        <section class="photos-section">
          <h2>Fotos</h2>
          @if (session(); as s) {
            <ch-session-photos [sessionId]="s.id" [photos]="s.photos" [editable]="true" (deleted)="photoDeleted($event)" />
          }
          <label class="btn slim upload" [class.disabled]="busy() || loading()">
            {{ uploading() ? uploading() : 'Fotos hinzufügen' }}
            <input type="file" accept="image/*" multiple hidden [disabled]="busy() || loading()" (change)="addPhotos($any($event.target))">
          </label>
          @if (!session()) { <span class="muted small">Beim ersten Foto wird die Einheit gespeichert.</span> }
          <p class="err status-line" role="alert">{{ photoError() ?? '' }}</p>
        </section>
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
  private readonly confirm = inject(ConfirmService);

  readonly allowed = hasClubAccess(this.auth);
  readonly group = signal<Group | null>(null);
  readonly date = signal('');
  readonly session = signal<SessionDetail | null>(null);
  /** Kind → Status; ohne Eintrag gilt es als nicht da. */
  readonly marks = signal<Record<number, Status>>({});
  readonly topic = signal('');
  /** „Was wurde gemacht?" — ausführlicher als das Thema. */
  readonly notes = signal('');
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly saved = signal<string | null>(null);
  /** „Foto 2 von 5 …" während des Hochladens, sonst leer. */
  readonly uploading = signal('');
  readonly photoError = signal<string | null>(null);
  /** „Heute" — als Feld, damit ein Test den Wochentag festlegen kann. */
  now: () => Date = () => new Date();
  /** Zählt die Tageswechsel — eine Antwort für einen inzwischen verlassenen Tag wird verworfen. */
  private epoch = 0;
  /** Die Tage, an denen die Gruppe eine Einheit hat — zum Blättern. */
  readonly sessionDates = signal<string[]>([]);
  /** Der Tag, für den die Liste von selbst aufgeht (jüngster Trainingstag) — das Ende des Blätterns nach vorn. */
  readonly home = signal('');
  /** Haken, Thema oder Text geändert und noch nicht gespeichert — vor dem Blättern wird dann gefragt. */
  readonly dirty = signal(false);
  readonly around = computed(() => dateNeighbours(this.sessionDates(), this.date(), this.home()));

  /** Alle auf der Liste: die Kinder der Gruppe und darunter die Trainer. */
  readonly listed = computed(() => { const g = this.group(); return g ? [...g.members, ...g.coaches] : []; });
  readonly present = computed(() => this.listed().filter(m => this.marks()[m.id] === 'present').length);
  readonly tally = computed(() => tallyText(this.present(), this.listed().length));
  readonly long = longDate;
  readonly short = shortDate;
  readonly head = nameHead;
  readonly tail = nameTail;

  ngOnInit(): void {
    if (!this.allowed) return;
    void this.init(Number(this.route.snapshot.paramMap.get('id')), this.route.snapshot.queryParamMap.get('datum'));
  }

  private async init(groupId: number, wanted: string | null): Promise<void> {
    try {
      const g = await this.api.group(groupId);
      this.group.set(g);
      this.home.set(trainingDate(g.weekday, this.now()));
      void this.loadDates(g.id);
      await this.loadDate(wanted && /^\d{4}-\d{2}-\d{2}$/.test(wanted) ? wanted : this.home());
    } catch (err) {
      this.error.set(apiErrorText(err, 'Die Gruppe konnte nicht geladen werden.'));
    }
  }

  /** Ohne die Tage gibt es nur kein Blättern — die Liste selbst bleibt benutzbar. */
  private async loadDates(groupId: number): Promise<void> {
    try {
      this.sessionDates.set(await this.api.sessionDates(groupId));
    } catch { /* still */ }
  }

  /**
   * Auf einen anderen Tag wechseln — über die Blätter-Knöpfe oder das Datumsfeld. Mit ungespeicherten Änderungen erst
   * nach Rückfrage; bei „nein" zeigt das Datumsfeld wieder den Tag, auf dem man steht.
   */
  changeDate(value: string, input?: HTMLInputElement): void {
    if (!value || value === this.date()) return;
    if (!this.dirty()) {
      this.open(value);
      return;
    }
    void firstValueFrom(this.confirm.ask('Die Änderungen an dieser Einheit sind noch nicht gespeichert. Trotzdem zu einem anderen Tag wechseln?'))
      .then(ok => {
        if (ok) this.open(value);
        else if (input) input.value = this.date();
      });
  }

  /** Den Tag öffnen und in die Adresse schreiben (`?datum=`), damit Neuladen und „Zurück" auf ihm bleiben. */
  private open(date: string): void {
    void this.loadDate(date);
    void this.router.navigate([], { relativeTo: this.route, queryParams: { datum: date === this.home() ? null : date }, queryParamsHandling: 'merge', replaceUrl: true });
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
    this.notes.set(s?.notes ?? '');
    const marks: Record<number, Status> = {};
    for (const a of s?.attendance ?? []) if (a.status === 'present' || a.status === 'absent') marks[a.memberId] = a.status;
    this.marks.set(marks);
    this.dirty.set(false);
  }

  /** Ein Tipp auf die Zeile: da ↔ nicht da. */
  toggle(memberId: number): void {
    this.mark(memberId, this.marks()[memberId] === 'present' ? 'absent' : 'present');
  }

  /** Thema bzw. „Was wurde gemacht?" ändern — danach gilt die Einheit nicht mehr als gespeichert. */
  edit(field: WritableSignal<string>, value: string): void {
    field.set(value);
    this.saved.set(null);
    this.dirty.set(true);
  }

  private mark(memberId: number, status: Status): void {
    this.marks.update(m => ({ ...m, [memberId]: status }));
    this.saved.set(null);
    this.dirty.set(true);
  }

  /**
   * Fotos hochladen, eines nach dem anderen (der nginx lässt 15 MB je Anfrage durch). Gibt es die Einheit noch nicht,
   * wird sie vorher gespeichert — ein Foto hängt an einer Einheit.
   */
  async addPhotos(input: HTMLInputElement): Promise<void> {
    const files = Array.from(input.files ?? []);
    input.value = '';
    if (!files.length || this.busy() || this.loading()) return;
    this.photoError.set(null);
    if (!this.session()) {
      await this.save();
      if (!this.session()) return;                                       // Speichern hat nicht geklappt — steht schon da
    }
    const s = this.session()!;
    const failed: string[] = [];
    let n = 0;
    for (const file of files) {
      this.uploading.set(`Foto ${++n} von ${files.length} …`);
      try {
        const photo = await this.api.uploadPhoto(s.id, file);
        this.session.update(cur => cur ? { ...cur, photos: [...cur.photos, photo] } : cur);
      } catch (err) {
        failed.push(apiErrorText(err, file.name));
      }
    }
    this.uploading.set('');
    if (failed.length) this.photoError.set(`Nicht hochgeladen: ${failed.join(' · ')}`);
  }

  photoDeleted(id: number): void {
    this.session.update(cur => cur ? { ...cur, photos: cur.photos.filter(p => p.id !== id) } : cur);
  }

  async save(): Promise<void> {
    const g = this.group();
    if (!g || this.loading()) return;
    this.busy.set(true);
    this.error.set(null);
    this.saved.set(null);
    try {
      const s = await this.api.saveSession(g.id, {
        date: this.date(), topic: this.topic().trim() || null, notes: this.notes().trim() || null,
        // Jeder auf der Liste (Kinder der Gruppe + Trainer) bekommt einen Eintrag: wer nicht abgehakt ist, hat gefehlt.
        attendance: this.listed().map(m => ({ memberId: m.id, status: this.marks()[m.id] ?? 'absent' })),
      });
      this.apply(s);
      this.sessionDates.update(d => d.includes(s.date) ? d : [...d, s.date]);
      this.saved.set(`Gespeichert: ${tallyText(s.present, this.listed().length)}.`);
    } catch (err) {
      this.error.set(apiErrorText(err, 'Speichern hat nicht geklappt — die Liste ist noch nicht gesichert.'));
    } finally {
      this.busy.set(false);
    }
  }

  async remove(s: SessionDetail): Promise<void> {
    const g = this.group();
    if (!g || !(await firstValueFrom(this.confirm.ask(`Die Einheit vom ${longDate(s.date)} samt Anwesenheit löschen?`)))) return;
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
