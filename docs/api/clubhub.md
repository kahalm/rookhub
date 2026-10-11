# ClubHub — Kartei der Kinder und Jugendlichen des Vereins (0.613.0)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → ClubHub — Kartei der Kinder und Jugendlichen des Vereins (0.613.0)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.


Verwaltung der Kinder/Jugendlichen (Wunsch 2026-09-30: „anlegen, Telefonnummern mit Hinweis hinterlegen — mehrere, Hinweis
klassisch ‚Mutter Daniela' oder ‚Vater Franz', auch E-Mail; am Freitag abhaken, wer da ist"). Eigene Oberfläche
`clubhub(-dev).oberschmid.homes` (fünftes Angular-Projekt), dieselbe API/DB/Konten.

- **Ein Kind ist ein KARTEIBLATT, kein Konto** (`Models/Club.cs`): `ClubMembers` (Name, Geburtsdatum ODER nur Jahrgang,
  Stufe als freier Text, `Archived`, seit 0.640.0 wieder `FideId` + `NationalId` — Notiz und Foto-Einwilligung gab es bis
  0.617.1, der User wollte sie nicht: „weitere Angaben entfernen", Migration `ClubMemberWithoutExtras`; die beiden Nummern
  fielen damals mit und kamen auf Wunsch zurück), `ClubContacts` (beliebig viele je
  Kind: `phone`/`email` + Wert + Hinweis, Reihenfolge wie eingegeben — der erste steht in der Liste), `ClubGroups`
  (+ `Weekday` 1 = Mo … 7 = So, `Schedule` Freitext), `ClubGroupMembers`, `ClubGroupTrainers` (Konto ↔ Gruppe),
  `ClubSessions` (eine Einheit je Gruppe und TAG, unique), `ClubAttendances` (Present/Absent), `ClubNotes`
  (datierte Trainer-Notizen zum Lernstand).
- **Trainer als PERSONEN** (0.620.0, Wunsch „liste unter den Kindern auch die Trainer auf bei der Anwesenheit"):
  `ClubMember.IsTrainer` — dieselbe Kartei (Kontakte, Archiv), aber ohne Gruppe, ohne Stufe/Lernstand/Konto, für jedes
  Club-Recht sichtbar, und in JEDER Gruppe unter den Kindern in Anwesenheitsliste und Tabelle (`ClubGroupDto.Coaches`).
  Nicht zu verwechseln mit `ClubGroupTrainers` = KONTEN mit Zugriff auf eine Gruppe (in der Oberfläche seit 0.620.0
  „Konten mit Zugriff", nicht mehr „Trainer"). Kartei: eigener Block „Trainer" unter den Registern, `/kind/neu?trainer=1`.
- **Fotos zur Einheit** (0.620.0, Wunsch „zu Trainings mehrere Fotos hochladen"): `ClubSessionPhotos` (LONGBLOB, wie die
  Formular-Fotos in der DB; aufrecht + auf 1600 px verkleinert über `ScoresheetImage.Prepare`, dazu ein 320-px-Vorschaubild),
  höchstens 30 je Einheit, EIN Bild je Anfrage (`POST /api/club/sessions/{id}/photos`, 15 MB wie der nginx-Deckel der
  `/api/`-Location), `GET …/photos/{photoId}[?thumb=1]` (privat, `Cache-Control: private`), `DELETE …`. Die Oberfläche holt
  die Bilder als Blob über die HTTP-Kette (ein `<img src>` schickt keinen Bearer) — `shared/session-photos.component.ts`,
  Objekt-Adressen werden beim Verlassen freigegeben. Hochladen auf der Abhak-Liste (speichert vorher die Einheit, wenn es sie
  noch nicht gibt), ansehen auch im Trainingstagebuch der Gruppe. Löschpfade (Einheit, Gruppe) entfernen Fotos über
  Stellvertreter mit Schlüssel — die Bytes werden nie geladen (`RemovePhotosWithoutLoading`).
- **Bild am Blatt + Nummern** (0.640.0, Wunsch 2026-10-02 „beim Anlegen/Bearbeiten ein Bild hinterlegen", „ein Feld für
  Personennummer/FIDE-Nr."): EIN Bild je Blatt in der eigenen Tabelle `ClubMemberPhotos` (PK = `MemberId`; JPEG ≤ 1200 px +
  Vorschaubild ≤ 256 px über `ScoresheetImage.Prepare`) — eigene Tabelle, damit keine Listen-Abfrage die Bytes lädt; ersetzt
  und gelöscht wird über Stellvertreter mit Schlüssel, ohne zu laden. Die Listen tragen nur die MARKE
  `ClubMember.PhotoVersion` (Unix-ms des Hochladens, streng steigend, `null` = kein Bild): daran sieht die Kartei, wer ein
  Bild hat, und die Oberfläche hängt sie als `?v=` an die Bildadresse (der Browser darf das Bild einen Tag behalten,
  `Cache-Control: private, max-age=86400`; ein ersetztes hat eine neue Adresse). `POST …/members/{id}/photo` (multipart
  `file`, 15 MB), `GET …/photo[?thumb=1]`, `DELETE …/photo` (idempotent) — wer das Blatt sieht, darf alle drei, auch bei
  Trainern (Personen). Oberfläche: `core/member-photo.store.ts` (EIN Speicher der Seite: je Blatt und Marke ein Abruf als
  Blob mit Anmeldung, höchstens 4 gleichzeitig — die Kartei zeigt viele auf einmal, und der globale Deckel ist 100
  Anfragen/min je IP —, beim Abmelden geleert) und `shared/member-photo.component.ts` (`ch-member-photo`: Vorschaubild oder
  Anfangsbuchstabe, mit `zoom` ein Tipp fürs große Bild). In der Kartei steht das Porträt vor dem Namen, sobald MINDESTENS
  ein Blatt ein Bild hat; am Blatt im Kopf; im Formular wählen/ersetzen/entfernen — hochgeladen wird NACH dem
  Speichern des Blatts (es hängt an dessen Kennung). Scheitert nur das Bild, bleibt das Formular als „Blatt ändern" offen
  (ein zweites Speichern legte sonst dasselbe Kind noch einmal an). `FideId` nur Ziffern (sonst 400, die Maske prüft es
  vorher), `NationalId` = Personennummer beim ÖSB, freier Text; beide ≤ 16 Zeichen, am Blatt als „PNr. …" und Link aufs
  FIDE-Profil. **Die Einwilligung der Eltern zum Bild ist Sache des Vereins** — ein Feld dafür gibt es (wieder) nicht.
- **Kreis ums Gesicht** (0.641.0, Wunsch 2026-10-03 „mit einem Kreis sein Gesicht auswählen — das soll beim Abhaken vorn
  beim Namen dabei sein"): `ClubMemberPhoto.FaceX/FaceY/FaceR` — Mittelpunkt als Anteil von Breite/Höhe, Radius als Anteil
  der KÜRZEREN Seite (0,05 … 0,5), also unabhängig von der Pixelgröße. `Services/Club/ClubFace.cs` holt den Kreis ins Bild
  (`Normalize`) und nennt das Quadrat um ihn (`Square`); daraus schneidet `ScoresheetImage.CropSquare` das VORSCHAUBILD
  (≤ 256 px, nie hochgerechnet). Das Bild selbst bleibt ganz — rund wird das Porträt erst im Browser (CSS). Ohne Kreis
  (alle drei `null`: Bilder von 0.640.0, Hochladen ohne `face`) ist das Vorschaubild das ganze Bild verkleinert.
  `POST …/members/{id}/photo` nimmt den Kreis als Formularfeld `face` (JSON-TEXT `{"x":…,"y":…,"r":…}` — keine
  Zahlenfelder, die würden nach der Sprache des Servers gelesen); `PUT …/members/{id}/photo/face` setzt ihn im VORHANDENEN
  Bild (lädt die Zeile, schreibt nur Vorschaubild + Kreis, die Marke `PhotoVersion` wechselt; 404 ohne Bild). Ein neues
  Bild ohne `face` löscht den alten Kreis. `ClubMemberDto.PhotoFace` bringt ihn ins Formular, `ClubGroupMemberRowDto.PhotoVersion`
  das Porträt in die Abhak-Liste (Kinder UND Trainer; wie in der Kartei erst, wenn EINER auf der Liste ein Bild hat, sonst
  der Anfangsbuchstabe). Oberfläche: `shared/face-picker.component.ts` (`ch-face-picker`: Bild groß, Kreis darüber —
  ziehen verschiebt, Griff am Rand und Regler ändern die Größe, ein Tipp ins Bild setzt ihn dorthin, Pfeiltasten und
  Plus/Minus; `touch-action: none` NUR am Kreis, damit ein Wisch übers Bild weiter die Seite rollt) und `core/face.ts`
  (`clampFace` = Spiegel von `ClubFace.Normalize`, LITERALE Werte in `face.spec.ts` ↔ `ClubFaceTests`). Im Formular: ein
  neu gewähltes Bild beginnt mit dem Anfangskreis (`DEFAULT_FACE`) und schickt ihn mit; für ein vorhandenes Bild holt das
  Formular das große Bild als Blob und speichert NUR einen angefassten Kreis. Alle kleinen Porträts sind seither rund
  (`.avatar`), am Kopf des Blatts 96 px — ein Tipp zeigt weiter das ganze Bild.
- **„Weiteres Kind anlegen"** (0.640.0, Wunsch „speichert altes Kind + gleich neue Maske"): zweiter Knopf in der klebenden
  Leiste von `/kind/neu` (bei Trainern „Weiteren Trainer anlegen"). Speichert, bleibt auf `/kind/neu` und leert das
  Formular — Gruppen und Kind/Trainer bleiben (man trägt meist eine ganze Gruppe hintereinander ein), der Cursor steht im
  Vornamen, darüber „<Name> ist gespeichert" mit Link aufs Blatt; „Abbrechen" heißt danach „Fertig".
- **Zwei Rechte**: `club.manage` (Leitung, Admin eingeschlossen — alles: alle Kinder, Gruppen anlegen, Trainer zuteilen,
  Blätter löschen) und `club.trainer` (nur die Gruppen, denen das Konto als Trainer zugeteilt ist, und deren Kinder). Die
  Zuteilung allein öffnet nichts — das Konto braucht eine Rolle mit `club.trainer`. Weil JEDE Action eines von beiden
  annimmt, hängt am `ClubController` kein `[HasPermission]`: die Rechte kommen live aus dem `PermissionResolver`, die Regeln
  stehen in `Services/Club/ClubService.cs` (`ClubActor`). **Sichtbarkeit ist die eine Regel**: ohne Recht 403, ein fremdes
  Kind/eine fremde Gruppe ist 404 wie ein unbekanntes — ein Trainer erfährt nicht, dass es das Blatt gibt. Ein Trainer legt
  Kinder nur in EIGENEN Gruppen an (sonst sähe er das Blatt selbst nicht) und lässt die Zugehörigkeit zu fremden Gruppen
  beim Speichern unberührt; löschen darf nur die Leitung.
- **Nur der Vorname ist Pflicht** (0.615.0, Wunsch „Nachname optional, ich weiß den oft nicht"): `LastName` ist dann ein
  LEERER Text (Spalte bleibt NOT NULL, keine Migration). Geordnet wird nach dem Nachnamen und ohne ihn nach dem Vornamen —
  am Server `ClubService.SortName` (CASE in SQL), im Browser `sortName`/`nameHead`/`nameTail` in `core/club-format.ts`
  (Register-Buchstabe, fetter Namensteil). Wer einen Namen anzeigt, nimmt diese Helfer, nie `lastName` direkt.
- **Kontakte**: leer gelassene Zeilen fallen still weg, ein Wert, der weder Nummer noch Adresse ist, ist 400 mit
  Nutzertext (`NormalizeContacts`). Die Kartei-Liste liefert die Kontakte mit — sie ist zugleich die Telefonliste. Gesucht
  wird im BROWSER: eine Namenssuche am Server hätte Namen von Kindern in die Adresse und damit in jedes Zugriffsprotokoll
  gebracht.
- **Anwesenheit**: `POST /api/club/groups/{id}/sessions` legt die Einheit des Tages an ODER ersetzt sie; nur Kinder der
  Gruppe zählen, leerer Status nimmt den Eintrag zurück. **Zwei Zustände, da oder nicht da** — „entschuldigt" gab es nur
  in 0.613.0 und ist seit 0.615.0 weg (Wunsch: „interessiert mich nicht"); die Migration `ClubAttendanceTwoStates` ist
  eine reine Datenkorrektur (Status 2 → 3), `excused` ist seither ein 400. Jede Einheit trägt **Thema** (`Topic`, eine
  Zeile) und **„Was wurde gemacht?"** (`Notes`, bis 2000 Zeichen) — beides auf der Abhak-Liste einzugeben, auf der
  Gruppenseite als Trainingstagebuch (neueste zuerst) zu lesen. Die Oberfläche schickt für JEDES Kind der Gruppe einen Status —
  wer nicht abgehakt ist, hat gefehlt (sonst wäre die Quote immer 100 %). **Vor dem ersten Tipp fragt die Liste
  `GET …/sessions/by-date/{datum}`** (204 = keine): ein Speichern ersetzt, eine leer geöffnete Liste überschriebe sonst
  eine erfasste Einheit. Die Gruppenseite zeigt die letzten 12 Einheiten als Tabelle (älteste links), die Quote zählt über
  alle. **Blättern** (0.640.0, Wunsch „eine Blätterfunktion, um zu alten Trainings zu kommen"): `GET
  …/groups/{id}/sessions/dates` liefert ALLE Tage mit Einheit (älteste zuerst); zwei Knöpfe über dem Datumsfeld führen zur
  Einheit davor/danach (`dateNeighbours` in `core/club-format.ts`: die Tage mit Einheit plus der Tag, für den die Liste
  von selbst aufgeht — von der letzten Einheit führt „weiter" dorthin zurück). Der Tag steht als `?datum=` in der Adresse;
  mit ungespeicherten Haken wird vor dem Wechsel gefragt (auch beim Datumsfeld). **Einen Knopf „Alle da" gibt es seit
  0.640.0 nicht mehr** (Wunsch: „das gibt's eigentlich nie").
- **Konto verknüpfen nur per Einwilligung**: der Trainer gibt einen Einmal-Code aus (10 Zeichen ohne 0/O/1/I, 14 Tage,
  `POST …/members/{id}/link-code`), EINLÖSEN muss ihn das Konto selbst (`POST /api/club/link`, „auth"-Limiter, nicht unter
  Impersonation). So hängt niemand fremde Konten an ein Blatt und liest deren Fortschritt. Ein Konto hängt an höchstens einem
  Blatt; trennen können beide Seiten (`DELETE …/members/{id}/link`, `DELETE /api/club/link`). Das verknüpfte Konto sieht von
  der Kartei nur den eigenen Vornamen. `GET …/members/{id}/progress` (204 ohne Verknüpfung) bündelt `PuzzleStatsService`,
  `TrainingGoalService.GetTrackerAsync(4 Wochen)` und `KidsProgressService` zu Summen (`ClubProgressService`) — keine
  eigene Zählung, keine Einzelversuche.
- **Daten von Minderjährigen**: nie anonym erreichbar, nie im Log (nur Ids), kein Service Worker (nichts bleibt auf einem
  geteilten Gerät liegen), `noindex`. Blatt löschen entfernt Kontakte, Notizen und Anwesenheit. Konto-Löschung
  (`ProfileService.DeleteAccountAsync`) löst die Verknüpfung und entfernt Trainer-Zuteilungen — das Blatt gehört dem Verein
  und bleibt; Notizen bleiben ohne Autorennamen.
- **Endpunkte** (`Controllers/Club/ClubController.cs`, alle `[Authorize]`): `GET/POST /api/club/members`, `GET/PUT/DELETE
  …/members/{id}`, `POST …/members/{id}/notes`, `DELETE …/notes/{noteId}`, `POST …/link-code`, `DELETE …/link`, `GET
  …/progress`, `POST/GET/DELETE …/members/{id}/photo`, `PUT …/members/{id}/photo/face`; `GET/POST /api/club/groups`, `GET/PUT/DELETE …/groups/{id}`, `POST/DELETE …/groups/{id}/trainers[/{userId}]`
  (Zuteilen über den Benutzernamen), `POST/DELETE …/groups/{id}/members/{memberId}`, `POST …/groups/{id}/sessions`, `PUT
  …/sessions/{sessionId}` (auch Datum ändern; 409, wenn der Tag belegt ist), `GET …/sessions/by-date/{datum}`, `GET …/groups/{id}/sessions/dates`, `GET/DELETE
  /api/club/sessions/{id}`; vom Konto aus `GET/POST/DELETE /api/club/link`.
- **Oberfläche** (`src-clubhub/`, `public-clubhub/`, Image `ghcr.io/kahalm/rookhub-clubhub:{dev,latest}`, `APP_PROJECT=clubhub`,
  Host-Port Dev **8101** / Prod **8102**): `/` Kartei (Register nach Anfangsbuchstaben, erste Telefonnummer samt Hinweis als
  `tel:`-Link, Suche im Browser, Gruppenfilter als KNÖPFE mit Kinderzahl je Gruppe (0.640.0, Wunsch „Filter auf
  Anfänger/Fortgeschrittene" — vorher eine Auswahlliste; ein zweiter Tipp auf dieselbe Gruppe zeigt wieder alle), Archiv; am Trainingstag oben der Streifen „Heute ist Freitag: … —
  Anwesenheit abhaken"), `/kind/neu` + `/kind/:id` (Blatt ansehen/ändern: Kontakte-Editor, Notizen, Anwesenheit, Konto +
  Lernstand), `/gruppen`, `/gruppen/:id` (Anwesenheitstabelle, Kinder dazunehmen, Leitung: Gruppe ändern + Trainer),
  `/gruppen/:id/anwesenheit[?datum=]` (die Abhak-Liste: geht für den jüngsten Trainingstag auf, der nicht in der Zukunft
  liegt — `trainingDate` in `core/club-format.ts`), `/verknuepfen[?code=]` (braucht nur ein Konto). Seite deutsch, hell/dunkel
  über den geteilten `ThemeService`, Gestaltung `src-clubhub/clubhub.scss` (Zilla Slab + Atkinson Hyperlegible, beide OFL,
  in `public-clubhub/fonts/` — Atkinson wegen eindeutiger Ziffern in Telefonnummern). Reine Regeln mit Vektoren in
  `core/club-format.ts` (Altersklasse nach Jahrgang, „Geburtsdatum oder Jahrgang" lesen, `tel:`-Link, Trainingstag).
  `ClubHubAssetTests` hält Symbol und Schriften gegen die Dateien auf der Platte.
- **Noch nicht**: Elo-Verlauf je Kind, Turniere/Termine, Eltern-Sicht, Sprung aus RookHub (kein Menüpunkt), eigener
  Datenschutz-Text für die Kartei (die geteilte Datenschutzseite beschreibt sie nicht). Deploy: Dev seit 30.09. (Port 8101,
  NPM-Host 43), Prod seit 30.09. mit v0.616.0 (Port 8102, NPM-Host 44) — beide Stacks haben den Dienst `clubhub`.
