# Ausbauplan für Space 2601AE

## Vom Handelsflug zum vollständigen Spiel

**Produktionsvorschlag vom 18.11.2026.** Ziel ist ein abgeschlossenes Einzelspielererlebnis im bestehenden Webspiel. Der stärkste Ansatz liegt schon in Helios. Als Pilot kann ich einer Schicht die Filter durchs Werkstor bringen. Um den Hafen für alle Frachter zu öffnen, brauche ich die Werft und muss einen Einsatz fliegen. Der Markt zeigt mir später, was mein Eingreifen bewirkt hat. Diese Verbindung aus Handel, Flug und Konsequenz soll die Kampagne tragen. Der dokumentierte Spielstand und die vorgesehenen Enden stehen in [GAMEPLAY.md](GAMEPLAY.md) und [STORY_2601AE.md](STORY_2601AE.md).

### Wer welchen Auftrag übernimmt

| Aufgabe | Erste Wahl | Kleinste sinnvolle Übergabe und erwartetes Ergebnis |
| --- | --- | --- |
| Priorität setzen, Gameplay implementieren, integrieren | ChatGPT/Codex im laufenden Projektthread | Ein Spielerziel, die betroffenen F#-Stellen und der Abnahmetest genügen. Codex liefert die Änderung und den Prüfbefund. Für gewöhnliche Tickets erledigt Codex beides selbst. |
| Missionsdramaturgie, deutsche Figurenstimmen, Konsequenzen | Claude Sonnet | Figurenprobe, tatsächliche Spielzustände und Platz im HUD mitgeben. Zurück kommen z. B. drei direkt einsetzbare Funkzeilen mit Auslöser, Sprecher und Untertitel. |
| Schwierige Design- oder Architekturentscheidung | Claude Opus | Wenn zwei umsetzbare Lösungen bei Kampagnenverlauf oder Spielstand-Kompatibilität konkurrieren, bekommt Opus die Alternativen, den konkreten Konflikt und einen alten Beispielstand. Ergebnis ist eine begründete Entscheidung mit einem Gegenbeispiel. |
| Systementwurf über mehrere Spielsysteme | Claude Fable 5.2 | Kandidat etwa für eine persistente Werftökonomie, in der Bauaufträge, Frachtrouten, Verlust und Wiederaufnahme zusammenpassen müssen. Eingabe sind das Zustandsmodell und konkrete Grenzfälle. Ein Auftrag liefert den Entwurf und durchgerechnete Beispiele. |
| Visuelle Lesbarkeit und Art Direction | Gemini API, für die Sichtung zunächst Gemini Flash | Cockpitbild plus Blickziel oder kurzer Clip vom fraglichen Anflug. Ergebnis sind bis zu drei Befunde mit Bildposition bzw. Zeitmarke und einer überprüfbaren Änderung. Für ein neues Dockbild bekommt das Gemini-Bildmodell eine vorhandene Stationsreferenz und den tatsächlichen Bildausschnitt im Spiel. |

