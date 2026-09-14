# Roadmap

Stand: 14. September 2026

## Aktueller Checkpoint

Die funktionale Basis ist abgeschlossen:

- Profile binden Anwendungen über den vollständigen EXE-Pfad und verwenden
  verständliche Programmnamen.
- Profile unterstützen einen oder mehrere explizite Monitorziele mit jeweils
  eigenem Anzeigemodus.
- Spezifische Monitore werden ausschließlich über ihren stabilen
  Windows-Gerätepfad angesprochen. Andere Monitore werden nicht implizit
  verändert.
- Profilmodi werden beim Prozessstart angewendet, entsprechend der gewählten
  Richtlinie stabilisiert und nach Prozessende wiederhergestellt.
- Steam-Starts sind optional und verwenden ausschließlich den offiziellen
  `steam://run/<App-ID>`-Aufruf. Die normale Profilerkennung startet keine
  Anwendung.
- Das Tray öffnet für manuelles Umschalten ein kompaktes Auswahlfenster mit
  getrennten Feldern für Monitor, Auflösung und Bildwiederholrate.
- Autostart, Diagnosebericht, Profilspeicherung und V1-zu-V2-Migration sind
  vorhanden.

Letzter automatischer Stand: Release-Build erfolgreich, **161/161 Tests**.

Manuell bestätigt:

- Ride kann mit 640×480 gestartet und während der Startphase stabilisiert
  werden.
- Ride wechselte im Test zweimal selbst zurück; beide Abweichungen wurden
  korrigiert und der Originalmodus nach dem Beenden wiederhergestellt.
- Das manuelle Tray-Schalten wurde mit zwei Monitoren geprüft. Ausschließlich
  der jeweils gewählte Monitor änderte seinen Modus.
- Die manuelle Updateprüfung hat das öffentliche stabile GitHub-Release
  `v1.0.1` erkannt und gegenüber App-Version `1.0.0.0` korrekt als neuer
  eingeordnet.

Das Icon-Arbeitspaket ist im Commit `0ed8bb8` festgehalten und zum Remote
gepusht.

## Reihenfolge für die nächsten Arbeitspakete

### 1. Kompaktes Fenster für manuelles Umschalten

Implementiert; automatische Prüfung und Hardware-Abnahme abgeschlossen.

Das frühere dreistufige Tray-Untermenü war bei vielen Auflösungen funktional,
aber unübersichtlich. Der Tray-Eintrag **Anzeigemodus manuell...** öffnet nun
ein kleines WinForms-Fenster.

Vorgesehener Umfang:

- Monitor bewusst über eine klar beschriftete Auswahlliste wählen.
- Aktuellen Modus des gewählten Monitors sichtbar anzeigen.
- Zielauflösung und Bildwiederholrate in zwei übersichtlichen Feldern wählen.
- Erst ein ausdrücklicher Klick auf **Anwenden** führt den Wechsel aus.
- Der eindeutige, sichere Primärmonitor, seine aktuell angebotene Auflösung
  und dafür die höchste verfügbare Bildwiederholrate werden als Bedienhilfe
  vorausgewählt. Ohne eindeutigen sicheren Primärmonitor bleibt die Wahl leer.
- Während eines aktiven, startenden oder wiederhergestellten Profils bleibt
  die Funktion gesperrt und erklärt den Grund.
- Weiterhin ausschließlich `ManualDisplaySwitcher` und die sichere,
  zielgebundene Display-Pipeline verwenden.
- Keine Vorauswahl unsicherer Monitore, kein Umschalten ohne **Anwenden** und
  kein automatischer Restore für manuelle Wechsel.
- Das bisherige lange Untermenü anschließend entfernen. Optional kann ein
  kurzer Eintrag für den aktuellen Modus bestehen bleiben, sofern er keine
  zweite Umschaltlogik erzeugt.

Abnahme:

1. Fenster über das Tray öffnen.
2. C49HG9x auswählen und auf einen anderen Modus stellen.
3. Prüfen, dass beide VC279 unverändert bleiben.
4. Einen VC279 auswählen und nur diesen umschalten.
5. Während eines aktiven Ride-Profils prüfen, dass **Anwenden** gesperrt ist.
6. Monitor nach geöffnetem Fenster trennen und ein sicheres, verständliches
   Fehlerverhalten prüfen.

