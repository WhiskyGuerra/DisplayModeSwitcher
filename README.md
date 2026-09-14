# DisplayModeSwitcher

Der aktuelle Entwicklungsstand und die nächsten Arbeitspakete stehen in der
[Roadmap](ROADMAP.md).

DisplayModeSwitcher ist ein Windows-Tool im Infobereich, das gezielt die
Anzeigemodi ausgewählter Monitore an ein laufendes Spiel oder Programm anpasst.
Ein Profil kann ein oder mehrere explizite Monitorziele enthalten. Nicht
ausgewählte Monitore bleiben unangetastet; beim Beenden des Prozesses werden
die zuvor aktiven Modi der vom Tool geänderten Ziele wiederhergestellt.

## Voraussetzungen

- Windows (WinForms und die Windows-Display-API werden verwendet)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Bedienung

Nach dem Start läuft das Tool ohne Hauptfenster im Infobereich. Der Tray-Eintrag
**Anzeigemodus manuell...** öffnet ein kompaktes Auswahlfenster. Dort werden
Monitor, Auflösung und Bildwiederholrate bewusst getrennt gewählt; erst
**Anwenden** führt den Wechsel aus. **Aktualisieren** liest Monitor-Topologie,
aktuelle Modi und verfügbare Modi erneut von Windows. Beim Öffnen wird der
eindeutige, sicher nutzbare Primärmonitor vorausgewählt. Die Monitorwahl übernimmt
dessen aktuelle Auflösung, sofern sie angeboten wird; für eine gewählte
Auflösung wird die höchste verfügbare Bildwiederholrate vorgeschlagen. Fehlt
ein eindeutiger sicherer Primärmonitor, bleibt die Monitorwahl leer. Keine
dieser Vorauswahlen schaltet selbsttätig um.

Unter **Profile verwalten...** werden pro Programm ein oder mehrere explizite
Monitorziele mit jeweils eigenem Zielmodus gespeichert. Neue Profile beginnen
bewusst ohne Monitorvorauswahl und können erst nach dem Hinzufügen mindestens
eines gültigen Ziels gespeichert werden. **Primärmonitor (dynamisch)** folgt bei
jeder Aktivierung dem dann aktuellen Windows-Primärmonitor. Ein spezifischer
Monitor wird dagegen ausschließlich über seinen stabilen Windows-Gerätepfad
gebunden; Anzeigename und EDID-Daten bleiben nur Hinweise. Ein fehlendes Ziel
wird niemals automatisch anhand dieser Hinweise ersetzt. **Ziel neu zuordnen**
zeigt stattdessen den alten und neuen Monitor und verlangt eine ausdrückliche
Bestätigung sowie gegebenenfalls eine neue Moduswahl.

Die Profilverwaltung führt in klar getrennten Bereichen durch Anwendung,
Monitorziele, Verhalten und Speichern. Monitor und Zielmodus sind sichtbar
beschriftet; **Ziel hinzufügen**, **Änderungen am Ziel übernehmen**,
**Auswahl leeren** und **Änderungen am Profil speichern** unterscheiden die
jeweilige Wirkung. Lade-, Fehler-, ungespeicherte, gespeicherte und blockierte
Zustände werden ausdrücklich angezeigt. Lange Namen und Pfade bleiben über
breitere Auswahllisten, Tooltips und horizontal scrollbare Listen vollständig
erreichbar. Die Oberfläche skaliert anhand der Windows-DPI-Einstellung. Wird
bei leerer Zielauswahl ein Monitor gewählt, der genau einem vorhandenen Ziel
entspricht, öffnet die Oberfläche dieses Ziel direkt zur Bearbeitung. Das
ändert weder die Monitorzuordnung noch den aktiven Anzeigemodus.

Mit **Modus beibehalten** wird außerdem festgelegt, wie das Tool auf spätere
Abweichungen reagiert:

- **Während Startphase stabilisieren** ist der sichere Standard. Nach der
  ersten erfolgreichen Aktivierung wird der Zielmodus höchstens 30 Sekunden
  lang und höchstens dreimal nachgesetzt.
- **Einmalig** setzt oder bestätigt den Modus beim Erkennen beziehungsweise
  Start genau initial und greift danach bis zum Wiederherstellen nicht mehr
  ein. Diese Einstellung empfiehlt sich für Spiele, die externe Moduswechsel
  während der Laufzeit schlecht vertragen.
