import { Component, OnInit, OnDestroy, DestroyRef, inject } from '@angular/core';
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
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { AdminService, AdminUser, Book, DailyPuzzleInfo, Group, GroupMember, GroupTrainingGoal, MenuItemConfig, MenuVisibilityLevel } from '../../core/admin.service';
import { MessageService, AdminThreadSummary, ChatMessage } from '../../core/message.service';
import { ChessableService, ChessableCredentialedUser, ChessableCourse, ChessableImport, ChessableImportTarget } from '../chessable/chessable.service';
import { timer, Subscription } from 'rxjs';
import { MenuService } from '../../core/menu.service';
import { AuthService } from '../../core/auth.service';
import { Router, ActivatedRoute } from '@angular/router';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';

@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatTableModule, MatPaginatorModule,
    MatButtonModule, MatIconModule, MatTabsModule, MatFormFieldModule, MatInputModule,
    MatChipsModule, MatSelectModule, MatTooltipModule, MatSlideToggleModule, TranslateModule, LoadingSpinnerComponent
  ],
  templateUrl: './admin.component.html',
  styleUrls: ['./admin.component.scss'],
})
export class AdminComponent implements OnInit, OnDestroy {
  users: AdminUser[] = [];
  userSearch = '';
  usersPage = 1;
  usersPageSize = 20;
  usersTotalCount = 0;
  usersLoading = false;
  userColumns = ['id', 'username', 'email', 'isAdmin', 'groups', 'createdAt', 'actions'];

  books: Book[] = [];
  booksLoading = false;
  booksUploading = false;
  bookColumns = ['displayName', 'puzzleCount', 'kind', 'difficulty', 'elo', 'forDaily', 'forRandom', 'forBlind', 'groups', 'actions'];

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

  /** Trainingsziel-Vorlage der ausgewählten Gruppe (Tagesziele je Kategorie + Wochenziel). */
  goalEdit = { puzzleMinutes: 0, bookMinutes: 0, playGames: 0, weeklyDaysTarget: 0 };
  goalHasTemplate = false;
  goalLoading = false;

  /** Kibana-URL aus dem Server-Env (leer = nicht konfiguriert → Link wird nicht angezeigt). */
  kibanaUrl = '';

  // --- Tagespuzzle ------------------------------------------------------
  /** Heute (UTC) als yyyy-MM-dd — obere Grenze fürs Datumsfeld (keine Zukunft). */
  readonly today = new Date().toISOString().slice(0, 10);
  /** Gewähltes Datum als yyyy-MM-dd (HTML date input); Default heute (UTC). */
  dailyDate = new Date().toISOString().slice(0, 10);
  dailyPuzzle: DailyPuzzleInfo | null = null;
  dailyLoading = false;
  dailyRegenerating = false;

  // --- Puzzles (Standard) -----------------------------------------------
  puzzleTagsBackfilling = false;

  // --- Menü-Sichtbarkeit ------------------------------------------------
  menuConfig: MenuItemConfig[] = [];
  menuLoading = false;
  menuSaving = false;
  readonly menuLevels: MenuVisibilityLevel[] = ['All', 'Registered', 'Groups', 'Admin'];

  impersonatingId: number | null = null;

  // --- Nachrichten (Admin↔User) ----------------------------------------
  threads: AdminThreadSummary[] = [];
  threadsLoading = false;
  /** Aktuell geöffneter Thread (null = keiner ausgewählt). */
  selectedThreadUserId: number | null = null;
  selectedThreadName = '';
  threadMessages: ChatMessage[] = [];
  threadLoading = false;
  adminDraft = '';
  adminSending = false;
  /** Neue Konversation: User-Suche. */
  msgUserSearch = '';
  msgUserResults: AdminUser[] = [];
  msgSearching = false;

  // --- Kurse von Usern holen (Chessable-Download im Namen eines Users) ---
  dlUsers: ChessableCredentialedUser[] = [];
  dlUsersLoading = false;
  dlSelectedUserId: number | null = null;
  dlCourses: ChessableCourse[] = [];
  dlCoursesLoading = false;
  dlCoursesError: string | null = null;
  /** Laufende/abgeschlossene Admin-Importe je bid (für Fortschritt + Button-Status). */
  dlImports: Record<string, ChessableImport> = {};
  private dlPollSubs: Record<string, Subscription> = {};

