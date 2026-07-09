import { Component, OnInit, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTabsModule } from '@angular/material/tabs';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { SnackbarService } from '../../core/snackbar.service';
import { MatChipsModule } from '@angular/material/chips';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { AdminService, AdminUser, Book, Group, GroupMember, GroupTrainingGoal } from '../../core/admin.service';
import { MenuService } from '../../core/menu.service';
import { AuthService } from '../../core/auth.service';
import { Router, ActivatedRoute } from '@angular/router';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { AdminGithubActionsComponent } from './admin-github-actions.component';
import { AdminChessableDownloadComponent } from './tabs/admin-chessable-download.component';
import { AdminDailyPuzzleComponent } from './tabs/admin-daily-puzzle.component';
import { AdminPuzzleTagsComponent } from './tabs/admin-puzzle-tags.component';
import { AdminMenuVisibilityComponent } from './tabs/admin-menu-visibility.component';
import { AdminMessagesComponent } from './tabs/admin-messages.component';
import { adminTabIndex, ADMIN_TAB_KEYS } from './admin-tabs';
import { clampGoal } from '../training-goals/goal.util';

@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatTableModule, MatPaginatorModule,
    MatButtonModule, MatIconModule, MatTabsModule, MatFormFieldModule, MatInputModule,
    MatChipsModule, MatSelectModule, MatTooltipModule, MatSlideToggleModule, MatCheckboxModule, MatProgressSpinnerModule, TranslateModule, LoadingSpinnerComponent,
    AdminGithubActionsComponent, AdminChessableDownloadComponent,
    AdminDailyPuzzleComponent, AdminPuzzleTagsComponent, AdminMenuVisibilityComponent, AdminMessagesComponent
  ],
  templateUrl: './admin.component.html',
  styleUrls: ['./admin.component.scss'],
})
export class AdminComponent implements OnInit {
  users: AdminUser[] = [];
  userSearch = '';
  usersPage = 1;
  usersPageSize = 20;
  usersTotalCount = 0;
  usersLoading = false;
  userColumns = ['id', 'username', 'email', 'isAdmin', 'groups', 'createdAt', 'actions'];

  books: Book[] = [];
  filteredBooks: Book[] = [];
  bookSearch = '';
  booksLoading = false;
  booksUploading = false;
  bookColumns = ['displayName', 'puzzleCount', 'kind', 'difficulty', 'elo', 'forDaily', 'forRandom', 'forBlind', 'isPublic', 'groups', 'actions'];

  /** Per-Spalten-Filter über dem Bücher-Grid (UND-verknüpft, zusätzlich zur globalen Suche). */
  bookFilters = this.emptyBookFilters();

  groups: Group[] = [];
  groupsLoading = false;
  groupColumns = ['name', 'memberCount', 'actions'];
  newGroupName = '';
  newGroupDescription = '';
  selectedGroup: Group | null = null;
  groupMembers: GroupMember[] = [];
  membersLoading = false;
  allUsers: AdminUser[] = [];
  addMemberUserId: number | null = null;

  /** Trainingsziel-Vorlage der ausgewählten Gruppe (ein Tageszeit-Ziel + Wochenziele). */
  goalEdit = { dailyMinutes: 0, playGames: 0, weeklyDaysTarget: 0 };
  goalHasTemplate = false;
  goalLoading = false;

  /** Kibana-URL aus dem Server-Env (leer = nicht konfiguriert → Link wird nicht angezeigt). */
  kibanaUrl = '';


  impersonatingId: number | null = null;

  /** Aktiver Tab (für Deep-Links wie /admin?tab=messages). Tab-Reihenfolge: siehe `admin-tabs.ts`. */
  selectedTabIndex = 0;
  private destroyRef = inject(DestroyRef);

  constructor(private adminService: AdminService, private menu: MenuService, private auth: AuthService, private router: Router, private route: ActivatedRoute, private snackbar: SnackbarService, private translate: TranslateService) {}

