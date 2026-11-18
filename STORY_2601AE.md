# SPACE 2601AE — Narrative Design & Weltbibel (Noir / Cyberpunk-Sci-Fi)

> **Tonalität & Vorbilder:** *Altered Carbon* (Unsterblichkeits-Kluft, Stacks/Sleeves, Meth-Arroganz) × *Shadowrun* (Konzern-Exterritorialität, SIN-Nummern statt Menschenrechte, schmutzige Fixer-Runs) × *SIGNAL LOST* (2034 → 2601 AE: gespiegelte Identitäten, Puffer-Lügen, analoge Ehrlichkeit).
> **Anti-Slop-Regel:** Keine generischen „Rette die Galaxis“-Texte. Kurz, kalt, bürokratisch oder zynisch – jede Zeile riecht nach Ozon, Altmetall und Schulden.

---

## 1. „Keine SIN, kein Sauerstoff“ im Registerjahr 2601 AE (567 Jahre nach dem Sturm)

- **Vom *Continuity*-Bug zum SIN-Gitter:** Was 2034 als kostensparende *AETHER Continuity Suite* begann („ein Zertifikat für zwei Körper“), ist im Jahr **2601 AE** zum weltumspannenden **SIN-Gitter (Systemic Identity Node)** versteinert.
- **Die zwei Klassen von Menschen:**
  - **Orbitals / „Platin-SINs“ (Direktor Vance & Morgenlicht):** Wer ein Platin-Zertifikat bezahlt, besitzt ein **Remote-Stack-Backup**. Stirbt der Körper bei einem Gefecht, wacht er Stunden später in einem frischen **Klon-Sleeve** auf *Bastion Morgenlicht* auf. Der Tod ist für sie nur ein Wartungsfenster.
  - **Gürtel-Volk / „Gebraucht-SINs“ (Du, Voss, Kipper):** Im Äußeren Gürtel teilt man sich aus Geldnot oft dieselbe **abgelaufene oder gespiegelte SIN** (zwei oder drei Schiffe auf einer Lizenz). Stirbst du hier draußen, gibt es keinen neuen Sleeve. Oft spielt dein Bordcomputer nur noch deine letzten Funksekunden als **synthetischen Puffer** in Endlosschleife, damit die Konzern-Versicherung keinen Ausfall melden muss.
- **Deine Ausgangslage (Der Run beginnt):** Du fliegst einen zusammengeflickten Erzfrachter mit einer **gestohlenen Kurier-SIN**, die laut Register längst als „terminiert“ gilt.

---

## 2. Die 4 Schlüsselfiguren (ohne Helden-Kitsch)

| Figur | Rolle & Ort | Charakter & Motivation |
|---|---|---|
| **„Old Man“ Voss** | Schicht-Dispatcher auf *Kestrel* | Rau, überarbeitet, kennt jeden toten Winkel. Hat seine eigene SIN vor zwanzig Jahren verkauft, um die Tochter auf eine innere Station zu bringen – weiß selbst nicht, ob sie noch lebt. Nennt dich erst *„Lizenzleiche“*, später *„Partner“*. |
| **„Kipper“ Taro** | SIN-Fixer & Schrott-Decker auf *Drift-9* | Lebt in einem halb verrotteten Industrie-Dock, bezahlt Schulden mit geklauten `NavData`-Stacks. Kein lustiger Sidekick, sondern ein zynischer Überlebenskünstler: *„Ich kleb dir 'ne saubere Kennung. Wenn sie bei Sektor 4 reißt, kenn ich dich nicht.“* |
| **Dr. Mara Lind** | Archivarin auf *Relais Theta* | Hat *RT-9 / ORPHEUS-7*-Analogbänder von 2034 ausgegraben. Erkennt im heutigen *TG-9* denselben Fehler. Warum AETHER damals eine Kennung für zwei Körper zuließ, erschließt sich ihr aus dem Archiv und den Lizenzregeln. |
| **Direktor Kaelen Vance** | „Rechtsnachfolger“ auf der goldenen *AETHER Sovereign* | *Borderlands-2-Jack*-Antagonist mit *Altered-Carbon*-Meth-Kälte: charmant, witzig-grausam, hält sich für den einzigen Erwachsenen im Raum. Lässt tote Crews als Puffer weitersenden und spottet persönlich über deinen Bordfunk. |

