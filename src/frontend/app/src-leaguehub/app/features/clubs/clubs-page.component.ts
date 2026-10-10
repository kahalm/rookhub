import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '@rh/core/auth.service';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';
import { LeagueApiService } from '../../core/league-api.service';
import { ClubContextService } from '../../core/club-context.service';
import { AdminClub, ClubInput, RhGroup } from '../../core/league.models';

/** Höchstlängen wie der Server (`LeagueClubAdminService.MaxName/MaxTeamPrefix/MaxAnonName`). */
export const CLUB_LIMITS = { name: 120, teamPrefix: 80, anonName: 60 } as const;

/** Region und Quellen getrennt — in der Tabelle steht die Quelle als graue zweite Zeile (UI-Sweep 2026-10-10, l-clubs-table). */
export function clubRegionName(region: string | null | undefined): string {
  return region === 'bayern' ? 'Bayern' : 'Tirol';
}
export function clubRegionSources(region: string | null | undefined): string {
  return region === 'bayern' ? 'Ligamanager + Schachkreis Zugspitze' : 'chess-results';
}

/** Die Region in Worten (0.704.0; vorher die Liga-Quelle). */
export function clubRegionText(region: string | null | undefined): string {
  return region === 'bayern' ? 'Bayern (Ligamanager + Schachkreis Zugspitze)' : 'Tirol (chess-results)';
}

/** Absage des Servers (`reason`) als Satz. */
export function clubErrorText(reason: string | undefined, status = 400): string {
  switch (reason) {
    case 'invalidName': return `Der Name fehlt oder ist zu lang (höchstens ${CLUB_LIMITS.name} Zeichen).`;
    case 'invalidTeamPrefix': return `Der Mannschafts-Präfix fehlt oder ist zu lang (höchstens ${CLUB_LIMITS.teamPrefix} Zeichen).`;
    case 'invalidAnonName': return `Der Anzeigename anonymisierter Spieler fehlt oder ist zu lang (höchstens ${CLUB_LIMITS.anonName} Zeichen).`;
    case 'invalidRegion': return 'Diese Region kennt LeagueHub nicht.';
    case 'duplicate': return 'Einen Verein mit diesem Namen gibt es schon.';
    case 'notFound': case 'clubNotFound': return 'Diesen Verein gibt es nicht mehr — die Liste ist neu geladen.';
    case 'groupNotFound': return 'Diese Gruppe gibt es nicht mehr — die Liste ist neu geladen.';
    case 'everyone': return '„Everyone" kann keinem Verein gehören — sonst gehörte jedes Konto dazu.';
    default: return status === 403 ? 'Das dürfen nur Admins mit dem Recht „league.manage".' : 'Das hat nicht geklappt — der Server antwortet gerade nicht.';
  }
}

/** „07.10.2026". */
export function clubDate(iso: string | null | undefined): string {
  return iso && iso.length >= 10 ? `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}` : '—';
}

const EMPTY: ClubInput = { name: '', teamPrefix: '', anonName: '', region: 'tirol' };

/**
 * Vereine verwalten (0.700.0, Wunsch 2026-10-07): die Mandanten von LeagueHub anlegen, ändern und ihnen Gruppen zuordnen —
 * bisher nur per API (`/api/league/admin/clubs…`, Haupt-CLAUDE.md „LeagueHub — Vereine als Mandanten"). Nur Admins mit
 * `league.manage` (der Server prüft beides). Nach jeder Änderung holt {@link ClubContextService.reload} die Vereine des Kontos
 * neu, damit der Umschalter im Kopf den neuen Verein kennt.
 */
