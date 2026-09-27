/**
 * Leertaste (oder Enter) = „Weiter" am PC. Nicht, wenn gerade etwas anderes die Taste bekommt:
 * ein Eingabefeld, die Sprachauswahl, ein Knopf oder Link mit Fokus — der loest sie selbst aus, und
 * ein zweites „Weiter" wuerde eine Aufgabe ueberspringen. Gehaltene Tasten (`repeat`) zaehlen nicht,
 * und wer die Taste schon verarbeitet hat (`defaultPrevented`), bekommt sie nicht noch einmal.
 */
export function isAdvanceKey(event: KeyboardEvent): boolean {
  if (event.defaultPrevented || event.repeat || event.altKey || event.ctrlKey || event.metaKey) return false;
  if (event.key !== ' ' && event.key !== 'Enter') return false;
  const target = event.target as HTMLElement | null;
  if (!target || typeof target.tagName !== 'string') return true;
  if (target.isContentEditable) return false;
  return !/^(INPUT|TEXTAREA|SELECT|BUTTON|A)$/.test(target.tagName);
}
