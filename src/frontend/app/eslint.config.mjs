// @ts-check
//
// Linter fuer alle Projekte des Workspaces (app, turnier, kidhub, leaguehub, clubhub) — Codereview F8-004.
//
// Vorher gab es weder Linter noch Formatter, und die Codebasis trug fuer dieselbe Sache zwei Stile
// nebeneinander (Default- neben OnPush-Change-Detection, Konstruktor-DI neben inject(), @Input() neben
// input()). Jede neue Seite wuchs im Stil des zuletzt kopierten Vorbilds. Wegen der Angular-22-Falle
// (nach HTTP ohne View-Mark kein Neuzeichnen, siehe render-after-http.interceptor) ist der CD-Stil
// keine Geschmacksfrage.
//
// PHASE 1 = NUR WARNUNGEN. Jede Regel steht auf 'warn': `npm run lint` endet gruen, die CI zaehlt die
// Warnungen je Regel in die Job-Summary (test.yml, build-frontend) und blockiert nichts. Phase 2 ist die
// Ratsche (Zahl darf nur sinken, z. B. per --max-warnings); erst danach einzelne Regeln auf 'error'.
// CiWorkflowTests haelt fest, dass hier nichts auf 'error' steht, solange der CI-Schritt nicht blockiert.
//
// Bewusst NUR die gemessenen Stilachsen, keine kompletten recommended-Saetze: jede Warnung soll eine
// bekannte Luecke sein, kein Rauschen. Ohne Typinformation (kein parserOptions.project) — schnell und
// unabhaengig vom Build. Specs, e2e und Konfigurationsdateien bleiben aussen vor.
import { defineConfig, globalIgnores } from 'eslint/config';
import tseslint from 'typescript-eslint';
import angular from 'angular-eslint';

export default defineConfig(
  globalIgnores(['dist/', '.angular/', 'e2e/', 'public*/', '*.js', '*.mjs', '**/*.spec.ts']),
  {
    // src = RookHub, src-* = die uebrigen Projekte; ein neues Projekt src-<name> ist automatisch dabei.
    files: ['src*/**/*.ts'],
    languageOptions: { parser: tseslint.parser },
    plugins: {
      '@typescript-eslint': tseslint.plugin,
      '@angular-eslint': angular.tsPlugin,
    },
    rules: {
      // Change-Detection: neue Komponenten OnPush (+ Signale), nicht Default.
      '@angular-eslint/prefer-on-push-component-change-detection': 'warn',
      // DI: inject() statt Konstruktor-Parameter.
      '@angular-eslint/prefer-inject': 'warn',
      // input()/viewChild() statt @Input()/@ViewChild(), Signal-Felder readonly.
      '@angular-eslint/prefer-signals': 'warn',
      '@typescript-eslint/no-explicit-any': 'warn',
      'no-console': 'warn',
      // Leere catch-Bloecke verschlucken Fehler still — ein Kommentar im Block erklaert die Absicht.
      'no-empty': ['warn', { allowEmptyCatch: false }],
      // Faengt u. a. `error: () => {}` (Fehler stumm geschluckt) und leere Lifecycle-Hooks.
      '@typescript-eslint/no-empty-function': 'warn',
    },
  },
);