---

## 3. Konkrete Storytelling-Elemente für Engine & UI (direkt nutzbar für Codex)

### A. Kalte Stations-Header im Hangar (statt „Willkommen!“)
Jede Station begrüßt den Spieler im Hangar-Header mit einem **1-Zeilen-Systemstatus** (ändert sich optional mit Ruf):
- **Kestrel:** `KURIER-SIN: TERMINIERT · LOKALE DOCKFREIGABE: VORLÄUFIG · SAUERSTOFF-KREDIT: GEDECKT`
- **Helios-IV:** `SCHICHT-MORTALITÄT: IM TOLERANZBEREICH · BESCHWERDEN DEAKTIVIERT`
- **Drift-9:** `FIXER-FREQUENZ OFFEN · TRANSPONDER-REINIGUNG: BAR · KEINE FRAGEN`
- **Kepler-07:** `WARNUNG: AUTONOME CONTINUITY-DROHNEN AKTIV · LIZENZPRÜFUNG SCHARF`
- **Relais Theta:** `ANALOG-KANAL RT-9 AKTIV · ACHTUNG: LIVE-FEEDS KÖNNEN PUFFER SEIN`
- **Morgenlicht:** `PLATIN-IMMUNITÄT AKTIV · UNZERTIFIZIERTE SIGNATUREN WERDEN LIQUIDIERT`

### B. Fixer-Runs / „Shadow-Contracts“ (mit Konzern-Haken)
Statt generischer Lieferquests gibt es pro Station **1 dreckigen Run** (2–3 Zeilen Text):
1. *Kestrel → Helios-IV (Gewerkschafts-Leak):* „Offiziell braucht Helios nur Kühlmittel. Inoffiziell ersticken drei Decks an Rauchgas. Schmuggel `MedSupplies` am Zoll-Scan vorbei – Vance zahlt nichts, aber die Gilde streicht deine Hafensteuer.“
2. *Drift-9 → Kepler-07 (Kippers SIN-Wäsche):* „Ein Kurier mit *deiner* alten SIN ist bei Kepler gesunken. Hol seinen Stack aus dem Wrack, bevor AETHER ihn gegen dich abgleicht.“
3. *Kepler-07 → Relais Theta (Blackbox-Run):* „Bringe 3× `NavData` zu Mara Lind. Wenn dir im Nebel eine Drohne auf deiner eigenen Kennung antwortet: Schieß zuerst.“

### C. Das „Geisterschiff“ (Altered-Carbon-/Signal-Lost-Begegnung im All)
- **Event in Sektor 3 oder 4:** Auf dem Radar erscheint kurz ein neutrales oder feindliches Signal mit **deiner eigenen Transponder-Kennung** (Folge der *Single-Seat*-Spiegelung).
- Nach dem Abschuss oder Scan steht im Logbuch: *„Das Wrack funkt mit meiner SIN. Laut Register ist der Träger seit 41 Tagen tot, aber der Ruf läuft noch.“*

### D. Das Schiffs-Logbuch (8 knackige Einträge, max. 2 Sätze pro Eintrag)
1. **Start (Kestrel):** *„200 Credits, ein geliehener Sleeve-Vertrag und eine tote Kennung. Kestrel lässt mich noch andocken.“*
2. **Erster Vance-Kontakt:** *„Vance hat sich direkt in meinen Bordfunk geschaltet. Für ihn bin ich kein Pilot, sondern eine unversicherte Abschreibung.“*
3. **Helios-IV:** *„In den Schmelzöfen riecht die Luft nach Kupfer und verbranntem Filter. Medizinische Vorräte sind hier teurer als ein ganzes Leben.“*
4. **Drift-9:** *„Kipper hat meine Transponder-Signatur neu gelötet. ‚Hält bis zur nächsten Patrouille‘, grinst er, ‚danach bist du offiziell wieder tot.‘“*
5. **Kepler-07:** *„Zwischen den Trockendocks treiben Wracks mit laufenden Notrufen. Alle klingen gleich – sauber, ruhig, synthetisch.“*
6. **Relais Theta:** *„Mara Lind hat mir das RT-9-Band von 2034 vorgespielt. Zwei Körper funken unter derselben ID.“*
7. **Voss’ Opfer (Vorbereitung Climax):** *„Vance hat die Kennung von Voss’ Schlepper gestrichen. Der alte Mann fährt trotzdem in den Sperrkorridor.“*
8. **Nach Morgenlicht:** *(je nach gewähltem Ende – siehe §4)*