- **Dauerhaft erzwingen** prüft und korrigiert den Modus während der gesamten
  Prozesslaufzeit mit begrenzter Rate. Diese Einstellung sollte nur bewusst
  verwendet werden, weil Spiel und Tool sonst wiederholt gegeneinander
  umschalten können.

Als Profil-Schlüssel kann der vollständige Pfad zur EXE verwendet werden. Die
ältere Schreibweise nur mit dem EXE-Namen bleibt kompatibel; sie darf aber nur
einen laufenden Prozess eindeutig treffen. Bei mehreren Treffern oder mehreren
gleichzeitig passenden Profilen schaltet das Tool aus Sicherheitsgründen nicht
automatisch.

Ein ausgewähltes Profil mit vorhandenem vollständigem EXE-Pfad kann über
**Optional: Profil jetzt starten** kontrolliert gestartet werden. Das Tool setzt und
bestätigt zuerst den Zielmodus und wählt danach eine klar ausgewiesene
Startart. Dieser Start ist strikt optional und geschieht ausschließlich durch
einen bewussten Klick auf diese Schaltfläche. Profilanlage, Tool-Autostart und
die normale automatische Erkennung einer bereits vom Nutzer gestarteten
Anwendung lösen niemals einen Spiel- oder Steam-Start aus. Liegt die Profil-EXE
nachweislich unter
`steamapps\common\<installdir>` und gehört im selben `steamapps`-Ordner ein
lesbares `appmanifest_<App-ID>.acf` mit passendem Installationsordner dazu,
wird **Steam** gewählt. Dabei öffnet das Tool ausschließlich
`steam://run/<App-ID>` über den offiziellen Windows-Protokollhandler. Es wartet
bis zu 120 Sekunden auf eine neue, eindeutig passende Instanz der exakten
Profil-EXE und hält den Zielmodus währenddessen mit begrenzter Prüfrate aktiv.
Diese sichere Wartephase ist von der gewählten Richtlinie unabhängig. Die
30-sekündige aktive Startphase beginnt bei Steam erst, wenn der eindeutige
Spielprozess übernommen wurde; bei einem Direktstart beginnt sie mit dem
erfolgreichen Start.
Der von Windows eventuell zurückgegebene Steam-Prozess wird nicht als Spiel
überwacht.

Ohne diesen belastbaren Manifestnachweis gilt **Direkt**. Das betrifft auch
Spiele anderer Stores: Die Profil-EXE wird wie bisher unmittelbar gestartet
und ihre von Windows gelieferte PID und Startzeit übernommen. Schlägt ein Start
fehl oder läuft das Steam-Wartefenster ab, wird ein zuvor vom Tool geänderter
Modus bestmöglich wiederhergestellt. Legacy-Profile nur mit EXE-Namen können
weiterhin automatisch erkannt, aber nicht über diese Schaltfläche gestartet
werden. Die Profilverwaltung zeigt für ein startbares ausgewähltes Profil
`Startart nur beim Klick: Steam` oder `Startart nur beim Klick: Direkt` an.

Die Überwachung prüft den Prozess regelmäßig. Ein fehlgeschlagener initialer
Wechsel wird unabhängig von der Richtlinie mit Abstand erneut versucht. Eine
spätere Abweichung wird gemäß der Profilrichtlinie behandelt. Beim Vergleich
gelten 99 und 100 Hz (allgemein ±1 Hz) bei gleicher Breite und Höhe als
gleichwertig, damit Rundungen von Windows oder Treiber keine unnötigen Wechsel
auslösen. Der konkret konfigurierte Modus bleibt weiterhin das Ziel beim
tatsächlichen Setzen. Der Status ist im Tray-Menü sichtbar.

### Diagnose

**Diagnose kopieren** im Tray legt einen kompakten Bericht in die
Zwischenablage. Neben Laufzeit, Profilzustand und den aktiven Monitorzielen
enthält er die letzten relevanten Ereignisse. Ein bewusster manueller Wechsel
wird mit Monitorname, kompakter Gerätekennung, Ausgangsmodus, Zielmodus und
Ergebnis protokolliert. Die initiale Profilaktivierung führt jedes Monitorziel
einmal einzeln auf; tatsächliche spätere Nachsetzungen nennen ebenfalls das
betroffene Ziel und beide Modi. Erfolgreiche Hintergrundprüfungen ohne
Displayänderung erzeugen keine zusätzlichen Einzelereignisse.