Ergebnis: Der Nutzer hat das Fenster mit zwei Monitoren erfolgreich geprüft;
jeweils ausschließlich der gewählte Monitor änderte seine Auflösung.

### 2. Profilverwaltung kosmetisch überarbeiten

Implementiert und automatisch geprüft; die manuelle UI-Abnahme bei
unterschiedlicher Windows-Skalierung ist noch offen.

Die Oberfläche wurde ohne neue Profilfunktionalität verständlicher aufgebaut:

- Anwendung, Monitorziele, Verhalten/Speichern und gespeicherte Profile sind
  als klar benannte Schritte beziehungsweise Bereiche getrennt.
- **Ziel hinzufügen**, **Änderungen am Ziel übernehmen**, **Auswahl leeren**
  und **Änderungen am Profil speichern** benennen Umfang und Wirkung eindeutig.
- Monitor und Zielmodus besitzen sichtbare Feldbeschriftungen.
- Lange Monitor-, Modus-, Anwendungs-, Ziel- und Profileinträge bleiben über
  verbreiterte Dropdowns, Tooltips und horizontale Listen erreichbar.
- Lade-, Fehler-, Hinweis-, neue, ungespeicherte, gespeicherte, blockierte und
  laufende Zustände sind ausdrücklich beschriftet und zusätzlich farblich
  unterscheidbar.
- Tab-Reihenfolge, Abstände, eine größere Mindestgröße und
  `AutoScaleMode.Dpi` sind gesetzt.
- Der Start ist als **Optional: Profil jetzt starten** klar von Auswahl und
  Speichern getrennt und bleibt ein bewusster Klick.
- Die Wahl eines bereits eindeutig vorhandenen Monitorziels öffnet dieses zur
  Bearbeitung und verhindert dadurch versehentliche Dubletten. Es erfolgt
  weiterhin keine automatische Neuzuordnung und keine Displayaktion.
- Reine Präsentationslogik für Zustände und Aktionsnamen ist ohne fragile
  Pixeltests abgedeckt.

Abnahme: Neues Profil, bestehendes Profil, mehrere Ziele, fehlender Monitor und
lange Namen jeweils bei 100 % und mindestens einer erhöhten Windows-Skalierung
durchspielen.

Automatisches Ergebnis: Release-Build ohne Warnungen, **141/141 Tests**.

### 3. Eigenes Icon-Set

Implementiert und technisch geprüft; die Sichtprüfung im echten Windows-Tray
und an der veröffentlichten EXE ist noch offen.

- Ein schlichtes, auch in 16×16 Pixeln erkennbares Symbol für Monitor und
  Moduswechsel entwerfen.
- Varianten für Anwendung, EXE und Tray in einer mehrstufigen `.ico`-Datei
  bereitstellen.
- Darstellung mit hellem und dunklem Windows-Theme prüfen.
- Ressourcenpfad, Projektdatei und veröffentlichte EXE kontrollieren.

Ergebnis: Ein Monitor mit zwei gegenläufigen cyanfarbenen und violetten
Wechselpfeilen liegt als transparente Quelldatei und als ICO mit 16, 20, 24,
32, 40, 48, 64, 128 und 256 Pixeln vor. Das Icon wird in die EXE eingebettet;
Tray und beide Fenster verwenden diese eingebettete Ressource, sodass zur
Laufzeit keine separate Icon-Datei erforderlich ist.

### 4. Mehrmonitor-Hardwareabnahme abschließen

In Arbeit. Die reproduzierbare Abnahme und ihre tatsächlich bestätigten
Ergebnisse werden in der [Hardware-Testmatrix](HARDWARE-TESTMATRIX.md)
festgehalten. Das manuelle gezielte Umschalten wurde auf dem ersten Rechner
bereits mit zwei Monitoren bestätigt; Profil-, Trennungs- und
Primärmonitor-Szenarien sowie der zweite Rechner sind noch offen.

Erster Rechner mit C49HG9x und VC279:

