# Menü-Sichtbarkeit (Admin konfiguriert, je Nutzer aufgelöst)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Menü-Sichtbarkeit (Admin konfiguriert, je Nutzer aufgelöst)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Admin legt pro Menüeintrag eine Sichtbarkeitsstufe fest: `All` (jeder, auch anonym) / `Registered` (eingeloggt) / `Groups` (Mitglieder bestimmter Gruppen, Admins immer) / `Admin`. Defaults in `Services/MenuRegistry.cs` (bilden das bisherige Verhalten ab); nur Overrides landen in der DB. `MenuVisibilityService` löst die effektive Sichtbarkeit auf. Frontend: `MenuService` (Navbar-Snapshot + frischer Guard-Check) + `menuGuard('<key>')` sperrt auch den direkten URL-Aufruf. „courses" bleibt zusätzlich content-gegated (courseAccessGuard).

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/menu` | AllowAnonymous | Sichtbare Menü-Keys für den (ggf. anonymen) Aufrufer |
| GET | `/api/admin/menu` | Admin | Vollständige Konfiguration (Defaults + Overrides) |
| PUT | `/api/admin/menu` | Admin | Konfiguration setzen (Liste `{ key, level, groupIds }`; unbekannte Keys ignoriert) |
