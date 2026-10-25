import { localStore, readJson, writeJson } from '@rh/core/local-json-store';

/** Ohne Konto merkt sich der Browser die Schlüssel seiner Einlesungen — sonst fände er sie nach dem Neuladen nicht. */
const ANON_KEYS = 'lh-anon-scans';

/** Die gemerkten Schlüssel je Teilen-Link. */
export function anonKeys(share: string): string[] {
  return (readJson<Record<string, string[]>>(localStore(), ANON_KEYS) ?? {})[share] ?? [];
}

export function rememberAnonKey(share: string, key: string, keep = true, store = ANON_KEYS): void {
  const all = readJson<Record<string, string[]>>(localStore(), store) ?? {};
  const list = (all[share] ?? []).filter(k => k !== key);
  all[share] = keep ? [key, ...list].slice(0, 20) : list;
  writeJson(localStore(), store, all);
}

/** Ohne Konto: die Schlüssel der eigenen Entwürfe (0.595.0) — wie bei den Formularen. */
const ANON_DRAFT_KEYS = 'lh-anon-drafts';
export const draftKeys = (share: string): string[] =>
  (readJson<Record<string, string[]>>(localStore(), ANON_DRAFT_KEYS) ?? {})[share] ?? [];
export const rememberDraftKey = (share: string, key: string, keep = true): void => rememberAnonKey(share, key, keep, ANON_DRAFT_KEYS);