- Ein einzelnes spezifisches Ziel.
- Zwei gleichzeitig konfigurierte Ziele.
- Dynamischer Primärmonitor gegenüber spezifischem Monitor.
- Windows-Primärmonitor wechseln und danach erneut testen.
- Monitor trennen, Profil starten und fail-safe Verhalten prüfen.
- Monitor wieder verbinden und die frische Zuordnung prüfen.
- Falls praktikabel: Klonmodus aktivieren und bestätigen, dass das Tool nicht
  uneindeutig schaltet.

Zweiter Rechner mit Odyssey Neo G9:

- Monitorname und Gerätepfadbindung prüfen.
- Native und kleinere Auflösungen sowie verfügbare Frequenzen prüfen.
- Profilstart, Startphasen-Nachsetzung und Wiederherstellung testen.
- Vorhandene weitere Monitore müssen unverändert bleiben.

Die Ergebnisse werden als kurze Hardware-Testmatrix dokumentiert. Ein Fehler
in der Monitorzuordnung hat Vorrang vor kosmetischen Erweiterungen.

### 5. Diagnose und Dokumentation abrunden

Implementiert und automatisch geprüft; ein realer Diagnosebericht nach einem
manuellen Hardwarewechsel bleibt Teil der manuellen Abnahme.

- Manuelle Displaywechsel mit Monitorname, Gerätepfad-Kurzform, Ausgangs- und
  Zielmodus nachvollziehbar diagnostizieren.
- Initiale Profilaktivierung und spätere Nachsetzungen pro Monitor verständlich
  darstellen, ohne das Log mit erfolgreichen Prüfungen zu überladen.
- README-Einleitung auf die Mehrmonitor-Funktion aktualisieren.
- Manuelle Abnahme von Helldivers-spezifischen Beispielen lösen und als
  allgemeine Testmatrix formulieren.
- Sicherheitsgrenzen für Steam, VAC und andere Anti-Cheat-Systeme beibehalten
  und dokumentieren.

Ergebnis: Manuelle Wechsel sowie initiale Profilaktivierungen und tatsächliche
Nachsetzungen werden zielbezogen mit Monitorname beziehungsweise Ziel,
kompakter Gerätekennung, Ausgangsmodus, Zielmodus und Ergebnis protokolliert.
No-op-Prüfungen erzeugen keine zusätzlichen Einzelereignisse. README,
allgemeine Testbeschreibung und Anti-Cheat-Grenzen entsprechen dem aktuellen
Mehrmonitor-Stand.

### 6. Updatefunktion über GitHub Releases

Implementiert; die reale Ende-zu-Ende-Abnahme mit einem neuen Release-Paket
steht noch aus. Das erste sichere Teilpaket – eine ausschließlich manuell
ausgelöste Metadatenprüfung – ist implementiert, automatisch geprüft und
manuell mit `v1.0.1` abgenommen. Sie
unterscheidet fehlende Releases, aktuellen Stand, verfügbare Version und
API-Fehler. Entwürfe, Vorab-Releases und ungültige Tags werden ignoriert; in
diesem ersten Teilpaket fand noch kein Download oder Dateiaustausch statt.

Die Release-Quelle ist jetzt öffentlich und ohne Token erreichbar. Persönliche
GitHub-Tokens werden weder eingebettet noch lokal vom Tool angefordert.

Das zweite Teilpaket ist implementiert und automatisch geprüft: Nur das exakt
benannte `win-x64`-ZIP samt SHA-256-Datei wird nach einer weiteren Bestätigung
geladen. Host- und Größenprüfung, isoliertes Staging unter `%LocalAppData%`,
Vergleich mit der veröffentlichten Assetgröße, Hashvergleich und das Entfernen
abgelehnter Downloads sind vorhanden.

