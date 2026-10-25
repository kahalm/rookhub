import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { reasonText } from '../../core/club-format';
import { CHESSBASE_MAX_UPLOAD_BYTES, chessBaseErrorText, chessBaseNote, chessBaseSelection, packForUpload } from '../../core/chessbase-upload';
import { FilePickDirective } from '../../shared/file-pick.directive';
import { ClubDraftStore } from './club-draft-store';

/**
 * Woher die PGN-Liste kommt (Hinzufügen-Seite): PGN-Datei, ChessBase-Datenbank, öffentliche Lichess-Studie oder
 * eingefügter Text — und „Partien prüfen". Jeder Weg führt in dieselbe Übersicht ({@link ClubDraftStore.startPreview});
 * der Zustand liegt im Store, damit der Wechsel auf „Partieformular" und zurück nichts verliert.
 */
@Component({
  selector: 'lh-pgn-source-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FilePickDirective],
  // Ohne eigenen Kasten: die Felder stehen weiter direkt im Panel der Seite.
  styles: [':host { display: contents; }'],
  template: `
    <label class="field">PGN-Datei
      <input type="file" lhFilePick accept=".pgn,application/x-chess-pgn,text/plain" [disabled]="store().busy()" (change)="pickFile($event)" />
      @if (store().readingFile()) { <span class="small muted"><b>Lese die Datei …</b></span> }
    </label>
    <label class="field">… oder eine ChessBase-Datenbank
      <input type="file" lhFilePick multiple [disabled]="store().busy()" (change)="pickChessBase($event)" />
      <span class="small muted">Alle Dateien der Datenbank auswählen (z. B. MeineSpiele.2cbh, .2cbg, .2lid … bzw. .cbh,
        .cbg, .cbp …) oder ein ZIP davon. Gelesen wird die Hauptvariante, ohne Kommentare.
        @if (store().readingDb()) { <b>Lese die Datenbank …</b> }</span>
    </label>
    <div class="field">… oder eine öffentliche Lichess-Studie
      <div class="linkrow">
        <input type="url" inputmode="url" placeholder="https://lichess.org/study/…" [value]="store().studyUrl()"
               (input)="store().studyUrl.set($any($event.target).value)" (keydown.enter)="$event.preventDefault(); loadStudy()" />
        <button type="button" class="btn-sec" [disabled]="store().busy() || !store().studyUrl().trim()" (click)="loadStudy()">
          {{ store().loadingStudy() ? 'Lade …' : 'Laden' }}</button>
      </div>
    </div>
    <label class="field">… oder hier einfügen
      <textarea rows="6" spellcheck="false" placeholder="[Event &quot;…&quot;]&#10;1. e4 c5 2. Nf3 …" [value]="store().pgn()"
                (input)="store().pgn.set($any($event.target).value)"></textarea>
    </label>
    <div class="actions">
      <button type="button" class="btn-pri" [disabled]="store().busy() || !store().pgn().trim()" (click)="store().startPreview()">
        {{ store().previewing() ? 'Lese …' : 'Partien prüfen' }}</button>
      <!-- a1-l-partien-pruefen-gesperrt: solange nichts da ist, sagt die Zeile, WARUM der Knopf gesperrt ist (DESIGN 5.4). -->
      @if (!store().busy() && !store().pgn().trim()) {
        <span class="muted small pick-reason">Erst eine PGN-Datei wählen, eine Studie laden oder Partien einfügen.</span>
      } @else {
        <span class="muted small">Erst kommt eine Übersicht — gespeichert wird erst mit „Importieren“. Mehr als 500 Partien kommen in Paketen.</span>
      }
      <span class="update-msg" [class.err]="!!store().importError()" role="status">{{ store().importError() ?? '' }}</span>
    </div>
  `,
})
export class PgnSourcePanelComponent {
  readonly store = input.required<ClubDraftStore>();

