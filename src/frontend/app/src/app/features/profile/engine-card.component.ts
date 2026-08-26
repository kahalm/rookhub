import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { ExternalEngineService, ExternalEngineInfo } from '../analysis/external-engine.service';

/**
 * Karte „Externe Engine (Lichess)": Lichess-API-Token (Scope engine:read) hinterlegen —
 * damit stehen im Analysebrett alle External Engines des Lichess-Kontos zur Wahl (eigener
 * Rechner via offiziellem Provider, Miet-Anbieter). Nach Speichern/Laden zeigt die Karte
 * die gefundenen Engines als direktes Funktioniert-Feedback; ein abgewiesener Token wird
 * benannt statt leer auszusehen.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-engine-card',
  standalone: true,
  imports: [
    MatSelectModule,
    CommonModule, FormsModule, MatFormFieldModule, MatInputModule,
    MatButtonModule, MatIconModule, TranslatePipe,
  ],
  template: `
    <div class="engine-section">
      <h4>{{ 'profile.engine.title' | translate }}</h4>
      <p class="engine-hint">{{ 'profile.engine.hint' | translate }}</p>
      <p class="engine-hint">
        <a href="https://lichess.org/account/oauth/token/create?scopes[]=engine:read&description=RookHub"
           target="_blank" rel="noopener">{{ 'profile.engine.createToken' | translate }}</a>
      </p>

      @if (hasCredentials) {
        <div class="engine-row">
          <span class="masked">{{ 'profile.engine.stored' | translate }}: <code>{{ maskedToken || '••••' }}</code></span>
          <button mat-stroked-button color="warn" type="button" (click)="remove()">
            <mat-icon>delete</mat-icon> {{ 'common.delete' | translate }}
          </button>
        </div>
      }

      <div class="engine-row">
        <mat-form-field appearance="outline" class="token-field">
          <mat-label>{{ 'profile.engine.tokenLabel' | translate }}</mat-label>
          <input matInput type="password" [(ngModel)]="tokenInput" name="lichessEngineToken"
                 autocomplete="off" (keyup.enter)="save()">
        </mat-form-field>
        <button mat-stroked-button type="button" [disabled]="!tokenInput.trim() || saving" (click)="save()">
          <mat-icon>save</mat-icon> {{ 'common.save' | translate }}
        </button>
      </div>

      @if (tokenInvalid) {
        <p class="engine-warn"><mat-icon>error_outline</mat-icon> {{ 'profile.engine.tokenInvalid' | translate }}</p>
      } @else if (listFailed) {
        <p class="engine-warn"><mat-icon>cloud_off</mat-icon> {{ 'profile.engine.listFailed' | translate }}</p>
      } @else if (hasCredentials && enginesLoaded) {
        @if (engines.length > 0) {
          <p class="engine-list-title">{{ 'profile.engine.enginesFound' | translate }}</p>
          <ul class="engine-list">
            @for (e of engines; track e.id) {
              <li><strong>{{ e.name }}</strong> — {{ 'profile.engine.specs' | translate: { threads: e.maxThreads, hash: e.maxHash } }}</li>
            }
          </ul>
          <div class="engine-row">
            <mat-form-field appearance="outline" class="bg-field" subscriptSizing="dynamic">
              <mat-label>{{ 'profile.engine.backgroundLabel' | translate }}</mat-label>
              <mat-select [(ngModel)]="backgroundEngineId" name="backgroundEngine" (selectionChange)="saveBackground()">
                <mat-option [value]="null">{{ 'profile.engine.backgroundNone' | translate }}</mat-option>
                @for (e of engines; track e.id) { <mat-option [value]="e.id">{{ e.name }}</mat-option> }
              </mat-select>
            </mat-form-field>
          </div>
          <p class="engine-hint">{{ 'profile.engine.backgroundHint' | translate }}</p>
        } @else {
          <p class="engine-hint">{{ 'profile.engine.noEngines' | translate }}</p>
        }
      }
    </div>
  `,
  styles: [`
    .engine-section h4 { margin: 0 0 0.25rem; color: #90caf9; }
    .engine-hint { color: #bdbdbd; font-size: 0.85rem; margin: 0 0 0.5rem; }
    .engine-hint a { color: #90caf9; }
    .engine-row { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin-bottom: 4px; }
    .token-field { width: 320px; max-width: 100%; }
    .bg-field { width: 320px; max-width: 100%; margin-top: 8px; }
    .masked { color: #ccc; font-size: 0.9rem; }
    .masked code { background: rgba(255,255,255,0.08); padding: 1px 6px; border-radius: 4px; }
    .engine-warn { display: flex; align-items: center; gap: 6px; color: #ef9a9a; font-size: 0.85rem; margin: 4px 0 0; }
    .engine-warn mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .engine-list-title { color: #ccc; font-size: 0.9rem; margin: 4px 0 2px; }
    .engine-list { margin: 0; padding-left: 20px; color: #ccc; font-size: 0.9rem; }
  `]
})
export class EngineCardComponent implements OnInit, OnDestroy {
  hasCredentials = false;
  maskedToken: string | null = null;
  tokenInput = '';
  saving = false;
  tokenInvalid = false;
  enginesLoaded = false;
  listFailed = false;
  engines: ExternalEngineInfo[] = [];
  /** Hintergrund-Engine für Analyseaufträge (null = keine). */
  backgroundEngineId: string | null = null;
  private listSub?: Subscription;
  private statusSub?: Subscription;

  constructor(
    private externalEngines: ExternalEngineService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
    // Angular 22 refresht nach HTTP nicht ohne View-Marke (CLAUDE.md-Konvention) — der ganze
    // Inhalt dieser Karte kommt aus HTTP-Antworten, also nach jeder markieren.
    private cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    this.statusSub = this.externalEngines.getCredentials().subscribe({
      next: s => {
        this.hasCredentials = s.hasCredentials;
        this.maskedToken = s.maskedToken;
        if (s.hasCredentials) this.loadEngines();
        this.cdr.markForCheck();
      },
      error: () => {},
    });
  }

  save(): void {
    const token = this.tokenInput.trim();
    if (!token || this.saving) return;
    this.saving = true;
    this.externalEngines.saveToken(token).subscribe({
      next: s => {
        this.saving = false;
        this.hasCredentials = s.hasCredentials;
        this.maskedToken = s.maskedToken;
        this.tokenInput = '';
        this.snackbar.success(this.translate.instant('profile.engine.saved'));
        this.loadEngines(true);   // direkt nach dem Speichern: Fehler sichtbar machen
        this.cdr.markForCheck();
      },
      error: () => {
        this.saving = false;
        this.snackbar.warn(this.translate.instant('profile.engine.saveFailed'));
        this.cdr.markForCheck();
      },
    });
  }

  remove(): void {
    this.externalEngines.deleteToken().subscribe({
      next: () => {
        this.hasCredentials = false;
        this.maskedToken = null;
        this.engines = [];
        this.enginesLoaded = false;
        this.tokenInvalid = false;
        this.listFailed = false;
        this.backgroundEngineId = null;
        this.cdr.markForCheck();
      },
      error: () => this.snackbar.warn(this.translate.instant('profile.engine.saveFailed')),
    });
  }

  /** Hintergrund-Engine speichern — sie rechnet die Analyse-Aufträge und fehlt dafür im Live-Picker. */
  saveBackground(): void {
    const chosen = this.backgroundEngineId;
    this.externalEngines.setBackgroundEngine(chosen).subscribe({
      next: r => { this.backgroundEngineId = r.backgroundEngineId; this.snackbar.success(this.translate.instant('profile.engine.backgroundSaved')); this.cdr.markForCheck(); },
      error: () => { this.snackbar.warn(this.translate.instant('profile.engine.backgroundFailed')); this.cdr.markForCheck(); },
    });
  }

  /** @param announceError true, wenn der Abruf einer Nutzer-Aktion folgt (Speichern) — dann darf
   *  ein Fehlschlag NICHT still bleiben, sonst wirkt „gespeichert" wie „geprüft und in Ordnung". */
  private loadEngines(announceError = false): void {
    this.listSub?.unsubscribe();
    this.listSub = this.externalEngines.listEngines().subscribe({
      next: r => {
        this.enginesLoaded = true;
        this.tokenInvalid = r.tokenInvalid;
        this.engines = r.engines;
        this.backgroundEngineId = r.backgroundEngineId ?? null;
        this.listFailed = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.enginesLoaded = false;
        this.listFailed = true;
        if (announceError) this.snackbar.warn(this.translate.instant('profile.engine.listFailed'));
        this.cdr.markForCheck();
      },
    });
  }

  ngOnDestroy(): void {
    this.listSub?.unsubscribe();
    this.statusSub?.unsubscribe();
  }
}
