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

Die Überwachung prüft den Prozess regelmäßig. Ein fehlgeschlagener Wechsel wird
mit Abstand erneut versucht, und eine spätere Abweichung vom Profilmodus wird
erneut korrigiert. Der Status ist im Tray-Menü sichtbar.

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

## Grenzen und Sicherheitsverhalten

- Es wird ausschließlich der Primärmonitor berücksichtigt.
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