### E. Stimmen & Dialog-Referenz (so klingen die Figuren im Spiel)
- **Direktor Kaelen Vance (spöttisch-charmant → eiskalt → tobend):**
  - *Akt I:* „Oh, niedlich! Ein rostiger Erzfrachter auf meiner Frequenz. Genieß deine 200 Credits, Kleiner – das ist vermutlich mehr, als dein Sleeve wert ist.“
  - *Akt II:* „Weißt du, warum niemand mehr nach Frachter Neun sucht? Weil mein Puffer viel höflicher funkt als ein erstickender Schürfer.“
  - *Climax (Schildbruch):* „Was?! Wer hat euch Analogsäcken die alten RT-9-Schlüssel gegeben?! Ich halte diesen ganzen verdammten Sektor zusammen – schießt ihn runter!“
- **„Old Man“ Voss (hart, loyal, ohne Pathos):** „Niemand hier draußen stirbt als Held, Partner. Man stirbt nur entweder still im Puffer – oder weil man wem den Weg freigehalten hat.“
- **„Kipper“ Taro (nervös, straßenschlau):** „Garantie? Klar, bis zur Luftschleuse. Wenn dir in Sektor 4 dein eigenes Schiff entgegenkommt, schieß zuerst und frag später nach deiner SIN.“
- **Dr. Mara Lind (präzise, gehetzt):** „Vertrau keiner Stimme ohne Rauschen. Rauschen heißt, da atmet noch jemand.“
- **„Null-Takt“ Orlov (heiser, verbittert):** „Noch 41 Jahre Schichten bis zum neuen Körper. Bis dahin schluck ich den Rauch und verkauf dir den Weg durch die Scanner.“

### F. Setup → Payoff-Netz (keine losen Fäden)
1. **Deine gestohlene SIN (`Kestrel-04`):** Das Geisterschiff in Sektor 3/4 sendet deine Kennung. Haldens Blackbox in Sektor 1 nennt den ursprünglichen Träger. Beide Funde funktionieren in beliebiger Reihenfolge. Erst zusammen erklären sie, wie Vance deiner Spur folgt.
2. **Voss’ Schweigen über seine Familie:** Andeutung in Akt I → Stimme seiner Tochter im *Puffer-Chor* (Sektor 3) → erklärt, warum er in Akt III seinen letzten Flug ohne Backup wagt.
3. **Vances „Unsterblichkeit“:** Auf die Prahlerei in Akt I/II können die Logs des privaten Satelliten in Sektor 6 folgen. Sie datieren den Tod des ersten Körpers auf 2564 und nennen die Nummer 7 des heute aktiven Sleeves. Die Entscheidung am Backup-Server funktioniert auch ohne diesen Fund.

---

## 4. Climax & Die zwei Enden (mit echtem Preis)

1. **Der Verlust:** Auf dem Anflug zu *Bastion Morgenlicht* streicht Vance die Kennung, unter der Voss’ Stör-Schlepper fliegt, und fängt ihn ab (*„Keine Sorge, Partner – in meinem Register hat es ihn nie gegeben.“*).
2. **Der Analog-Override:** Mara Lind speist das *RT-9*-Signal von 2034 direkt auf deinen Transponder – Vances *TG-9*-Schild bricht, und der selbstherrliche Direktor verliert zum ersten Mal die Beherrschung.
3. **Die letzte Entscheidung nach Zerstörung der *AETHER Sovereign*:**
   - **Ende A — „Burn the Stack“ (Schatten-Ende):** Du zerstörst auch den **Platin-Backup-Server** auf *Morgenlicht*.
     *Endscreen-Text:* „Morgenlicht meldet zum letzten Mal einen fehlgeschlagenen Wiederherstellungsversuch. Das SIN-Gitter fällt aus, jede Schuld und jede Sperrliste brennt mit – ab morgen fliegt der Gürtel ohne Leine, und ohne Netz.“
   - **Ende B — „New Meth“ (Altered-Carbon-Ende):** Du überschreibst Vances Platin-Backup mit *deiner* eigenen Signatur.
     *Endscreen-Text:* „Kaelen Vance bleibt gelöscht. Du übernimmst die Platin-Schlüssel von Morgenlicht – und irgendwo auf Drift-9 lötet heute Nacht ein neuer Kurier eine gestohlene SIN zusammen, um dich zu stürzen.“

