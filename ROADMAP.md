# Roadmap

Stand: 13. September 2026

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
- Das Tray unterstützt manuelles Umschalten über
  **Monitor → Auflösung → Bildwiederholrate**.
- Autostart, Diagnosebericht, Profilspeicherung und V1-zu-V2-Migration sind
  vorhanden.

Letzter automatischer Stand: Release-Build erfolgreich, **130/130 Tests**.

Manuell bestätigt:

- Ride kann mit 640×480 gestartet und während der Startphase stabilisiert
  werden.
- Ride wechselte im Test zweimal selbst zurück; beide Abweichungen wurden
  korrigiert und der Originalmodus nach dem Beenden wiederhergestellt.
- Das manuelle Tray-Schalten wurde mit zwei Monitoren geprüft. Ausschließlich
  der jeweils gewählte Monitor änderte seinen Modus.

Der Punkt-2-Arbeitsstand ist im Commit `2e4e3de` festgehalten. Vor der nächsten
Arbeitssitzung ist nur zu prüfen, ob dieser Commit bereits zum Remote gepusht
wurde.

## Reihenfolge für die nächsten Arbeitspakete

### 1. Kompaktes Fenster für manuelles Umschalten

Das dreistufige Tray-Untermenü ist bei vielen Auflösungen funktional, aber
unübersichtlich. Der Tray-Eintrag **Anzeigemodus manuell...** soll deshalb ein
kleines WinForms-Fenster öffnen.

Vorgesehener Umfang:

- Monitor bewusst über eine klar beschriftete Auswahlliste wählen.
- Aktuellen Modus des gewählten Monitors sichtbar anzeigen.
- Zielauflösung und Bildwiederholrate in zwei übersichtlichen Feldern wählen.
- Erst ein ausdrücklicher Klick auf **Anwenden** führt den Wechsel aus.
- Während eines aktiven, startenden oder wiederhergestellten Profils bleibt
  die Funktion gesperrt und erklärt den Grund.
- Weiterhin ausschließlich `ManualDisplaySwitcher` und die sichere,
  zielgebundene Display-Pipeline verwenden.
- Keine automatische Monitorvorauswahl, kein Fallback auf den Primärmonitor
  und kein automatischer Restore für manuelle Wechsel.
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

### 2. Profilverwaltung kosmetisch überarbeiten

Keine neue Profilfunktionalität, sondern eine verständlichere Oberfläche:

- Monitorziel-Bereich und Profilbereich optisch klarer trennen.
- Bedeutung von **Ziel aktualisieren**, **Profil aktualisieren** und
  **Ziel neu zuordnen** eindeutiger machen.
- Lange Monitor- und Anwendungstitel mit Tooltips oder geeigneten Spalten
  lesbar halten.
- Lade-, Fehler-, ungespeichert- und gespeichert-Zustände deutlicher zeigen.
- Tab-Reihenfolge, Abstände, Mindestgröße und Verhalten bei hoher
  Windows-Skalierung prüfen.
- Keine impliziten Monitorwechsel oder automatische Zielauswahl im Zuge der
  kosmetischen Arbeiten einführen.

Abnahme: Neues Profil, bestehendes Profil, mehrere Ziele, fehlender Monitor und
lange Namen jeweils bei 100 % und mindestens einer erhöhten Windows-Skalierung
durchspielen.

### 3. Eigenes Icon-Set

- Ein schlichtes, auch in 16×16 Pixeln erkennbares Symbol für Monitor und
  Moduswechsel entwerfen.
- Varianten für Anwendung, EXE und Tray in einer mehrstufigen `.ico`-Datei
  bereitstellen.
- Darstellung mit hellem und dunklem Windows-Theme prüfen.
- Ressourcenpfad, Projektdatei und veröffentlichte EXE kontrollieren.

### 4. Mehrmonitor-Hardwareabnahme abschließen

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

- Manuelle Displaywechsel mit Monitorname, Gerätepfad-Kurzform, Ausgangs- und
  Zielmodus nachvollziehbar diagnostizieren.
- Initiale Profilaktivierung und spätere Nachsetzungen pro Monitor verständlich
  darstellen, ohne das Log mit erfolgreichen Prüfungen zu überladen.
- README-Einleitung auf die Mehrmonitor-Funktion aktualisieren.
- Manuelle Abnahme von Helldivers-spezifischen Beispielen lösen und als
  allgemeine Testmatrix formulieren.
- Sicherheitsgrenzen für Steam, VAC und andere Anti-Cheat-Systeme beibehalten
  und dokumentieren.

### 6. Release-Vorbereitung

- Versionierung für das erste Pre-Release festlegen.
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

1. `git status` und letzten Commit prüfen; der Arbeitsbaum muss vor neuen
   Änderungen nachvollziehbar sein.
2. Release-Build und vollständigen Testlauf als Baseline ausführen.
3. Mit Arbeitspaket **1 – Kompaktes Fenster für manuelles Umschalten** beginnen.
4. UI und Logik trennen: Das Fenster darf nur den bestehenden
   `ManualDisplaySwitcher` bedienen.
5. Nach automatischen Tests zuerst den C49HG9x und anschließend einen VC279
   manuell testen.
6. Danach committen/pushen und erst mit Arbeitspaket 2 fortfahren.
