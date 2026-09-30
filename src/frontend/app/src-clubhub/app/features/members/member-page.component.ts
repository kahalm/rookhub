import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { hasClubAccess } from '../../core/club-access';
import { ClubApiService, apiErrorText } from '../../core/club-api.service';
import { Contact, ContactKind, GroupRow, Member, MemberInput, Progress } from '../../core/club.models';
import { STATUS_LABEL, ageClass, attendanceText, emptyInput, formatBirth, formatLinkCode, fullName, parseBirth, shortDate, telHref, toInput } from '../../core/club-format';

/** „30.09.2026" aus einem Zeitstempel der API (Ortszeit des Betrachters). */
export function formatDay(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  return `${String(d.getDate()).padStart(2, '0')}.${String(d.getMonth() + 1).padStart(2, '0')}.${d.getFullYear()}`;
}

/** Leere Zeichenketten sind „nicht angegeben" — so kommen sie beim Server an. */
function orNull(value: string | null | undefined): string | null {
  const t = (value ?? '').trim();
  return t ? t : null;
}

/**
 * Das Karteiblatt eines Kindes (Wunsch 2026-09-30): anlegen, ansehen, ändern. Kontakte sind eine LISTE — beliebig viele
 * Telefonnummern und E-Mail-Adressen, jede mit einem Hinweis, wessen sie ist („Mutter Daniela", „Vater Franz"); in der
 * Ansicht ist jede Nummer ein Anruf-Link. Darunter Lernstand (Stufe + datierte Notizen), Anwesenheit, und das verknüpfte
 * Konto: den Code dafür gibt der Trainer aus, EINLÖSEN muss ihn das Konto selbst (`/verknuepfen`).
 */