---

## 5. Schaufenster & Promo-Brücke zu *SIGNAL LOST* & *SIGNAL LOST: FALLLINIE*

**Prinzip:** *Space 2601AE* ist **kein Lernspiel** und versteckt keine Lektionen. Es ist ein eigenständiges, atmosphärisches Space-Spiel, das durch **dichtes Worldbuilding, moralische Konsequenzen und sauberes Setup-Payoff-Storytelling** zeigt, wie durchdacht das gesamte Universum gebaut ist – und damit Lust auf die beiden Ursprungs-Spiele von **2034** macht.

### Wie *Space 2601AE* die anderen Teile ohne „Werbesprech“ verkauft:
1. **Spürbare Story-Qualität als Beweis:**
   - Jede Figur (*Voss, Mara Lind, Kipper, J. Halden, Kaelen Vance*) und jedes Relikt (*Kestrel-04*, *Puffer-Chor*, *Sleeve-Satellit #7*) zahlt auf einen gemeinsamen Bogen ein. Der Spieler merkt: *„Hier ist nichts zufällig zusammengewürfelt.“*
2. **Die „LIVE vs. PUFFER“-Lüge (*Falllinie*-Signatur):**
   - Status-Tags wie `[RT-9 ANALOG]` vs. `[AETHER PUFFER]` lassen den Spieler schon im Raumkampf spüren, wie das zentrale Täuschungs-Prinzip von *FALLLINIE* funktioniert.
3. **10-Sekunden-„Routing-Override“ bei *Relais Theta* (pure Spannung, kein Unterricht):**
   - Vor dem Angriff auf *Morgenlicht* erscheint ein kurzes 2034er-Notfallprotokoll mit 3 Zeilen historischem C#-Code (`if (feed.IsBuffered && !node.Verified) RouteAnalog("RT-9");`) – der Spieler entscheidet zwischen `ORPHEUS-SIG-A [PUFFER]` und `ORPHEUS-SIG-B [ANALOG-ECHO]`.
4. **Zwei stumme 2034er-Archivzeilen (wecken Neugier auf die Vorgeschichte):**
   - **Relikt 5 (*RT-9 / ORPHEUS 7*):** `[ARCHIV 14.03.2034 · RT-9] ORPHEUS-7: 2 KÖRPER, 1 IDENTITÄT · EMPFANG ABGEBROCHEN · WIEDEREINTRITT UNBESTÄTIGT.`
   - **Bei *Sera* (*HERMES 2*):** `[NEBENKANAL 14.03.2034 · HERMES-2] SELECT node_id FROM weave_relay WHERE storm='G5'; // Restfragment: Chen / Pharos.`
5. **Atmosphärische Dossier-Karten im „Archiv 2034“ & Abspann:**
   - Am Ende der Geschichte (ohne Pop-ups im Spiel) zeigen zwei knappe Karten, was dich in den 2034er-Teilen erwartet – Code nicht als „Lernaufgabe“, sondern als **Waffe und Überlebenswerkzeug**:
     - **SIGNAL LOST (*HERMES 2*):** *„Der Sturm vom 14. März 2034. In 24 dichten Episoden greifst du mit **Python, SQL und HTTP** live in ein sterbendes Orbitalnetz ein – jede Abfrage entscheidet über Vertrauen und Überleben.“*
     - **SIGNAL LOST: FALLLINIE (*RT-9 / ORPHEUS 7*):** *„Dieselbe Nacht, ein anderes Signal: Allein im dunklen **3D-Relaisraum RT-9** mit Thermodrucker und Röhrenmonitor steuerst du Funk, IP-Routing und Schicksale direkt mit **echtem C#-Code** – jede Entscheidung bleibt physisch im Raum.“*
