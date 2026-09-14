# Hardware-Testmatrix

Stand: 14. September 2026

Diese Matrix dokumentiert ausschließlich tatsächlich durchgeführte Tests.
Offene Felder sind keine Fehler, sondern noch ausstehende Hardwareabnahmen.
Vor jedem Test müssen die ursprünglichen Modi aller angeschlossenen Monitore
notiert werden. Ein Test gilt nur dann als bestanden, wenn nicht ausgewählte
Monitore unverändert bleiben und die Ausgangsmodi anschließend wiederhergestellt
werden.

## Rechner 1 – Samsung C49HG9x und zwei ASUS VC279

| Test | Erwartung | Status | Nachweis/Anmerkung |
| --- | --- | --- | --- |
| Manuelles Umschalten des C49HG9x | Nur der C49HG9x ändert den Modus | Bestanden | Mit zwei angeschlossenen Monitoren bestätigt |
| Manuelles Umschalten eines VC279 | Nur der gewählte VC279 ändert den Modus | Bestanden | Mit zwei angeschlossenen Monitoren bestätigt |
| Profil mit einem spezifischen Ziel | Nur das gespeicherte Gerät wird geändert und wiederhergestellt | Offen | – |
| Profil mit zwei gleichzeitigen Zielen | Beide Ziele werden geändert und jeweils korrekt wiederhergestellt | Offen | – |
| Dynamischer Primärmonitor | Ausschließlich der beim Start aktuelle Primärmonitor wird geändert | Offen | – |
| Windows-Primärmonitor wechseln | Dynamisches Ziel folgt dem neuen Primärmonitor; spezifisches Ziel bleibt am gespeicherten Gerät | Offen | – |
| Spezifischen Monitor vor Profilstart trennen | Profil bleibt fail-safe; kein anderer Monitor wird ersatzweise geändert | Offen | – |
| Monitor wieder verbinden | Erst eine frische eindeutige Zuordnung erlaubt den nächsten Wechsel | Offen | – |
| Klonmodus, sofern praktikabel | Uneindeutiges Ziel wird blockiert; kein Display wird verändert | Offen | – |

## Rechner 2 – Samsung Odyssey Neo G9

| Test | Erwartung | Status | Nachweis/Anmerkung |
| --- | --- | --- | --- |
| Monitorname und Gerätepfad | Neo G9 wird verständlich angezeigt und stabil gebunden | Offen | – |
| Nativer Modus | Verfügbare native Auflösung und Frequenzen werden vollständig angeboten | Offen | – |
| Kleinerer Modus | Gewählter kleinerer Modus betrifft nur den Neo G9 | Offen | – |
| Profilstart und Startphase | Ziel wird aktiviert und nur innerhalb der gewählten Richtlinie nachgesetzt | Offen | – |
| Prozessende | Ursprünglicher Modus wird wiederhergestellt | Offen | – |
| Weitere angeschlossene Monitore | Nicht konfigurierte Monitore bleiben während Apply und Restore unverändert | Offen | – |

## Pro Testlauf festhalten

- App-Version beziehungsweise Commit
- Windows-Version und Grafikkartentreiber
- Monitorname, Anschluss und Windows-Gerätepfad-Kurzform
- Ausgangsmodus, Zielmodus und tatsächlich beobachteter Modus
- Gewählte Profilrichtlinie
- Ergebnis sowie bei Abweichungen den kopierten Diagnosebericht

