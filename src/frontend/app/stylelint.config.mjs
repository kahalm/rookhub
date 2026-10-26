// @ts-check
//
// stylelint fuer die Styles aller Projekte des Workspaces — Codereview F8-003 (fest verdrahtete Farbwerte).
//
// Gemessen beim Review: ~580 Hex-Werte in Komponenten-Styles, dieselbe Bedeutung in bis zu sechs Varianten (sechs
// Rottoene, fuenf Gruentoene, Primaerblau neben dem Azure-Theme), und keiner passt sich dem Dunkelmodus an, der die
// Vorgabe ist. Die semantischen Tokens stehen seit F8-003 in src/_tokens.scss (`var(--rh-error)` …); diese Konfiguration
// haelt fest, dass der Bestand an Hex-Werten nicht WAECHST. KEINE Massenumstellung — die Ratsche wie bei ESLint:
// scripts/stylelint-ratchet.mjs zaehlt je Regel und vergleicht mit stylelint-baseline.json (`npm run lint:css:ratchet`,
// CI-Schritt in test.yml, build-frontend). Abbau → `npm run lint:css:ratchet:update`. Details: docs/ci.md.
//
// Gelintet werden *.scss/*.css UND die Inline-Styles der Komponenten (`styles: [\`…\`]` in *.ts) — dort steht der
// Grossteil; das Skript zieht sie heraus, stylelint selbst kennt keine TS-Dateien. Alle Regeln 'warning' (die Ratsche
// entscheidet), bewusst nur die Farb-Regeln, kein standard-Regelsatz (sonst Tausende Stil-Meldungen ohne Bezug).
/** @type {import('stylelint').Config} */
export default {
  // SCSS-Syntax (postcss-scss kommt mit stylelint-config-standard-scss); Komponenten-Styles sind ebenfalls SCSS
  // (angular.json: inlineStyleLanguage scss).
  customSyntax: 'postcss-scss',
  // Die Token-Datei ist die EINE Stelle, an der Farbwerte als Hex stehen sollen.
  ignoreFiles: ['src/_tokens.scss'],
  defaultSeverity: 'warning',
  rules: {
    // Jeder Hex-Farbwert — auch in Token-Definitionen anderer Projekte und var()-Fallbacks. Der grobe Zaehler.
    'color-no-hex': true,
    // Die scharfe Regel: Text- und Hintergrundfarbe als Hex OHNE Token (kein var() im Wert) — genau das, was im
    // Dunkelmodus unlesbar wird. Neue Stellen: `color: var(--rh-error)` statt `color: #c62828`.
    'declaration-property-value-disallowed-list': [
      { '/^(color|background.*)$/': ['/^(?!.*var\\().*#[0-9a-fA-F]{3,8}\\b/'] },
      { message: (prop) => `Farbe für „${prop}“ als Hex-Wert ohne Token — var(--rh-…) aus src/_tokens.scss nehmen (F8-003)` },
    ],
  },
};
