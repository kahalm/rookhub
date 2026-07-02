import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../../core/snackbar.service';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { AdminService, Group, MenuItemConfig, MenuVisibilityLevel } from '../../../core/admin.service';
import { MenuService } from '../../../core/menu.service';

/**
 * Admin-Tab „Menü-Sichtbarkeit": pro Menüeintrag die Sichtbarkeitsstufe (All/Registered/Groups/Admin)
 * + bei „Groups" die freigegebenen Gruppen. Aus <c>AdminComponent</c> ausgegliedert; lädt Config +
 * Gruppenliste selbst (self-contained).
 */
@Component({
  selector: 'app-admin-menu-visibility',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatSelectModule,
    TranslateModule, LoadingSpinnerComponent,
  ],
  templateUrl: './admin-menu-visibility.component.html',
  styleUrl: './admin-menu-visibility.component.scss',
})
export class AdminMenuVisibilityComponent implements OnInit {
  menuConfig: MenuItemConfig[] = [];
  menuLoading = false;
  menuSaving = false;
  readonly menuLevels: MenuVisibilityLevel[] = ['All', 'Registered', 'Groups', 'Admin'];
  groups: Group[] = [];

  constructor(
    private adminService: AdminService,
    private menu: MenuService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  ngOnInit(): void {
    this.loadMenuConfig();
    this.adminService.getGroups().subscribe({ next: g => this.groups = g, error: () => {} });
  }

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
}
