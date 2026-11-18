# SPACE 2601AE — Piloten-Personalisierung, Skilltree & Engine-Integration

> **Ziel:** Jede Wahl soll sich im Cockpit **sofort spürbar** anfühlen (Geschwindigkeit, Fracht, Feuerkraft, Scanner, Überleben) **und** gleichzeitig neue RP-Dialoge, geheime Kanäle sowie konkrete Hinweise auf die **2034er-Vergangenheit (*SIGNAL LOST* / *FALLLINIE*)** freischalten.
> Alle Werte sind direkt auf die realen Konstanten in `PhysicsAndPool.fs`, `Mission.fs`, `Salvage.fs` und `TradeSystem.fs` abgestimmt (`MaxHull = 100`, `MaxShield = 100`, `LaserDamage = 12`, `CruiseSpeed = 60`, `BoostSpeed = 75`, `MagnetRadius = 55`, `Cargo = 16..40 t`).

---

## Spielbarer Stand im Dock

Unter **Piloten-Stack** sind alle neun Fähigkeiten verfügbar. Ein neuer Lauf beginnt mit einem Stack-Punkt. Für fünf weitere Sektoren und sechs einmalige Reliktscans gibt es insgesamt elf weitere Punkte. Die ersten beiden Stufen jedes Zweigs kosten je einen Punkt, die dritte kostet zwei. Damit ist der gesamte Baum in einem Handelsflug erreichbar. Funde lassen sich im selben Tab nachlesen. Frühere Spielstände erhalten den Startpunkt und die aus dem Spielstand belegbaren Sektorbesuche.

Archivsignale erscheinen auf 350 m, mit Demodulator auf 700 m. Jeder Fund hat einen eigenen Bergevorgang. Beim Anflug zeigt der Bordempfänger die Bedienung. [Fundorte und Bergung](GAMEPLAY.md)

**V** betätigt das Geister-Protokoll. **F** bleibt für die Schubregelung verfügbar. Handelsboni beziehen sich auf die positive Marge nach Einstandskosten. Die 35 % für Nav-Daten und die 15 % des Schatten-Rats addieren sich. Die Turret-Anschaffung kostet weiterhin 350 CR. Der Adrenalin-Bypass erhöht die vorhandene Abschussprämie von 16 auf 31 Schildpunkte. Die Achte Kopie stabilisiert den lebenden Sleeve beim Treffer. Der Spielstand merkt sich jeden Sektor, in dem die Rettung ausgelöst hat.

Die folgenden Abschnitte enthalten neben den umgesetzten Skills auch Entwürfe für Herkunft, Fixer-Aufträge und spätere Dialogverzweigungen.

## 1. Piloten- & Schiffs-Personalisierung („SIN-Akte“)

Beim Start eines neuen Handels- oder Story-Laufs (1-Screen-Terminal vor dem Abdocken auf *Kestrel*) konfiguriert der Spieler drei Ebenen:

### A. Rufzeichen & Transponder-Signatur
- **Callsign:** Freie Eingabe oder 4 Presets (`KESTREL-04`, `ASH-7`, `STATIC`, `ZERO-LAG`). Erscheint im HUD-Header, in Logbuch-Einträgen und in den Funk-Untertiteln von Voss, Kipper, Mara Lind & Vance.
- **Triebwerks- & HUD-Signaturfarbe (4 Noir-Paletten):**
  1. `Kurier-Cyan` (`#76e5f9`) — Standard
  2. `RT-9 Bernstein` (`#f4a24b`) — Analog-Relais-Look
  3. `Drift-Phosphor` (`#5ef29d`) — Schwarzmarkt-Grün
  4. `Sovereign-Karmin` (`#ff5a67`) — Kampfpiloten-Rot

### B. 3 Herkunfts-Sleeves (Signifikante Start-Unterschiede + Story-Reaktionen)

