/**
 * Löst einen Browser-Download für einen Blob aus (Object-URL anlegen, anklicken, wieder freigeben).
 * Bündelt das ansonsten mehrfach kopierte createElement('a')/createObjectURL/revokeObjectURL-Boilerplate.
 */
export function downloadBlob(blob: Blob, filename: string): void {
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = filename;
  a.click();
  URL.revokeObjectURL(a.href);
}