  /** Aktiver Tab (für Deep-Links wie /admin?tab=messages). Reihenfolge siehe admin.component.html. */
  selectedTabIndex = 0;
  /** Index des „Nachrichten"-Tabs (0-basiert in der mat-tab-group). */
  private readonly messagesTabIndex = 6;
  /** Per Deep-Link zu öffnender Thread (User-Id), sobald die Thread-Liste geladen ist. */
  private pendingThreadUserId: number | null = null;
  private destroyRef = inject(DestroyRef);

  constructor(private adminService: AdminService, private messageService: MessageService, private menu: MenuService, private auth: AuthService, private router: Router, private route: ActivatedRoute, private snackbar: SnackbarService, private translate: TranslateService, private chessable: ChessableService) {}

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
    this.loadDailyPuzzle();
    this.loadMenuConfig();
    // Deep-Link aus Benachrichtigungen: /admin?tab=messages&thread=<userId>. Als laufendes Abo (nicht
    // nur snapshot), damit aufeinanderfolgende Glocken-Klicks auf verschiedene Threads — ohne Component-
    // Neuerstellung — jeweils greifen: Tab wechseln + die richtige Konversation öffnen.
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(qp => {
      if (qp.get('tab') === 'messages') this.selectedTabIndex = this.messagesTabIndex;
      const thread = qp.get('thread');
      if (thread && !isNaN(+thread)) {
        const uid = +thread;
        const t = this.threads.find(x => x.userId === uid);
        if (t) this.openThread(uid, t.username);   // Liste schon geladen → sofort öffnen
        else this.pendingThreadUserId = uid;        // sonst öffnet loadThreads(), sobald die Liste da ist
      }
    });