Bei einem Fehler sollte der Bericht zusammen mit App-Version beziehungsweise
Commit, Windows-Version, Grafikkartentreiber und dem beobachteten Verhalten
aufbewahrt werden. Der Bericht enthält Prozess- und Monitorinformationen, aber
keine Speicherinhalte eines Spiels und keine Interaktion mit Anti-Cheat-
Systemen.

### Updates

**Auf Updates prüfen...** im Tray fragt ausschließlich nach einem bewussten
Klick die öffentlichen Release-Metadaten des festen GitHub-Projekts ab. Dabei
werden nur veröffentlichte stabile Releases berücksichtigt; Entwürfe,
Vorab-Releases und ungültige Versions-Tags werden ignoriert. Die Anwendung
meldet getrennt, ob kein stabiles Release existiert, die installierte Version
aktuell ist, eine neuere Version vorliegt oder GitHub nicht erreichbar ist.

Der aktuelle Zwischenstand lädt noch keine Datei herunter und verändert die
Installation nicht. Bei einer gefundenen Version kann lediglich die geprüfte
GitHub-Release-Seite nach ausdrücklicher Bestätigung geöffnet werden. Der
signaturähnliche SHA-256-Prüf- und Installationspfad folgt als separates
Arbeitspaket.

### Autostart mit Windows

Der Menüpunkt **Autostart mit Windows** verwaltet den Autostart für den
aktuellen Windows-Benutzer. Der Eintrag enthält den vollständig aufgelösten,
quotierten Pfad zur aktuellen EXE und wird nach dem Schreiben verifiziert.
Beim nächsten Anmelden startet das Tool damit ohne zusätzliche Parameter.

### Profile, Speicherort und Migration

Aktuelle Profile liegen unter
`%LocalAppData%\DisplayModeSwitcher\profiles.json`. Der Ordner wird beim
Speichern automatisch angelegt. Eine vorhandene Legacy-Datei `profiles.json`
neben der EXE wird beim ersten Start in diesen Speicherort übernommen und dabei
nicht gelöscht. Beschädigte oder ungültige Profildaten werden nicht
überschrieben; die Datei kann nach Sicherung korrigiert oder entfernt werden.
Profile aus früheren Versionen ohne Richtlinienfeld werden beim Laden als
**Während Startphase stabilisieren** behandelt und beim Laden transaktional in
das neue, versionierte V2-Format übernommen. Dabei wird der bisherige Zielmodus
verlustfrei einem expliziten Ziel **Primärmonitor** zugeordnet. V2 kann bereits
stabile Monitor-Gerätepfade, Anzeigenamen als Hinweis sowie mehrere Monitorziele
verlustfrei speichern. Anzeigename und optionale EDID-Werte werden dabei nie als
automatischer Ersatz für die Geräteidentität verwendet. Unbekannte Versionen,
Monitorarten oder Richtlinien sowie ungültige und doppelte Ziele gelten als
ungültige Profildaten und blockieren ein Überschreiben ebenso wie andere
Dateifehler.

## Grenzen und Sicherheitsverhalten

- Die Profil-Engine führt automatische und kontrolliert gestartete Profile über
  gezielte, transaktionale Monitorziele aus. Alle Ziele werden vor dem Start
  vollständig vorgeprüft; die ersten konkreten Gerätepfad-Belege bleiben für
  die Wiederherstellung erhalten. Teil-Rollback-Schulden werden vor jedem
  erneuten Apply oder Start ausschließlich wiederhergestellt. Dies ist mit
  Fakes getestet, aber noch keine Hardwareabnahme.
- Die Topologieschicht trennt physische Monitor-Gerätepfade und Friendly Names
  von der aktuellen `DISPLAYx`-Zuordnung. Sie blockiert unklare Pfade,
  Klon-Gruppen, doppelte Quellen und mehrdeutige Modi vor dem ersten Apply.