  /** „Als Nutzer einsteigen": Impersonation-Token holen, übernehmen und ins Dashboard wechseln. */
  impersonate(u: AdminUser): void {
    this.impersonatingId = u.id;
    this.adminService.impersonate(u.id).subscribe({
      next: res => {
        this.impersonatingId = null;
        this.auth.impersonate(res);
        this.menu.refresh();
        this.snackbar.info(this.translate.instant('admin.users.impersonateStarted', { name: u.username }));
        this.router.navigate(['/dashboard']);
      },
      error: () => {
        this.impersonatingId = null;
        this.snackbar.info(this.translate.instant('admin.users.impersonateFailed'));
      }
    });
  }

  ngOnInit(): void {
    this.loadUsers();
    this.loadBooks();
    this.loadGroups();
    this.loadAllUsers();
    // Deep-Link: /admin?tab=<key> — Tab aus der URL wählen (der `?thread=`-Teil wird vom
    // Nachrichten-Tab selbst behandelt, siehe AdminMessagesComponent).
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(qp => {
      const tabIdx = adminTabIndex(qp.get('tab'));   // beliebiger Tab-Key, Index aus admin-tabs.ts
      if (tabIdx >= 0) this.selectedTabIndex = tabIdx;
    });

    this.adminService.getConfig().subscribe({
      next: cfg => { this.kibanaUrl = cfg.kibanaUrl || ''; },
      error: () => { /* still keine Pflicht — Link bleibt versteckt */ }
    });
  }

