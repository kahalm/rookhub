import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { ExternalEngineService, ExternalEngineInfo, engineSourceOf } from '../analysis/external-engine.service';

/**
 * Karte „Externe Engine" — zwei Quellen, EINE Liste:
 * - **RookHub direkt** (`rhe_…`): der Provider auf dem eigenen Rechner meldet sich mit einem API-Token
 *   (Bereich „Engine") direkt bei RookHub an — kein Lichess-Konto nötig. Die Karte zeigt je Engine einen
 *   Online-Punkt (Provider hat in den letzten 30 s abgefragt) und kann die Registrierung entfernen.
 * - **Über Lichess** (`eei_…`, für Cloud-Anbieter): Lichess-API-Token (Scope engine:read) hinterlegen —
 *   dann stehen alle External Engines des Lichess-Kontos zur Wahl.
 * Die Liste wird IMMER geholt (direkte Engines gibt es auch ohne Lichess-Token); ein abgewiesener Token
 * und ein nicht antwortendes Lichess werden benannt statt leer auszusehen. Hintergrund-Auswahl und
 * Haus-Engine gelten für beide Quellen.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-engine-card',
  standalone: true,
  imports: [
    MatSelectModule, MatCheckboxModule,
    CommonModule, FormsModule, MatFormFieldModule, MatInputModule,
    MatButtonModule, MatIconModule, TranslatePipe,
  ],
  template: `
    <div class="engine-section">
      <h4>{{ 'profile.engine.title' | translate }}</h4>
      <p class="engine-hint">{{ 'profile.engine.hint' | translate }}</p>

      <h5>{{ 'profile.engine.directTitle' | translate }}</h5>
      <p class="engine-hint">{{ 'profile.engine.directHint' | translate }}</p>
      @if (enginesLoaded) {
        @if (directEngines.length > 0) {
          <ul class="engine-list direct-list">
            @for (e of directEngines; track e.id) {
              <li>
                <span class="dot" [class.on]="e.online === true"
                      [attr.title]="(e.online ? 'profile.engine.online' : 'profile.engine.offline') | translate"></span>
                <strong>{{ e.name }}</strong> — {{ 'profile.engine.specs' | translate: { threads: e.maxThreads, hash: e.maxHash } }}
                <span class="state">{{ (e.online ? 'profile.engine.online' : 'profile.engine.offline') | translate }}</span>
                <button mat-icon-button type="button" class="del" (click)="removeDirect(e)"
                        [attr.aria-label]="'profile.engine.deleteEngine' | translate"
                        [attr.title]="'profile.engine.deleteEngine' | translate">
                  <mat-icon>delete</mat-icon>
                </button>
              </li>
            }
          </ul>
        } @else {
          <p class="engine-hint">{{ 'profile.engine.directNone' | translate }}</p>
        }
      }

      <h5>{{ 'profile.engine.lichessTitle' | translate }}</h5>
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

      @if (listFailed) {
        <p class="engine-warn"><mat-icon>cloud_off</mat-icon> {{ 'profile.engine.listFailed' | translate }}</p>
      } @else if (enginesLoaded) {
        @if (tokenInvalid) {
          <p class="engine-warn"><mat-icon>error_outline</mat-icon> {{ 'profile.engine.tokenInvalid' | translate }}</p>
        }
        @if (lichessUnreachable) {
          <p class="engine-warn"><mat-icon>cloud_off</mat-icon> {{ 'profile.engine.lichessUnreachable' | translate }}</p>
        }
        @if (hasCredentials && lichessEngines.length > 0) {
          <ul class="engine-list">
            @for (e of lichessEngines; track e.id) {
              <li><strong>{{ e.name }}</strong> — {{ 'profile.engine.specs' | translate: { threads: e.maxThreads, hash: e.maxHash } }}</li>
            }
          </ul>
        } @else if (hasCredentials && !tokenInvalid && !lichessUnreachable) {
          <p class="engine-hint">{{ 'profile.engine.noLichessEngines' | translate }}</p>
        }

        @if (engines.length > 0) {
          <div class="engine-row">
            <mat-form-field appearance="outline" class="bg-field" subscriptSizing="dynamic">
              <mat-label>{{ 'profile.engine.backgroundLabel' | translate }}</mat-label>
              <mat-select [(ngModel)]="backgroundEngineIds" name="backgroundEngine" multiple
                          (selectionChange)="saveBackground()">
                @for (e of engines; track e.id) {
                  <mat-option [value]="e.id">{{ e.name }} · {{ sourceLabel(e) | translate }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
          </div>
          <p class="engine-hint">{{ 'profile.engine.backgroundHint' | translate }}</p>
          @if (canShareHouseEngine && backgroundEngineIds.length > 0) {
            <mat-checkbox [(ngModel)]="shareAsHouseEngine" name="shareAsHouseEngine"
                          (change)="saveHouseEngine()">
              {{ 'profile.engine.houseLabel' | translate }}
            </mat-checkbox>
            <p class="engine-hint">{{ 'profile.engine.houseHint' | translate }}</p>
          }
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
    .engine-section h5 { margin: 0.75rem 0 0.25rem; color: #e0e0e0; font-size: 0.95rem; font-weight: 500; }
    .direct-list { list-style: none; padding-left: 0; }
    .direct-list li { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; }
    .dot { width: 10px; height: 10px; border-radius: 50%; background: #757575; flex: 0 0 10px; }
    .dot.on { background: #66bb6a; }
    .state { color: #9e9e9e; font-size: 0.8rem; }
    .del { margin-left: auto; }
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
  /** Direkt bei RookHub angemeldete Engines (mit Online-Punkt) bzw. die des Lichess-Kontos — beide aus `engines`. */
  directEngines: ExternalEngineInfo[] = [];
  lichessEngines: ExternalEngineInfo[] = [];
  /** Lichess antwortete nicht; die Liste enthält nur die direkt angemeldeten Engines. */
  lichessUnreachable = false;
  /** Hintergrund-Engine für Analyseaufträge (null = keine). */
  backgroundEngineIds: string[] = [];
  /** Haus-Engine: die eigenen Hintergrund-Engines rechnen auch fremde eingeworfene Partien. */
  shareAsHouseEngine = false;
  /** Nur ein Admin bekommt das Häkchen überhaupt zu sehen. */
  canShareHouseEngine = false;
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
        this.cdr.markForCheck();
      },
      error: () => {},
    });
    // Immer: direkt angemeldete Engines gibt es auch ohne Lichess-Token.
    this.loadEngines();
  }

  /** i18n-Schlüssel der Quelle für die Hintergrund-Auswahl. */
  sourceLabel(e: ExternalEngineInfo): string {
    return engineSourceOf(e) === 'rookhub' ? 'profile.engine.sourceRookhub' : 'profile.engine.sourceLichess';
  }

  /** Registrierung einer direkt angemeldeten Engine entfernen (der Server nimmt sie auch aus der
   *  Hintergrund-Liste). Läuft ihr Provider noch, meldet er sich beim nächsten Start wieder an. */
  removeDirect(e: ExternalEngineInfo): void {
    if (!confirm(this.translate.instant('profile.engine.deleteConfirm', { name: e.name }))) return;
    this.externalEngines.deleteDirectEngine(e.id).subscribe({
      next: () => {
        this.snackbar.success(this.translate.instant('profile.engine.deleted'));
        this.loadEngines();
      },
      error: () => this.snackbar.warn(this.translate.instant('profile.engine.deleteFailed')),
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
        this.tokenInvalid = false;
        this.listFailed = false;
        // Die Lichess-Engines fallen weg, direkt angemeldete bleiben (samt ihrem Platz in der
        // Hintergrund-Liste) — also neu holen statt alles zu leeren.
        this.loadEngines();
        this.cdr.markForCheck();
      },
      error: () => this.snackbar.warn(this.translate.instant('profile.engine.saveFailed')),
    });
  }

  /**
   * Hintergrund-Engines speichern — sie rechnen die Analyse-Aufträge und fehlen dafür im
   * Live-Picker. Mehrere sind erlaubt und der Sinn der Sache: der Server rechnet je Engine EINEN
   * Auftrag, es laufen also so viele nebeneinander, wie hier ausgewählt sind.
   */
  saveBackground(): void {
    this.externalEngines.setBackgroundEngines(this.backgroundEngineIds).subscribe({
      next: r => { this.backgroundEngineIds = r.backgroundEngineIds ?? []; this.snackbar.success(this.translate.instant('profile.engine.backgroundSaved')); this.cdr.markForCheck(); },
      error: () => { this.snackbar.warn(this.translate.instant('profile.engine.backgroundFailed')); this.cdr.markForCheck(); },
    });
  }

  /**
   * Haus-Engine schalten: die eigenen Hintergrund-Engines rechnen dann auch die Partien, die andere
   * auf der Punktepartie-Seite einwerfen. Das ist verschenkte Rechenzeit der eigenen Maschine, also
   * eine bewusste Entscheidung — und nur ein Admin darf sie treffen (der Server prüft es nochmal).
   */
  saveHouseEngine(): void {
    this.externalEngines.setHouseEngine(this.shareAsHouseEngine).subscribe({
      next: r => {
        this.shareAsHouseEngine = r.shareAsHouseEngine;
        this.snackbar.success(this.translate.instant('profile.engine.houseSaved'));
        this.cdr.markForCheck();
      },
      error: () => {
        // Zurückdrehen: sonst zeigt das Häkchen einen Zustand, den der Server nicht kennt.
        this.shareAsHouseEngine = !this.shareAsHouseEngine;
        this.snackbar.warn(this.translate.instant('profile.engine.backgroundFailed'));
        this.cdr.markForCheck();
      },
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
        this.lichessUnreachable = r.lichessUnreachable ?? false;
        this.engines = r.engines;
        this.directEngines = r.engines.filter(e => engineSourceOf(e) === 'rookhub');
        this.lichessEngines = r.engines.filter(e => engineSourceOf(e) === 'lichess');
        this.backgroundEngineIds = r.backgroundEngineIds ?? [];
        this.shareAsHouseEngine = r.shareAsHouseEngine ?? false;
        this.canShareHouseEngine = r.canShareHouseEngine ?? false;
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