| Herkunft | Start-Modifikator auf `Ship` & `Cargo` | Wirtschaft / Welt | Story- & RP-Effekt |
|---|---|---|---|
| **1. Gürtel-Schürfer** (*„Rost-Sleeve“*) | `MaxHull = 125.0` (`+25 %`) · `Cargo = 20 t` (`+4 t`) · `LaserDamage = 10.5` (`-12 %`) | `+30 %` Kryo-Erz-Menge beim Bergen · Start-Ruf `+1 Gürtel-Gilde` | Voss & Orlov akzeptieren dich sofort als Schicht-Veteran; Schmuggel-Tipps auf *Helios-IV* früher sichtbar. |
| **2. Drift-Schmuggler** (*„Geklebte SIN“*) | `CruiseSpeed = 54.0` · `BoostSpeed = 75.0` · `MaxShield = 80.0` (`-20 %`) | `-25 %` Geleitschutz- & Hafengebühren · Start-Ruf `+1 Drift-Syndikat` | Kipper warnt dich vor Patrouillen; Vance spottet ab Sektor 1 gezielt über deine „geklebte Leichen-ID“. |
| **3. Abtrünniger Analyst** (*„Gelöschter Stack“*) | `MagnetRadius = 95.0` (`+40 m`) · `MaxShield = 115.0` | `+25 %` Verkaufswert auf `NavData` · `+20 %` Reparaturkosten bei der Gilde · Start-Ruf `+1 Relais-Kreis` | Dr. Mara Lind erkennt deine *AETHER*-Signatur; Vance behandelt dich als persönlichen Verräter. |

### C. 2 Wählbare Bord-Module („Cockpit-Prägung“ ab Start)
- **„Analog-Modem“:** Alle Funksprüche zeigen `[RT-9 ANALOG]` vs. `[AETHER PUFFER]`-Tags; Relikt-Kapseln leuchten auf größere Distanz auf.
- **„Heißer Kondensator“:** `Overdrive`-Dauer `+1.5 s`, Laser verbrauchen weniger Hitze/Zeit bei schnellen Salven.

---

## 2. Cortical-Skilltree (3 Zweige × 3 Stufen — Signifikante Vorteile + RP/Lore)

### Weitere geplante Punktquelle und bestehende Entdeckungen
- **+1 Punkt** pro entdecktem **Äther-Relikt** (`0/6` in den Sektoren 1–6)
- **+1 Punkt** beim ersten Erreichen eines neuen Sektors (`5 Punkte`)
- **Geplant:** +1 Punkt pro abgeschlossenem **Shadow-Contract** (Fixer-Auftrag am Dock)

---

### Zweig I — **SIGNAL-ARCHÄOLOGE** *(Forensik, EMP-Kriegsführung & die 2034-Wahrheit)*

| Stufe | Name | Signifikanter Gameplay-Vorteil | RP-Zugang & 2034-Vergangenheits-Hint |
|---|---|---|---|
| **Lv 1** | **RT-9-Demodulator** | `MagnetRadius +30 m` (gesamt `85–125 m`); Relikt-Kapseln & Wracks werden `2×` früher auf dem Radar markiert; Notrufe zeigen vorab `[ECHT]` vs. `[PUFFER]`. | Öffnet den verrauschten **Sekundärkanal** (*Sera* & verwaiste Puffer-Echos). **Hint 2034:** Rohe Signatur `RT-9 // 14.03.2034` taucht im Header auf. |
| **Lv 2** | **Cortical-Forensik** | Jede geborgene `NavData`-Kapsel erzielt **`+35 %` Credits**; besiegte Elite-Gegner (Gunship/Bomber) droppen zusätzlich `1 t NavData`. | Entschlüsselt **gelöschte Crew-Erinnerungen** in Wracks (*J. Haldens* echte Mission auf *Kestrel-04*). **Hint 2034:** Zitiert das alte *AETHER CS-7.3*-Handbuch (*„Split-Brain außerhalb des Lieferumfangs“*). |
| **Lv 3** | **Geister-Protokoll** *(Capstone)* | **Aktive Fähigkeit `[V]` (`45 s` Cooldown):** Sendet einen *Continuity*-Störimpuls – feindliche Schilde regenerieren **`8 s` lang nicht** und Drohnen verlieren `3 s` lang die Zielaufschaltung. | Schaltet bei *Relais Theta* und im Finale eine **Konfrontations-Option** frei: Du spielst Vance live die *ORPHEUS-7*-Bänder vor und entlarvst sein Imperium als 600 Jahre alten Abrechnungsfehler. |