Das dritte Teilpaket ist ebenfalls implementiert und automatisch geprüft:
Pfadsicheres Entpacken, Ausschluss von Profildateien und Verweisen, Prüfung der
vollständigen App-Dateien, zweite ausdrückliche Installationsbestätigung und
Abgleich der Paketversion mit dem Release-Tag sowie Sperre bei aktivem Profil.
Eine isolierte Kopie aus dem bereits geprüften neuen Payload arbeitet als
separate Updater-Instanz, sodass Releases auch den Updater selbst korrigieren
können. Sie wartet auf das saubere Programmende, sichert überschriebene
Dateien, tauscht ausschließlich den geprüften Payload aus und startet die neue
Version. Austausch- und Neustartfehler lösen einen Rollback samt sichtbarer
Fehlermeldung aus. Profile und der stabile Installationspfad bleiben unberührt.
Die manuelle Ende-zu-Ende-Abnahme wartet auf ein neueres Release mit dem
Assetpaar.

Nicht als direktes `git pull`, sondern als kontrollierter Binär-Updatepfad:

- GitHub Releases auf eine neuere stabile beziehungsweise freigegebene Version
  prüfen.
- Verfügbare Version anzeigen und nur nach ausdrücklicher Bestätigung laden.
- Fertiges frameworkabhängiges ZIP und veröffentlichte SHA-256-Prüfsumme
  verwenden; kein Build aus Quellcode auf dem Zielrechner.
- Download zunächst in ein Staging-Verzeichnis schreiben und vollständig
  prüfen.
- Einen kleinen separaten Updater verwenden, der das laufende Tool beendet,
  Dateien austauscht und die neue Version startet. *(implementiert)*
- Bei einem fehlgeschlagenen Austausch auf die vorige Version zurückrollen.
  *(implementiert und automatisch geprüft)*
- Profile unter `%LocalAppData%` und den stabilen Installations-/Autostart-Pfad
  unverändert erhalten.
- Keine unbeaufsichtigten Updates; Prüfung und Installation müssen getrennt
  abschaltbar sein.

### 7. Release-Vorbereitung

- Versionierung für das erste installierbare Release festlegen. `v1.0.2`
  deckte beim Ende-zu-Ende-Test eine Windows-Dateisperre auf. `v1.0.3`
  enthielt den Fix bereits im Payload, startete beim Update von `v1.0.1` aber
  weiterhin dessen alten Runner. Ein lokaler `v1.0.4`-Bootstrap und das
  Release `v1.0.5` prüfen den korrigierten, selbstaktualisierbaren Runnerpfad.
- Frameworkabhängiges `win-x64`-Bundle erzeugen; .NET 8 darf vorausgesetzt
  werden.
- App-, Datei- und Assembly-Version konsistent setzen.
- Release Notes mit Funktionen, bekannten Grenzen und Testmatrix erstellen.
- Clean Release-Build und vollständigen Testlauf ausführen.
- Bundle auf einem zweiten Rechner entpacken und ohne Entwicklungsumgebung
  starten.
- Autostart mit dem tatsächlich ausgelieferten EXE-Pfad erneut prüfen.

## Später, nicht für das erste Pre-Release erforderlich

- Spezifische Startintegration für weitere Stores wie Epic oder GOG. Die
  normale automatische Profilerkennung bleibt der sichere Standard.
- Profil-Import und -Export.
- Komfortfunktionen wie Suche oder Sortierung bei sehr vielen Profilen.
- Optionales Speichern häufig verwendeter manueller Modi, ohne daraus
  automatisch Prozessprofile zu erzeugen.

## Einstieg in die nächste Arbeitssitzung

1. Die offenen Szenarien des ersten Rechners anhand der
   [Hardware-Testmatrix](HARDWARE-TESTMATRIX.md) durchführen.
2. Danach die Neo-G9-Abnahme auf dem zweiten Rechner ausführen.
3. Abweichungen immer zusammen mit App-Version beziehungsweise Commit und dem
   Diagnosebericht dokumentieren.
4. Die Hardwareabnahme kann unabhängig von der Entwicklung fortgeführt werden.
   Als nächster Schritt folgt die **Ende-zu-Ende-Abnahme von Arbeitspaket 6**:
   Quelle committen und pushen, ein höher versioniertes frameworkabhängiges
   `win-x64`-Bundle samt SHA-256-Datei veröffentlichen und das Update aus einem
   älteren Testbuild durchführen.
5. Danach beginnt Arbeitspaket **7 – Release-Vorbereitung**.
