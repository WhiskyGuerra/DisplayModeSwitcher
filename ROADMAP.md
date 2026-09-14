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

Letzter automatischer Stand: Release-Build erfolgreich, **141/141 Tests**.

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

### 6. Updatefunktion über GitHub Releases

Nicht als direktes `git pull`, sondern als kontrollierter Binär-Updatepfad:

- GitHub Releases auf eine neuere stabile beziehungsweise freigegebene Version
  prüfen.
- Verfügbare Version anzeigen und nur nach ausdrücklicher Bestätigung laden.
- Fertiges frameworkabhängiges ZIP und veröffentlichte SHA-256-Prüfsumme
  verwenden; kein Build aus Quellcode auf dem Zielrechner.
- Download zunächst in ein Staging-Verzeichnis schreiben und vollständig
  prüfen.
- Einen kleinen separaten Updater verwenden, der das laufende Tool beendet,
  Dateien austauscht und die neue Version startet.
- Bei einem fehlgeschlagenen Austausch auf die vorige Version zurückrollen.
- Profile unter `%LocalAppData%` und den stabilen Installations-/Autostart-Pfad
  unverändert erhalten.
- Keine unbeaufsichtigten Updates; Prüfung und Installation müssen getrennt
  abschaltbar sein.

### 7. Release-Vorbereitung

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

1. Die offene UI-Abnahme von Arbeitspaket 2 bei 100 % und erhöhter
   Windows-Skalierung durchführen und das Ergebnis dokumentieren.
2. Dabei neues Profil, bestehendes Profil, mehrere Ziele, fehlenden Monitor und
   lange Namen prüfen; Speicher-, Start- und Displayverhalten müssen unverändert
   bleiben.
3. Nach erfolgreicher Abnahme den Arbeitsstand committen und pushen.
4. Anschließend mit Arbeitspaket **3 – Eigenes Icon-Set** beginnen.