---

### Zweig II — **SCHATTEN-FIXER** *(Schmuggel, Geldmacht & Gürtel-Allianz)*

| Stufe | Name | Signifikanter Gameplay-Vorteil | RP-Zugang & 2034-Vergangenheits-Hint |
|---|---|---|---|
| **Lv 1** | **Gassen-Chiffre** | **`-25 %` Geleitschutz- & Ausbaukosten**; Hangar zeigt Kurstendenzen (`Bargain`/`HighDemand`) der Nachbarstationen vor dem Abflug an. | Schaltet **geheime Fixer-Notizen** in den Stationstabs frei (*Orlovs* Leak auf *Helios-IV*, *Kippers* Schuldenbuch auf *Drift-9*). **Hint 2034:** Erwähnt alte **„Weave-Restknoten“** (*SIGNAL LOST* 2034). |
| **Lv 2** | **Doppel-Transponder** | **`+8 t` abgeschirmter Frachtraum** (sofort zusätzlich zu `CargoHold`, max. `48 t`); Piraten-Patrouillen auf Routen spawnen **`12 s` später** (genug Zeit zum Durchbrechen!). | Erlaubt das Mitlesen interner **Orbital-Memos** von *Morgenlicht*. **Hint 2034:** Ein Memo vergleicht den Sektor-Lockdown zynisch mit den **„Pharos- / Lazarus-Notfallprotokollen“** von 2034. |
| **Lv 3** | **Schatten-Rat** *(Capstone)* | Auto-Turret feuert **`+40 %` schneller** und `25 %` seines Schadens durchdringt Schilde direkt auf die Hülle; **`+15 %` auf die positive Marge** aller Waren. | Verbindet *Gürtel-Gilde* (*Orlov*) und *Drift-Syndikat* (*Kipper*): Im Endkampf auf *Morgenlicht* greifen verbündete Schiffe/Störbojen aktiv in die Blockade ein. |

---

### Zweig III — **SLEEVE-GEHÄRTET** *(Kampf-Dominanz, Overdrive & Zweites Leben)*

| Stufe | Name | Signifikanter Gameplay-Vorteil | RP-Zugang & 2034-Vergangenheits-Hint |
|---|---|---|---|
| **Lv 1** | **Phantom-Muskelgedächtnis** | **`+20 %` LaserDamage** (`12.0 → 14.4`), **`+20 %` Wendigkeit** (`Pitch/Yaw/Roll`) und **`+6.0` CruiseSpeed** (`60 → 66`); Retro-Bremse `[X]` reagiert `35 %` schneller. | Löst bei Stress **fremde Körper-Erinnerungen** im Bord-Logbuch aus. **Hint 2034:** Eine Erinnerung zitiert eine alte **ESA-/GTSA-Notfallcheckliste** (weiterkopierte Trainingsdaten). |
| **Lv 2** | **Adrenalin-Bypass** | Fällt `ship.Shield <= 0.0`, zündet automatisch **`4.0 s` Overdrive** (`25 s` Cooldown); jeder Abschuss gibt sofort **`+15.0` Schild** zurück. | Vance wird am Funk nervös und verplappert sich über seinen ersten Sleeve `2564 AE`. **Hint 2034:** Enthüllt, dass sein Klon-Labor auf *Morgenlicht* aus einem **geplünderten 2034er-ESA-Rettungsmodul** gebaut wurde. |
| **Lv 3** | **Die Achte Kopie** *(Capstone)* | **Notfall-Cortical-Sync:** Einmal pro Sektor fängt dein Stack einen tödlichen Treffer ab → `Hull = 30 %`, `Shield = 100 %`, `3.0 s` Overdrive + `Sound.shieldDown`. | Schaltet zusammen mit Relikt 1 (*Kestrel-04*) die **versteckte Selbst-Enthüllung** frei: Ein aktives Speicherfragment von **J. Halden** fliegt die ganze Zeit in deinem eigenen Schiffskern mit. |