  /** Tab-Wechsel: Index übernehmen UND als ?tab=<key> in die URL schreiben, damit Reload/Back
   *  den Tab behält (queryParamsHandling 'merge' lässt ?thread=… o. Ä. unberührt; replaceUrl
   *  vermeidet eine zusätzliche History-Position je Klick). */
  onTabChange(index: number): void {
    this.selectedTabIndex = index;
    const tab = ADMIN_TAB_KEYS[index];
    if (!tab) return;
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tab },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }



  loadUsers(): void {
    this.usersLoading = true;
    this.adminService.getUsers(this.userSearch, this.usersPage, this.usersPageSize).subscribe({
      next: res => {
        this.users = res.items;
        this.usersTotalCount = res.totalCount;
        this.usersLoading = false;
      },
      error: () => {
        this.snackbar.info(this.translate.instant('admin.users.errors.load'));
        this.usersLoading = false;
      }
    });
  }

  onUsersPageChange(event: PageEvent): void {
    this.usersPage = event.pageIndex + 1;
    this.usersPageSize = event.pageSize;
    this.loadUsers();
  }

  toggleAdmin(user: AdminUser): void {
    this.adminService.toggleAdmin(user.id).subscribe({
      next: updated => {
        user.isAdmin = updated.isAdmin;
        const key = updated.isAdmin ? 'admin.users.nowAdmin' : 'admin.users.noLongerAdmin';
        this.snackbar.info(this.translate.instant(key, { username: user.username }));
      },
      error: err => {
        this.snackbar.info(err.error?.message || this.translate.instant('admin.users.errors.toggleAdmin'));
      }
    });
  }

  deleteUser(user: AdminUser): void {
    if (!confirm(this.translate.instant('admin.users.deleteConfirm', { username: user.username }))) return;

    this.adminService.deleteUser(user.id).subscribe({
      next: () => {
        this.snackbar.info(this.translate.instant('admin.users.deleted', { username: user.username }));
        this.loadUsers();
      },
      error: err => {
        this.snackbar.info(err.error?.message || this.translate.instant('admin.users.errors.delete'));
      }
    });
  }

  // --- Bücher -----------------------------------------------------------
  loadBooks(): void {
    this.booksLoading = true;
    this.adminService.getBooks().subscribe({
      next: books => {
        this.books = books;
        this.applyBookFilter();
        this.booksLoading = false;
      },
      error: () => {
        this.snackbar.info(this.translate.instant('admin.books.errors.load'));
        this.booksLoading = false;
      }
    });
  }

  /** Clientseitiger Filter über die bereits geladenen Bücher (Name/Dateiname/Tags, case-insensitive). */
  private emptyBookFilters() {
    return {
      name: '',
      kind: '' as '' | 'Puzzle' | 'Study',
      difficulty: '',
      eloMin: null as number | null,
      eloMax: null as number | null,
      puzzlesMin: null as number | null,
      puzzlesMax: null as number | null,
      daily: '' as '' | 'yes' | 'no',
      random: '' as '' | 'yes' | 'no',
      blind: '' as '' | 'yes' | 'no',
      public: '' as '' | 'yes' | 'no',
      group: '' as '' | 'none' | number,
    };
  }

  applyBookFilter(): void {
    const q = this.bookSearch.trim().toLowerCase();
    const f = this.bookFilters;
    const name = f.name.trim().toLowerCase();
    const diff = f.difficulty.trim().toLowerCase();
    const tri = (v: '' | 'yes' | 'no', actual: boolean) => v === '' || (v === 'yes') === actual;
    this.filteredBooks = this.books.filter(b => {
      if (q && !((b.displayName ?? '').toLowerCase().includes(q) ||
                 (b.fileName ?? '').toLowerCase().includes(q) ||
                 (b.tags ?? '').toLowerCase().includes(q))) return false;
      if (name && !((b.displayName ?? '').toLowerCase().includes(name) ||
                    (b.fileName ?? '').toLowerCase().includes(name))) return false;
      if (f.kind && b.kind !== f.kind) return false;
      if (diff && !(b.difficulty ?? '').toLowerCase().includes(diff)) return false;
      if (f.eloMin != null && (b.minElo == null || b.minElo < f.eloMin)) return false;
      if (f.eloMax != null && (b.maxElo == null || b.maxElo > f.eloMax)) return false;
      if (f.puzzlesMin != null && b.puzzleCount < f.puzzlesMin) return false;
      if (f.puzzlesMax != null && b.puzzleCount > f.puzzlesMax) return false;
      if (!tri(f.daily, b.forDaily)) return false;
      if (!tri(f.random, b.forRandom)) return false;
      if (!tri(f.blind, b.forBlind)) return false;
      if (!tri(f.public, b.isPublic)) return false;
      if (f.group === 'none' && (b.accessGroupIds?.length ?? 0) > 0) return false;
      if (typeof f.group === 'number' && !(b.accessGroupIds ?? []).includes(f.group)) return false;
      return true;
    });
  }

  hasActiveBookFilters(): boolean {
    const f = this.bookFilters;
    return !!(f.name || f.kind || f.difficulty || f.eloMin != null || f.eloMax != null ||
      f.puzzlesMin != null || f.puzzlesMax != null || f.daily || f.random || f.blind ||
      f.public || f.group !== '');
  }

  resetBookFilters(): void {
    this.bookFilters = this.emptyBookFilters();
    this.applyBookFilter();
  }

  clearBookSearch(): void {
    this.bookSearch = '';
    this.applyBookFilter();
  }

  /** Buch umbenennen (nur DisplayName; Backend akzeptiert das im Update-DTO). */
  renameBook(book: Book): void {
    const next = prompt(this.translate.instant('admin.books.renamePrompt'), book.displayName);
    if (next == null) return;
    const name = next.trim();
    if (!name || name === book.displayName) return;
    this.adminService.updateBook(book.id, { displayName: name }).subscribe({
      next: () => {
        book.displayName = name;
        this.applyBookFilter();
      },
      error: err => this.snackbar.info(err.error?.message || this.translate.instant('admin.books.errors.save'))
    });
  }

  onBookFilesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input.files || input.files.length === 0) return;

    this.booksUploading = true;
    this.adminService.importBooks(input.files).subscribe({
      next: res => {
        const parts = [this.translate.instant('admin.books.importedCount', { count: res.totalImported })];
        if (res.totalSkipped > 0) parts.push(this.translate.instant('admin.books.duplicatesCount', { count: res.totalSkipped }));
        if (res.totalInvalid > 0) parts.push(this.translate.instant('admin.books.invalidCount', { count: res.totalInvalid }));
        this.snackbar.info(parts.join(', '), { duration: 6000 });
        this.booksUploading = false;
        input.value = '';
        this.loadBooks();
      },
      error: err => {
        this.snackbar.info(err.error?.message || this.translate.instant('admin.books.errors.import'), { duration: 4000 });
        this.booksUploading = false;
        input.value = '';
      }
    });
  }

  saveBook(book: Book): void {
    this.adminService.updateBook(book.id, {
      forDaily: book.forDaily,
      forRandom: book.forRandom,
      forBlind: book.forBlind,
      isPublic: book.isPublic,
      publicSlug: book.publicSlug ?? '',
      kind: book.kind,
      minElo: book.minElo,
      maxElo: book.maxElo
    }).subscribe({
      error: err => {
        this.snackbar.info(err.error?.message || this.translate.instant('admin.books.errors.save'));
        this.loadBooks(); // Stand zurücksetzen
      }
    });
  }

  saveBookGroups(book: Book): void {
    this.adminService.updateBookGroups(book.id, book.accessGroupIds ?? []).subscribe({
      error: err => {
        this.snackbar.info(err.error?.message || this.translate.instant('admin.books.errors.saveGroups'));
        this.loadBooks(); // Stand zurücksetzen
      }
    });
  }

  deleteBook(book: Book): void {
    if (!confirm(this.translate.instant('admin.books.deleteConfirm', { name: book.displayName, count: book.puzzleCount }))) return;

    this.adminService.deleteBook(book.id).subscribe({
      next: () => {
        this.snackbar.info(this.translate.instant('admin.books.deleted', { name: book.displayName }));
        this.loadBooks();
      },
      error: err => {
        this.snackbar.info(err.error?.message || this.translate.instant('admin.books.errors.delete'));
      }
    });
  }


  // --- Gruppen ----------------------------------------------------------
  loadGroups(): void {
    this.groupsLoading = true;
    this.adminService.getGroups().subscribe({
      next: groups => {
        this.groups = groups;
        this.groupsLoading = false;
        // Auswahl aktualisieren, falls die gewählte Gruppe noch existiert
        if (this.selectedGroup) {
          this.selectedGroup = groups.find(g => g.id === this.selectedGroup!.id) ?? null;
        }
      },
      error: () => {
        this.snackbar.info(this.translate.instant('admin.groups.errors.load'));
        this.groupsLoading = false;
      }
    });
  }

  /** Obere Schranke der ins Mitglieder-Dropdown geladenen User. Bewusst hart: das Dropdown ist
   *  für einen kleinen Nutzerkreis gedacht; die paginierte User-TABELLE (loadUsers) ist davon
   *  unberührt. Sollte der Bestand die Schranke je überschreiten, wird das geloggt, damit der Cap
   *  sichtbar wird (statt still abzuschneiden) — dann ist ein Such-/Lazy-Picker fällig. */
  private static readonly MEMBER_PICKER_CAP = 500;

  loadAllUsers(): void {
    const cap = AdminComponent.MEMBER_PICKER_CAP;
    this.adminService.getUsers('', 1, cap).subscribe({
      next: res => {
        this.allUsers = res.items;
        if (res.totalCount > cap) {
          // Cap erreicht → das Dropdown zeigt nicht alle User. Nicht still schlucken.
          console.warn(`[admin] Mitglieder-Dropdown auf ${cap} von ${res.totalCount} Usern begrenzt — Such-Picker erwägen.`);
        }
        this.recomputeAvailableUsers();
      },
      error: () => this.snackbar.info(this.translate.instant('admin.users.errors.load'))
    });
  }

  createGroup(): void {
    const name = this.newGroupName.trim();
    if (!name) return;
    this.adminService.createGroup(name, this.newGroupDescription.trim() || null).subscribe({
      next: () => {
        this.snackbar.info(this.translate.instant('admin.groups.created', { name }));
        this.newGroupName = '';
        this.newGroupDescription = '';
        this.loadGroups();
      },
      error: err => {
        this.snackbar.info(err.error?.message || this.translate.instant('admin.groups.errors.create'));
      }
    });
  }

  deleteGroup(group: Group): void {
    if (!confirm(this.translate.instant('admin.groups.deleteConfirm', { name: group.name }))) return;
    this.adminService.deleteGroup(group.id).subscribe({
      next: () => {
        this.snackbar.info(this.translate.instant('admin.groups.deleted', { name: group.name }));
        if (this.selectedGroup?.id === group.id) {
          this.selectedGroup = null;
          this.groupMembers = [];
          this.recomputeAvailableUsers();
        }
        this.loadGroups();
      },
      error: err => {
        this.snackbar.info(err.error?.message || this.translate.instant('admin.groups.errors.delete'));
      }
    });
  }

  selectGroup(group: Group): void {
    this.selectedGroup = group;
    this.addMemberUserId = null;
    this.loadMembers(group.id);
    this.loadGroupGoal(group.id);
  }

  loadGroupGoal(groupId: number): void {
    this.goalLoading = true;
    this.adminService.getGroupTrainingGoal(groupId).subscribe({
      next: g => {
        this.goalHasTemplate = g.source === 'group';
        this.goalEdit = {
          dailyMinutes: g.dailyMinutes,
          playGames: g.playGames,
          weeklyDaysTarget: g.weeklyDaysTarget,
        };
        this.goalLoading = false;
      },
      error: () => {
        this.snackbar.info(this.translate.instant('admin.groups.goal.errors.load'));
        this.goalLoading = false;
      }
    });
  }

  saveGroupGoal(): void {
    if (!this.selectedGroup) return;
    const goal = {
      dailyMinutes: clampGoal(this.goalEdit.dailyMinutes, 600),
      playGames: clampGoal(this.goalEdit.playGames, 200),
      weeklyDaysTarget: clampGoal(this.goalEdit.weeklyDaysTarget, 7),
    };
    this.adminService.setGroupTrainingGoal(this.selectedGroup.id, goal).subscribe({
      next: () => { this.goalHasTemplate = true; this.snackbar.info(this.translate.instant('admin.groups.goal.saved')); },
      error: () => this.snackbar.info(this.translate.instant('admin.groups.goal.errors.save')),
    });
  }

  clearGroupGoal(): void {
    if (!this.selectedGroup) return;
    const groupId = this.selectedGroup.id;
    this.adminService.deleteGroupTrainingGoal(groupId).subscribe({
      next: () => {
        this.goalHasTemplate = false;
        this.goalEdit = { dailyMinutes: 0, playGames: 0, weeklyDaysTarget: 0 };
        this.snackbar.info(this.translate.instant('admin.groups.goal.cleared'));
      },
      error: () => this.snackbar.info(this.translate.instant('admin.groups.goal.errors.save')),
    });
  }

  loadMembers(groupId: number): void {
    this.membersLoading = true;
    this.adminService.getGroupMembers(groupId).subscribe({
      next: members => {
        this.groupMembers = members;
        this.recomputeAvailableUsers();
        this.membersLoading = false;
      },
      error: () => {
        this.snackbar.info(this.translate.instant('admin.groups.errors.loadMembers'));
        this.membersLoading = false;
      }
    });
  }

  /** Nicht-Mitglieder des gewählten Gruppe (Auswahl im „Mitglied hinzufügen"-Dropdown).
   *  Wird bei Datenänderung neu berechnet statt je CD-Zyklus (Set + filter). */
  availableUsers: AdminUser[] = [];

  private recomputeAvailableUsers(): void {
    const memberIds = new Set(this.groupMembers.map(m => m.userId));
    this.availableUsers = this.allUsers.filter(u => !memberIds.has(u.id));
  }

  addMember(): void {
    if (!this.selectedGroup || !this.addMemberUserId) return;
    const groupId = this.selectedGroup.id;
    this.adminService.addGroupMember(groupId, this.addMemberUserId).subscribe({
      next: () => {
        this.addMemberUserId = null;
        this.loadMembers(groupId);
        this.loadGroups(); // Mitgliederzahl aktualisieren
      },
      error: err => {
        this.snackbar.info(err.error?.message || this.translate.instant('admin.groups.errors.addMember'));
      }
    });
  }

  removeMember(member: GroupMember): void {
    if (!this.selectedGroup) return;
    const groupId = this.selectedGroup.id;
    this.adminService.removeGroupMember(groupId, member.userId).subscribe({
      next: () => {
        this.loadMembers(groupId);
        this.loadGroups(); // Mitgliederzahl aktualisieren
      },
      error: err => {
        this.snackbar.info(err.error?.message || this.translate.instant('admin.groups.errors.removeMember'));
      }
    });
  }
}
