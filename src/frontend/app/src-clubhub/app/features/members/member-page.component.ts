import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';
import { firstValueFrom } from 'rxjs';
import { hasClubAccess } from '../../core/club-access';
import { ClubApiService, apiErrorText } from '../../core/club-api.service';
import { Contact, ContactKind, GroupRow, Member, MemberInput, Progress } from '../../core/club.models';
import { DEFAULT_FACE, Face } from '../../core/face';
import { FacePickerComponent } from '../../shared/face-picker.component';
import { MemberPhotoComponent } from '../../shared/member-photo.component';
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
 *
 * Seit 2026-10-02 (Wunsch): ein BILD je Blatt (im Formular wählen, ersetzen, entfernen — hochgeladen wird es nach dem
 * Speichern des Blatts, denn es hängt an dessen Kennung), Personennummer (ÖSB) und FIDE-Nummer, und beim Anlegen der
 * Knopf „Weiteres Kind anlegen": speichert dieses Blatt und öffnet gleich ein leeres (Gruppen bleiben angehakt — man
 * trägt meist eine ganze Gruppe hintereinander ein). Foto-Einwilligung und Notiz zum Kind gibt es weiter nicht.
 *
 * Seit 2026-10-03 (Wunsch „mit einem Kreis sein Gesicht auswählen"): im Formular steht das Bild groß mit einem KREIS
 * darüber (`ch-face-picker`) — verschieben, Größe ändern. Der Ausschnitt ist das Porträt in Kartei, Abhak-Liste und am
 * Kopf des Blatts. Bei einem neuen Bild geht der Kreis mit dem Hochladen mit; bei einem vorhandenen wird nur der Kreis
 * gespeichert (und nur, wenn er angefasst wurde). Das vorhandene Bild holt das Formular dafür groß als Blob.
 */
@Component({
  selector: 'ch-member-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MemberPhotoComponent, FacePickerComponent],
  template: `
    @if (!allowed) {
      <section class="gate"><h1>Nicht freigeschaltet</h1><p>Karteiblätter sehen die Trainer und die Leitung des Vereins.</p></section>
    } @else if (editing()) {
      <form class="sheet form" (submit)="$event.preventDefault(); save()">
        <header class="sheet-head"><h1>{{ member() ? 'Blatt ändern' : (form().isTrainer ? 'Trainer anlegen' : 'Kind anlegen') }}</h1></header>
        @if (lastSaved(); as s) {
          <p class="ok saved-note" role="status"><a [routerLink]="['/kind', s.id]">{{ s.name }}</a> ist gespeichert — hier ist das nächste Blatt.</p>
        }
        <div class="kind-pick" role="radiogroup" aria-label="Kind oder Trainer">
          <label class="check"><input type="radio" name="isTrainer" [checked]="!form().isTrainer" (change)="set('isTrainer', false)"> Kind</label>
          <label class="check"><input type="radio" name="isTrainer" [checked]="form().isTrainer" (change)="set('isTrainer', true)"> Trainer</label>
        </div>
        <div class="grid-2">
          <label class="field"><span>Vorname</span>
            <input #firstName name="firstName" autocomplete="off" maxlength="80" [value]="form().firstName" (input)="set('firstName', $any($event.target).value)"></label>
          <label class="field"><span>Nachname (wenn bekannt)</span>
            <input name="lastName" autocomplete="off" maxlength="80" [value]="form().lastName" (input)="set('lastName', $any($event.target).value)"></label>
        </div>
        <div class="grid-2">
          <label class="field"><span>Geburtsdatum oder Jahrgang</span>
            <input name="birth" inputmode="numeric" autocomplete="off" placeholder="12.3.2015 oder 2015" [value]="birthText()" (input)="birthText.set($any($event.target).value)"></label>
          @if (!form().isTrainer) {
            <label class="field"><span>Stufe oder Diplom</span>
              <input name="level" autocomplete="off" maxlength="60" placeholder="z. B. Bauerndiplom" [value]="form().level ?? ''" (input)="set('level', $any($event.target).value)"></label>
          }
        </div>
        <div class="grid-2">
          <label class="field"><span>Personennummer (ÖSB)</span>
            <input name="nationalId" autocomplete="off" maxlength="16" [value]="form().nationalId ?? ''" (input)="set('nationalId', $any($event.target).value)"></label>
          <label class="field"><span>FIDE-Nummer</span>
            <input name="fideId" inputmode="numeric" autocomplete="off" maxlength="16" [value]="form().fideId ?? ''" (input)="set('fideId', $any($event.target).value)"></label>
        </div>
        <div class="photo-field">
          <span class="photo-label">Bild</span>
          @if (pickerSrc(); as src) {
            <ch-face-picker [src]="src" [face]="face()" [disabled]="busy()" (faceChange)="setFace($event)" />
            <p class="muted small face-hint">Zieh den Kreis aufs Gesicht. Dieser Ausschnitt steht in der Kartei und beim Abhaken vor dem Namen.</p>
          } @else if (keptPhoto(); as k) {
            <ch-member-photo class="portrait" [memberId]="k.id" [version]="k.version" [name]="form().firstName" />
          } @else {
            <span class="avatar portrait"><span class="avatar-ph none">kein Bild</span></span>
          }
          <div class="photo-actions">
            <label class="btn slim upload pick-photo" [class.disabled]="busy()">
              {{ hasPicture() ? 'Anderes Bild wählen' : 'Bild wählen' }}
              <input type="file" name="photo" accept="image/*" hidden [disabled]="busy()" (change)="pickPhoto($any($event.target))">
            </label>
            @if (hasPicture()) { <button type="button" class="btn-link danger drop-photo" [disabled]="busy()" (click)="dropPhoto()">Bild entfernen</button> }
          </div>
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

        <section [hidden]="form().isTrainer">
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
          <label class="check"><input type="checkbox" [checked]="form().archived" (change)="set('archived', $any($event.target).checked)">
            Im Archiv (kommt nicht mehr ins Training)</label>
          <!-- Löschen steht hier am Ende und nicht in der klebenden Leiste: selten, endgültig, kein Griff daneben. -->
          @if (member()?.canDelete) { <p class="mt-s"><button type="button" class="btn-link danger" [disabled]="busy()" (click)="remove()">Blatt löschen</button></p> }
        </section>

        <p class="err status-line" role="alert">{{ error() ?? '' }}</p>
        <!-- Klebt am unteren Rand, solange das Formular im Bild ist: Speichern soll immer erreichbar sein, egal wo man
             gerade schreibt (Wunsch 2026-09-30). -->
        <div class="actions form-save">
          <button type="submit" class="btn primary" [disabled]="busy()">{{ member() ? 'Änderungen speichern' : (form().isTrainer ? 'Trainer anlegen' : 'Kind anlegen') }}</button>
          @if (!member()) {
            <button type="button" class="btn save-next" [disabled]="busy()" title="Speichert dieses Blatt und öffnet gleich ein leeres" (click)="save(true)">{{ form().isTrainer ? 'Weiteren Trainer anlegen' : 'Weiteres Kind anlegen' }}</button>
          }
          <button type="button" class="btn" [disabled]="busy()" (click)="cancel()">{{ lastSaved() && !member() ? 'Fertig' : 'Abbrechen' }}</button>
        </div>
      </form>
    } @else if (member(); as m) {
      <article class="sheet">
        <header class="sheet-head" [class.has-photo]="m.photoVersion != null">
          @if (m.photoVersion != null) { <ch-member-photo class="portrait" [memberId]="m.id" [version]="m.photoVersion" [name]="name()" [zoom]="true" /> }
          <h1>{{ name() }}</h1>
          <button type="button" class="btn slim edit" (click)="startEdit()">Bearbeiten</button>
          <p class="sub">
            @if (m.isTrainer) { <span class="chip">Trainer</span> }
            @if (m.birthYear) { <span>{{ m.birthDate ? 'Geboren ' + birth() : 'Jahrgang ' + m.birthYear }}@if (cls(); as c) {, {{ c }} }</span> }
            @if (m.level) { <span>{{ m.level }}</span> }
            @if (m.nationalId) { <span class="pnr">PNr. {{ m.nationalId }}</span> }
            @if (m.fideId) { <a class="fide" [href]="'https://ratings.fide.com/profile/' + m.fideId" target="_blank" rel="noopener">FIDE {{ m.fideId }}</a> }
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

        <section [hidden]="m.isTrainer">
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
            <p>{{ attendance() }}.</p>
            <ul class="notes">
              @for (a of m.attendance.recent; track a.sessionId) {
                <li><span class="cell" [class]="a.status" aria-hidden="true">{{ mark(a.status) }}</span>
                  {{ short(a.date) }} {{ label(a.status) }}, {{ a.group }}{{ a.topic ? ' — ' + a.topic : '' }}</li>
              }
            </ul>
          } @else { <p class="muted">Noch keine Einheit erfasst.</p> }
        </section>

        <section class="link" [hidden]="m.isTrainer">
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
  private readonly confirm = inject(ConfirmService);

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
  /** Nach „Weiteres Kind anlegen": wessen Blatt gerade gespeichert wurde (Zeile über dem leeren Formular). */
  readonly lastSaved = signal<{ id: number; name: string } | null>(null);

  /** Das im Formular GEWÄHLTE Bild — hochgeladen wird es erst nach dem Speichern des Blatts. */
  private photoFile: File | null = null;
  /** Objekt-Adresse der Vorschau des gewählten Bilds. */
  readonly photoPreview = signal<string | null>(null);
  /** „Bild entfernen" gedrückt: das vorhandene Bild geht beim Speichern. */
  readonly photoRemove = signal(false);
  /** Das Bild, das das Blatt schon hat und behält — `null` ohne Bild oder wenn es entfernt werden soll. */
  readonly keptPhoto = computed(() => {
    const m = this.member();
    return m && m.photoVersion != null && !this.photoRemove() ? { id: m.id, version: m.photoVersion } : null;
  });
  readonly hasPicture = computed(() => !!this.photoPreview() || !!this.keptPhoto());
  /** Das VORHANDENE Bild groß (Objekt-Adresse des Blobs) — für den Kreis im Formular; `null`, solange es lädt. */
  readonly existingPhoto = signal<string | null>(null);
  /** Worüber der Kreis liegt: die Vorschau des gewählten Bilds, sonst das vorhandene Bild. */
  readonly pickerSrc = computed(() => this.photoPreview() ?? (this.keptPhoto() ? this.existingPhoto() : null));
  /** Der Kreis ums Gesicht im Formular; `null` = noch keiner gewählt (der Kreis-Wähler zeigt dann seinen Anfangskreis). */
  readonly face = signal<Face | null>(null);
  /** Der Kreis wurde im Formular angefasst — nur dann wird er bei einem VORHANDENEN Bild gespeichert. */
  private faceTouched = false;
  /** Zählt die Bearbeitungen — ein großes Bild, das für eine frühere geladen wurde, kommt nicht mehr ins Formular. */
  private photoEpoch = 0;
  private readonly firstNameInput = viewChild<ElementRef<HTMLInputElement>>('firstName');

  readonly name = computed(() => { const m = this.member(); return m ? fullName(m) : ''; });
  /** Altersklasse nur für Kinder — bei einem Trainer sagt „U18" nichts. */
  readonly cls = computed(() => this.member()?.isTrainer ? null : ageClass(this.member()?.birthYear, new Date().getFullYear()));
  readonly birth = computed(() => formatBirth(this.member() ?? {}));
  readonly recorded = computed(() => {
    const a = this.member()?.attendance;
    return a ? a.present + a.absent : 0;
  });
  readonly attendance = computed(() => attendanceText(this.member()?.attendance.present ?? 0, this.recorded()));

  readonly tel = telHref;
  readonly day = formatDay;
  readonly short = shortDate;
  readonly code = formatLinkCode;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.clearPhotoDraft());
  }

  ngOnInit(): void {
    if (!this.allowed) return;
    const id = Number(this.route.snapshot.paramMap.get('id'));
    if (id) {
      void this.load(id);
    } else {
      const group = Number(this.route.snapshot.queryParamMap.get('gruppe'));
      const trainer = this.route.snapshot.queryParamMap.get('trainer') === '1';
      this.form.set({ ...emptyInput(), groupIds: group ? [group] : [], isTrainer: trainer });
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
    this.clearPhotoDraft();
    this.face.set(m.photoFace ?? null);
    this.error.set(null);
    this.editing.set(true);
    void this.loadGroups();
    void this.loadExistingPhoto(m);
  }

  /** Das vorhandene Bild groß holen (hinter der Anmeldung, also als Blob) — darüber liegt im Formular der Kreis. */
  private async loadExistingPhoto(m: Member): Promise<void> {
    if (m.photoVersion == null) return;
    const mine = this.photoEpoch;
    try {
      const blob = await this.api.memberPhotoBlob(m.id, false, m.photoVersion);
      if (mine !== this.photoEpoch) return;                              // inzwischen abgebrochen oder neu begonnen
      this.existingPhoto.set(URL.createObjectURL(blob));
    } catch { /* ohne das große Bild bleibt das kleine Porträt stehen — der Kreis lässt sich dann gerade nicht ändern */ }
  }

  /** Der Kreis wurde verschoben oder in der Größe geändert. */
  setFace(face: Face): void {
    this.face.set(face);
    this.faceTouched = true;
  }

  cancel(): void {
    this.error.set(null);
    this.clearPhotoDraft();
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

  /** Ein Bild gewählt (Datei oder, am Handy, direkt die Kamera) — es ersetzt Auswahl und „entfernen" von vorher. */
  pickPhoto(input: HTMLInputElement): void {
    const file = input.files?.[0] ?? null;
    input.value = '';                                                    // dieselbe Datei soll noch einmal gehen
    if (!file) return;
    this.clearPhotoDraft();
    this.photoFile = file;
    this.photoPreview.set(URL.createObjectURL(file));
    this.face.set(DEFAULT_FACE);                                         // ein neues Bild beginnt mit dem Anfangskreis
  }

  /** Kein Bild: die Auswahl fällt weg, und ein vorhandenes Bild geht beim Speichern. */
  dropPhoto(): void {
    this.clearPhotoDraft();
    this.photoRemove.set(true);
  }

  private clearPhotoDraft(): void {
    this.photoEpoch++;
    for (const url of [this.photoPreview(), this.existingPhoto()]) if (url) URL.revokeObjectURL(url);
    this.photoFile = null;
    this.photoPreview.set(null);
    this.existingPhoto.set(null);
    this.photoRemove.set(false);
    this.face.set(null);
    this.faceTouched = false;
  }

  /**
   * Das Bild nach dem Speichern des Blatts nachziehen: hochladen (samt Kreis ums Gesicht), entfernen — oder bei einem
   * vorhandenen Bild nur den angefassten Kreis speichern. Liefert das Blatt mit der neuen Marke.
   */
  private async syncPhoto(saved: Member): Promise<Member> {
    if (this.photoFile) {
      const state = await this.api.uploadMemberPhoto(saved.id, this.photoFile, this.face() ?? DEFAULT_FACE);
      this.clearPhotoDraft();
      return { ...saved, photoVersion: state.photoVersion, photoFace: state.face ?? null };
    }
    if (this.photoRemove() && saved.photoVersion != null) {
      await this.api.deleteMemberPhoto(saved.id);
      this.clearPhotoDraft();
      return { ...saved, photoVersion: null, photoFace: null };
    }
    const face = this.face();
    if (this.faceTouched && face && saved.photoVersion != null) {
      const state = await this.api.setMemberPhotoFace(saved.id, face);
      this.clearPhotoDraft();
      return { ...saved, photoVersion: state.photoVersion, photoFace: state.face ?? face };
    }
    return saved;
  }

  /**
   * Speichern. `andNew` (nur beim Anlegen): danach kein Wechsel aufs Blatt, sondern gleich ein leeres Formular für das
   * nächste Kind — Gruppen und „Kind/Trainer" bleiben, wie sie waren.
   */
  async save(andNew = false): Promise<void> {
    if (this.busy()) return;
    const f = this.form();
    if (!f.firstName.trim()) {
      this.error.set('Der Vorname fehlt noch.');
      return;
    }
    const birth = parseBirth(this.birthText());
    if (!birth) {
      this.error.set('Das Geburtsdatum ist so nicht lesbar. Schreib es wie 12.3.2015 — oder nur den Jahrgang, 2015.');
      return;
    }
    const fideId = orNull(f.fideId);
    if (fideId && !/^\d+$/.test(fideId)) {
      this.error.set('Die FIDE-Nummer besteht nur aus Ziffern.');
      return;
    }
    const input: MemberInput = {
      ...f, ...birth, firstName: f.firstName.trim(), lastName: f.lastName.trim(),
      level: orNull(f.level), fideId, nationalId: orNull(f.nationalId),
      // Leer gelassene Zeilen fallen weg; der Server prüft, ob der Rest Nummern bzw. Adressen sind.
      contacts: f.contacts.filter(c => c.value.trim()).map(c => ({ kind: c.kind, value: c.value.trim(), label: orNull(c.label) })),
    };
    this.busy.set(true);
    this.error.set(null);
    try {
      const existing = this.member();
      let saved = existing ? await this.api.updateMember(existing.id, input) : await this.api.createMember(input);
      try {
        saved = await this.syncPhoto(saved);
      } catch (err) {
        // Das Blatt IST gespeichert, nur das Bild nicht: das Formular bleibt als „Blatt ändern" offen (ein zweites
        // Speichern legte sonst dasselbe Kind noch einmal an), die Bildauswahl bleibt für den nächsten Versuch.
        this.show(saved);
        this.form.set(toInput(saved));
        this.birthText.set(formatBirth(saved));
        this.error.set(`Das Blatt ist gespeichert, das Bild aber nicht: ${apiErrorText(err, 'es ließ sich nicht hochladen.')}`);
        return;
      }
      if (andNew && !existing) {
        this.startNext(saved, f);
        return;
      }
      this.show(saved);
      this.editing.set(false);
      this.clearPhotoDraft();                                            // auch das große Bild fürs Formular wieder freigeben
      this.lastSaved.set(null);
      // Von „/kind/neu" aus (auch nach einem ersten Versuch, bei dem nur das Bild scheiterte) weiter auf die Adresse des Blatts.
      if (!this.route.snapshot.paramMap.get('id')) void this.router.navigate(['/kind', saved.id], { replaceUrl: true });
    } catch (err) {
      this.error.set(apiErrorText(err, 'Speichern hat nicht geklappt.'));
    } finally {
      this.busy.set(false);
    }
  }

  /** Nach „Weiteres Kind anlegen": leeres Formular, Gruppen und Art bleiben, der Cursor steht im Vornamen. */
  private startNext(saved: Member, previous: MemberInput): void {
    this.lastSaved.set({ id: saved.id, name: fullName(saved) });
    this.member.set(null);
    this.progress.set(null);
    this.form.set({ ...emptyInput(), isTrainer: previous.isTrainer, groupIds: previous.isTrainer ? [] : [...previous.groupIds] });
    this.birthText.set('');
    this.clearPhotoDraft();
    setTimeout(() => this.firstNameInput()?.nativeElement.focus());
  }

  async remove(): Promise<void> {
    const m = this.member();
    if (!m || !(await firstValueFrom(this.confirm.ask(`Das Blatt von ${fullName(m)} löschen — mit Kontakten, Notizen und Anwesenheit? Das lässt sich nicht rückgängig machen.`)))) return;
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
    if (!m || !(await firstValueFrom(this.confirm.ask('Diese Notiz löschen?')))) return;
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
    if (!m || (m.linked && !(await firstValueFrom(this.confirm.ask('Die Verknüpfung mit dem Konto trennen? Der Lernstand aus dem Konto ist dann hier nicht mehr zu sehen.'))))) return;
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
    return status === 'present' ? '✓' : '–';
  }

  label(status: keyof typeof STATUS_LABEL): string {
    return STATUS_LABEL[status];
  }
}
