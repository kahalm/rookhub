import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatChipsModule } from '@angular/material/chips';
import { MatMenuModule } from '@angular/material/menu';
import { MatDialog } from '@angular/material/dialog';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AdminService, AdminUser } from '../../../core/admin.service';
import { AuthService } from '../../../core/auth.service';
import { MenuService } from '../../../core/menu.service';
import { SnackbarService } from '../../../core/snackbar.service';
import { apiErrorText } from '../../../core/api-error';
import { ConfirmService } from '../../../shared/confirm-dialog/confirm-dialog.component';
import { IconLabelDirective } from '../../../shared/icon-label/icon-label.directive';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { ResetLinkDialogComponent, ResetLinkDialogData } from '../reset-link-dialog.component';
import { LockUserDialogComponent, LockUserDialogData, LockUserDialogResult } from '../lock-user-dialog.component';
import { isIndefiniteLock } from '../lock-user.util';

/**
 * Admin-Tab „Nutzer" (users.manage): paginierte Kontenliste (Tabelle ab 900 px, darunter Karten), Einsteigen als Nutzer,
 * Reset-Link (UX-002), Admin-Recht (F5-013, nur Admins), Sperren/Entsperren (P-4) und Löschen. Aus `AdminComponent` ausgelagert (Zerlegung Z30);
 * die Seite rendert den Tab nur mit `users.manage` (F5-005), er lädt daher beim Seitenaufruf ohne weitere Prüfung.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-admin-users-tab',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatTableModule, MatPaginatorModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatChipsModule, MatMenuModule, IconLabelDirective, TranslatePipe,
    LoadingSpinnerComponent,
  ],
  templateUrl: './admin-users-tab.component.html',
  styleUrl: './admin-users-tab.component.scss',
})
export class AdminUsersTabComponent implements OnInit {
  private readonly adminService = inject(AdminService);
  private readonly auth = inject(AuthService);
  private readonly menu = inject(MenuService);
  private readonly router = inject(Router);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);
  private readonly confirm = inject(ConfirmService);
  private readonly dialog = inject(MatDialog);
  private readonly cdr = inject(ChangeDetectorRef);

  readonly users = signal<AdminUser[]>([]);
  readonly userSearch = signal('');
  readonly usersPage = signal(1);
  readonly usersPageSize = signal(20);
  readonly usersTotalCount = signal(0);
  readonly usersLoading = signal(false);
  readonly userColumns = ['id', 'username', 'email', 'isAdmin', 'groups', 'createdAt', 'actions'];

  readonly impersonatingId = signal<number | null>(null);
  /** Konto, dessen Admin-Recht gerade gespeichert wird — sein Knopf ist so lange gesperrt (F5-013). */
  readonly adminBusyId = signal<number | null>(null);
  /** Konto, für das gerade ein Reset-Link erzeugt wird (UX-002) — sein Knopf ist so lange gesperrt. */
  readonly resetLinkBusyId = signal<number | null>(null);
  /** Konto, dessen Sperre gerade gesetzt/aufgehoben wird (P-4). */
  readonly lockBusyId = signal<number | null>(null);

  /** Nur Admins dürfen das Admin-Flag setzen und Admin-Konten anfassen (Server: „Only an admin may …", 403). */
  get actorIsAdmin(): boolean { return this.auth.isAdminLive; }
  /** Einsteigen/Reset-Link/Löschen: für Admin-Konten nur durch Admins — sonst gar nicht anbieten (Server: 403). */
  canManageUser(u: AdminUser): boolean { return this.actorIsAdmin || !u.isAdmin; }

  /** Sperren geht nicht für das eigene Konto (Server: „Cannot lock yourself“). */
  canLock(u: AdminUser): boolean { return this.canManageUser(u) && u.id !== this.auth.currentUser?.userId; }
  /** Läuft eine Sperre? Der Server liefert `lockedUntil` nur dann. */
  isLocked(u: AdminUser): boolean { return !!u.lockedUntil; }
  isIndefinite(u: AdminUser): boolean { return isIndefiniteLock(u.lockedUntil); }

  ngOnInit(): void {
    this.loadUsers();
  }

  loadUsers(): void {
    this.usersLoading.set(true);
    this.adminService.getUsers(this.userSearch(), this.usersPage(), this.usersPageSize()).subscribe({
      next: res => {
        this.users.set(res.items);
        this.usersTotalCount.set(res.totalCount);
        this.usersLoading.set(false);
      },
      error: () => {
        this.snackbar.info(this.translate.instant('admin.users.errors.load'));
        this.usersLoading.set(false);
      }
    });
  }

  onUsersPageChange(event: PageEvent): void {
    this.usersPage.set(event.pageIndex + 1);
    this.usersPageSize.set(event.pageSize);
    this.loadUsers();
  }

  /** „Als Nutzer einsteigen": Impersonation-Token holen, übernehmen und ins Dashboard wechseln. */
  impersonate(u: AdminUser): void {
    this.impersonatingId.set(u.id);
    this.adminService.impersonate(u.id).subscribe({
      next: res => {
        this.impersonatingId.set(null);
        // Admin-Sicherung nicht schreibbar (Speicher voll/gesperrt): nicht einsteigen — ohne sie gäbe es keinen Rücksprung.
        if (!this.auth.impersonate(res)) {
          this.snackbar.info(this.translate.instant('admin.users.impersonateFailed'));
          return;
        }
        this.menu.refresh();
        this.snackbar.info(this.translate.instant('admin.users.impersonateStarted', { name: u.username }));
        this.router.navigate(['/dashboard']);
      },
      error: () => {
        this.impersonatingId.set(null);
        this.snackbar.info(this.translate.instant('admin.users.impersonateFailed'));
      }
    });
  }

  /** Einmaligen Reset-Link (24 h) erzeugen und im Dialog zum Weitergeben zeigen (UX-002 / P-2): für Konten ohne
   *  E-Mail, deren „Passwort vergessen“ ins Leere läuft. Der Admin sieht und setzt kein Passwort. */
  createResetLink(u: AdminUser): void {
    if (this.resetLinkBusyId() !== null) return;
    this.resetLinkBusyId.set(u.id);
    this.adminService.createPasswordResetLink(u.id).subscribe({
      next: res => {
        this.resetLinkBusyId.set(null);
        const data: ResetLinkDialogData = { username: u.username, link: res.link, expiresAt: res.expiresAt, hasEmail: !!u.email };
        this.dialog.open(ResetLinkDialogComponent, { data, autoFocus: false });
      },
      error: err => {
        this.resetLinkBusyId.set(null);
        this.snackbar.info(apiErrorText(err, this.translate, 'admin.users.resetLink.error'));
      },
    });
  }

  /** Admin-Recht vergeben bzw. entziehen (F5-013): erst nach Rückfrage mit dem Kontonamen, als Soll-Wert (ein
   *  Doppelklick oder eine veraltete Liste schaltet nicht zurück) und mit gesperrtem Knopf, solange gespeichert wird. */
  toggleAdmin(user: AdminUser): void {
    if (this.adminBusyId() !== null) return;
    const target = !user.isAdmin;
    this.confirm.ask(target ? 'admin.users.confirmPromote' : 'admin.users.confirmDemote', { username: user.username })
      .subscribe(ok => {
        if (!ok || this.adminBusyId() !== null) return;
        this.adminBusyId.set(user.id);
        this.adminService.setAdmin(user.id, target).subscribe({
          next: updated => {
            this.adminBusyId.set(null);
            user.isAdmin = updated.isAdmin;
            this.cdr.markForCheck();   // Zeile und Karte zeigen das geänderte Konto-Objekt
            const key = updated.isAdmin ? 'admin.users.nowAdmin' : 'admin.users.noLongerAdmin';
            this.snackbar.info(this.translate.instant(key, { username: user.username }));
          },
          error: err => {
            this.adminBusyId.set(null);
            this.snackbar.info(apiErrorText(err, this.translate, 'admin.users.errors.toggleAdmin'));
          }
        });
      });
  }

  /** „Sperren …“ (P-4): Dialog mit Ende (Tag oder unbefristet) und optionalem Grund; laufende Sitzungen enden sofort. */
  lockUser(user: AdminUser): void {
    if (this.lockBusyId() !== null) return;
    const data: LockUserDialogData = { username: user.username };
    this.dialog.open<LockUserDialogComponent, LockUserDialogData, LockUserDialogResult>(LockUserDialogComponent, { data })
      .afterClosed().subscribe(res => {
        if (!res || this.lockBusyId() !== null) return;
        this.lockBusyId.set(user.id);
        this.adminService.lockUser(user.id, res.until, res.reason).subscribe({
          next: updated => this.lockDone(user, updated, 'admin.users.lock.locked'),
          error: err => this.lockFailed(err, 'admin.users.lock.lockFailed'),
        });
      });
  }

  /** „Entsperren“ (P-4) — ohne Rückfrage, die Aktion ist harmlos und umkehrbar. */
  unlockUser(user: AdminUser): void {
    if (this.lockBusyId() !== null) return;
    this.lockBusyId.set(user.id);
    this.adminService.unlockUser(user.id).subscribe({
      next: updated => this.lockDone(user, updated, 'admin.users.lock.unlocked'),
      error: err => this.lockFailed(err, 'admin.users.lock.unlockFailed'),
    });
  }

  private lockDone(user: AdminUser, updated: AdminUser, key: string): void {
    this.lockBusyId.set(null);
    user.lockedUntil = updated.lockedUntil ?? null;
    this.users.update(list => [...list]);   // Zeile und Karte zeigen den neuen Status
    this.cdr.markForCheck();
    this.snackbar.info(this.translate.instant(key, { username: user.username }));
  }

  private lockFailed(err: unknown, fallbackKey: string): void {
    this.lockBusyId.set(null);
    this.snackbar.info(apiErrorText(err, this.translate, fallbackKey));
  }

  deleteUser(user: AdminUser): void {
    this.confirm.ask('admin.users.deleteConfirm', { username: user.username }).subscribe(ok => {
      if (!ok) return;

      this.adminService.deleteUser(user.id).subscribe({
        next: () => {
          this.snackbar.info(this.translate.instant('admin.users.deleted', { username: user.username }));
          this.loadUsers();
        },
        error: err => {
          this.snackbar.info(apiErrorText(err, this.translate, 'admin.users.errors.delete'));
        }
      });
    });
  }
}