@Component({
  selector: 'lh-clubs-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (!allowed) {
      <section class="gate">
        <h2>Nicht freigeschaltet</h2>
        <p>Vereine verwalten nur Admins mit dem Recht „league.manage".</p>
      </section>
    } @else {
      <section class="clubs-page">
        <h2>Vereine</h2>
        <p class="muted clubs-help">Ein Verein hat in LeagueHub seine eigene Vereins-Datenbank, eigene Partieformulare, Entwürfe und
          Teilen-Links — kein Verein sieht die eines anderen. Wer in einer Gruppe des Vereins ist, arbeitet in diesem Verein; was er
          darf (ansehen, beitragen, verwalten), kommt weiter aus den Rollen der Gruppe. Wer in keiner Vereinsgruppe ist, sieht nichts.</p>

        @if (msg(); as m) { <p class="update-msg" [class.err]="m.err" role="status">{{ m.text }}</p> }
        @if (loadError()) { <p class="err">{{ loadError() }}</p> }
        @if (loading() && !clubs().length) { <p class="muted">Lade …</p> }

        <div class="clubs-bar">
          <button type="button" class="btn-sec" (click)="startNew()" [disabled]="editing() === 'new'">Neuer Verein</button>
        </div>

        @if (clubs().length) {
          <table class="rtable clubs-tbl">
            <thead>
              <tr>
                <th>Verein</th>
                <th class="hide-s">Anonym als</th>
                <th class="hide-s">Mannschaften</th>
                <th class="hide-s">Region</th>
                <th class="num">Gruppen</th>
                <th class="num">Partien</th>
                <th class="hide-s">Angelegt</th>
                <th><span class="sr">Aktion</span></th>
              </tr>
            </thead>
            <tbody>
              @for (c of clubs(); track c.id) {
                <tr [class.sel]="editing() === c.id">
                  <td class="club-name">{{ c.name }}
                    <span class="small show-s">{{ regionText(c.region) }} · anonym „{{ c.anonName }}"</span>
                  </td>
                  <td class="hide-s nowrap">{{ c.anonName }}</td>
                  <td class="hide-s nowrap">{{ c.teamPrefix }}</td>
                  <td class="hide-s region">{{ regionName(c.region) }}<span class="region-src">{{ regionSources(c.region) }}</span></td>
                  <td class="num">{{ c.groups.length }}</td>
                  <td class="num">{{ c.clubGames }}</td>
                  <td class="hide-s">{{ date(c.createdAt) }}</td>
                  <td><button type="button" class="btn-link" (click)="edit(c)">Bearbeiten</button></td>
                </tr>
              }
            </tbody>
          </table>
        } @else if (!loading() && !loadError()) {
          <p class="muted">Noch kein Verein angelegt.</p>
        }

        @if (editing() !== null) {
          <section class="panel club-edit">
            <h3 class="club-h3">{{ editing() === 'new' ? 'Neuer Verein' : 'Verein ändern: ' + (selected()?.name ?? '') }}</h3>
            <form (submit)="$event.preventDefault(); save()">
              <label class="field">Name
                <input name="name" autocomplete="off" [maxLength]="limits.name" [value]="form().name"
                       (input)="set('name', $any($event.target).value)" placeholder="SK Weilheim" />
              </label>
              <label class="field">Mannschafts-Präfix
                <input name="teamPrefix" autocomplete="off" [maxLength]="limits.teamPrefix" [value]="form().teamPrefix"
                       (input)="set('teamPrefix', $any($event.target).value)" placeholder="SK Weilheim" />
                <span class="field-hint">So beginnen die Mannschaften in Spielplan und Meldeliste, z. B. „SK Weilheim" für
                  „SK Weilheim 1" und „SK Weilheim 2".</span>
              </label>
              <label class="field">Anzeigename anonymisierter Spieler
                <input name="anonName" autocomplete="off" [maxLength]="limits.anonName" [value]="form().anonName"
                       (input)="set('anonName', $any($event.target).value)" placeholder="Weilheim" />
                <span class="field-hint">Unter diesem Namen erscheinen Spieler des Vereins, deren Partien anonym hochgeladen wurden.</span>
              </label>
              <label class="field">Region
                <select name="region" (change)="set('region', $any($event.target).value)">
                  <option value="tirol" [selected]="form().region === 'tirol'">Tirol (chess-results)</option>
                  <option value="bayern" [selected]="form().region === 'bayern'">Bayern (Ligamanager + Schachkreis Zugspitze)</option>
                </select>
                <span class="field-hint">Die Startseite zeigt die Ligen aller Quellen dieser Region.</span>
              </label>
              @if (formError()) { <p class="err" role="alert">{{ formError() }}</p> }
              <div class="actions">
                <button type="submit" class="btn-pri" [disabled]="saving() || !canSave()">{{ saving() ? 'Speichert …' : (editing() === 'new' ? 'Verein anlegen' : 'Speichern') }}</button>
                <button type="button" class="btn-link" (click)="close()">Schließen</button>
              </div>
            </form>

            @if (selected(); as c) {
              <h3 class="club-h3">Gruppen des Vereins</h3>
              <ul class="club-groups">
                @for (g of c.groups; track g.id) {
                  <li>
                    <span>{{ g.name }} <span class="small muted">· {{ g.members }} {{ g.members === 1 ? 'Konto' : 'Konten' }}</span></span>
                    <button type="button" class="btn-link del" [disabled]="busyGroup() === g.id" (click)="removeGroup(c, g)">Entfernen</button>
                  </li>
                } @empty {
                  <li class="muted">Noch keine Gruppe — bis eine zugeordnet ist, arbeitet niemand außer den Admins in diesem Verein.</li>
                }
              </ul>
              <p class="small muted">Wer in einer dieser Gruppen ist, gehört zum Verein. Beim Zuordnen gibt LeagueHub der Gruppe
                auch den Taktik-Kurs des Vereins frei (beim Entfernen wird die Freigabe zurückgenommen).</p>

              <label class="field group-search">Gruppe hinzufügen
                <input type="search" autocomplete="off" placeholder="Gruppe suchen …" [value]="query()"
                       (input)="query.set($any($event.target).value)" />
              </label>
              @if (groupsError()) { <p class="err">{{ groupsError() }}</p> }
              @if (query().trim()) {
                <ul class="group-hits">
                  @for (h of hits(); track h.group.id) {
                    <li [class.off]="!!h.owner">
                      <span>{{ h.group.name }} <span class="small muted">· {{ h.group.memberCount }} {{ h.group.memberCount === 1 ? 'Konto' : 'Konten' }}</span>
                        @if (h.owner) { <span class="small muted owner">gehört schon zu {{ h.owner }} — eine Gruppe gehört zu höchstens einem Verein</span> }
                      </span>
                      <button type="button" class="btn-sec" [disabled]="!!h.owner || busyGroup() === h.group.id" (click)="addGroup(c, h.group)">Zuordnen</button>
                    </li>
                  } @empty {
                    <li class="muted">Keine passende Gruppe.</li>
                  }
                </ul>
              }
            }
          </section>
        }
      </section>
    }
  `,
})
export class ClubsPageComponent implements OnInit {
  private readonly api = inject(LeagueApiService);
  private readonly auth = inject(AuthService);
  private readonly confirm = inject(ConfirmService);
  private readonly context = inject(ClubContextService);

  /** Der Server verlangt Admin UND `league.manage`. */
  readonly allowed = !!this.auth.isAdmin && this.auth.has('league.manage');
  readonly limits = CLUB_LIMITS;
  readonly regionText = clubRegionText;
  readonly regionName = clubRegionName;
  readonly regionSources = clubRegionSources;
  readonly date = clubDate;

  readonly clubs = signal<AdminClub[]>([]);
  readonly groups = signal<RhGroup[]>([]);
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly groupsError = signal<string | null>(null);
  readonly msg = signal<{ text: string; err: boolean } | null>(null);

  /** `null` = kein Formular offen, `'new'` = anlegen, Zahl = Id des Vereins. */
  readonly editing = signal<number | 'new' | null>(null);
  readonly form = signal<ClubInput>({ ...EMPTY });
  readonly formError = signal<string | null>(null);
  readonly saving = signal(false);
  readonly query = signal('');
  readonly busyGroup = signal<number | null>(null);

  readonly selected = computed(() => {
    const id = this.editing();
    return typeof id === 'number' ? this.clubs().find(c => c.id === id) ?? null : null;
  });
  readonly canSave = computed(() => {
    const f = this.form();
    return !!f.name.trim() && !!f.teamPrefix.trim() && !!f.anonName.trim();
  });

  /** Treffer der Gruppensuche: ohne „Everyone" und ohne die eigenen; Gruppen eines ANDEREN Vereins mit dessen Namen (nicht wählbar). */
  readonly hits = computed(() => {
    const club = this.selected();
    const q = this.query().trim().toLowerCase();
    if (!club || !q) return [];
    const owner = new Map<number, string>();
    for (const c of this.clubs()) for (const g of c.groups) owner.set(g.id, c.name);
    return this.groups()
      .filter(g => !g.isEveryone && !club.groups.some(m => m.id === g.id) && g.name.toLowerCase().includes(q))
      .slice(0, 12)
      .map(g => ({ group: g, owner: owner.get(g.id) ?? null }));
  });

  ngOnInit(): void {
    if (!this.allowed) return;
    void this.load();
    void this.loadGroups();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.loadError.set(null);
    try {
      this.clubs.set(await this.api.adminClubs());
    } catch {
      this.loadError.set('Die Vereine konnten nicht geladen werden.');
    } finally {
      this.loading.set(false);
    }
  }

  async loadGroups(): Promise<void> {
    this.groupsError.set(null);
    try {
      this.groups.set(await this.api.groups());
    } catch {
      this.groupsError.set('Die Gruppen konnten nicht geladen werden.');
    }
  }

  startNew(): void {
    this.editing.set('new');
    this.form.set({ ...EMPTY });
    this.formError.set(null);
    this.msg.set(null);
  }

  edit(c: AdminClub): void {
    this.editing.set(c.id);
    this.form.set({ name: c.name, teamPrefix: c.teamPrefix, anonName: c.anonName, region: c.region || 'tirol' });
    this.formError.set(null);
    this.query.set('');
    this.msg.set(null);
  }

  close(): void {
    this.editing.set(null);
    this.formError.set(null);
  }

  set(field: keyof ClubInput, value: string): void {
    this.form.update(f => ({ ...f, [field]: value }));
  }

  async save(): Promise<void> {
    if (!this.canSave() || this.saving()) return;
    const id = this.editing();
    const f = this.form();
    const input: ClubInput = { name: f.name.trim(), teamPrefix: f.teamPrefix.trim(), anonName: f.anonName.trim(), region: f.region };
    const old = this.selected();
    // Die Region nachträglich ändern: die Startseite des Vereins zeigt danach die Ligen der anderen Region.
    if (old && (old.region || 'tirol') !== input.region && !(await firstValueFrom(this.confirm.ask(
      `Region von „${old.name}" auf ${clubRegionText(input.region)} umstellen? Die Startseite des Vereins zeigt dann ` +
      `nur noch Ligen dieser Region, und „einer von uns" wird dort gesucht. Vereinspartien und Formulare bleiben.`)))) return;
    this.saving.set(true);
    this.formError.set(null);
    try {
      const club = id === 'new' ? await this.api.createClub(input) : await this.api.updateClub(id as number, input);
      await this.load();
      // ein neuer Verein bleibt offen — als Nächstes kommt die Gruppe
      if (id === 'new') this.edit(this.clubs().find(c => c.id === club.id) ?? { ...club, groups: [], clubGames: 0, createdAt: null });
      this.msg.set({ text: id === 'new' ? `Verein „${club.name}" angelegt — jetzt eine Gruppe zuordnen.` : `„${club.name}" gespeichert.`, err: false });
      this.refreshContext();
    } catch (err) {
      const e = err instanceof HttpErrorResponse ? err : null;
      this.formError.set(clubErrorText(e?.error?.reason, e?.status));
      if (e?.status === 404) await this.load();
    } finally {
      this.saving.set(false);
    }
  }

  async addGroup(club: AdminClub, group: RhGroup): Promise<void> {
    this.busyGroup.set(group.id);
    this.msg.set(null);
    try {
      await this.api.addClubGroup(club.id, group.id);
      this.msg.set({ text: `Gruppe „${group.name}" gehört jetzt zu ${club.name}.`, err: false });
      this.query.set('');
      await this.load();
      this.refreshContext();
    } catch (err) {
      const e = err instanceof HttpErrorResponse ? err : null;
      this.msg.set({ text: clubErrorText(e?.error?.reason, e?.status), err: true });
      if (e?.status === 404) { await this.load(); await this.loadGroups(); }
    } finally {
      this.busyGroup.set(null);
    }
  }

  async removeGroup(club: AdminClub, group: { id: number; name: string; members: number }): Promise<void> {
    if (!(await firstValueFrom(this.confirm.ask(
      `Gruppe „${group.name}" von ${club.name} lösen? Ihre ${group.members} ${group.members === 1 ? 'Konto sieht' : 'Konten sehen'} ` +
      `die Vereins-Datenbank dann nicht mehr, und die Freigabe des Taktik-Kurses fällt weg.`)))) return;
    this.busyGroup.set(group.id);
    this.msg.set(null);
    try {
      await this.api.removeClubGroup(club.id, group.id);
      this.msg.set({ text: `Gruppe „${group.name}" gehört nicht mehr zu ${club.name}.`, err: false });
      await this.load();
      this.refreshContext();
    } catch (err) {
      const e = err instanceof HttpErrorResponse ? err : null;
      this.msg.set({ text: e?.status === 404 ? 'Die Gruppe gehörte schon nicht mehr dazu — die Liste ist neu geladen.' : clubErrorText(e?.error?.reason, e?.status), err: true });
      if (e?.status === 404) await this.load();
    } finally {
      this.busyGroup.set(null);
    }
  }

  /** Der Umschalter im Kopf soll den neuen/geänderten Verein kennen. */
  private refreshContext(): void {
    this.context.reload().subscribe();
  }
}
