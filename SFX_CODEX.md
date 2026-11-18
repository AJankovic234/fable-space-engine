# Space 2601AE — Sound-API & Audio-Assets für Codex (Astra 6.2)

Die 125 lokalen Aufnahmen liegen unter `public/assets/sfx/`. Beim Freischalten lädt `Sound.unlock` elf häufig benötigte Dateien für Triebwerk, Atmosphäre und erste Bediengeräusche (3,53 MB). Weitere Geräusche und Stimmen lädt ihr erstes Spielereignis. Ein gemeinsam genutzter Puffer pro Datei verhindert doppelte Downloads und Dekodierungen. Der Mixer verwendet einen Master-Kompressor, Stereo-Delay, Faltungshall, Ambience-Ducking, getrennte Lautstärkeregler (`master`, `sfx`, `radio`, `ambience` unter `space2601.audio`), einen Cockpit-Funkbandpass von 360 bis 2850 Hz sowie echtes 3D-Stereo-Panning mit Rückraum-Tiefenabsenkung (`Sound.setListener`).

## Bereits aktive / verdrahtete `Sound.*`-Aufrufe
- `Sound.create () : SoundEngine`
- `Sound.unlock (s: SoundEngine)`
- `Sound.toggleMute (s: SoundEngine)`
- `Sound.setVolumes / getVolumes` — Getrennte Regler für Master, Effekte, Funk/Stimmen und Triebwerk/Raum (im Hauptmenü und über `F8`)
- `Sound.setListener (s: SoundEngine) (pos: Vec3) (rot: Quat)` — 3D-Hörerposition und -ausrichtung für echtes Stereo-Panning und Rückraum-Filterung
- `Sound.setFlightContext` — Rumpf-spezifische Triebwerkskurven (Kutter, Lastkahn, Sperber), Kit-Klang (inkl. `ShieldRelay`), Cockpit- vs. Verfolger-Mix, Sektor-Ambience & Sprungtor-Näherungssummen
- `Sound.setSceneMode` — Trennt Menü/Ende (`0`, still), Flug (`1`), Raumpause (`2`, gedämpfter Raumklang) und Hangar/Dock (`3`)
- `Sound.updateEngine (s: SoundEngine) (active: bool) (throttle: float) (boost: float)` — Triebwerk + `afterburner.ogg`-Boost-Layer + `retro_thrust.ogg`-Bremsdüse + `space_amb`/`nebula_drone`/`cockpit_hum`/`hangar_amb`/`hangar_comp` Crossfade + `thruster_0..2` Boost-Zündung
- `Sound.laser / laserAt` — Alternierendes L/R-Panning, skalierend mit Waffenstufe/Impulsfokus/Overdrive sowie gegnerischen Rollen (`Drone`, `Scout`, `Gunship`, `Bomber`)
- `Sound.hit / hitAt` — 5× Schild (`forceField_000..004`), Hülle + 5× Metall (`impactMetal_000..004`) + elektrische Funken (`hull_arc_0..1`) + automatische `comm_shield_down.mp3`-Warnung (18s-Cooldown)
- `Sound.explosion / explosionAt` — Geschichtet (`explosion` + `explosionCrunch_000..004` + `lowFrequency_explosion_000..001` + Ambience-Ducking + 3D-Panning)
- `Sound.targetLock (s: SoundEngine)` — nutzt jetzt `target_lock.ogg` (`telemetrie_empfang.ogg`, dezent bei 0.15 Vol)
- `Sound.receiver s signal` mischt auf Drift-9 zwei sechssekündige lokale Funkspuren nach Peilung (0–1). Ein negativer Wert schließt den Empfänger. `drift_tug.wav` und `drift_handset.wav` wurden mit den deutschen Windows-Stimmen Stefan und Katja offline eingesprochen und für den archivierten Notruf bandbegrenzt.
- `Sound.waveStart / waveCleared / gameOver (s: SoundEngine)` — inkl. `comm_wave_start.mp3` & `comm_wave_clear.mp3`
- `Sound.stationComm (s: SoundEngine) (stationId: string) (kind: int)` — Stationsfunk (`kind`: `0` = Stations-Begrüßung, `1` = Andockfreigabe, `2` = Dock gesperrt) mit `radio_squelch.ogg` & 5s-Cooldown
- `Sound.resetStationComm (s: SoundEngine) (stationId: string)`
- `Sound.pirateAmbush (s: SoundEngine)` / `Sound.sectorClear (s: SoundEngine)` — Szenario-Funksprüche (`comm_pirate_ambush.mp3`, `comm_sector_clear.mp3`)
- `Sound.aetherChatter (s: SoundEngine)` — manueller Äther-Funk (zusätzlich spielt im aktiven Flug automatisch alle 26–48 s einer von 16 `chatter_0..15.mp3`-Funksprüchen mit Stereo-Panning & Squelch)
- `Sound.shieldDown / hullCritical / upgradeReady / tradeProfit / jumpReady / autoTurretOnline (s: SoundEngine)` — Cockpit- & Missions-Funksprüche (`comm_shield_down.mp3`, `comm_hull_critical.mp3`, `comm_upgrade_ready.mp3`, `comm_trade_profit.mp3`, `comm_jump_ready.mp3`, `comm_auto_turret.mp3`)
- `Sound.storyBeat (s: SoundEngine) (beat: int)` — 18 *Signal-Lost*-Kanon-Story- & Dialog-Vertonungen (alle auch über `DockContact` / `Sound.tryContactBeat` abrufbar): `0` = Akt I Voss, `1` = Akt I AETHER TG-9, `2` = Akt II Dr. Mara Lind (Puffer-Notrufe), `3` = Akt II Voss (Kepler-Lizenzliste), `4` = Akt III Lind (Orpheus-7-Archivband), `5` = Akt III TG-9 (Bastion Morgenlicht Lockdown), `6` = Vance Intro-Spott, `7` = Kipper Taro (Drift-9), `8` = Vance Puffer-Hohn, `9` = Climax Voss („Zieh das Feuer auf meinen Schlepper“), `10` = Climax Vance Rage, `11` = Climax Lind Sieg, `12` = „Null-Takt“ Orlov (Helios-IV Begrüßung), `13` = Orlov Brief (Schichtliste), `14` = Voss Brief (Aufnahme von Drift-9 / Tochter), `15` = Mara Lind Brief (Lizenzordner / beide Sender), `16` = Sera (Stimmenwächterin über Haldens Kennung), `17` = J. Haldens Blackbox (*Kestrel-04*).

