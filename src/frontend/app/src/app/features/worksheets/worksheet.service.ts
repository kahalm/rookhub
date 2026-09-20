import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, of, tap } from 'rxjs';
import { TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';

/** Ein Aufgabenblatt in der Übersicht (ohne Stellungen). */
export interface WorksheetSummary {
  id: number;
  name: string;
  isClipboard: boolean;
  /** Diagramme je A4-Seite (2/4/6). */
  perPage: number;
  itemCount: number;
  createdAt: string;
  updatedAt: string;
}

/** Eine Aufgabe des Blatts: die Stellung selbst plus die Worte, die der Ersteller dazu schreibt. */
export interface WorksheetItem {
  id: number;
  sortOrder: number;
  fen: string;
  orientation: 'white' | 'black';
  heading: string;
  text: string;
  source: 'Manual' | 'Standard' | 'Book';
  sourceId: number | null;
  bookId: number | null;
}

export interface Worksheet extends WorksheetSummary {
  items: WorksheetItem[];
}

/** Eine zu sendende Stellung. Sie wird ausgeschrieben, nicht verlinkt (siehe `WorksheetItem` im Backend). */
export interface NewWorksheetItem {
  fen: string;
  orientation: 'white' | 'black';
  heading?: string;
  text?: string;
  source?: 'Manual' | 'Standard' | 'Book';
  sourceId?: number | null;
  bookId?: number | null;
}

/** Antwort des Sendens — sagt, wohin es ging und was ankam. */
export interface AddItemsResult {
  worksheetId: number;
  name: string;
  isClipboard: boolean;
  added: number;
  skipped: number;
  total: number;
  full: boolean;
}

/**
 * Aufgabenblätter: die Zwischenablage (der Sammelkorb, in dem standardmäßig alles landet, was man
 * „an ein Aufgabenblatt schickt") und die benannten Blätter.
 *
 * <p>Neben den HTTP-Aufrufen hält der Service die ZIELLISTE für das „An Aufgabenblatt senden"-Menü
 * vor (ein Menü darf nicht bei jedem Aufklappen nachladen) und erledigt das Senden samt Rückmeldung
 * ({@link sendAndNotify}) — dieselbe Snackbar in Kurs, Kapitel und Puzzle-Menü statt dreimal
 * nachgebaut.</p>
 */
@Injectable({ providedIn: 'root' })
export class WorksheetService {
  private readonly apiUrl = '/api/worksheets';
  private targets: WorksheetSummary[] | null = null;

  private http = inject(HttpClient);
  private router = inject(Router);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);

  list(): Observable<WorksheetSummary[]> {
    return this.http.get<WorksheetSummary[]>(this.apiUrl).pipe(tap(l => (this.targets = l)));
  }

  /** Zielliste fürs Sende-Menü; beim ersten Aufklappen geladen, danach aus dem Gedächtnis. */
  targetList(): Observable<WorksheetSummary[]> {
    return this.targets ? of(this.targets) : this.list();
  }

  /** Nach jeder Änderung, die Namen/Anzahl betrifft: Zielliste beim nächsten Aufklappen neu holen. */
  invalidateTargets(): void { this.targets = null; }

  clipboard(): Observable<Worksheet> {
    return this.http.get<Worksheet>(`${this.apiUrl}/clipboard`);
  }

  get(id: number): Observable<Worksheet> {
    return this.http.get<Worksheet>(`${this.apiUrl}/${id}`);
  }

  create(name: string, perPage?: number): Observable<Worksheet> {
    return this.http.post<Worksheet>(this.apiUrl, { name, perPage }).pipe(tap(() => this.invalidateTargets()));
  }

  /** Zwischenablage unter einem Namen sichern — die Stellungen wandern mit, die Ablage bleibt leer zurück. */
  saveClipboardAs(name: string, perPage?: number): Observable<Worksheet> {
    return this.http.post<Worksheet>(`${this.apiUrl}/clipboard/save`, { name, perPage })
      .pipe(tap(() => this.invalidateTargets()));
  }

  update(id: number, patch: { name?: string; perPage?: number }): Observable<Worksheet> {
    return this.http.put<Worksheet>(`${this.apiUrl}/${id}`, patch).pipe(tap(() => this.invalidateTargets()));
  }

  remove(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`).pipe(tap(() => this.invalidateTargets()));
  }

  /** Alle Stellungen eines Blatts entfernen (das Blatt bleibt). */
  clear(id: number): Observable<Worksheet> {
    return this.http.delete<Worksheet>(`${this.apiUrl}/${id}/items`).pipe(tap(() => this.invalidateTargets()));
  }

  /** Stellungen anhängen; `worksheetId` `null` = Zwischenablage. */
  addItems(worksheetId: number | null, items: NewWorksheetItem[]): Observable<AddItemsResult> {
    return this.http.post<AddItemsResult>(`${this.apiUrl}/items`, { worksheetId, items })
      .pipe(tap(() => this.invalidateTargets()));
  }

  updateItem(id: number, itemId: number, patch: Partial<Pick<WorksheetItem, 'heading' | 'text' | 'orientation'>>): Observable<WorksheetItem> {
    return this.http.put<WorksheetItem>(`${this.apiUrl}/${id}/items/${itemId}`, patch);
  }

  removeItem(id: number, itemId: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}/items/${itemId}`).pipe(tap(() => this.invalidateTargets()));
  }

  /** Reihenfolge setzen (Item-IDs in Wunsch-Abfolge). */
  reorder(id: number, itemIds: number[]): Observable<Worksheet> {
    return this.http.put<Worksheet>(`${this.apiUrl}/${id}/order`, { itemIds });
  }

  /**
   * „An Aufgabenblatt senden" samt Rückmeldung: Snackbar mit Ziel + Anzahl und einem „Öffnen", das
   * direkt ins Blatt führt. Ohne Stellungen (nichts Druckbares in der Auswahl) gibt es nur den
   * Hinweis, keinen Serveraufruf.
   */
  sendAndNotify(worksheetId: number | null, items: NewWorksheetItem[]): void {
    if (items.length === 0) {
      this.snackbar.info(this.translate.instant('worksheets.send.nothing'));
      return;
    }
    this.addItems(worksheetId, items).subscribe({
      next: res => {
        const target = res.isClipboard ? this.translate.instant('worksheets.clipboard') : res.name;
        const msg = res.full
          ? this.translate.instant('worksheets.send.full', { target, n: res.total })
          : res.added === 0
            ? this.translate.instant('worksheets.send.allKnown', { target })
            : this.translate.instant('worksheets.send.done', { n: res.added, target });
        const ref = this.snackbar.show(msg, { action: 'worksheets.send.open', duration: 6000 });
        ref.onAction().subscribe(() => this.router.navigate(['/worksheets', res.worksheetId]));
      },
      error: () => this.snackbar.warn(this.translate.instant('worksheets.send.error')),
    });
  }
}