- Die Profilverwaltung unterstützt die explizite Wahl des dynamischen
  Primärmonitors, spezifische Monitore und mehrere Ziele. Nicht persistierbare,
  mehrdeutige und geklonte Endpoints werden fail-safe nicht angeboten oder
  blockiert. Das manuelle Auswahlfenster schaltet ausschließlich den ausdrücklich
  gewählten physischen Monitor über seinen stabilen Gerätepfad temporär um;
  es entsteht weder ein Profil noch ein automatischer Restore-Anspruch.
  Unklare, nicht persistierbare oder geklonte Ziele bleiben deaktiviert. Solange
  die Profilüberwachung nicht wartet, bleibt **Anwenden** gesperrt und zeigt den
  Grund an. Statusänderungen bei geöffnetem Fenster werden berücksichtigt.
- Geschützte oder bereits beendete Prozesse können nicht als Profil verwendet
  werden.
- Epic, GOG und sonstige Launcher werden derzeit nicht eigens erkannt. Sie
  verwenden den Direktstart. Übergibt deren Bootstrapper den Start an einen
  anderen Prozess, kann dieser nicht automatisch als gestartete Instanz
  übernommen werden; die normale automatische Profilerkennung bleibt davon
  unberührt. Eine universelle Launcher-Unterstützung wird nicht behauptet.
- Zur möglichst risikoarmen Nutzung mit VAC und anderer Anti-Cheat-Software
  beschränkt sich der Steam-Pfad auf den offiziellen Steam-URI, das Umschalten
  des Windows-Anzeigemodus und eine ausschließlich lesende Beobachtung
  öffentlicher Prozessmetadaten (Pfad, PID und Startzeit). Es gibt keine
  Speicherzugriffe auf Spiele, DLL-Injektion, Hooks, Debugger, Remote Threads,
  Eingabesimulation, Overlays oder Interaktion mit Anti-Cheat-Systemen. Steam-
  und Spieldateien werden nicht verändert; Manifeste werden nur gelesen. Eine
  absolute VAC- oder Anti-Cheat-Garantie kann das Tool dennoch nicht geben.
- Das ursprüngliche Anzeigemodus wird nur dann beim Prozessende bzw. beim
  Beenden des Tools wiederhergestellt, wenn das Tool den Wechsel erfolgreich
  durchgeführt hat.
- Bei unklarem Prozess-Match oder einem Fehler bleibt der aktuelle Modus
  unverändert; automatische Wechsel werden wiederholt, soweit dies sicher ist.

## Build und Tests

Im Repository-Ordner:

```powershell
dotnet build DisplayModeSwitcher.sln -c Release
dotnet run --project DisplayModeSwitcher.Tests -c Release --no-build
```

Das Testprojekt ist eine kleine Konsolenanwendung und gibt die einzelnen
Ergebnisse sowie eine Zusammenfassung aus. Für den Build außerhalb von Windows
ist gegebenenfalls `EnableWindowsTargeting` erforderlich; die eigentliche
Anwendung ist für Windows bestimmt.

## Manuelle Abnahme

Die detaillierten Mehrmonitor- und Geräteszenarien stehen in der
[Hardware-Testmatrix](HARDWARE-TESTMATRIX.md). Dort werden nur tatsächlich
durchgeführte Tests als bestanden markiert.

1. Release-Build starten und die erzeugte Anwendung ausführen.
2. Im Tray **Autostart mit Windows** aktivieren, ab- und wieder anmelden und
   prüfen, dass das Tool ohne Dialog im Infobereich erscheint.
3. Autostart wieder deaktivieren und nach der nächsten Anmeldung prüfen, dass
   es nicht mehr automatisch startet.
4. In **Profile verwalten...** prüfen, dass ein neues Profil keinen Monitor
   vorauswählt. Den dynamischen Primärmonitor und – sofern vorhanden – einen
   spezifischen zweiten Monitor jeweils mit passendem Modus hinzufügen; Profil
   speichern, erneut öffnen und Zielbeschriftungen und Modi prüfen.
5. Ein Profil für eine tatsächlich installierte Testanwendung mit vollem
   EXE-Pfad anlegen, die Anwendung starten und den Wechsel an allen gewählten
   Monitoren beobachten.
6. Die Testanwendung beenden und prüfen, dass die ursprünglichen Modi
   wiederhergestellt werden. Diese Abnahme muss auf dem Zielsystem erfolgen; ein
   realer Anwendungstest ist nicht Bestandteil des automatisierten Tests.