## Geräusche im Flug und am Dock

**Kepler-07.** Beim Handelssprung zur Orbitalwerft meldet der örtliche Verkehrsfunk einen auslaufenden schweren Kreuzer. Erst beim Andocken bestätigt Kepler die verriegelten Dockklammern. Der Kran arbeitet, sobald die Werft einen Bauauftrag annimmt. Morgenlicht verwendet ebenfalls die schweren Dockgeräusche und hat seinen eigenen Stationsfunk.

**Helios.** Bei der Ankunft im blockierten Sektor klingt das Horn des Zollkreuzers. Seine Geschütze sind an tatsächlich erzeugte Projektile gekoppelt. Der erste Torpedotreffer hat einen Einschlagton. Beim zweiten zerbricht der Kreuzer mit der mehrstufigen Großschiffexplosion.

**Kampffinale.** Die Sovereign meldet sich beim Auftauchen des Kommandoschiffs. Sobald der Funkkanal frei ist, begleitet Vances Warnung einmal eine laufende Ladephase der Lanzenbatterie. Die Batterie hat einen eigenen Feuerklang.

Werft- und Flottenfunk warten auf ein freies Sprachfenster. Beim Öffnen der Speicheransicht, beim Stummschalten und beim Verlassen des Docks werden laufende Durchsagen beendet. Das Laden eines angedockten Spielstands löst keine erneute Dockankunft aus.

Für untertitelte Aufnahmen belegt `Sound.tryContactBeat s beat` den Sprachkanal. Ein positiver Rückgabewert gibt die Laufzeit einschließlich Funkauftakt in Sekunden an. Bei `0` wird noch geladen oder der Kanal ist belegt. Bei `-1` ist die Textfassung verfügbar. `Sound.stopBeat s beat` stoppt gezielt diese Aufnahme. `Sound.contactInterrupted s beat` meldet einen vorzeitigen Abbruch bis zum nächsten erfolgreichen Start desselben Beats.

