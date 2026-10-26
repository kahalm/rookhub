import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnInit, inject, linkedSignal, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, catchError, debounceTime, distinctUntilChanged, of, switchMap } from 'rxjs';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatChipsModule } from '@angular/material/chips';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AdminService, AdminUser, Group, GroupMember } from '../../../core/admin.service';
import { AuthService } from '../../../core/auth.service';
import { SnackbarService } from '../../../core/snackbar.service';
import { apiErrorText } from '../../../core/api-error';
import { ConfirmService } from '../../../shared/confirm-dialog/confirm-dialog.component';
import { LatestRequest } from '../../../shared/latest-request.util';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { clampGoal } from '../../training-goals/goal.util';
import { AdminGroupsStore } from '../admin-groups.store';

/**
 * Admin-Tab „Gruppen" (groups.manage): anlegen/löschen, Mitglieder (Suche + Liste, 0.591.0), Gruppe in Gruppe
 * (2026-10-14) und Trainingsziel-Vorlage je Gruppe. Aus `AdminComponent` ausgelagert (Zerlegung Z30). Die Gruppenliste
 * gehört der Seite ({@link AdminGroupsStore}): nach jeder Änderung hier neu geladen, damit Bücher, Menü und Rollen
 * dieselbe Liste sehen (F5-020).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-admin-groups-tab',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatTableModule, MatButtonModule, MatIconModule, MatFormFieldModule,
    MatInputModule, MatChipsModule, MatSelectModule, TranslatePipe, LoadingSpinnerComponent,
  ],
  templateUrl: './admin-groups-tab.component.html',
  styleUrl: './admin-groups-tab.component.scss',
})
export class AdminGroupsTabComponent implements OnInit {
  private readonly adminService = inject(AdminService);
  private readonly auth = inject(AuthService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);
  private readonly confirm = inject(ConfirmService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly store = inject(AdminGroupsStore);

  readonly groups = this.store.groups;
  readonly groupsLoading = this.store.groupsLoading;
  readonly groupColumns = ['name', 'memberCount', 'actions'];
  readonly newGroupName = signal('');
  readonly newGroupDescription = signal('');
  /** Gewählte Gruppe; nach jedem Neuladen der Liste auf deren frischen Stand gezogen (verschwunden → keine Auswahl). */
  readonly selectedGroup = linkedSignal<Group[], Group | null>({
    source: this.store.groups,
    computation: (groups, previous) => {
      const current = previous?.value ?? null;
      return current ? groups.find(g => g.id === current.id) ?? null : null;
    },
  });
  readonly groupMembers = signal<GroupMember[]>([]);
  readonly membersLoading = signal(false);
  /** Mitgliederliste der GEWÄHLTEN Gruppe: ein Gruppenwechsel bricht das Laden der vorigen ab. */
  private readonly membersRequest = new LatestRequest();
  readonly allUsers = signal<AdminUser[]>([]);
  /** Suche beim Hinzufügen (0.591.0): leer = die neuesten Konten aus {@link allUsers}, sonst Server-Suche. */
  readonly memberSearch = signal('');
  readonly memberCandidates = signal<AdminUser[]>([]);
  readonly addingUserId = signal<number | null>(null);
  /** Nicht-Mitglieder der gewählten Gruppe; wird bei Datenänderung neu berechnet statt je CD-Zyklus (Set + filter). */
  readonly availableUsers = signal<AdminUser[]>([]);
  private searchResults: AdminUser[] | null = null;
  private readonly memberSearch$ = new Subject<string>();
  /** Höchstens so viele Treffer in der Liste — mehr liest niemand, die Suche grenzt weiter ein. */
  static readonly CandidateLimit = 30;

  /** Trainingsziel-Vorlage der ausgewählten Gruppe (ein Tageszeit-Ziel + Wochenziele); Felder per ngModel. */
  goalEdit = { dailyMinutes: 0, playGames: 0, weeklyDaysTarget: 0 };
  readonly goalHasTemplate = signal(false);
  readonly goalLoading = signal(false);
  /** Erst nach erfolgreichem Laden der GEWÄHLTEN Gruppe true — sonst stünde nach einem Ladefehler noch die
   *  Vorlage der vorher gewählten Gruppe im Formular, und Speichern schriebe sie auf die neue. */
  readonly goalLoaded = signal(false);

  /** So viele der NEUESTEN Konten lädt die Mitglieder-Vorschlagsliste vorab (Server-Höchstwert je Seite). Ältere Konten
   *  findet die Server-Suche ab zwei Zeichen. Vorher wurden 500 angefordert, der Server klemmt aber auf 100 und
   *  sortierte nach Id — die Vorschläge „neueste zuerst“ kamen so nur aus den 100 ÄLTESTEN Konten (F5-010). */
  private static readonly MEMBER_PICKER_CAP = 100;

  constructor() {
    // Suche beim Hinzufügen von Mitgliedern: ab zwei Zeichen über den Server (auch jenseits der vorab geladenen neuesten).
    this.memberSearch$.pipe(
      debounceTime(250),
      distinctUntilChanged(),
      // catchError INNEN: ein Fehler fällt auf die vorab geladenen Konten zurück, ohne den Strom zu beenden
      // (sonst reagierte die Suche bis zum Neuladen nicht mehr).
      switchMap(q => q.trim().length < 2 ? of(null) : this.adminService.getUsers(q.trim(), 1, 50).pipe(catchError(() => of(null)))),
      takeUntilDestroyed(),
    ).subscribe(res => { this.searchResults = res?.items ?? null; this.recomputeAvailableUsers(); });
  }

  ngOnInit(): void {
    // Mitglieder-Auswahl: kommt aus der Nutzerliste (users.manage).
    if (this.auth.has('users.manage')) this.loadAllUsers();
  }

  loadAllUsers(): void {
    const cap = AdminGroupsTabComponent.MEMBER_PICKER_CAP;
    this.adminService.getUsers('', 1, cap, 'createdDesc').subscribe({
      next: res => {
        this.allUsers.set(res.items);
        if (res.items.length < res.totalCount) {
          // Mehr Konten als vorab geladen → ältere nur über die Suche. Nicht still schlucken.
          console.warn(`[admin] Mitglieder-Vorschläge: ${res.items.length} neueste von ${res.totalCount} Usern geladen — ältere über die Suche.`);
        }
        this.recomputeAvailableUsers();
      },
      error: () => this.snackbar.info(this.translate.instant('admin.users.errors.load'))
    });
  }

  createGroup(): void {
    const name = this.newGroupName().trim();
    if (!name) return;
    this.adminService.createGroup(name, this.newGroupDescription().trim() || null).subscribe({
      next: () => {
        this.snackbar.info(this.translate.instant('admin.groups.created', { name }));
        this.newGroupName.set('');
        this.newGroupDescription.set('');
        this.store.loadGroups();
      },
      error: err => {
        this.snackbar.info(apiErrorText(err, this.translate, 'admin.groups.errors.create'));
      }
    });
  }

  deleteGroup(group: Group): void {
    this.confirm.ask('admin.groups.deleteConfirm', { name: group.name }).subscribe(ok => {
      if (!ok) return;
      this.adminService.deleteGroup(group.id).subscribe({
        next: () => {
          this.snackbar.info(this.translate.instant('admin.groups.deleted', { name: group.name }));
          if (this.selectedGroup()?.id === group.id) {
            this.membersRequest.cancel();
            this.membersLoading.set(false);
            this.selectedGroup.set(null);
            this.groupMembers.set([]);
            this.recomputeAvailableUsers();
          }
          this.store.loadGroups();
        },
        error: err => {
          this.snackbar.info(apiErrorText(err, this.translate, 'admin.groups.errors.delete'));
        }
      });
    });
  }

  selectGroup(group: Group): void {
    this.selectedGroup.set(group);
    this.memberSearch.set('');
    this.searchResults = null;
    this.loadMembers(group.id);
    this.loadGroupGoal(group.id);
  }

  loadGroupGoal(groupId: number): void {
    this.goalLoading.set(true);
    this.goalLoaded.set(false);
    this.goalHasTemplate.set(false);
    this.goalEdit = { dailyMinutes: 0, playGames: 0, weeklyDaysTarget: 0 };
    this.adminService.getGroupTrainingGoal(groupId).subscribe({
      next: g => {
        const selected = this.selectedGroup();
        if (selected && selected.id !== groupId) return;   // veraltete Antwort
        this.goalHasTemplate.set(g.source === 'group');
        this.goalEdit = {
          dailyMinutes: g.dailyMinutes,
          playGames: g.playGames,
          weeklyDaysTarget: g.weeklyDaysTarget,
        };
        this.goalLoaded.set(true);
        this.goalLoading.set(false);
        this.cdr.markForCheck();   // goalEdit ist ein einfaches Objekt (ngModel)
      },
      error: () => {
        const selected = this.selectedGroup();
        if (selected && selected.id !== groupId) return;
        this.snackbar.info(this.translate.instant('admin.groups.goal.errors.load'));
        this.goalLoading.set(false);
      }
    });
  }

  saveGroupGoal(): void {
    const group = this.selectedGroup();
    if (!group || !this.goalLoaded()) return;
    const goal = {
      dailyMinutes: clampGoal(this.goalEdit.dailyMinutes, 600),
      playGames: clampGoal(this.goalEdit.playGames, 200),
      weeklyDaysTarget: clampGoal(this.goalEdit.weeklyDaysTarget, 7),
    };
    this.adminService.setGroupTrainingGoal(group.id, goal).subscribe({
      next: () => { this.goalHasTemplate.set(true); this.snackbar.info(this.translate.instant('admin.groups.goal.saved')); },
      error: () => this.snackbar.info(this.translate.instant('admin.groups.goal.errors.save')),
    });
  }

  clearGroupGoal(): void {
    const group = this.selectedGroup();
    if (!group || !this.goalLoaded()) return;
    this.adminService.deleteGroupTrainingGoal(group.id).subscribe({
      next: () => {
        this.goalHasTemplate.set(false);
        this.goalEdit = { dailyMinutes: 0, playGames: 0, weeklyDaysTarget: 0 };
        this.cdr.markForCheck();
        this.snackbar.info(this.translate.instant('admin.groups.goal.cleared'));
      },
      error: () => this.snackbar.info(this.translate.instant('admin.groups.goal.errors.save')),
    });
  }

  loadMembers(groupId: number): void {
    this.membersLoading.set(true);
    this.membersRequest.run(this.adminService.getGroupMembers(groupId), {
      next: members => {
        this.groupMembers.set(members);
        this.recomputeAvailableUsers();
        this.membersLoading.set(false);
      },
      error: () => {
        this.snackbar.info(this.translate.instant('admin.groups.errors.loadMembers'));
        this.membersLoading.set(false);
      }
    });
  }

  private recomputeAvailableUsers(): void {
    const memberIds = new Set(this.groupMembers().map(m => m.userId));
    const available = this.allUsers().filter(u => !memberIds.has(u.id));
    this.availableUsers.set(available);
    // Ohne Suche die NEUESTEN zuerst (wer gerade dazukommen soll, hat sich meist eben registriert), mit Suche die Treffer
    // des Servers — auch jenseits der vorab geladenen.
    const source = this.searchResults ?? [...available].sort((a, b) => (b.createdAt ?? '').localeCompare(a.createdAt ?? ''));
    this.memberCandidates.set(source.filter(u => !memberIds.has(u.id)).slice(0, AdminGroupsTabComponent.CandidateLimit));
  }

  onMemberSearch(q: string): void {
    if (q.trim().length < 2) { this.searchResults = null; this.recomputeAvailableUsers(); }
    this.memberSearch$.next(q);
  }

  addMember(user: AdminUser): void {
    const group = this.selectedGroup();
    if (!group || this.addingUserId()) return;
    const groupId = group.id;
    this.addingUserId.set(user.id);
    this.adminService.addGroupMember(groupId, user.id).subscribe({
      next: () => {
        this.addingUserId.set(null);
        this.snackbar.info(this.translate.instant('admin.groups.added', { name: user.username }));
        if (this.selectedGroup()?.id === groupId) this.loadMembers(groupId);   // inzwischen andere Gruppe gewählt → deren Liste bleibt
        this.store.loadGroups(); // Mitgliederzahl aktualisieren
      },
      error: err => {
        this.addingUserId.set(null);
        this.snackbar.info(apiErrorText(err, this.translate, 'admin.groups.errors.addMember'));
      }
    });
  }

  // --- Gruppe in Gruppe ---------------------------------------------------
  /** Die direkt in der gewählten Gruppe enthaltenen Gruppen. */
  includedGroups(): Group[] {
    const ids = new Set(this.selectedGroup()?.includedGroupIds ?? []);
    return this.groups().filter(g => ids.has(g.id));
  }

  /** Namen der direkt enthaltenen Gruppen einer Zeile (Tabelle). */
  includedNames(g: Group): string {
    const ids = new Set(g.includedGroupIds ?? []);
    return this.groups().filter(o => ids.has(o.id)).map(o => o.name).join(', ');
  }

  enclosingNames(): string {
    return this.enclosingGroups().map(g => g.name).join(', ');
  }

  /** Gruppen, die die gewählte Gruppe direkt enthalten. */
  enclosingGroups(): Group[] {
    const id = this.selectedGroup()?.id;
    return this.groups().filter(g => id != null && (g.includedGroupIds ?? []).includes(id));
  }

  /** Aufnehmbare Gruppen: nicht sie selbst, nicht „Everyone", nicht schon enthalten (Kreise lehnt der Server ab). */
  includableGroups(): Group[] {
    const g = this.selectedGroup();
    if (!g) return [];
    const ids = new Set(g.includedGroupIds ?? []);
    return this.groups().filter(o => o.id !== g.id && !o.isEveryone && !ids.has(o.id));
  }

  addInclusion(includedId: number | null): void {
    const group = this.selectedGroup();
    if (!group || includedId == null) return;
    const groupId = group.id;
    this.adminService.addGroupInclusion(groupId, includedId).subscribe({
      next: () => {
        if (this.selectedGroup()?.id === groupId) this.loadMembers(groupId);
        this.store.loadGroups();
      },
      error: err => this.snackbar.info(apiErrorText(err, this.translate, 'admin.groups.errors.include')),
    });
  }

  removeInclusion(included: Group): void {
    const group = this.selectedGroup();
    if (!group) return;
    this.confirm.ask('admin.groups.removeIncludeConfirm', { included: included.name, group: group.name }).subscribe(ok => {
      if (!ok) return;
      this.adminService.removeGroupInclusion(group.id, included.id).subscribe({
        next: () => {
          if (this.selectedGroup()?.id === group.id) this.loadMembers(group.id);
          this.store.loadGroups();
        },
        error: err => this.snackbar.info(apiErrorText(err, this.translate, 'admin.groups.errors.include')),
      });
    });
  }

  /** Mitglied aus der Gruppe nehmen — erst nach Rückfrage (F5-016): das ✕ am Chip liegt klein neben dem Namen. */
  removeMember(member: GroupMember): void {
    const group = this.selectedGroup();
    if (!group) return;
    this.confirm.ask('admin.groups.removeMemberConfirm', { username: member.username, group: group.name }).subscribe(ok => {
      if (!ok) return;
      this.adminService.removeGroupMember(group.id, member.userId).subscribe({
        next: () => {
          if (this.selectedGroup()?.id === group.id) this.loadMembers(group.id);
          this.store.loadGroups(); // Mitgliederzahl aktualisieren
        },
        error: err => {
          this.snackbar.info(apiErrorText(err, this.translate, 'admin.groups.errors.removeMember'));
        }
      });
    });
  }
}
