/** Wie lange die Object-URL nach dem Klick lebt — sofort freigegeben griffe der Download ins Leere. */
export const DOWNLOAD_URL_TTL_MS = 10_000;

/**
 * Legt einen Blob dem Browser zum Speichern vor — die EINE Stelle fuer alle Blob-Downloads der
 * Oberflaechen (vorher sieben Kopien mit fuenf Regeln, Codereview F8-006). Das Hilfselement wird
 * in den Baum gehaengt (Firefox loest einen Klick auf ein loses Element nicht aus) und gleich
 * wieder entfernt; die Object-URL erst nach {@link DOWNLOAD_URL_TTL_MS} freigegeben.
 * `false`, wenn das nicht ging (gesperrter Speicher, Umgebung ohne Blob-URLs) — der Aufrufer
 * kann es dann sagen, statt stumm nichts zu tun.
 */
export function downloadBlob(blob: Blob, filename: string): boolean {
  try {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.style.display = 'none';
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), DOWNLOAD_URL_TTL_MS);
    return true;
  } catch {
    return false;
  }
}
