# DisplayModeSwitcher

DisplayModeSwitcher ist ein Windows-Tool im Infobereich, das den Anzeigemodus
des Primärmonitors automatisch an ein laufendes Spiel oder Programm anpasst.
Beim Beenden des Prozesses wird der zuvor aktive Modus wiederhergestellt.

## Voraussetzungen

- Windows (WinForms und die Windows-Display-API werden verwendet)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Bedienung

Nach dem Start läuft das Tool ohne Hauptfenster im Infobereich. Über das
Tray-Menü können Auflösung und Bildwiederholrate manuell gewählt werden.

Unter **Profile verwalten...** wird pro Programm ein Zielmodus gespeichert.
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
**Im Profilmodus starten** kontrolliert gestartet werden. Das Tool setzt und
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

- Eine neue interne Windows-Schicht kann die aktive Topologie bereits
  erfassen: physische Monitor-Gerätepfade und die von
  Windows gelieferten Friendly Names werden dabei getrennt von der aktuellen
  `DISPLAYx`-Zuordnung, Primärstatus, Anschluss und Position gehalten. Auch die
  aktuellen und verfügbaren Modi können pro exakter Anzeigequelle gelesen
  werden. Eine noch nicht verdrahtete, testbare Target-Service-Schicht kann
  außerdem explizite Monitorziele vollständig vorprüfen, temporär und
  quellbezogen anwenden sowie anhand konkreter Gerätepfad-Belege
  wiederherstellen. Sie blockiert unklare Pfade, Klon-Gruppen, doppelte Quellen
  und mehrdeutige Modi vor dem ersten Apply und rollt Teiländerungen nur auf den
  ausgewählten Monitoren zurück. Diese Schicht ist noch nicht an Profile,
  Laufzeit oder Oberfläche angebunden und stellt daher noch keine fertige oder
  hardwareverifizierte Multi-Monitor-Unterstützung dar.
- Die Laufzeit und die aktuelle Profiloberfläche berücksichtigen in diesem
  Zwischenstand weiterhin ausschließlich genau ein Ziel für den Primärmonitor.
  Bereits gespeicherte spezifische oder mehrere Monitorziele werden angezeigt
  und bewahrt, aber weder ausgeführt noch in der Oberfläche bearbeitet. Ein
  kontrollierter oder automatischer Start wird für solche Profile ohne
  Display- oder Startaktion abgelehnt. Oberflächenintegration, manuelle
  Neuverknüpfung und die Anbindung des gezielten Target-Service folgen in
  späteren Checkpoints.
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

1. Release-Build starten und die erzeugte Anwendung ausführen.
2. Im Tray **Autostart mit Windows** aktivieren, ab- und wieder anmelden und
   prüfen, dass das Tool ohne Dialog im Infobereich erscheint.
3. Autostart wieder deaktivieren und nach der nächsten Anmeldung prüfen, dass
   es nicht mehr automatisch startet.
4. Ein Profil für die tatsächliche `helldivers2.exe` (vorzugsweise mit vollem
   EXE-Pfad) anlegen, Helldivers 2 starten und den Wechsel am Primärmonitor
   beobachten.
5. Helldivers 2 beenden und prüfen, dass der ursprüngliche Modus
   wiederhergestellt wird. Diese Abnahme muss auf dem Zielsystem erfolgen; ein
   realer Spieltest ist nicht Bestandteil des automatisierten Tests.