| F#-Funktion | Wann aufrufen | Verwendete Assets (`public/assets/sfx/`) |
|---|---|---|
| `Sound.cargoPickup s` | Erz / Kristall eingesammelt (`Salvage.update`) | `cargo_pickup.ogg` |
| `Sound.tradeConfirm s` | Kauf / Verkauf / Upgrade erfolgreich | `trade_confirm.ogg` |
| `Sound.uiError s` | Dock gesperrt, zu wenig Credits, Frachtraum voll | `ui_error.ogg` |
| `Sound.dockOpen s` | Beim Andocken in Hangar (`showDock`) | `docking_clank.mp3` + `airlock_open.ogg` |
| `Sound.undock s` | Beim Abdocken aus Hangar (`departShop`) | `undock_release.ogg` + `airlock_close.ogg` |
| `Sound.jumpGate s` | Bei Sprungtor-Aktivierung / Sektorwechsel | `jump_charge.ogg` |
| `Sound.lowHullWarn s` | Bei niedriger Hülle (hat intern 6s-Cooldown!) | `low_hull_warn.ogg` |
| `Sound.turretFire s dist` | Wenn Auto-Turret feuert (mit Distanzdämpfung) | `turret_0.ogg` .. `turret_2.ogg` |
| `Sound.scannerPing s` | Sektor-Wechsel / Scan | `scanner_ping.ogg` |
| `Sound.uiConfirm s` | Dezente Cockpit-/Menü-Quittung | `ui_confirm.ogg` |
| `Sound.capitalHorn s dist` | Tiefes W40K-Warhorn bei Großschiff-/Kreuzer-Auftritt (8s Cooldown) | `capital_horn.mp3` + `deep_thunder.mp3` |
| `Sound.macroCannon s dist` | Schwere Makro-/Lanzenbatterie großer Schiffe | `macro_cannon_0..1.ogg` + `exp_sub_0` |
| `Sound.capitalExplosion s dist` | 4-stufige Großschiff-Zerstörung (Implosion, Donner, reißendes Metall, sterbendes Horn) | `explosion.mp3`, `deep_thunder.mp3`, `capital_horn.mp3`, `explosionCrunch_*`, `impactMetal_*` |
| `Sound.shipyardEnter s` | Einfahrt / Andocken an Werft (*Kepler-07*, *Morgenlicht*) | `docking_clank.mp3`, `shipyard_crane_0.ogg`, `capital_engine_0.ogg` |
| `Sound.shipyardCrane s` | Mechanische Werft-Kräne / Dockklammern | `shipyard_crane_0..1.ogg` |
| `Sound.capitalComm s kind` | Flotten-/Werft-Vox (`0` = Kreuzer-Ausfahrt, `1` = Werft-Dockklammern, `2` = *AETHER Sovereign*, `3` = Makro-Salve) | `comm_capital_inbound.mp3`, `comm_shipyard_bay.mp3`, `comm_sovereign_arrive.mp3`, `comm_macro_salvo.mp3` |
| `Sound.printerTick s` | Neuer Fundbericht nach erfolgreicher Reliktbergung | `printer_tick.ogg` |
| `Sound.ecmJam s` | Geister-Protokoll mit `[V]` erfolgreich ausgelöst | `ecm_jam_0..1.ogg` |
| `Sound.dockJam s` | Andockversuch mit `[B]` an einer wegen Kampf gesperrten Station im Andockradius | `dock_jam.ogg` |
| `Sound.collisionImpact s pos intensity isPlayer` | Rammstoß gegen Asteroiden oder Schiffe (`CollisionImpact`) | `impactMetal_*`, `hull_creak.ogg`, `lowFrequency_explosion_000.ogg` |
| `Sound.mineralCrack s pos` | Ersttreffer bricht Erzvorkommen im Asteroiden auf (`MineralReleased`) | `cargo_pickup.ogg`, `hull_arc_0.ogg` |
| `Sound.capitalHit s pos armour gunDestroyed` | Lasertreffer auf Panzerung oder Geschütze des Zollkreuzers (`CapitalHit`) | `impactMetal_*`, `explosion.mp3`, `explosionCrunch_*` |
| `Sound.overdriveOn / shieldBreak / shieldRecharge` | 3x-Kombo-Overdrive, Schildzusammenbruch, beginnende Schildregeneration | `thruster_2.ogg`, `forceField_*`, `hull_arc_0.ogg`, `comm_shield_down.mp3` |
| `Sound.boostEmpty / boostReady / macroCharge` | Booster-Überhitzung/Entriegelung, Aufladen schwerer Kreuzer-/Bomber-Salven | `retro_thrust.ogg`, `ui_error.ogg`, `ui_confirm.ogg`, `jump_charge.ogg` |
| `Sound.refit / torpedoImpact / uiClick` | Schiffsrumpf-/Kit-Umbau, Torpedo-Rumpftreffer, dezentes Cockpit-/Tab-Relais | `shipyard_crane_*`, `docking_clank.mp3`, `explosion.mp3`, `ui_confirm.ogg` |
