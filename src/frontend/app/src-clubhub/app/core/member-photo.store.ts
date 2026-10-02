import { Injectable, inject, signal, untracked } from '@angular/core';
import { ClubApiService } from './club-api.service';

interface Job { key: string; id: number; version: number; }

/**
 * Die Vorschaubilder der Karteiblätter (Wunsch 2026-10-02: „ein Bild hinterlegen"). Die Bilder liegen hinter der Anmeldung —
 * ein `<img src>` schickt keinen Bearer —, deshalb holt der Speicher jedes als Blob über die HTTP-Kette und gibt eine
 * Objekt-Adresse heraus. EIN Speicher für die ganze Seite, weil dieselben Bilder in Kartei und Blatt stehen: jedes wird je
 * Marke (`photoVersion`) höchstens einmal geholt, höchstens `Parallel` gleichzeitig (die Kartei zeigt viele auf einmal, und
 * der Server deckelt Anfragen je Adresse). Ein ersetztes Bild hat eine neue Marke; die alte Adresse wird dann freigegeben.
 * Beim Abmelden räumt die Hülle alles ab (`clear`).
 */
@Injectable({ providedIn: 'root' })
export class MemberPhotoStore {
  static readonly Parallel = 4;

  private readonly api = inject(ClubApiService);
  /** `id:marke` → Objekt-Adresse; `''` = unterwegs. Ein gescheiterter Abruf fällt wieder heraus (nächster Anlauf holt neu). */
  private readonly urls = signal<Record<string, string>>({});
  private readonly queue: Job[] = [];
  private running = 0;
  /** Zählt `clear()` — ein Abruf, der davor begann, legt danach nichts mehr ab. */
  private epoch = 0;

  /** Die Adresse des Vorschaubilds — `null`, solange es nicht da ist. Liest ein Signal, darf also in `computed` stehen. */
  url(id: number, version: number | null | undefined): string | null {
    return version == null ? null : this.urls()[keyOf(id, version)] || null;
  }

  /** Das Vorschaubild holen, falls es noch nicht da oder unterwegs ist. Schreibt ein Signal — aus einem `effect` rufen. */
  request(id: number, version: number | null | undefined): void {
    if (version == null) return;
    const key = keyOf(id, version);
    const known = untracked(this.urls);
    if (key in known) return;
    // Ältere Marken desselben Blatts sind überholt (das Bild wurde ersetzt).
    const stale = Object.keys(known).filter(k => k.startsWith(`${id}:`));
    for (const k of stale) if (known[k]) URL.revokeObjectURL(known[k]);
    this.urls.update(u => {
      const next: Record<string, string> = { ...u, [key]: '' };
      for (const k of stale) delete next[k];
      return next;
    });
    this.queue.push({ key, id, version });
    this.pump();
  }

  /** Alles freigeben (Abmelden): auf einem geteilten Gerät soll kein Bild im Speicher der Seite bleiben. */
  clear(): void {
    this.epoch++;
    this.queue.length = 0;
    for (const url of Object.values(untracked(this.urls))) if (url) URL.revokeObjectURL(url);
    this.urls.set({});
  }

  private pump(): void {
    while (this.running < MemberPhotoStore.Parallel && this.queue.length) {
      const job = this.queue.shift()!;
      this.running++;
      void this.load(job).finally(() => {
        this.running--;
        this.pump();
      });
    }
  }

  private async load(job: Job): Promise<void> {
    const mine = this.epoch;
    try {
      const blob = await this.api.memberPhotoBlob(job.id, true, job.version);
      if (mine !== this.epoch || !(job.key in untracked(this.urls))) return;      // inzwischen abgemeldet bzw. überholt
      const url = URL.createObjectURL(blob);
      this.urls.update(u => ({ ...u, [job.key]: url }));
    } catch {
      // Still: ohne Bild steht der Anfangsbuchstabe da, und der nächste Besuch der Seite holt es erneut.
      if (mine === this.epoch) this.urls.update(u => { const { [job.key]: _, ...rest } = u; return rest; });
    }
  }
}

function keyOf(id: number, version: number): string {
  return `${id}:${version}`;
}
