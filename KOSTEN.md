# Kosten und Token: Fable Space Engine

Das 3D-Weltraumspiel (F#, Fable 5.2, natives WebGL2, Docker/nginx) wurde in **einer Claude-Code-Sitzung** gebaut:
Modell **Claude Fable 5.2**, Denkaufwand (Effort) **High** (bei allen 142 Anfragen im Protokoll so eingestellt).

## Kurzfassung

| | |
| --- | --- |
| **Gesamtkosten Projekt** | **29,87 €** (= $32,90 zu Listenpreisen der Anthropic-API) |
| Eingabe-Token gesamt | **47.228.490** |
| Ausgabe-Token gesamt | **380.966** (davon 141.791 Denk-Token) |
| Zeitraum | 17.11.2026, 10:39 bis ca. 12:43 Uhr (Meilenstein 4 fertig) |
| Laufzeit der Sitzung | 2 h 11 min, davon 61 min reine API-Zeit und 9 min Werkzeuglaufzeit |
| Von Claude Code gezählte Änderungen | 8.006 Zeilen geschrieben, 695 entfernt |

**Umrechnung:** Die Preise der API gelten in US-Dollar. Für Euro gilt der Referenzkurs der Europäischen Zentralbank vom 16.11.2026 (der aktuellste zum Zeitpunkt der Auswertung): **1 € = 1,1014 $**. Euro-Betrag = Dollar-Betrag geteilt durch 1,1014. Der Kurs schwankt täglich; zuzüglich eventueller Umsatzsteuer oder Kartengebühren weicht ein tatsächlicher Rechnungsbetrag ab.

Die Eingabe ist so groß, weil jede der Anfragen die **gesamte bisherige Unterhaltung** erneut mitschickt (im Mittel rund 318.000 Token, am Ende ca. 550.000 von 1 Mio. Kontextfenster).
Fast alles davon kommt aus dem Prompt-Cache und kostet nur ein Vierzigstel des normalen Eingabepreises.
Ohne Cache hätten dieselben 47.228.490 Eingabe-Token zum Normalpreis **343,04 €** ($377,83) gekostet.

## Preise (je 1 Mio. Token, in US-Dollar)

| Modell | Eingabe | Ausgabe | Cache lesen | Cache schreiben (5 Min / 1 Std) |
| --- | --- | --- | --- | --- |
| Claude Fable 5.2 | $8,00 (7,26 €) | $40,00 (36,32 €) | $0,20 (0,18 €) | $10,00 / $16,00 (9,08 € / 14,53 €) |
| Claude Haiku 5.5 (Hintergrundaufgabe, siehe unten) | $0,10 | $0,50 | | |

Quelle: Preistabelle im `claude-api`-Skill von Claude Code (Stand 2026-11-03). Denk-Token sind in den Ausgabe-Token enthalten und werden zum Ausgabepreis berechnet.
Claude Code schreibt Cache-Einträge in dieser Sitzung ausschließlich mit 1 Stunde Lebensdauer.

## Token und Kosten nach Art (Fable 5.2)

| Art | Token | Kosten (€) | Kosten ($) | Anteil |
| --- | --- | --- | --- | --- |
| Eingabe, neu (ohne Cache) | 2.824 | 0,0205 € | $0,0226 | 0,1 % |
| Eingabe, in den Cache geschrieben (1 Std) | 518.380 | 7,53 € | $8,29 | 25,2 % |
| Eingabe, aus dem Cache gelesen | 46.707.286 | 8,48 € | $9,34 | 28,4 % |
| **Eingabe gesamt** | **47.228.490** | **16,03 €** | **$17,66** | 53,7 % |
| **Ausgabe** (davon 141.791 Denk-Token) | **380.966** | **13,84 €** | **$15,24** | 46,3 % |
| **Fable 5.2 gesamt** | 47.609.456 | **29,87 €** | **$32,90** | 100 % |
| Haiku 5.5 (Sitzungstitel u. Ä.): 5.449 Eingabe, 33 Ausgabe | 5.482 | 0,0005 € | $0,0006 | |
| **Projekt gesamt** | | **29,87 €** | **$32,90** | |

Gegenprobe: Die Nachrechnung aus diesen Token und Preisen ergibt $32,8968 und stimmt mit dem Kostenzähler von Claude Code ($32,8968 für Fable, $32,8973 insgesamt) überein.

## Kosten nach Abschnitt

Aufgeteilt an den vier „weiter“-Eingaben. Die Werte stammen aus den im Protokoll gespeicherten Nachrichten (Fable 5.2).

| Abschnitt | Anfragen | Ausgabe-Token | Cache gelesen | Cache geschrieben | Kosten (€) | Kosten ($) |
| --- | --- | --- | --- | --- | --- | --- |
| Einrichtung, Meilenstein 1 (Setup, Math3D, WebGL2-Renderer, Docker) | 38 | 88.516 | 4.540.023 | 134.706 | **6,00 €** | $6,60 |
| Meilenstein 2 (Raumschiff, Flugphysik, Kamera) | 37 | 100.393 | 9.649.550 | 130.470 | **7,29 €** | $8,03 |
| Meilenstein 3 (Laser, Pools, Gegner-KI) | 35 | 120.027 | 14.093.492 | 149.954 | **9,10 €** | $10,02 |
| Meilenstein 4 (HUD, Zielerfassung, Waves) | 31 | 70.306 | 15.862.537 | 95.612 | **6,82 €** | $7,52 |
| Rückfrage nach Meilenstein 4 (abgelehnte Auswahlfrage) | 1 | 1.478 | 549.561 | 60 | **0,15 €** | $0,17 |
| Hintergrundanfragen, nicht als Nachricht gespeichert (Differenz zum Kostenzähler) | | 246 | 2.012.123 | 7.578 | **0,50 €** | $0,55 |
| **Summe Fable 5.2** | 142 | 380.966 | 46.707.286 | 518.380 | **29,87 €** | **$32,90** |

Meilenstein 3 war der teuerste Abschnitt: Dort entstand die meiste Ausgabe (Physik, Kollision, KI).
Die Cache-Lesekosten wachsen dagegen mit der Kontextgröße, weil jede Anfrage die ganze bisherige Unterhaltung erneut liest. Deshalb hat Meilenstein 4 die meisten Cache-Lese-Token, wegen der geringeren Ausgabe aber nicht die höchsten Kosten.

## Nicht im Projektpreis enthalten

- **Diese Kostenabfrage** lief nach einem Modellwechsel auf Claude Sonnet 5.5 (14 Anfragen bis zum Schreiben dieser Datei, 43.387 Ausgabe-Token): ca. **2,70 €** ($2,98) nach Listenpreis (Eingabe $2,00 / Ausgabe $10,00 / Cache lesen $0,10 / Cache schreiben 1 Std $4,00 je Mio.). Davon entfallen **1,74 €** darauf, dass der gesamte Kontext (479.420 Token) beim neuen Modell noch einmal in den Cache geschrieben werden musste. Caches gelten pro Modell. Der Betrag wächst mit jeder weiteren Antwort in dieser Sitzung, und Hintergrundanfragen sind darin nicht erfasst.
- **Eine frühere Sitzung vom 16.11.** im selben Ordner (Arbeit an `test.html`, Fable 5.2 und Sonnet 5.5, Effort xhigh) gehört nicht zum Weltraumprojekt: 9,06 € ($9,98) laut Kostenzähler.

## Hinweise zur Genauigkeit

- **Kostenzähler gegen Nachrichtenprotokoll:** Claude Code zählt rund 0,50 € mehr, als aus den gespeicherten Nachrichten folgt (2.012.123 Cache-Lese-Token, 2.530 neue Eingabe-Token). Das passt zu kurzen Hintergrundanfragen, die den Kontext mitlesen, aber keine Nachricht im Protokoll hinterlassen. Die genaue Ursache lässt sich aus der Datei nicht ablesen; für den Gesamtpreis gilt der Zähler.
- **Listenpreis, nicht zwingend Rechnungsbetrag:** Alle Beträge sind Token mal API-Listenpreis. Wird Claude Code über ein Abonnement statt über API-Abrechnung genutzt, werden diese Beträge nicht einzeln abgebucht, sondern sind der Gegenwert.
- **Zeitpunkt des Zählers:** Der Kostenzähler wurde nach der letzten Fable-Anfrage gespeichert (Projektende) und enthält keine Sonnet-Anfragen.
- **Nur Modellanfragen kosten Geld.** Docker-Build, Browser-Tests mit Playwright und die Node-Prüfskripte laufen lokal und verursachen keine API-Kosten.

## Methode

Ausgewertet wurde das Sitzungsprotokoll `~/.claude/projects/e--HTML/78b22d56-90be-43fe-9d67-a8b6962bbd40.jsonl`: jede API-Anfrage (Zeilen mit gleicher `requestId` zusammengefasst, größte Ausgabe je Anfrage) mit ihren `usage`-Feldern, dazu der `cost-state`-Eintrag von Claude Code. Der Wechselkurs stammt von `https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml`.
Die Auswertungsskripte liegen nicht im Projekt; die Zahlen lassen sich mit `/cost` in Claude Code gegenprüfen.
