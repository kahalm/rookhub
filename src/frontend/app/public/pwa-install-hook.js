// Faengt das PWA-Install-Event ab, falls es vor dem Angular-Bootstrap feuert.
// PwaInstallService liest window.__rookhubInstallPrompt beim Start aus, damit die
// Installationsseite (/install) den nativen Installieren-Button anbieten kann.
// Ausgelagert aus index.html (Inline-Script), damit es unter der strikten CSP
// (script-src 'self') laeuft, ohne 'unsafe-inline'/Hash — als /pwa-install-hook.js
// von 'self' gedeckt.
window.addEventListener('beforeinstallprompt', function (e) {
  e.preventDefault();
  window.__rookhubInstallPrompt = e;
});
