// Karma-Konfiguration für `ng test`.
//
// Headless-Browser: In CI/Container/Headless-Dev-Umgebungen ist meist kein
// system-weites Chrome installiert (und `apt install chromium` braucht root).
// Deshalb suchen wir die von Puppeteer gecachte `chrome-headless-shell`
// (~/.cache/puppeteer/...) und setzen sie als CHROME_BIN — sofern nicht bereits
// extern gesetzt. So läuft `ng test` ohne sudo und ohne manuelles CHROME_BIN.
//
// Wenn weder CHROME_BIN noch eine Cache-Shell vorhanden ist, fällt karma auf
// das normale Chrome-Lookup zurück (lokale Entwickler mit installiertem Chrome).
const { existsSync, readdirSync } = require('fs');
const { join } = require('path');

function findPuppeteerHeadlessShell() {
  const home = process.env.HOME || process.env.USERPROFILE || '';
  const base = join(home, '.cache', 'puppeteer', 'chrome-headless-shell');
  if (!existsSync(base)) return null;
  // Höchste vorhandene Version zuerst (lexikografisch absteigend reicht hier).
  for (const version of readdirSync(base).sort().reverse()) {
    const bin = join(base, version, 'chrome-headless-shell-linux64', 'chrome-headless-shell');
    if (existsSync(bin)) return bin;
  }
  return null;
}

if (!process.env.CHROME_BIN) {
  const shell = findPuppeteerHeadlessShell();
  if (shell) process.env.CHROME_BIN = shell;
}

module.exports = function (config) {
  config.set({
    basePath: '',
    frameworks: ['jasmine', '@angular-devkit/build-angular'],
    plugins: [
      require('karma-jasmine'),
      require('karma-chrome-launcher'),
      require('karma-jasmine-html-reporter'),
      require('karma-coverage'),
      require('@angular-devkit/build-angular/plugins/karma'),
    ],
    reporters: ['progress', 'kjhtml'],
    browsers: ['ChromeHeadlessNoSandbox'],
    customLaunchers: {
      // --no-sandbox: nötig, wenn als root / ohne User-Namespaces im Container
      // gestartet (sonst startet Chrome gar nicht). Schadet lokal nicht.
      ChromeHeadlessNoSandbox: {
        base: 'ChromeHeadless',
        flags: ['--no-sandbox', '--disable-gpu', '--disable-dev-shm-usage'],
      },
    },
    restartOnFileChange: true,
  });
};
