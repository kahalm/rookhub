import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged, switchMap } from 'rxjs/operators';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../../core/snackbar.service';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { AdminService, AdminUser, Group, Role } from '../../../core/admin.service';

/**
 * Admin-Tab „Rollen & Berechtigungen" (RBAC Phase 4): Rollen anlegen/bearbeiten/löschen inkl.
 * Permission-Auswahl (Checkboxen aus den Code-Konstanten) + Rollen-Zuweisung je Nutzer.
 *
 * Leitplanken spiegeln das Backend: die System-Rolle „admin" ist nicht editierbar/löschbar
 * (trägt immer alle Permissions, gesteuert übers IsAdmin-Flag), „member" nicht löschbar. Die
 * admin-Rollenmitgliedschaft wird NICHT hier verwaltet (folgt dem Admin-Toggle im Nutzer-Tab) —
 * die admin-Rolle erscheint daher in der Nutzer-Zuweisung nicht als Checkbox.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-admin-roles',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatCheckboxModule, MatChipsModule, MatTooltipModule, TranslatePipe, LoadingSpinnerComponent,
  ],
  templateUrl: './admin-roles.component.html',
  styleUrl: './admin-roles.component.scss',
})
export class AdminRolesComponent implements OnInit {
  /** Key der System-Admin-Rolle (nicht editier-/löschbar, nicht in der Nutzer-Zuweisung). */
  static readonly ADMIN_KEY = 'admin';

  roles: Role[] = [];
  allPermissions: string[] = [];
  loading = false;

  // Anlegen
  newKey = '';
  newName = '';
  newPerms = new Set<string>();
  creating = false;

  // Inline-Bearbeiten
  editingId: number | null = null;
  editName = '';
  editPerms = new Set<string>();
  savingRole = false;

  // Nutzer-Rollen-Zuweisung
  userSearch = '';
  userResults: AdminUser[] = [];
  private searchInput = new Subject<string>();
  searchingUsers = false;
  selectedUser: AdminUser | null = null;
  userRoleIds = new Set<number>();
  /** Erst nach erfolgreichem Laden true — der Server ERSETZT den Satz, ein Speichern auf leerem/fremdem
   *  Stand (Ladefehler, Antwort eines vorher gewählten Nutzers) entzöge sonst alle übrigen Rollen. */
  userRolesLoaded = false;
  savingUserRoles = false;

  // Gruppen-Rollen (0.589.0)
  groups: Group[] = [];
  selectedGroup: Group | null = null;
  groupRoleIds = new Set<number>();
  /** Wie {@link userRolesLoaded}: Speichern erst, wenn der Ist-Stand der gewählten Gruppe geladen ist. */
  groupRolesLoaded = false;
  savingGroupRoles = false;

  constructor(
    private admin: AdminService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  ngOnInit(): void {
    this.loadRoles();
    this.admin.getPermissions().subscribe({ next: p => this.allPermissions = p, error: () => {} });
    this.admin.getGroups().subscribe({ next: g => this.groups = g, error: () => {} });
    this.searchInput.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      switchMap(q => { this.searchingUsers = true; return this.admin.getUsers(q, 1, 20); }),
    ).subscribe({
      next: res => { this.userResults = res.items; this.searchingUsers = false; },
      error: () => { this.searchingUsers = false; },
    });
  }

  loadRoles(): void {
    this.loading = true;
    this.admin.getRoles().subscribe({
      next: r => { this.roles = r; this.loading = false; },
      error: () => { this.snackbar.info(this.translate.instant('admin.roles.loadError')); this.loading = false; },
    });
  }

  /** Nur-lesbar: die admin-Rolle trägt implizit alle Permissions und wird nicht editiert. */
  isAdminRole(role: Role): boolean { return role.key === AdminRolesComponent.ADMIN_KEY; }

  /** i18n-Key für einen Permission-Schlüssel — Punkte werden zu „_", damit ngx-translate
   *  (das „." als Pfadtrenner deutet) den flachen Schlüssel findet: users.manage → perm.users_manage. */
  permKey(p: string): string { return 'admin.roles.perm.' + p.replace(/\./g, '_'); }

  // --- Anlegen --------------------------------------------------------
  toggleNewPerm(p: string): void {
    if (this.newPerms.has(p)) this.newPerms.delete(p); else this.newPerms.add(p);
  }

  get canCreate(): boolean {
    return /^[a-z][a-z0-9._-]{1,49}$/.test(this.newKey.trim().toLowerCase()) && this.newName.trim().length > 0;
  }

  createRole(): void {
    if (!this.canCreate || this.creating) return;
    this.creating = true;
    this.admin.createRole({
      key: this.newKey.trim().toLowerCase(),
      name: this.newName.trim(),
      permissions: [...this.newPerms],
    }).subscribe({
      next: () => {
        this.creating = false;
        this.newKey = ''; this.newName = ''; this.newPerms.clear();
        this.snackbar.info(this.translate.instant('admin.roles.created'));
        this.loadRoles();
      },
      error: err => {
        this.creating = false;
        this.snackbar.info(err?.error?.message || this.translate.instant('admin.roles.saveError'));
      },
    });
  }

  // --- Bearbeiten -----------------------------------------------------
  startEdit(role: Role): void {
    this.editingId = role.id;
    this.editName = role.name;
    this.editPerms = new Set(role.permissions);
  }

  cancelEdit(): void {
    this.editingId = null;
    this.editPerms.clear();
  }

  toggleEditPerm(p: string): void {
    if (this.editPerms.has(p)) this.editPerms.delete(p); else this.editPerms.add(p);
  }

  saveEdit(role: Role): void {
    if (this.savingRole) return;
    this.savingRole = true;
    this.admin.updateRole(role.id, { name: this.editName.trim(), permissions: [...this.editPerms] }).subscribe({
      next: () => {
        this.savingRole = false;
        this.cancelEdit();
        this.snackbar.info(this.translate.instant('admin.roles.saved'));
        this.loadRoles();
      },
      error: err => {
        this.savingRole = false;
        this.snackbar.info(err?.error?.message || this.translate.instant('admin.roles.saveError'));
      },
    });
  }

  deleteRole(role: Role): void {
    if (role.isSystem) return;
    if (!confirm(this.translate.instant('admin.roles.confirmDelete', { name: role.name }))) return;
    this.admin.deleteRole(role.id).subscribe({
      next: () => { this.snackbar.info(this.translate.instant('admin.roles.deleted')); this.loadRoles(); },
      error: err => this.snackbar.info(err?.error?.message || this.translate.instant('admin.roles.saveError')),
    });
  }

  // --- Gruppen-Zuweisung (0.589.0) ------------------------------------
  /** „Everyone" hat keine Mitgliedszeilen — eine Rolle dort wirkte nirgends (der Server lehnt sie ab). */
  get assignableGroups(): Group[] { return this.groups.filter(g => !g.isEveryone); }

  selectGroup(group: Group): void {
    this.selectedGroup = group;
    this.groupRoleIds = new Set();
    this.groupRolesLoaded = false;
    this.admin.getGroupRoles(group.id).subscribe({
      next: gr => {
        if (this.selectedGroup?.id !== group.id) return;   // veraltete Antwort einer vorher gewählten Gruppe
        this.groupRoleIds = new Set(gr.roleIds);
        this.groupRolesLoaded = true;
      },
      error: () => {
        if (this.selectedGroup?.id !== group.id) return;
        this.snackbar.info(this.translate.instant('admin.roles.loadError'));
      },
    });
  }

  toggleGroupRole(roleId: number): void {
    if (this.groupRoleIds.has(roleId)) this.groupRoleIds.delete(roleId); else this.groupRoleIds.add(roleId);
  }

  saveGroupRoles(): void {
    if (!this.selectedGroup || !this.groupRolesLoaded || this.savingGroupRoles) return;
    this.savingGroupRoles = true;
    this.admin.setGroupRoles(this.selectedGroup.id, [...this.groupRoleIds]).subscribe({
      next: () => {
        this.savingGroupRoles = false;
        this.snackbar.info(this.translate.instant('admin.roles.groupRolesSaved'));
        this.loadRoles();   // die Rollenkarten nennen ihre Gruppen
      },
      error: err => {
        this.savingGroupRoles = false;
        this.snackbar.info(err?.error?.message || this.translate.instant('admin.roles.saveError'));
      },
    });
  }

  // --- Nutzer-Zuweisung ----------------------------------------------
  onUserSearch(q: string): void { this.searchInput.next(q); }

  /** Rollen, die einem Nutzer zugewiesen werden können (admin-Rolle folgt dem IsAdmin-Flag). */
  get assignableRoles(): Role[] { return this.roles.filter(r => !this.isAdminRole(r)); }

  selectUser(user: AdminUser): void {
    this.selectedUser = user;
    this.userRoleIds = new Set();
    this.userRolesLoaded = false;
    this.admin.getUserRoles(user.id).subscribe({
      next: ur => {
        if (this.selectedUser?.id !== user.id) return;     // veraltete Antwort eines vorher gewählten Nutzers
        this.userRoleIds = new Set(ur.roleIds);
        this.userRolesLoaded = true;
      },
      error: () => {
        if (this.selectedUser?.id !== user.id) return;
        this.snackbar.info(this.translate.instant('admin.roles.loadError'));
      },
    });
  }

  toggleUserRole(roleId: number): void {
    if (this.userRoleIds.has(roleId)) this.userRoleIds.delete(roleId); else this.userRoleIds.add(roleId);
  }

  saveUserRoles(): void {
    if (!this.selectedUser || !this.userRolesLoaded || this.savingUserRoles) return;
    this.savingUserRoles = true;
    this.admin.setUserRoles(this.selectedUser.id, [...this.userRoleIds]).subscribe({
      next: () => {
        this.savingUserRoles = false;
        this.snackbar.info(this.translate.instant('admin.roles.userRolesSaved'));
        this.loadRoles(); // Mitgliederzahlen aktualisieren
      },
      error: () => {
        this.savingUserRoles = false;
        this.snackbar.info(this.translate.instant('admin.roles.saveError'));
      },
    });
  }
}