---

## 3. Technische Integration für Codex (Astra 6.2)

Alle Effekte lassen sich sauber und ohne Engine-Bruch in den bestehenden Modulen bündeln:

1. **`GameSave.fs`:** `PilotProfile` ergänzen (`Callsign: string`, `AccentHex: string`, `Origin: int` (`0..2`), `ModuleId: int` (`0..1`), `StackPoints: int`, `Tree: int[]` mit 3 Längen `0..3`, `RelicsFound: bool[]` mit Länge `6`). Abwärtskompatibles Fallback für alte Saves.
2. **`Mission.fs`:** Neue Funktion `applyPilotProfile (ship: Ship) (cargo: CargoHold) (profile: PilotProfile)` einmal beim `startGame` / `departShop` aufrufen — setzt `MaxHull`, `MaxShield`, `LaserDamage`, `CruiseSpeed`, `BoostSpeed`, `MaxPitchRate/YawRate/RollRate` und `CargoHold`-Bonus.
3. **`Salvage.fs`:** `update` bekommt `magnetBonus: float` (aus Herkunft `RogueAnalyst` + `Signal-Archäologe Lv 1`) und `oreMultiplier: float` (aus `BeltMiner`).
4. **`TradeSystem.fs`:** `sellPrice` / `buyGood` berücksichtigt `NavData`-Bonus (`RogueAnalyst` + `Cortical-Forensik`), `Schatten-Rat +15 %` und `Gassen-Chiffre -25 %` Geleitschutz.
5. **`Shop.fs` / Hangar-UI:** Neuer kompakter Tab **„Stack & Akte“** direkt neben Upgrades & Routen: zeigt Herkunft, Callsign, `Archiv 0/6`, die 3 Zweige mit RP-Notiz/2034-Hint pro Knoten und nutzt `Sound.upgradeReady` / `Sound.uiConfirm` beim Freischalten.

---

## 4. Bereitgestellte UI-, Menü- & Porträt-Assets (`public/assets/`)

Die Originalvorlagen liegen im Asset-Ordner. Das Spiel lädt das komprimierte [Menübild](public/assets/menu-backdrop.webp), [Kestrels Erzverladung](public/assets/docks/kestrel-ore.webp) und den [Funkporträtbogen](public/assets/portraits/dock-radio.webp). Die drei SVGs erscheinen direkt an den Zweignamen im Piloten-Stack.
- **Hauptmenü-Hintergrund (16:9, links abgedunkelt fürs Menü):** `/assets/menu-backdrop.jpg`
- **Kestrel, Vorlage für die Erzverladung:** `/assets/docks/kestrel.jpg`
- **Charakter-Porträts (1:1 Monitor-Look):** `/assets/portraits/voss.jpg`, `/assets/portraits/mara_lind.jpg`, `/assets/portraits/kipper.jpg`, `/assets/portraits/kaelen_vance.jpg`
- **Skilltree-Zweig-Icons (SVG):** `/assets/icons/skill-archaeologist.svg`, `/assets/icons/skill-fixer.svg`, `/assets/icons/skill-sleeve.svg`
- **Piloten-Embleme & Live-Menü-Editor (`#menu-pilot`, `localStorage["space2601ae.pilot"]`, `window.__spacePilot`):** `/assets/emblems/emblem-kestrel.svg`, `emblem-eclipse.svg`, `emblem-rt9.svg`, `emblem-skull.svg`, `emblem-hex.svg`, `emblem-lance.svg` (gesteuert durch `/pilot-profile.js`).