    this.loadThreads();
    this.loadDlUsers();
    this.adminService.getConfig().subscribe({
      next: cfg => { this.kibanaUrl = cfg.kibanaUrl || ''; },
      error: () => { /* still keine Pflicht — Link bleibt versteckt */ }
    });
  }

  // --- Nachrichten (Admin↔User) ----------------------------------------

  loadThreads(): void {
    this.threadsLoading = true;
    this.messageService.getThreads().subscribe({
      next: list => {
        this.threads = list;
        this.threadsLoading = false;
        this.messageService.refreshAdminUnread();
        // Deep-Link-Ziel öffnen, sobald die Liste (mit Usernamen) da ist.
        if (this.pendingThreadUserId != null) {
          const uid = this.pendingThreadUserId;
          this.pendingThreadUserId = null;
          const t = this.threads.find(x => x.userId === uid);
          if (t) this.openThread(uid, t.username);
        }
      },
      error: () => { this.threadsLoading = false; },
    });
  }

  /** Bestehenden Thread öffnen + User-Antworten als gelesen markieren. */
  openThread(userId: number, username: string): void {
    this.selectedThreadUserId = userId;
    this.selectedThreadName = username;
    this.msgUserResults = [];
    this.msgUserSearch = '';
    this.threadLoading = true;
    this.messageService.getAdminThread(userId).subscribe({
      next: list => {
        this.threadMessages = list;
        this.threadLoading = false;
        if (list.some(m => !m.fromAdmin && !m.readByRecipient)) {
          this.messageService.markAdminSeen(userId).subscribe({
            next: () => this.loadThreads(),
            error: () => {},
          });
        }
      },
      error: () => { this.threadLoading = false; },
    });
  }

  /** Nachricht des Admins absenden (startet den Thread, falls neu). */
  sendAdminMessage(): void {
    const body = this.adminDraft.trim();
    if (!body || this.adminSending || this.selectedThreadUserId == null) return;
    this.adminSending = true;
    this.messageService.sendToUser(this.selectedThreadUserId, body).subscribe({
      next: m => {
        this.threadMessages = [...this.threadMessages, m];
        this.adminDraft = '';
        this.adminSending = false;
        this.loadThreads();
      },
      error: () => {
        this.adminSending = false;
        this.snackbar.show(this.translate.instant('messages.sendError'), { duration: 3000 });
      },
    });
  }

  /** User für eine neue Konversation suchen (Username/E-Mail). */
  searchMsgUsers(): void {
    const q = this.msgUserSearch.trim();
    if (q.length < 1) { this.msgUserResults = []; return; }
    this.msgSearching = true;
    this.adminService.getUsers(q, 1, 10).subscribe({
      next: res => { this.msgUserResults = res.items; this.msgSearching = false; },
      error: () => { this.msgSearching = false; },
    });
  }

  /** Neue (oder bestehende) Konversation mit dem gewählten User beginnen. */
  startConversation(user: AdminUser): void {
    const existing = this.threads.find(t => t.userId === user.id);
    if (existing) { this.openThread(user.id, user.username); return; }
    this.selectedThreadUserId = user.id;
    this.selectedThreadName = user.username;
    this.threadMessages = [];
    this.msgUserResults = [];
    this.msgUserSearch = '';
  }

  closeThread(): void {
    this.selectedThreadUserId = null;
    this.threadMessages = [];
    this.adminDraft = '';
  }

  /** Übersicht-Eintrag des gerade offenen Threads (für Claim-Status im Detail-Header). */
  get selectedThread(): AdminThreadSummary | null {
    return this.threads.find(t => t.userId === this.selectedThreadUserId) ?? null;
  }

  /** Eigene Admin-Id (zum Erkennen „von mir übernommen"). */
  get myId(): number | null {
    return this.auth.currentUser?.userId ?? null;
  }

  /** Thread übernehmen (Zuweisung an mich). */
  claimThread(userId: number): void {
    this.messageService.claimThread(userId).subscribe({
      next: () => this.loadThreads(),
      error: () => {},
    });
  }

  /** Thread wieder freigeben. */
  releaseThread(userId: number): void {
    this.messageService.releaseThread(userId).subscribe({
      next: () => this.loadThreads(),
      error: () => {},
    });
  }

  // --- Kurse von Usern holen ---------------------------------------------

  loadDlUsers(): void {
    this.dlUsersLoading = true;
    this.chessable.getCredentialedUsersAdmin().subscribe({
      next: list => { this.dlUsers = list; this.dlUsersLoading = false; },
      error: () => { this.dlUsersLoading = false; },
    });
  }

  /** User gewählt → dessen Chessable-Kursliste laden. */
  onDlUserChange(): void {
    this.dlCourses = [];
    this.dlCoursesError = null;
    if (this.dlSelectedUserId != null) this.loadDlCourses(false);
  }

  loadDlCourses(refresh: boolean): void {
    if (this.dlSelectedUserId == null) return;
    this.dlCoursesLoading = true;
    this.dlCoursesError = null;
    this.chessable.getUserCoursesAdmin(this.dlSelectedUserId, refresh).subscribe({
      next: res => { this.dlCourses = res.courses; this.dlCoursesLoading = false; },
      error: err => {
        this.dlCoursesError = err?.error?.message || this.translate.instant('admin.courseDl.loadError');
        this.dlCoursesLoading = false;
      },
    });
  }

  /** Kurs des Users ins eigene Admin-Konto herunterladen — als Repertoire oder Buch. */
  dlImport(course: ChessableCourse, target: ChessableImportTarget): void {
    if (this.dlSelectedUserId == null) return;
    const bid = course.bid;
    this.dlImports[bid] = { ...(this.dlImports[bid] ?? {} as ChessableImport), status: 'running', phase: 'queued', bid, target } as ChessableImport;
    this.chessable.importForUserAdmin(this.dlSelectedUserId, bid, target, course.name).subscribe({
      next: imp => { this.dlImports[bid] = imp; this.pollDlImport(bid, imp.id); },
      error: err => {
        delete this.dlImports[bid];
        this.snackbar.show(err?.error?.message || this.translate.instant('admin.courseDl.importError'), { duration: 3500 });
      },
    });
  }

  private pollDlImport(bid: string, id: number): void {
    this.dlPollSubs[bid]?.unsubscribe();
    this.dlPollSubs[bid] = timer(2500, 2500).subscribe(() => {
      this.chessable.getImport(id).subscribe({
        next: imp => {
          this.dlImports[bid] = imp;
          if (imp.status !== 'running') { this.dlPollSubs[bid]?.unsubscribe(); delete this.dlPollSubs[bid]; }
        },
        error: () => { this.dlPollSubs[bid]?.unsubscribe(); delete this.dlPollSubs[bid]; },
      });
    });
  }

  ngOnDestroy(): void {
    Object.values(this.dlPollSubs).forEach(s => s.unsubscribe());
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
        this.booksLoading = false;
      },
      error: () => {
        this.snackbar.info(this.translate.instant('admin.books.errors.load'));
        this.booksLoading = false;
      }
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

  // --- Tagespuzzle ------------------------------------------------------
  /** yyyy-MM-dd → yyyyMMdd für die API-Route. */
  private compactDate(d: string): string {
    return (d || '').replace(/-/g, '');
  }

  loadDailyPuzzle(): void {
    const date = this.compactDate(this.dailyDate);
    if (date.length !== 8) return;
    this.dailyLoading = true;
    this.dailyPuzzle = null;
    this.adminService.getDailyPuzzle(date).subscribe({
      next: p => { this.dailyPuzzle = p; this.dailyLoading = false; },
      error: err => {
        this.dailyLoading = false;
        // 404 = noch kein Tagespuzzle für dieses Datum (z. B. leerer Pool) — kein Fehler-Toast nötig.
        if (err.status !== 404) {
          this.snackbar.info(err.error?.message || this.translate.instant('admin.daily.errors.load'));
        }
      }
    });
  }

  regenerateDailyPuzzle(): void {
    const date = this.compactDate(this.dailyDate);
    if (date.length !== 8) return;
    if (!confirm(this.translate.instant('admin.daily.regenerateConfirm'))) return;

    this.dailyRegenerating = true;
    this.adminService.regenerateDailyPuzzle(date).subscribe({
      next: p => {
        this.dailyPuzzle = p;
        this.dailyRegenerating = false;
        this.snackbar.info(this.translate.instant('admin.daily.regenerated'));
      },
      error: err => {
        this.dailyRegenerating = false;
        this.snackbar.info(err.error?.message || this.translate.instant('admin.daily.errors.regenerate'));
      }
    });
  }

  // --- Puzzles (Standard) -----------------------------------------------
  backfillPuzzleTags(): void {
    if (this.puzzleTagsBackfilling) return;
    if (!confirm(this.translate.instant('admin.puzzles.backfillConfirm'))) return;
    this.puzzleTagsBackfilling = true;
    this.adminService.backfillPuzzleTags().subscribe({
      next: () => {
        this.puzzleTagsBackfilling = false;
        this.snackbar.info(this.translate.instant('admin.puzzles.backfillStarted'));
      },
      error: err => {
        this.puzzleTagsBackfilling = false;
        this.snackbar.info(err.error?.message || this.translate.instant('admin.puzzles.backfillError'));
      }
    });
  }

  // --- Menü-Sichtbarkeit ------------------------------------------------
  loadMenuConfig(): void {
    this.menuLoading = true;
    this.adminService.getMenuConfig().subscribe({
      next: cfg => { this.menuConfig = cfg; this.menuLoading = false; },
      error: () => { this.snackbar.info(this.translate.instant('admin.menu.loadError')); this.menuLoading = false; }
    });
  }

  saveMenuConfig(): void {
    this.menuSaving = true;
    // Gruppen nur bei Level=Groups mitschicken (sonst leeren).
    const payload = this.menuConfig.map(i => ({ ...i, groupIds: i.level === 'Groups' ? i.groupIds : [] }));
    this.adminService.saveMenuConfig(payload).subscribe({
      next: cfg => {
        this.menuConfig = cfg;
        this.menuSaving = false;
        this.menu.refresh(); // eigene Navbar sofort aktualisieren
        this.snackbar.info(this.translate.instant('admin.menu.saved'));
      },
      error: () => { this.snackbar.info(this.translate.instant('admin.menu.saveError')); this.menuSaving = false; }
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

  loadAllUsers(): void {
    // User-Liste fuer das Mitglieder-Dropdown (kleiner Nutzerkreis).
    this.adminService.getUsers('', 1, 500).subscribe({
      next: res => this.allUsers = res.items
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
          puzzleMinutes: g.puzzleMinutes,
          bookMinutes: g.bookMinutes,
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
    const clamp = (v: number, max: number) => Math.max(0, Math.min(max, Math.round(v || 0)));
    const goal = {
      puzzleMinutes: clamp(this.goalEdit.puzzleMinutes, 600),
      bookMinutes: clamp(this.goalEdit.bookMinutes, 600),
      playGames: clamp(this.goalEdit.playGames, 200),
      weeklyDaysTarget: clamp(this.goalEdit.weeklyDaysTarget, 7),
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
        this.goalEdit = { puzzleMinutes: 0, bookMinutes: 0, playGames: 0, weeklyDaysTarget: 0 };
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
        this.membersLoading = false;
      },
      error: () => {
        this.snackbar.info(this.translate.instant('admin.groups.errors.loadMembers'));
        this.membersLoading = false;
      }
    });
  }

  availableUsers(): AdminUser[] {
    const memberIds = new Set(this.groupMembers.map(m => m.userId));
    return this.allUsers.filter(u => !memberIds.has(u.id));
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