  /** Eine PGN-Datei: lesen und gleich in die Übersicht — wie ChessBase-Datenbank und Lichess-Studie (UX-037). Vorher füllte
   *  die Wahl nur das Textfeld, das am Handy unter dem Rand lag: sichtbar passierte nichts, und dieselbe Datei noch einmal
   *  zu wählen löste kein `change` aus. */
  async pickFile(ev: Event): Promise<void> {
    const s = this.store();
    const input = ev.target as HTMLInputElement;
    const f = input.files?.[0];
    input.value = '';                                                  // dieselbe Auswahl darf noch einmal kommen
    // Läuft schon eine Liste, ist das Feld gesperrt — kommt trotzdem eine Wahl durch, keine zweite Vorschau daneben.
    if (!f || s.busy()) return;
    s.readingFile.set(true);
    try {
      s.dbNote.set(null);
      s.portionNote.set(null);
      s.importError.set(null);
      try {
        s.pgn.set(await f.text());
      } catch {
        s.importError.set('Die Datei ließ sich nicht lesen.');
        return;
      }
      s.loaded(s.pgn(), 'datei', f.name);
      s.result.set(null);
      await s.startPreview();                                       // teilt in Pakete wie jede andere Liste
    } finally {
      s.readingFile.set(false);
    }
  }

  /**
   * Eine ChessBase-Datenbank (0.598.0): die nötigen Dateien gepackt hochladen, der Server liefert das PGN — ab dort wie
   * jede andere Liste, auch das Aufteilen in Pakete (`startPreview`).
   */
  async pickChessBase(ev: Event): Promise<void> {
    const s = this.store();
    const input = ev.target as HTMLInputElement;
    const picked = Array.from(input.files ?? []);
    input.value = '';                                                  // dieselbe Auswahl darf noch einmal kommen
    if (!picked.length || s.busy()) return;
    s.importError.set(null);
    s.dbNote.set(null);
    s.portionNote.set(null);
    s.result.set(null);
    const { send, hasDatabase } = chessBaseSelection(picked);
    if (!hasDatabase) {
      s.importError.set('Keine ChessBase-Datenbank dabei — die Datei mit der Endung .cbh oder .2cbh muss mit (oder ein ZIP).');
      return;
    }
    s.readingDb.set(true);
    try {
      const packed = await Promise.all(send.map(packForUpload));
      if (packed.reduce((sum, p) => sum + p.blob.size, 0) > CHESSBASE_MAX_UPLOAD_BYTES) {
        s.importError.set('Die Datenbank ist zu groß für einen Upload (gepackt höchstens 15 MB).');
        return;
      }
      const r = await s.client.chessBase(packed);
      if (!r.converted) {
        s.importError.set(r.games ? chessBaseNote(r) + ' Nichts zu importieren.' : `${r.name}: keine Partien in der Datenbank.`);
        return;
      }
      s.dbNote.set(chessBaseNote(r));
      s.pgn.set(r.pgn);
      s.loaded(r.pgn, 'chessbase', `${r.name}.${r.format}`);
      await s.startPreview();                                       // teilt in Pakete wie jede andere Liste
    } catch (err) {
      s.importError.set(chessBaseErrorText(err));
    } finally {
      s.readingDb.set(false);
    }
  }

  /** Eine öffentliche Lichess-Studie holen (der Server ruft Lichess) — danach geht es weiter wie mit einer Datei. */
  async loadStudy(): Promise<void> {
    const s = this.store();
    if (s.busy()) return;                                           // Enter im Feld umgeht den gesperrten Knopf
    s.loadingStudy.set(true);
    s.importError.set(null);
    s.dbNote.set(null);
    s.portionNote.set(null);
    try {
      s.pgn.set(await s.client.lichess(s.studyUrl().trim()));
      s.loaded(s.pgn(), 'lichess', s.studyUrl().trim());
      s.result.set(null);
      await s.startPreview();
    } catch (err) {
      const e = err instanceof HttpErrorResponse ? err : null;
      s.importError.set(e?.error?.reason ? reasonText(e.error.reason) : 'Die Studie ließ sich nicht laden.');
    } finally {
      s.loadingStudy.set(false);
    }
  }
}