@Component({
  selector: 'ch-member-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    @if (!allowed) {
      <section class="gate"><h1>Nicht freigeschaltet</h1><p>Karteiblätter sehen die Trainer und die Leitung des Vereins.</p></section>
    } @else if (editing()) {
      <form class="sheet form" (submit)="$event.preventDefault(); save()">
        <header class="sheet-head"><h1>{{ member() ? 'Blatt ändern' : 'Kind anlegen' }}</h1></header>
        <div class="grid-2">
          <label class="field"><span>Vorname</span>
            <input name="firstName" autocomplete="off" maxlength="80" [value]="form().firstName" (input)="set('firstName', $any($event.target).value)"></label>
          <label class="field"><span>Nachname</span>
            <input name="lastName" autocomplete="off" maxlength="80" [value]="form().lastName" (input)="set('lastName', $any($event.target).value)"></label>
        </div>
        <div class="grid-2">
          <label class="field"><span>Geburtsdatum oder Jahrgang</span>
            <input name="birth" inputmode="numeric" autocomplete="off" placeholder="12.3.2015 oder 2015" [value]="birthText()" (input)="birthText.set($any($event.target).value)"></label>
          <label class="field"><span>Stufe oder Diplom</span>
            <input name="level" autocomplete="off" maxlength="60" placeholder="z. B. Bauerndiplom" [value]="form().level ?? ''" (input)="set('level', $any($event.target).value)"></label>
        </div>

        <section>
          <h2>Kontakte</h2>
          @for (c of form().contacts; track $index) {
            <div class="contact-edit">
              <label class="field"><span>Art</span>
                <select [attr.aria-label]="'Art des Kontakts ' + ($index + 1)" (change)="setContact($index, { kind: $any($event.target).value })">
                  <option value="phone" [selected]="c.kind === 'phone'">Telefon</option>
                  <option value="email" [selected]="c.kind === 'email'">E-Mail</option>
                </select></label>
              <label class="field value"><span>{{ c.kind === 'phone' ? 'Nummer' : 'Adresse' }}</span>
                <input [type]="c.kind === 'phone' ? 'tel' : 'email'" autocomplete="off" maxlength="200" [value]="c.value" (input)="setContact($index, { value: $any($event.target).value })"></label>
              <label class="field"><span>Wessen?</span>
                <input autocomplete="off" maxlength="80" placeholder="z. B. Mutter Daniela" [value]="c.label ?? ''" (input)="setContact($index, { label: $any($event.target).value })"></label>
              <button type="button" class="btn slim" [attr.aria-label]="'Kontakt ' + ($index + 1) + ' entfernen'" (click)="removeContact($index)">Entfernen</button>
            </div>
          }
          <div class="actions mt">
            <button type="button" class="btn slim add-phone" (click)="addContact('phone')">Telefonnummer hinzufügen</button>
            <button type="button" class="btn slim add-email" (click)="addContact('email')">E-Mail hinzufügen</button>
          </div>
        </section>

        <section>
          <h2>Gruppen</h2>
          @if (groups().length) {
            <div class="group-picks">
              @for (g of groups(); track g.id) {
                <label class="check"><input type="checkbox" [checked]="form().groupIds.includes(g.id)" (change)="toggleGroup(g.id, $any($event.target).checked)"> {{ g.name }}</label>
              }
            </div>
          } @else { <p class="muted">Noch keine Gruppe. Gruppen legt die Leitung unter „Gruppen" an.</p> }
        </section>

        <section>
          <h2>Weitere Angaben</h2>
          <div class="grid-2">
            <label class="field"><span>FIDE-ID</span>
              <input inputmode="numeric" autocomplete="off" maxlength="16" [value]="form().fideId ?? ''" (input)="set('fideId', $any($event.target).value)"></label>
            <label class="field"><span>ÖSB-Nummer</span>
              <input inputmode="numeric" autocomplete="off" maxlength="16" [value]="form().nationalId ?? ''" (input)="set('nationalId', $any($event.target).value)"></label>
          </div>
          <label class="field mt"><span>Fotos vom Kind dürfen veröffentlicht werden</span>
            <select (change)="setConsent($any($event.target).value)">
              <option value="" [selected]="form().photoConsent === null">nicht geklärt</option>
              <option value="yes" [selected]="form().photoConsent === true">ja, Einwilligung liegt vor</option>
              <option value="no" [selected]="form().photoConsent === false">nein</option>
            </select></label>
          <label class="field mt"><span>Notiz zum Kind</span>
            <textarea maxlength="4000" [value]="form().notes ?? ''" (input)="set('notes', $any($event.target).value)"></textarea>
            <span class="hint">Nur, was die Trainer wissen müssen (z. B. „wird um 18 Uhr abgeholt").</span></label>
          <label class="check mt-s"><input type="checkbox" [checked]="form().archived" (change)="set('archived', $any($event.target).checked)">
            Im Archiv (kommt nicht mehr ins Training)</label>
        </section>

        <p class="err status-line" role="alert">{{ error() ?? '' }}</p>
        <div class="actions pb">
          <button type="submit" class="btn primary" [disabled]="busy()">{{ member() ? 'Änderungen speichern' : 'Kind anlegen' }}</button>
          <button type="button" class="btn" [disabled]="busy()" (click)="cancel()">Abbrechen</button>
          @if (member()?.canDelete) { <button type="button" class="btn-link danger" [disabled]="busy()" (click)="remove()">Blatt löschen</button> }
        </div>
      </form>
    } @else if (member(); as m) {
      <article class="sheet">
        <header class="sheet-head">
          <h1>{{ m.firstName }} {{ m.lastName }}</h1>
          <button type="button" class="btn slim edit" (click)="startEdit()">Bearbeiten</button>
          <p class="sub">
            @if (m.birthYear) { <span>Jahrgang {{ m.birthYear }}@if (cls(); as c) {, {{ c }} }</span> }
            @if (m.level) { <span>{{ m.level }}</span> }
            @for (g of m.groups; track g.id) { <a class="chip" [routerLink]="['/gruppen', g.id]">{{ g.name }}</a> }
            @if (m.archived) { <span class="chip">im Archiv</span> }
          </p>
        </header>

        <section>
          <h2>Kontakte</h2>
          @if (m.contacts.length) {
            <ul class="contacts">
              @for (c of m.contacts; track $index) {
                <li>
                  <span class="whose">{{ c.label || (c.kind === 'phone' ? 'Telefon' : 'E-Mail') }}</span>
                  <a [href]="c.kind === 'phone' ? tel(c.value) : 'mailto:' + c.value">{{ c.value }}</a>
                </li>
              }
            </ul>
          } @else { <p class="muted">Noch kein Kontakt eingetragen. Über „Bearbeiten" kommen Telefonnummern und E-Mail-Adressen dazu.</p> }
        </section>

        <section>
          <h2>Lernstand</h2>
          <div class="note-form">
            <label class="field"><span>Neue Notiz</span>
              <input class="note-input" maxlength="2000" placeholder="z. B. kann die Gabel, übt Matt mit zwei Türmen" [value]="noteText()" (input)="noteText.set($any($event.target).value)" (keydown.enter)="addNote()"></label>
            <button type="button" class="btn add-note" [disabled]="busy() || !noteText().trim()" (click)="addNote()">Notiz speichern</button>
          </div>
          @if (m.noteEntries.length) {
            <ul class="notes">
              @for (n of m.noteEntries; track n.id) {
                <li>
                  <p class="pre">{{ n.text }}</p>
                  <p class="when"><span>{{ day(n.createdAt) }}</span>@if (n.author) { <span>{{ n.author }}</span> }
                    @if (n.canDelete) { <button type="button" class="btn-link danger" [disabled]="busy()" (click)="deleteNote(n.id)">Löschen</button> }</p>
                </li>
              }
            </ul>
          }
        </section>

        <section>
          <h2>Anwesenheit</h2>
          @if (recorded()) {
            <p>{{ attendance() }}@if (m.attendance.excused) {, {{ m.attendance.excused }}-mal entschuldigt}.</p>
            <ul class="notes">
              @for (a of m.attendance.recent; track a.sessionId) {
                <li><span class="cell" [class]="a.status" aria-hidden="true">{{ mark(a.status) }}</span>
                  {{ short(a.date) }} {{ label(a.status) }}, {{ a.group }}{{ a.topic ? ' — ' + a.topic : '' }}</li>
              }
            </ul>
          } @else { <p class="muted">Noch keine Einheit erfasst.</p> }
        </section>

        <section class="link">
          <h2>Konto</h2>
          @if (m.linked) {
            <p>Verknüpft mit dem Konto <b>{{ m.linkedUsername ?? '(gelöscht)' }}</b>.
              <button type="button" class="btn-link danger" [disabled]="busy()" (click)="unlink()">Verknüpfung trennen</button></p>
            @if (progress(); as p) {
              <dl class="stats">
                <div><dd>{{ p.minutes28 }}</dd><dt>Trainingsminuten in 4 Wochen</dt></div>
                <div><dd>{{ p.activeDays28 }}</dd><dt>Trainingstage in 4 Wochen</dt></div>
                <div><dd>{{ p.puzzlesSolved }}</dd><dt>Puzzles gelöst</dt></div>
                @if (p.puzzleAttempts) { <div><dd>{{ p.puzzleElo }}</dd><dt>Puzzle-Wertung</dt></div> }
                @if (p.kidsLevelsDone) { <div><dd>{{ p.kidsLevelsDone }}</dd><dt>KidHub-Stufen geschafft</dt></div> }
                @if (p.kidsCourseLines) { <div><dd>{{ p.kidsCourseLines }}</dd><dt>KidHub-Kursaufgaben</dt></div> }
              </dl>
              <p class="muted small">{{ p.lastActive ? 'Zuletzt trainiert am ' + short(p.lastActive) : 'In den letzten 4 Wochen nicht trainiert.' }}</p>
            }
          } @else if (m.linkCode) {
            <p>Das Kind meldet sich mit seinem RookHub- oder KidHub-Konto hier an und gibt unter „Konto verknüpfen" diesen Code ein:</p>
            <p><span class="code">{{ code(m.linkCode) }}</span></p>
            <p class="small">Oder es öffnet diesen Link: <a [href]="linkUrl(m.linkCode)">{{ linkUrl(m.linkCode) }}</a></p>
            <p class="muted small">Gültig bis {{ day(m.linkCodeExpires) }}, einmal einlösbar.
              <button type="button" class="btn-link" [disabled]="busy()" (click)="newCode()">Neuen Code erzeugen</button>
              <button type="button" class="btn-link danger" [disabled]="busy()" (click)="unlink()">Code verwerfen</button></p>
          } @else {
            <p>Hat das Kind ein RookHub- oder KidHub-Konto? Mit einem Code verknüpft es sich selbst — danach steht hier, wie viel es trainiert.</p>
            <button type="button" class="btn slim new-code" [disabled]="busy()" (click)="newCode()">Code erzeugen</button>
          }
        </section>

        <section>
          <h2>Weitere Angaben</h2>
          <dl class="facts">
            @if (m.birthDate) { <dt>Geburtsdatum</dt><dd>{{ birth() }}</dd> }
            @if (m.fideId) { <dt>FIDE-ID</dt><dd><a [href]="'https://ratings.fide.com/profile/' + m.fideId" target="_blank" rel="noopener">{{ m.fideId }}</a></dd> }
            @if (m.nationalId) { <dt>ÖSB-Nummer</dt><dd>{{ m.nationalId }}</dd> }
            <dt>Fotos</dt><dd>{{ m.photoConsent === true ? 'Einwilligung liegt vor' : m.photoConsent === false ? 'keine Fotos veröffentlichen' : 'nicht geklärt' }}</dd>
            @if (m.notes) { <dt>Notiz</dt><dd class="pre">{{ m.notes }}</dd> }
            <dt>Angelegt</dt><dd>{{ day(m.createdAt) }}</dd>
          </dl>
        </section>
        <p class="err status-line" role="alert">{{ error() ?? '' }}</p>
      </article>
    } @else if (error()) {
      <section class="gate"><h1>Blatt nicht gefunden</h1><p>{{ error() }}</p><p><a routerLink="/">Zur Kartei</a></p></section>
    } @else {
      <p class="muted">Lade …</p>
    }
  `,
})
export class MemberPageComponent implements OnInit {
  private readonly api = inject(ClubApiService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly allowed = hasClubAccess(this.auth);
  readonly member = signal<Member | null>(null);
  readonly groups = signal<GroupRow[]>([]);
  readonly progress = signal<Progress | null>(null);
  readonly editing = signal(false);
  readonly form = signal<MemberInput>(emptyInput());
  readonly birthText = signal('');
  readonly noteText = signal('');
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  readonly cls = computed(() => ageClass(this.member()?.birthYear, new Date().getFullYear()));
  readonly birth = computed(() => formatBirth(this.member() ?? {}));
  readonly recorded = computed(() => {
    const a = this.member()?.attendance;
    return a ? a.present + a.excused + a.absent : 0;
  });
  readonly attendance = computed(() => attendanceText(this.member()?.attendance.present ?? 0, this.recorded()));

  readonly tel = telHref;
  readonly day = formatDay;
  readonly short = shortDate;
  readonly code = formatLinkCode;

  ngOnInit(): void {
    if (!this.allowed) return;
    const id = Number(this.route.snapshot.paramMap.get('id'));
    if (id) {
      void this.load(id);
    } else {
      const group = Number(this.route.snapshot.queryParamMap.get('gruppe'));
      this.form.set({ ...emptyInput(), groupIds: group ? [group] : [] });
      this.editing.set(true);
      void this.loadGroups();
    }
  }

  private async load(id: number): Promise<void> {
    try {
      this.show(await this.api.member(id));
    } catch (err) {
      this.error.set(apiErrorText(err, 'Das Blatt konnte nicht geladen werden.'));
    }
  }

  private async loadGroups(): Promise<void> {
    try {
      this.groups.set((await this.api.groups()).filter(g => !g.archived));
    } catch { /* ohne Gruppenliste bleibt das Formular benutzbar; der Server prüft die Auswahl ohnehin */ }
  }

  /** Neuen Stand des Blatts übernehmen; der Lernstand aus dem Konto wird nur bei bestehender Verknüpfung geholt. */
  private show(m: Member): void {
    this.member.set(m);
    if (!m.linked) this.progress.set(null);
    else void this.api.progress(m.id).then(p => this.progress.set(p), () => this.progress.set(null));
  }

  startEdit(): void {
    const m = this.member();
    if (!m) return;
    this.form.set(toInput(m));
    this.birthText.set(formatBirth(m));
    this.error.set(null);
    this.editing.set(true);
    void this.loadGroups();
  }

  cancel(): void {
    this.error.set(null);
    if (this.member()) this.editing.set(false);
    else void this.router.navigateByUrl('/');
  }

  set<K extends keyof MemberInput>(key: K, value: MemberInput[K]): void {
    this.form.update(f => ({ ...f, [key]: value }));
  }

  setContact(index: number, patch: Partial<Contact>): void {
    this.form.update(f => ({ ...f, contacts: f.contacts.map((c, i) => i === index ? { ...c, ...patch } : c) }));
  }

  addContact(kind: ContactKind): void {
    this.form.update(f => ({ ...f, contacts: [...f.contacts, { kind, value: '', label: '' }] }));
  }

  removeContact(index: number): void {
    this.form.update(f => ({ ...f, contacts: f.contacts.filter((_, i) => i !== index) }));
  }

  toggleGroup(id: number, on: boolean): void {
    this.form.update(f => ({ ...f, groupIds: on ? [...new Set([...f.groupIds, id])] : f.groupIds.filter(g => g !== id) }));
  }

  setConsent(value: string): void {
    this.set('photoConsent', value === 'yes' ? true : value === 'no' ? false : null);
  }

  async save(): Promise<void> {
    const f = this.form();
    if (!f.firstName.trim() || !f.lastName.trim()) {
      this.error.set('Vor- und Nachname fehlen noch.');
      return;
    }
    const birth = parseBirth(this.birthText());
    if (!birth) {
      this.error.set('Das Geburtsdatum ist so nicht lesbar. Schreib es wie 12.3.2015 — oder nur den Jahrgang, 2015.');
      return;
    }
    const input: MemberInput = {
      ...f, ...birth, firstName: f.firstName.trim(), lastName: f.lastName.trim(),
      level: orNull(f.level), fideId: orNull(f.fideId), nationalId: orNull(f.nationalId), notes: orNull(f.notes),
      // Leer gelassene Zeilen fallen weg; der Server prüft, ob der Rest Nummern bzw. Adressen sind.
      contacts: f.contacts.filter(c => c.value.trim()).map(c => ({ kind: c.kind, value: c.value.trim(), label: orNull(c.label) })),
    };
    this.busy.set(true);
    this.error.set(null);
    try {
      const existing = this.member();
      const saved = existing ? await this.api.updateMember(existing.id, input) : await this.api.createMember(input);
      this.show(saved);
      this.editing.set(false);
      if (!existing) void this.router.navigate(['/kind', saved.id], { replaceUrl: true });
    } catch (err) {
      this.error.set(apiErrorText(err, 'Speichern hat nicht geklappt.'));
    } finally {
      this.busy.set(false);
    }
  }

  async remove(): Promise<void> {
    const m = this.member();
    if (!m || !confirm(`Das Blatt von ${fullName(m)} löschen — mit Kontakten, Notizen und Anwesenheit? Das lässt sich nicht rückgängig machen.`)) return;
    await this.run(async () => {
      await this.api.deleteMember(m.id);
      void this.router.navigateByUrl('/');
    }, 'Löschen hat nicht geklappt.');
  }

  async addNote(): Promise<void> {
    const m = this.member();
    const text = this.noteText().trim();
    if (!m || !text || this.busy()) return;
    await this.run(async () => {
      this.member.set(await this.api.addNote(m.id, text));
      this.noteText.set('');
    }, 'Die Notiz konnte nicht gespeichert werden.');
  }

  async deleteNote(noteId: number): Promise<void> {
    const m = this.member();
    if (!m || !confirm('Diese Notiz löschen?')) return;
    await this.run(async () => this.member.set(await this.api.deleteNote(m.id, noteId)), 'Die Notiz konnte nicht gelöscht werden.');
  }

  async newCode(): Promise<void> {
    const m = this.member();
    if (!m) return;
    await this.run(async () => {
      const c = await this.api.createLinkCode(m.id);
      this.member.set({ ...m, linkCode: c.code, linkCodeExpires: c.expires });
    }, 'Der Code konnte nicht erzeugt werden.');
  }

  async unlink(): Promise<void> {
    const m = this.member();
    if (!m || (m.linked && !confirm('Die Verknüpfung mit dem Konto trennen? Der Lernstand aus dem Konto ist dann hier nicht mehr zu sehen.'))) return;
    await this.run(async () => this.show(await this.api.unlink(m.id)), 'Trennen hat nicht geklappt.');
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

  linkUrl(code: string): string {
    return `${location.origin}/verknuepfen?code=${formatLinkCode(code)}`;
  }

  mark(status: string): string {
    return status === 'present' ? '✓' : status === 'excused' ? 'E' : '–';
  }

  label(status: keyof typeof STATUS_LABEL): string {
    return STATUS_LABEL[status];
  }
}