Die Zuordnung ist eine Startentscheidung für dieses Projekt. Ob sie sich bewährt, zeigt sich an akzeptierten Ergebnissen, Nacharbeit und Verbrauch je fertigem Spielelement. [Claude-Modellauswahl](https://platform.claude.com/docs/en/about-claude/models/choosing-a-model) · [Gemini-Videoanalyse](https://ai.google.dev/gemini-api/docs/video-understanding) · [Gemini-Bilderzeugung](https://ai.google.dev/gemini-api/docs/image-generation)

### So läuft ein Ticket

1. **Codex formuliert die Abnahme aus Spielersicht.** Erstes umgesetztes Beispiel ist Kippers Frachtbrief. Nach dem Kauf nennen Cockpit und Auftragsfenster den nächsten Hafen. Die Routenkarte markiert eine erreichbare Etappe bis zur Quittung auf Helios. [Zielführung](src/Blockade.fs)
2. **Ein Bearbeiter erhält nur den nötigen Ausschnitt.** Für dieses Ticket reicht Codex. Ein Spezialauftrag enthält Ziel, Referenz, Lieferformat und Abnahme. Ein Beispiel für Gemini wäre der Vergleich zweier Helios-Anflüge aus gleicher Entfernung mit der Frage, ab wann Frachter und Andockring auseinanderzuhalten sind.
3. **Lokal prüfen und abschließen.** Beim Frachtbrief zählen der Abflug, das erneute Laden unterwegs und das Verschwinden des Hinweises nach der Quittung. Während eines Piratenangriffs hat die Gefahrenmeldung Vorrang. Passende Logiktests und der normale Browserweg prüfen unterschiedliche Fehler. Zum integrierten Meilenstein gehören `npm test` und `npm run build`.
4. **Vier kurze Angaben für die Fortsetzung sichern.** Ergebnis und Dateien, Prüfbefund, offener Punkt, nächster Schritt. Eine Sitzung je zusammenhängender Arbeit weiterführen. Vor einem Modellwechsel nur die entscheidenden Ausschnitte übergeben. Das frühere Protokoll weist 46,7 Mio. aus dem Cache gelesene Eingabetoken aus. [Projektkosten](KOSTEN.md)

Pro Auftrag bleibt es regulär bei höchstens einem zusätzlichen Agenten. Produktionsprogramme und lokale Tests laufen direkt. Beim normalen Abschluss werden Modell, verfügbare Token- oder Kostenangabe, externe Medien-Credits und nötige Nacharbeit kurz vermerkt. Fehlende Verbrauchsdaten bleiben als unbekannt markiert. Das erlaubt eine spätere Rollenänderung anhand echter Tickets.

### Blender und ElevenLabs im Produktionsablauf

**Schiffswerft, erste ausgelieferte Serie.** Codex hat die Rümpfe und den Ausrüstungseffekt in den Handelsflug integriert. Ein Modellbau-Agent hat Lastkahn und Sperber auf die vorhandenen Waffen- und Motorpositionen gebaut. Blender rendert die Werftkarten aus denselben Meshes wie das Spiel. Claude Sonnet 5.5 hat mit den realen Erträgen früher Erzfahrten die Einstiegspreise vorgeschlagen. Gemini 3.8 Flash hat die drei Renderings auf Silhouetten und Farberkennung bei 230 Pixel Kartenbreite gesichtet. Die Prüfung ergab eine Freigabe der Rumpfformen. [Schiffe und Einbauten](GAMEPLAY.md#eigene-schiffe-und-spezialausrüstung)

Für die nächste Serie entscheidet eine Spielprobe über den Schwerpunkt. Beobachten, welcher Rumpf zuerst erworben wird, wie viele Fahrten er bis zur nächsten Anschaffung braucht und ob ein Wechsel zwischen Handel und Eskorte freiwillig erfolgt. Bei schwachen Erträgen gehen die beobachteten Strecken und Gewinne an Claude. Bei schlecht erkennbaren Rümpfen bekommt Gemini einen kurzen Mitschnitt der betroffenen Kameraeinstellung. Erst der Befund löst eine weitere Modell- oder Balancerunde aus.

- **3D.** Erster Kandidat ist Frachter Sechs. Der Loader-Test liest den vorhandenen Frachter-Export bereits ein, im Flug erzeugt die Engine das Frachter-Mesh prozedural. Zuerst den vorhandenen GLB am Helios-Tor im Spiel ansehen. Silhouette, Ausrichtung, Maßstab und Bildzeit bei identischer Kamera mit dem heutigen Frachter vergleichen. Ein nötiger Umbau bekommt eine editierbare Blender-Quelle und einen GLB-Export mit den vom Loader unterstützten Vertexfarben und Materialwerten. Der bisherige Frachter eignet sich als Lade-Fallback. [Frachter-GLB](public/assets/models/cargo-freighter.glb) · [Loader](src/GlbLoader.fs) · [Importtest](tests/glb-loader.test.mjs)
- **Sprache und Sound.** Für die Filterquittung zuerst Orlovs vorhandene Aufnahmen und Untertitel am Dock anhören. Fehlt eine passende Reaktion, die neue Zeile im Spiel als Text abnehmen. Anschließend genau diese Zeile mit fester Figurenstimme und Aussprache produzieren. Aufnahme pro Text, Voice-ID, Modell und Einstellungen wiederverwenden. Am Dock mit laufenden Maschinen prüfen, ob die erste Silbe hörbar ist und der Untertitel zum Einsatz passt. Für das stumme Spiel dieselbe Aussage als Text erhalten. Der Browser spielt die fertige Datei lokal ab. [Vorhandener Funk](src/StoryRadio.fs) · [Audiobestand und Mixer](SFX_CODEX.md) · [ElevenLabs-Sprachausgabe](https://elevenlabs.io/docs/api-reference/text-to-speech/convert)

### Meilensteine mit spielbarer Abnahme

#### Handel, Fortschritt und Spielgefühl

| Vorbild und belegte Mechanik | Was wir im eigenen Spiel erproben | Beobachtung im Spieltest |
| --- | --- | --- |
| *Freelancer* zeigt die Lage lokaler Preise zum galaktischen Mittel und lässt den Piloten eine Route planen. [Originalhandbuch, S. 10–11 und 16–17](https://download.wcnews.com/files/manuals/Freelancer%20-%20Manual.pdf) | Für die eigene Ladung Verkaufserlös und erwartetes Handelsergebnis je Reiseart zeigen. Ein sichtbarer Frachtraumausbau gibt den verdienten Credits einen Zweck. | Kann ein neuer Spieler zwischen Eigenflug und Geleit wählen, seine Entscheidung begründen und sagen, wie viel zum Ausbau fehlt? |
| *Star Citizen* bietet Kurieraufträge neben eigenfinanziertem Warenhandel an. RSI beschreibt Preisänderungen und Piraterie als Risiken. [RSI über Fracht und Handel](https://support.robertsspaceindustries.com/hc/en-us/articles/16444523494423-Daily-Life-in-the-Verse) | Die Frachtbörse bietet wiederholbare Aufträge zu 8 und 24 t. Wer Platz übrig hat, kann auf derselben Fahrt eigene Ware verkaufen. Für die Vorschau gelten die simulierten Lagerbestände und die gewählte Geleitgebühr. [Frachtaufträge](GAMEPLAY.md#frachtaufträge) | Kombiniert ein Pilot Auftrag und Eigenhandel? Trägt die Aussicht auf größere Sendungen seine erste Frachtraumerweiterung? |
| *Wing Commander: Armada* versorgt Werften mit Rohstoffen und verbindet strategische Bestände mit fliegbaren Gefechten. [Originalhandbuch, „Introduction to Armada“](https://download.wcnews.com/files/arma/Armada%20-%20Play%20Guide%20-%20Text.pdf) | Einen späteren Werftauftrag in Teillieferungen finanzieren. Die gebaute Eskorte senkt bereits heute die Transportkosten zu ihrem Zielhafen. Ein künftiger Konvoi könnte durch das Flugergebnis mehr oder weniger Baumaterial abliefern. | Sieht der Spieler nach der Landung, welche Lieferung seinen Bau vorangebracht hat? Verändert das neue Schiff seine nächste Routenwahl? |

Als Arbeitshypothese reizt an diesem Kreislauf, einen eigenen Plan zu fassen, den Ausgang zu beeinflussen und mit dem Ertrag neue Möglichkeiten zu erwerben. Für die erste Spielprobe beobachten wir Routenwahl, tatsächlich verbuchtes Handelsergebnis und erste größere Anschaffung. Grafik und Bedienung gehören zur selben Probe. Der Spieler soll aus normaler Anflugentfernung den richtigen Hafen erkennen und im Dock Erlös, Risiko und Reiseergebnis lesen können. Vergleichsbilder aus identischer Kameraposition und ein Test mit einem Erstspieler beantworten unterschiedliche Fragen.

**Erster geprüfter Stand.** Im Chrome-Release-Build ergaben fünf Einheiten Kryo-Erz von Kestrel nach Helios 193 CR Erlös. Die Reiseoptionen zeigten +99 CR im Eigenflug und +49 CR mit Geleit. Nach Geleit und Verkauf stieg das gespeicherte Handelsergebnis um 49 CR. Ein eigener Durchlauf bestätigte die Piratenwarnung bei offenem Frachtbrief. Die Andockpositionen kamen aus vorbereiteten Spielständen. Build und 112 automatisierte Tests bestanden. Die Probe mit einem neuen Spieler steht aus.

**Spielprobe Frachtbörse, 18.11.2026.** Claude Sonnet 5.5 hat Größen und Risikoprämien anhand der erzielten Handelsmargen verglichen. Für den Großauftrag wurden 24 t gewählt, damit der erste Kutterausbau ihn freischaltet und auf dem Lastkahn Platz für freien Handel bleibt. Die verplombte Kiste am Schiff und ihr Blender-Rendering am Markt verwenden dieselbe Geometrie. Im Chrome-Release-Build brachte ein Kutter acht Tonnen Bohrkronen und fünf Einheiten eigenes Erz nach Helios. Nach Speichern unterwegs, erneutem Laden, Anflug, Verkauf und Übergabe entsprach die Buchung von **+125 CR** der Routenkarte. Die offene Balancefrage ist, wann Spieler von reiner Auftragsfracht zu einer gemischten Ladung wechseln.

#### Kampagne und Veröffentlichung

| Etappe | Was ein Spieler am Ende kann | Woran wir sie abnehmen |
| --- | --- | --- |
| 1. Erste Schicht im Gürtel | Von der Flugschule über den ersten Handel zu Kippers Frachtbrief und Orlovs Quittung kommen. Der Kreuzer bleibt als größeres Problem sichtbar. | Eine neue Testperson findet die Lieferroute ohne Erklärung. Nach Laden unterwegs stimmt das nächste Ziel. Die Quittung zahlt einmalig, und am Dock ist verständlich, wem die Lieferung half. |
| 2. Helios und Relais Theta | Über die Werft den Korridor öffnen, die Versorgung am Markt beobachten und aus der Geschichte der betroffenen Schicht eine Spur zum Archiv auf Theta aufnehmen. | Bomber und Eskorte führen beide zum Durchbruch. Jede Versorgungslieferung bleibt über Speichern und Laden nachvollziehbar. Ein konkreter Fund führt zur nächsten Kampagnenhandlung. |
| 3. Morgenlicht | Aus dem Handelsflug heraus Vance erreichen und die in der Storybibel angelegte Entscheidung über das Platin-Backup treffen. | Beide Wege sind ab einem gemeinsamen Spielstand erreichbar. Ein Epilog zeigt pro Ende eine andere Folge für einen bereits besuchten Hafen oder eine bekannte Figur. |
| 4. Veröffentlichungsfassung | Vom Hauptmenü bis zum Abspann spielen, unterbrechen und später fortsetzen. | Ein kompletter Durchlauf im Release-Build, alte Spielstände laden, beide Enden prüfen. Für vereinbarte Zielhardware Bildzeit im dichtesten Gefecht und Ladezeit messen. Steuerung, Untertitel und Audiomix mit Erstspielern prüfen. |

---

## Universumskarte für den Handelsflug

**Spielbarer Stand.** Das Handelsnetz umfasst neun Sektoren und dreizehn beidseitige Verbindungen. **F7** und der Reiter **Routen & Kurse** öffnen die Karte mit Frachtziel, speicherbarem Fernziel, optionalem Zwischenstopp und Handelsvorschau für die nächste Etappe. Schleierpass verbindet Kaltstrom und Wracksaum. Zoom und Verschieben bleiben Teil des Kartenentwurfs.

### Karte und Bedienung

- Unter **Routen & Kurse** öffnet sich die Übersicht. Im Flug öffnet **N** dieselbe Karte mit dem eigenen Sektor im Zentrum. Für den Einzelspieler pausiert die Karte die Simulation. Schließen stellt den vorherigen Flug- und Eingabezustand wieder her.
- Die Übersicht zeigt feste Positionen für alle bekannten Sektoren. Jede Welt bekommt eine erkennbare Silhouette aus der vorhandenen Flugansicht, etwa Kestrels Ringplaneten, Helios’ Lavawelt oder Thetas Pulsar. Dünne Linien verbinden die Tore. Das eigene Schiff hat einen Ring und eine Beschriftung, ein Lieferziel ein Frachtsymbol. Der gewählte Kurs trägt Etappenziffern. Zoom und Verschieben ergänzen die Auswahl per Tastatur oder antippbarer Hafenliste.
- Antippen eines Hafens öffnet dessen Stationsbild, Dienste und Verbindungen. **Kurs setzen** merkt das Fernziel. Das Cockpit markiert den ersten Sprung, nach der Ankunft rückt die Markierung eine Etappe vor. Die bestehende Dockreise bleibt der erste ausführbare Reiseweg. Eigenflug und Geleit werden für die jeweils nächste Etappe bestätigt und abgerechnet.
- Im Beispiel **Kestrel → Kepler-07** stehen zwei gleich kurze Wege zur Wahl. Über Helios lässt sich ein dort fälliger Frachtbrief abgeben, über Drift-9 erreicht man den Freihafen. Ein antippbarer Zwischenstopp hält die Wahl fest. Preise, verfügbarer Lagerplatz, erwartete Verkaufsmarge, Honorar und Geleit für die nächste Etappe kommen aus den vorhandenen Angeboten. Am Zwischenhafen berechnet sich die nächste Vorschau erneut. Ein Auftrag zahlt erst bei Übergabe.

### Zwei mögliche zusätzliche Sektoren

| Sektorentwurf | Neue beidseitige Tore | Spielbarer Anlass für einen Besuch |
| --- | --- | --- |
| **Kaltstrom** mit dem Kondensatdepot am Eisriesen | Helios-IV und Relais Theta | Das Depot braucht Treibstoffzellen für seine Pumpen und liefert Kryo-Erz. Ein Händler kann günstig eingekaufte Zellen gegen eine Rückladung für die Raffinerie tauschen. Zwischen Helios und Theta entsteht eine Verbindung mit zwei Sprüngen. |
| **Wracksaum** mit dem Schlepperdock im Trümmerring | Drift-9 und Bastion Morgenlicht | Ein Wrack abseits der Torlinie birgt Plasmakerne. Die direkte Passage zum zweiten Tor bleibt offen. Wer ausschert, setzt vorhandene Ladung und Hülle für die zusätzliche Bergung aufs Spiel. |

Beide Namen und Warenprofile sind Vorschläge. Frachter und Eskorte sollen unterschiedliche Gründe für dieselbe Route haben. Die Helios-Blockade und ihre Lieferfreigaben gelten bei jeder Anflugrichtung.

### Lieferung und Abnahme für Opus

1. **Sechs Sektoren navigierbar machen.** Kartenpositionen nach Sektor-ID hinterlegen, Kanten aus `Universe.sectors[].Gates` erzeugen und `TradeRoutes.nextHop` für die einzelnen Etappen verwenden. Optionalen Zwischenstopp und Fernziel per ID speichern. Abnahme mit einem laufenden Frachtauftrag, beiden Wegen von Kestrel nach Kepler, Laden unterwegs sowie Tastatur- und schmaler Bildschirmansicht.
2. **Kaltstrom als siebten Sektor anschließen.** Hafen, Markt, Rücktore und eigenes Stationsbild gemeinsam liefern. Vorher die bisherigen Sechsergrenzen für Marktlisten, Besuchs-/Reliktmasken und Talentzählung prüfen. Märkte nach ID übernehmen und fehlende neue Märkte initialisieren. Die sechs Warenarten haben eine eigene, unabhängige Anzahl. Abnahme mit einem alten Spielstand, bestehendem Kontostand, Marktbeständen, Auftrag und Talentstand sowie einer vollständigen Hin- und Rückreise.
3. **Wracksaum nach dem ersten Spieltest ergänzen.** Im Test den zusätzlichen Einkauf und Verkauf in Kaltstrom einer direkten Fahrt gegenüberstellen. Bei Wracksaum muss der Spieler das zweite Tor erreichen oder für die Bergung sichtbar vom Kurs abweichen können. Der [Torauftrag für Sol](#einzelauftrag-für-gpt-62-sol--sprungtore-im-anflug) führt den physischen Durchflug schon im bestehenden Sechs-Sektoren-Netz ein.

## 1. WebGL2-Darstellung (aufbauend auf `WebGLRenderer.fs`)

**Bereits in `WebGLRenderer.fs` vorbereitet (17.11.2026):**
- **ACES-Filmic-Tonemapping** im Fragment-Shader für kontrastreichere Metall-Reflexe (Schiffe/Turret) und weicheres Ausrollen heller Treffer-/Laser-Highlights.
- **Erhöhtes Emissive-Boost** vor dem Tonemapper für kräftigere Triebwerke und Projektile.
- **`BlendMode.Alpha`** (`SRC_ALPHA, ONE_MINUS_SRC_ALPHA`) als dritter Blend-Modus neben `Opaque` und `Additive`.

**Nächste Schritte (nach Abschluss der laufenden Gameplay-Runde):**
1. **Offscreen-HDR-Framebuffer (`RGBA16F`) + Bloom-Pass:**
   - Szene in eine `RGBA16F`-Textur rendern, helle Emissive-Pixel (> 1.0) in zwei Halb-/Viertel-Auflösungs-Passes weichzeichnen und mit der Hauptszene vor dem ACES-Tonemapping additiv mischen.
   - Effekt: Echter Glow um Laser, Triebwerke, Explosionen und Schildtreffer ohne Änderung an den 3D-Meshes.
2. **Schildblasen & Partikel-Trails über `BlendMode.Alpha`:**
   - Beim Treffer auf ein Schiff kurzzeitig eine halbtransparente Kugel (`BlendMode.Alpha`) aufleuchten lassen.
3. **Sektor-spezifische Lichtstimmung (`SkyBackdrop.fs` + `Lights`):**
   - Je Sektor (1–6) eigene Werte für `SunColor`, `Ambient` und `ClearColor` (in `Universe.SectorAtmosphere` bereits definiert), damit die Sektoren Richtung Kommandoschiff optisch erkennbar wechseln.
4. **Warp-Streaks beim Boost (`DustBatch` in `Main.fs`):**
   - Staubpartikel entlang der Flugrichtung proportional zu `Speed + Boost` strecken (`Scale.Z`), sodass beim Boost echtes Hyperraum-/Tempogefühl entsteht.
5. **Hemisphere-AO im Fragment-Shader (`WebGLRenderer.fs`):**
   - Sanfte Abdunklung nach unten (`N.y`) bzw. in lokalen Vertiefungen, damit Unterseiten von Flügeln und Stationsringen plastischer wirken.
6. **3D-`.glb`-Modelle einbinden (`public/assets/models/*.glb`):**
   - `outpost-station.glb`, `jump-gate.glb` und `cargo-freighter.glb` über `GlbLoader.loadAsync` laden und in den Sektoren sichtbar machen.

### Einzelauftrag für GPT-6.2 Sol · Sprungtore im Anflug

**Spielerziel.** Ich sehe von Kestrel aus das Tor nach Helios, richte mein Schiff an der Öffnung aus und booste hindurch. Beim Passieren des Rings springe ich automatisch nach Helios und fliege dort aus dem Gegentor. Dafür brauche ich weder eine Taste noch das Dockmenü.

**Ausgangspunkt.** Das vorhandene [Tormodell](public/assets/models/jump-gate.glb) hat ein emissives Material. Der Flug zeichnet dort bislang zwei Lichtflächen und ein Punktlicht. In Tornähe erklingt ein Signal. Die Radiusabfrage in [Universe.fs](src/Universe.fs) erkennt Nähe. Für den physischen Sprung zählt, ob die Flugbahn während eines Simulationsschritts die sichtbare Toröffnung kreuzt. Tor und Durchflugfläche brauchen dieselbe Position, Ausrichtung und Öffnungsweite.

**Inszenierung.** Aus rund 500 Metern prägen große, gegeneinander versetzte Ringsegmente die Silhouette. Beim schrägen Anflug aus rund 80 Metern machen Streben und Wartungsleuchten die Bautiefe sichtbar. Als gestalterisches Leitmotiv läuft eine Lichtwelle über die inneren Segmente bis zum Scheitel. Beim Durchqueren ziehen die Sterne kurz in Flugrichtung. Der Pilot sieht den freien Durchlass und nach dem Austritt den Himmel des Zielsektors. Sol entscheidet über die Ausführung anhand des tatsächlichen Cockpitbilds.

**Spielregeln.** Physischer Durchflug ist Eigenflug. Es gelten die vorhandenen Regeln für Ladung, Aufträge, Patrouillen und Blockade. Der Reiseabschluss zählt den Flug einmal. Das Schiff erscheint jenseits der Austrittsebene mit Abstand zum Rahmen und fliegt vom Tor weg. Geleit bleibt am Dock buchbar.

**Abnahme im Spiel.**

- Von Kestrel das Helios-Tor mit voller Boostgeschwindigkeit mittig durchfliegen. Der Zielsektor wechselt genau einmal. Den Rückflug manuell antreten. Das zweite Tor von Kestrel führt entsprechend nach Drift-9.
- Außen am Ring vorbeistreifen, vor der Öffnung abbremsen und den Rahmen berühren. In allen drei Fällen bleibt der Pilot im Ausgangssektor. Bei Rahmenkontakt gibt es sichtbares Kontaktfeedback.
- Mit einem laufenden Frachtauftrag springen, direkt nach Ankunft speichern und neu laden. Auftrag und Ladung bleiben an Bord, das Honorar ist bis zur Übergabe offen. Reiseanzahl, Gebühren und Blockadefortschritt entsprechen einer einzigen Eigenflug-Etappe.
- Als Grafiknachweis einen kurzen Spielmitschnitt vom Anflug bis zum Austritt und ein Vorher-nachher-Bild vom selben Blickpunkt liefern. Produktionsbuild und vorhandene Dockreisen prüfen. Sol trägt Modellkennung und gemessene Token unter diesem Auftrag im README-Benchmark ein.

**Spielprobe im Produktionsbuild.** Vom Kestrel-Dock erreichte ein Kutter das Helios-Tor mit Boost und trat mit 134,9 m/s am Gegentor aus. Nach dem Neuladen standen eine Reise und 200 CR im Spielstand. Die acht Tonnen Bohrkronen waren weiter versiegelt, das Honorar von 76 CR war offen. Rückflug, zweites Tor nach Drift-9, Bremsen vor der Öffnung, Rahmenkontakt und ein seitlicher Vorbeiflug wurden im Cockpit geprüft. Ein laufender Transporterbau rückte bei der Torreise genauso weit vor wie beim gebuchten Eigenflug. [Spielmitschnitt](artifacts/sprungtor/kestrel-helios.webm) · [Cockpitvergleich aus 534 und 85 Metern](artifacts/sprungtor/kestrel-helios-vergleich.webp)

---

## 2. Handelssystem statt pauschaler Credits

**Ziel:** Bergung als Fracht mit Warenwerten und Sektor-Kursen. Die vom Nutzer gewünschten 100 CR Abschussprämie bleiben zusätzlich erhalten.

**Grundlage eingebunden (17.11.2026, in `App.fsproj` enthalten):**
- [`src/Universe.fs`](src/Universe.fs): 6 verbundene Sektoren mit Sprungtoren (`JumpGate`), jeweils eigener Licht-/Nebelstimmung (`SectorAtmosphere`) und physisch im 3D-Raum anfliegbaren Handelsposten (`Outpost` mit `evaluateDocking`, `approachProgress`, `findTriggeredGate`).
- [`src/TradeSystem.fs`](src/TradeSystem.fs): 6 Handelsgüter (`TitaniumScrap`, `CryoOre`, `PlasmaCores`, `NavData`, `FuelCells`, `MedSupplies`), Frachtraum (`CargoHold` mit Volumen, Durchschnittskosten & Ausbau), dynamische Stationsmärkte mit Lagerbestand und Preistendenz (`Bargain`/`Normal`/`HighDemand`).
- [`tests/universe-trade.test.mjs`](tests/universe-trade.test.mjs): Tests für Sektor-Graph, Andocken, Frachtraum, Stückpreise, Rücktauschverlust, begrenzte Lager und Teilverkäufe.

**Erster spielbarer Handelskreislauf:**
Kryo-Erz bergen → 16 t Frachtraum → markierte 3D-Station anfliegen → mit B andocken → verkaufen/einkaufen/aufrüsten. Ausbau bis 40 t. Der normale Handelsflug startet friedlich mit 200 CR; Nachbarstationen sind über Routen & Kurse frei erreichbar, auch auf dem Rückweg. Geleitschutz kostet Geld, Eigenflug kann Piraten auslösen. Märkte, Fracht und Beschädigungen bleiben erhalten. Die bisherige Kampfmission läuft separat über F2. Details: `GAMEPLAY.md`.

**Noch offen** sind zusätzliche physische Wrackgüter. Im Handelsflug stehen die Sprungtore und die Routenbuchung am Dock zur Verfügung. Die sechs Sektoren haben eigene Lichtstimmungen und Asteroidenfelder.

**Geplanter Zuschnitt:**
1. **Waren & Frachtraum (`Economy.fs` / `Salvage.fs`):**
   - Vier Gütertypen statt Einheits-Kristall: *Titanschrott* (Standard-Wracks), *Kryo-Erz* (Asteroiden), *Plasmakerne* (Kanonenboote/Bomber), *Nav-Daten* (Späher/Kommandoschiff).
   - Schiff hat eine begrenzte **Frachtkapazität** (z. B. Start `12 t`, im Shop ausbaubar).
2. **Sektor-Marktpreise (`Mission.fs` / `Shop.fs`):**
   - Jeder Sektor würfelt/definiert Kursfaktoren je Gut (z. B. Sektor 2 Bergbauzone: Kryo-Erz günstig, Plasmakerne teuer; Sektor 4+ Belagerung: Reparatur & Erze teurer).
   - Im `#hangar`-Overlay neben den Schiffs-Upgrades ein kompakter **Handels-Block**:
     - Vorhandene Fracht zum aktuellen Sektor-Kurs verkaufen („Alles verkaufen“ oder einzeln),
     - Günstige Ware einkaufen, um sie im nächsten Sektor teurer abzustoßen (Risiko vs. Ertrag),
     - Optional: Hüllenreparatur direkt über Credits oder Titanschrott.
3. **Shop-Balance:**
   - **Frachtraum-Erweiterung** als fünfte Ausbaustufe im Hangar, die mit Kampf-Upgrades (Turret, Laser, Schild, Hülle) konkurriert.
