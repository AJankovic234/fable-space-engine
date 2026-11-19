namespace SpaceEngine

// =====================================================================================
//  Sound.fs  -  Sample-basierte Stereo-Web-Audio-Engine (ohne 08/15-Gepiepse)
//
//  Laedt echte Sci-Fi-Samples aus /assets/sfx/ (ElevenLabs + Godot_Signal_Lost-SFX)
//  einmalig beim Entsperren des AudioContext in den Speicher:
//    * Nahtlose Equal-Power-Loops: Reaktor-Triebwerk + Space-/Hangar-Ambience
//    * Stereo-Panning, Delay + prozeduraler Stereo-Convolver-Reverb & Ambience-Ducking
//    * Boost-Zuendung (Thruster), 5 Schild-/Metall-Varianten & geschichtete Explosionen
//    * Master-DynamicsCompressor gegen Clipping; ohne geladenes Sample bleibt es stumm.
// =====================================================================================

open System
open Fable.Core

type SoundEngine =
    { mutable Handle: obj
      mutable Muted: bool }

[<RequireQualifiedAccess>]
module Sound =

    [<Emit("""(() => {
        const win = typeof window !== "undefined" ? window : globalThis;
        const Ctx = win && (win.AudioContext || win.webkitAudioContext);
        if (!Ctx) return null;
        let ctx = null;
        let master = null;
        let sfxBus = null;
        let ambienceBus = null;
        let compressor = null;
        let echoSend = null;
        const buffers = Object.create(null);
        const loads = Object.create(null);
        const pendingSamples = new Map();
        let audioGeneration = 0;
        let muted = false;

        const AUDIO_KEY = "space2601.audio";
        function clamp01(v) {
            const n = Number(v);
            return Number.isFinite(n) ? Math.max(0.0, Math.min(1.0, n)) : 1.0;
        }
        const vol = { master: 1.0, sfx: 1.0, radio: 1.0, ambience: 1.0 };
        try {
            const raw = win.localStorage && win.localStorage.getItem(AUDIO_KEY);
            if (raw) {
                const parsed = JSON.parse(raw);
                if (parsed && typeof parsed === "object") {
                    vol.master = clamp01(parsed.master);
                    vol.sfx = clamp01(parsed.sfx);
                    vol.radio = clamp01(parsed.radio);
                    vol.ambience = clamp01(parsed.ambience);
                }
            }
        } catch {}

        let engSource = null;
        let engFilter = null;
        let engGain = null;
        let spaceSource = null;
        let spaceGain = null;
        let hangarSource = null;
        let hangarGain = null;
        let hangarCompSource = null;
        let hangarCompGain = null;
        let boostSource = null;
        let boostFilter = null;
        let boostGain = null;
        let nebulaSource = null;
        let nebulaGain = null;
        let cockpitSource = null;
        let cockpitGain = null;
        let capitalAmbSource = null;
        let capitalAmbGain = null;
        let gateHumSource = null;
        let gateHumFilter = null;
        let gateHumGain = null;
        let gunSide = 1;
        let wasBoosting = false;
        let prevThrottle = 0;
        let lastBrakeAt = -99;
        let duckUntil = 0;
        let radioBus = null;
        let lastRadioAt = -99;
        let radioVoiceEndsAt = 0;
        let radioSource = null;
        let contactSource = null;
        let activeBeat = -1;
        const interruptedBeats = new Set();
        let pendingCapital = -1;
        let receiverChannels = null;
        let radioLevel = null;
        let radioOut = null;
        let nextChatterAt = -1;
        let lastChatterIdx = -1;
        let lastHullWarnAt = -99;
        let lastHullCriticalAt = -99;
        let lastDockJamAt = -99;
        let lastShieldDownAt = -99;
        let lastHornAt = -99;
        let lastCollisionAt = -99;
        let lastMacroChargeAt = -99;
        let lastMineralCrackAt = -99;
        let lastCapitalHitAt = -99;
        const stationAnnounced = Object.create(null);

        const listener = { px: 0, py: 0, pz: 0, rx: 1, ry: 0, rz: 0, fx: 0, fy: 0, fz: -1 };
        const flightCtx = { hull: 0, kit: 0, sector: 0, cockpit: false, outpostKind: 0, gateProx: 0, cruiserNear: false, mode: -1 };

        const FILES = {
            laser_player: "/assets/sfx/laser_player.mp3",
            laser_enemy:  "/assets/sfx/laser_enemy.mp3",
            explosion:    "/assets/sfx/explosion.mp3",
            docking:      "/assets/sfx/docking_clank.mp3",
            engine:       "/assets/sfx/engine_hum.mp3",
            space_amb:    "/assets/sfx/space_ambience.mp3",
            hangar_amb:   "/assets/sfx/hangar_ambience.mp3",
            hull:         "/assets/sfx/eva_arc_contact.ogg",
            shield_0:     "/assets/sfx/forceField_000.ogg",
            shield_1:     "/assets/sfx/forceField_001.ogg",
            shield_2:     "/assets/sfx/forceField_002.ogg",
            shield_3:     "/assets/sfx/forceField_003.ogg",
            shield_4:     "/assets/sfx/forceField_004.ogg",
            exp_crunch_0: "/assets/sfx/explosionCrunch_000.ogg",
            exp_crunch_1: "/assets/sfx/explosionCrunch_001.ogg",
            exp_crunch_2: "/assets/sfx/explosionCrunch_002.ogg",
            exp_crunch_3: "/assets/sfx/explosionCrunch_003.ogg",
            exp_crunch_4: "/assets/sfx/explosionCrunch_004.ogg",
            exp_sub_0:    "/assets/sfx/lowFrequency_explosion_000.ogg",
            exp_sub_1:    "/assets/sfx/lowFrequency_explosion_001.ogg",
            metal_0:      "/assets/sfx/impactMetal_000.ogg",
            metal_1:      "/assets/sfx/impactMetal_001.ogg",
            metal_2:      "/assets/sfx/impactMetal_002.ogg",
            metal_3:      "/assets/sfx/impactMetal_003.ogg",
            metal_4:      "/assets/sfx/impactMetal_004.ogg",
            thruster_0:   "/assets/sfx/thrusterFire_000.ogg",
            thruster_1:   "/assets/sfx/thrusterFire_001.ogg",
            thruster_2:   "/assets/sfx/thrusterFire_002.ogg",
            comm_kestrel: "/assets/sfx/comm_kestrel.mp3",
            comm_helios:  "/assets/sfx/comm_helios.mp3",
            comm_bazaar:  "/assets/sfx/comm_bazaar.mp3",
            comm_kepler:  "/assets/sfx/comm_kepler.mp3",
            comm_aether:  "/assets/sfx/comm_aether.mp3",
            comm_bastion: "/assets/sfx/comm_bastion.mp3",
            comm_ready:   "/assets/sfx/comm_ready.mp3",
            comm_blocked: "/assets/sfx/comm_blocked.mp3",
            radio_squelch:"/assets/sfx/radio_squelch.ogg",
            jump_charge:  "/assets/sfx/jump_charge.ogg",
            airlock_open: "/assets/sfx/airlock_open.ogg",
            airlock_close:"/assets/sfx/airlock_close.ogg",
            cargo_pickup: "/assets/sfx/cargo_pickup.ogg",
            trade_confirm:"/assets/sfx/trade_confirm.ogg",
            ui_error:     "/assets/sfx/ui_error.ogg",
            undock:       "/assets/sfx/undock_release.ogg",
            target_lock:  "/assets/sfx/target_lock.ogg",
            turret_0:     "/assets/sfx/turret_0.ogg",
            turret_1:     "/assets/sfx/turret_1.ogg",
            turret_2:     "/assets/sfx/turret_2.ogg",
            low_hull_warn:"/assets/sfx/low_hull_warn.ogg",
            chatter_0:    "/assets/sfx/chatter_0.mp3",
            chatter_1:    "/assets/sfx/chatter_1.mp3",
            chatter_2:    "/assets/sfx/chatter_2.mp3",
            chatter_3:    "/assets/sfx/chatter_3.mp3",
            chatter_4:    "/assets/sfx/chatter_4.mp3",
            chatter_5:    "/assets/sfx/chatter_5.mp3",
            chatter_6:    "/assets/sfx/chatter_6.mp3",
            chatter_7:    "/assets/sfx/chatter_7.mp3",
            chatter_8:    "/assets/sfx/chatter_8.mp3",
            chatter_9:    "/assets/sfx/chatter_9.mp3",
            comm_pirate_ambush: "/assets/sfx/comm_pirate_ambush.mp3",
            comm_sector_clear:  "/assets/sfx/comm_sector_clear.mp3",
            comm_shield_down:   "/assets/sfx/comm_shield_down.mp3",
            comm_hull_critical: "/assets/sfx/comm_hull_critical.mp3",
            afterburner:        "/assets/sfx/afterburner.ogg",
            hangar_comp:        "/assets/sfx/hangar_computer.mp3",
            laser_heavy_0:      "/assets/sfx/laser_heavy_0.ogg",
            laser_heavy_1:      "/assets/sfx/laser_heavy_1.ogg",
            laser_heavy_2:      "/assets/sfx/laser_heavy_2.ogg",
            nebula_drone:       "/assets/sfx/nebula_drone.ogg",
            cockpit_hum:        "/assets/sfx/cockpit_hum.mp3",
            retro_thrust:       "/assets/sfx/retro_thrust.ogg",
            hull_arc_0:         "/assets/sfx/hull_arc_0.ogg",
            hull_arc_1:         "/assets/sfx/hull_arc_1.ogg",
            scanner_ping:       "/assets/sfx/scanner_ping.ogg",
            drift_tug:          "/assets/sfx/drift_tug.wav",
            drift_handset:      "/assets/sfx/drift_handset.wav",
            ui_confirm:         "/assets/sfx/ui_confirm.ogg",
            chatter_10:         "/assets/sfx/chatter_10.mp3",
            chatter_11:         "/assets/sfx/chatter_11.mp3",
            chatter_12:         "/assets/sfx/chatter_12.mp3",
            chatter_13:         "/assets/sfx/chatter_13.mp3",
            chatter_14:         "/assets/sfx/chatter_14.mp3",
            chatter_15:         "/assets/sfx/chatter_15.mp3",
            comm_wave_start:    "/assets/sfx/comm_wave_start.mp3",
            comm_wave_clear:    "/assets/sfx/comm_wave_clear.mp3",
            comm_upgrade_ready: "/assets/sfx/comm_upgrade_ready.mp3",
            comm_trade_profit:  "/assets/sfx/comm_trade_profit.mp3",
            comm_jump_ready:    "/assets/sfx/comm_jump_ready.mp3",
            comm_auto_turret:   "/assets/sfx/comm_auto_turret.mp3",
            story_act1_voss:    "/assets/sfx/story_act1_voss.mp3",
            story_act1_tg9:     "/assets/sfx/story_act1_tg9.mp3",
            story_act2_lind:    "/assets/sfx/story_act2_lind.mp3",
            story_act2_voss:    "/assets/sfx/story_act2_voss.mp3",
            story_act3_lind:    "/assets/sfx/story_act3_lind.mp3",
            story_act3_tg9:     "/assets/sfx/story_act3_tg9.mp3",
            story_vance_intro:  "/assets/sfx/story_vance_intro.mp3",
            story_kipper_drift: "/assets/sfx/story_kipper_drift.mp3",
            story_vance_taunt:  "/assets/sfx/story_vance_taunt.mp3",
            story_climax_voss:  "/assets/sfx/story_climax_voss.mp3",
            story_climax_vance_rage: "/assets/sfx/story_climax_vance_rage.mp3",
            story_climax_lind_win:   "/assets/sfx/story_climax_lind_win.mp3",
            capital_engine_0:        "/assets/sfx/capital_engine_0.ogg",
            capital_engine_1:        "/assets/sfx/capital_engine_1.ogg",
            shipyard_crane_0:        "/assets/sfx/shipyard_crane_0.ogg",
            shipyard_crane_1:        "/assets/sfx/shipyard_crane_1.ogg",
            macro_cannon_0:          "/assets/sfx/macro_cannon_0.ogg",
            macro_cannon_1:          "/assets/sfx/macro_cannon_1.ogg",
            capital_alarm:           "/assets/sfx/capital_alarm.mp3",
            deep_thunder:            "/assets/sfx/deep_thunder.mp3",
            capital_horn:            "/assets/sfx/capital_horn.mp3",
            comm_capital_inbound:    "/assets/sfx/comm_capital_inbound.mp3",
            comm_shipyard_bay:       "/assets/sfx/comm_shipyard_bay.mp3",
            comm_sovereign_arrive:   "/assets/sfx/comm_sovereign_arrive.mp3",
            comm_macro_salvo:        "/assets/sfx/comm_macro_salvo.mp3",
            dock_jam:                "/assets/sfx/dock_jam.ogg",
            printer_tick:            "/assets/sfx/printer_tick.ogg",
            ecm_jam_0:               "/assets/sfx/ecm_jam_0.ogg",
            ecm_jam_1:               "/assets/sfx/ecm_jam_1.ogg",
            hull_creak:              "/assets/sfx/hull_creak.ogg",
            story_helios_orlov:      "/assets/sfx/story_helios_orlov.mp3",
            story_letter_orlov:      "/assets/sfx/story_letter_orlov.mp3",
            story_letter_voss:       "/assets/sfx/story_letter_voss.mp3",
            story_letter_lind:       "/assets/sfx/story_letter_lind.mp3",
            story_sera_ghosts:       "/assets/sfx/story_sera_ghosts.mp3",
            story_halden_blackbox:   "/assets/sfx/story_halden_blackbox.mp3"
        };

        const STATION_COMM_KEYS = {
            "station-kestrel": "comm_kestrel",
            "station-helios":  "comm_helios",
            "station-bazaar":  "comm_bazaar",
            "station-kepler":  "comm_kepler",
            "station-aether":  "comm_aether",
            "station-bastion": "comm_bastion"
        };

        const STORY_KEYS = [
            "story_act1_voss", "story_act1_tg9", "story_act2_lind", "story_act2_voss",
            "story_act3_lind", "story_act3_tg9", "story_vance_intro", "story_kipper_drift",
            "story_vance_taunt", "story_climax_voss", "story_climax_vance_rage", "story_climax_lind_win",
            "story_helios_orlov", "story_letter_orlov", "story_letter_voss", "story_letter_lind",
            "story_sera_ghosts", "story_halden_blackbox"
        ];
        const CAPITAL_KEYS = ["comm_capital_inbound", "comm_shipyard_bay", "comm_sovereign_arrive", "comm_macro_salvo"];

        function createReverbImpulse(c, seconds) {
            const len = Math.max(1, Math.floor(c.sampleRate * seconds));
            const ir = c.createBuffer(2, len, c.sampleRate);
            for (let ch = 0; ch < 2; ch++) {
                const data = ir.getChannelData(ch);
                let lp = 0;
                for (let i = 0; i < len; i++) {
                    const t = i / len;
                    const env = Math.pow(1 - t, 2.7) * (i < 96 ? i / 96 : 1);
                    const white = Math.random() * 2 - 1;
                    lp = lp * 0.68 + white * 0.32;
                    data[i] = lp * env;
                }
            }
            return ir;
        }

        function startEngineLoop() {
            if (!ctx || !buffers.engine || engSource) return;
            engSource = ctx.createBufferSource();
            engSource.buffer = buffers.engine;
            engSource.loop = true;
            engSource.playbackRate.value = 0.86;
            engSource.connect(engFilter);
            engSource.start();
        }

        function startAmbienceLoop(key, targetGain) {
            if (!ctx || !buffers[key]) return null;
            const src = ctx.createBufferSource();
            src.buffer = buffers[key];
            src.loop = true;
            src.connect(targetGain);
            src.start();
            return src;
        }

        function loadBuffer(key) {
            if (!loads[key]) {
                loads[key] = fetch(FILES[key])
                    .then((r) => (r.ok ? r.arrayBuffer() : null))
                    .then((ab) => (ab ? ctx.decodeAudioData(ab) : null))
                    .then((buf) => {
                        buffers[key] = buf;
                        if (!buf) return;
                        if (key === "engine") startEngineLoop();
                        else if (key === "space_amb" && !spaceSource) spaceSource = startAmbienceLoop("space_amb", spaceGain);
                        else if (key === "hangar_amb" && !hangarSource) hangarSource = startAmbienceLoop("hangar_amb", hangarGain);
                        else if (key === "hangar_comp" && !hangarCompSource) hangarCompSource = startAmbienceLoop("hangar_comp", hangarCompGain);
                        else if (key === "nebula_drone" && !nebulaSource) nebulaSource = startAmbienceLoop("nebula_drone", nebulaGain);
                        else if (key === "cockpit_hum" && !cockpitSource) cockpitSource = startAmbienceLoop("cockpit_hum", cockpitGain);
                        else if (key === "capital_engine_0" && !capitalAmbSource && capitalAmbGain) {
                            capitalAmbSource = startAmbienceLoop("capital_engine_0", capitalAmbGain);
                            if (capitalAmbSource) capitalAmbSource.playbackRate.value = 0.72;
                        }
                        else if (key === "capital_engine_1" && !gateHumSource && gateHumFilter) {
                            gateHumSource = ctx.createBufferSource();
                            gateHumSource.buffer = buf;
                            gateHumSource.loop = true;
                            gateHumSource.playbackRate.value = 1.35;
                            gateHumSource.connect(gateHumFilter);
                            gateHumSource.start();
                        }
                        else if (key === "afterburner" && !boostSource && boostFilter) {
                            boostSource = ctx.createBufferSource();
                            boostSource.buffer = buf;
                            boostSource.loop = true;
                            boostSource.playbackRate.value = 0.96;
                            boostSource.connect(boostFilter);
                            boostSource.start();
                        }
                    })
                    .catch(() => { buffers[key] = null; });
            }
            return loads[key];
        }

        function applyVolumesNow(now) {
            if (!ctx) return;
            const t = now !== undefined ? now : ctx.currentTime;
            if (master) master.gain.setTargetAtTime(muted ? 0.0 : 0.56 * vol.master, t, 0.03);
            if (sfxBus) sfxBus.gain.setTargetAtTime(vol.sfx, t, 0.03);
            if (radioOut) radioOut.gain.setTargetAtTime(vol.radio, t, 0.03);
            if (ambienceBus) ambienceBus.gain.setTargetAtTime(vol.ambience, t, 0.03);
        }

        function ensure() {
            if (!ctx) {
                ctx = new Ctx();
                compressor = ctx.createDynamicsCompressor();
                compressor.threshold.value = -14;
                compressor.knee.value = 18;
                compressor.ratio.value = 6;
                compressor.attack.value = 0.005;
                compressor.release.value = 0.16;

                master = ctx.createGain();
                master.gain.value = muted ? 0.0 : 0.56 * vol.master;
                master.connect(compressor);
                compressor.connect(ctx.destination);

                sfxBus = ctx.createGain();
                sfxBus.gain.value = vol.sfx;
                sfxBus.connect(master);

                ambienceBus = ctx.createGain();
                ambienceBus.gain.value = vol.ambience;
                ambienceBus.connect(master);

                // Dezenter Stereo-Echo- + prozeduraler Convolver-Reverb-Bus fuer Raumtiefe
                const delay = ctx.createDelay(0.5);
                delay.delayTime.value = 0.135;
                const fb = ctx.createGain();
                fb.gain.value = 0.22;
                const echoTone = ctx.createBiquadFilter();
                echoTone.type = "lowpass";
                echoTone.frequency.value = 1750;
                echoSend = ctx.createGain();
                echoSend.gain.value = 0.18;
                echoSend.connect(delay);
                delay.connect(echoTone);
                echoTone.connect(fb);
                fb.connect(delay);
                echoTone.connect(master);

                if (typeof ctx.createConvolver === "function") {
                    const convolver = ctx.createConvolver();
                    convolver.buffer = createReverbImpulse(ctx, 0.65);
                    const revTone = ctx.createBiquadFilter();
                    revTone.type = "lowpass";
                    revTone.frequency.value = 2200;
                    const revGain = ctx.createGain();
                    revGain.gain.value = 0.28;
                    echoSend.connect(convolver);
                    convolver.connect(revTone);
                    revTone.connect(revGain);
                    revGain.connect(master);
                }

                engGain = ctx.createGain();
                engGain.gain.value = 0.0;
                engFilter = ctx.createBiquadFilter();
                engFilter.type = "lowpass";
                engFilter.frequency.value = 160;
                engFilter.Q.value = 1.1;
                engFilter.connect(engGain);
                engGain.connect(ambienceBus);

                spaceGain = ctx.createGain();
                spaceGain.gain.value = 0.0;
                spaceGain.connect(ambienceBus);

                hangarGain = ctx.createGain();
                hangarGain.gain.value = 0.0;
                hangarGain.connect(ambienceBus);

                hangarCompGain = ctx.createGain();
                hangarCompGain.gain.value = 0.0;
                const hangarCompLp = ctx.createBiquadFilter();
                hangarCompLp.type = "lowpass";
                hangarCompLp.frequency.value = 2400;
                hangarCompGain.connect(hangarCompLp);
                hangarCompLp.connect(ambienceBus);

                boostGain = ctx.createGain();
                boostGain.gain.value = 0.0;
                boostFilter = ctx.createBiquadFilter();
                boostFilter.type = "lowpass";
                boostFilter.frequency.value = 880;
                boostFilter.Q.value = 0.9;
                boostFilter.connect(boostGain);
                boostGain.connect(ambienceBus);

                nebulaGain = ctx.createGain();
                nebulaGain.gain.value = 0.0;
                const nebulaLp = ctx.createBiquadFilter();
                nebulaLp.type = "lowpass";
                nebulaLp.frequency.value = 920;
                nebulaGain.connect(nebulaLp);
                nebulaLp.connect(ambienceBus);

                cockpitGain = ctx.createGain();
                cockpitGain.gain.value = 0.0;
                const cockpitLp = ctx.createBiquadFilter();
                cockpitLp.type = "lowpass";
                cockpitLp.frequency.value = 1350;
                cockpitGain.connect(cockpitLp);
                cockpitLp.connect(ambienceBus);

                capitalAmbGain = ctx.createGain();
                capitalAmbGain.gain.value = 0.0;
                const capitalAmbLp = ctx.createBiquadFilter();
                capitalAmbLp.type = "lowpass";
                capitalAmbLp.frequency.value = 340;
                capitalAmbGain.connect(capitalAmbLp);
                capitalAmbLp.connect(ambienceBus);

                gateHumGain = ctx.createGain();
                gateHumGain.gain.value = 0.0;
                gateHumFilter = ctx.createBiquadFilter();
                gateHumFilter.type = "bandpass";
                gateHumFilter.frequency.value = 540;
                gateHumFilter.Q.value = 1.4;
                gateHumFilter.connect(gateHumGain);
                gateHumGain.connect(ambienceBus);

                // Funk-Filter-Bus (360-2850 Hz Cockpit-Bandpass + dezenter Raumanteil)
                const radioHp = ctx.createBiquadFilter();
                radioHp.type = "highpass";
                radioHp.frequency.value = 360;
                radioHp.Q.value = 0.85;
                const radioLp = ctx.createBiquadFilter();
                radioLp.type = "lowpass";
                radioLp.frequency.value = 2850;
                radioLp.Q.value = 1.05;
                const radioGain = ctx.createGain();
                radioGain.gain.value = 0.92;
                radioOut = ctx.createGain();
                radioOut.gain.value = vol.radio;
                radioHp.connect(radioLp);
                radioLp.connect(radioGain);
                radioGain.connect(radioOut);
                radioOut.connect(master);
                const radioEcho = ctx.createGain();
                radioEcho.gain.value = 0.14;
                radioOut.connect(radioEcho);
                radioEcho.connect(echoSend);
                radioBus = radioHp;
                radioLevel = radioGain;

                // Dauerquellen und häufiges erstes Feedback vorbereiten. Alle anderen Samples lädt ihr Ereignis.
                for (const key of ["engine", "space_amb", "hangar_amb", "hangar_comp", "nebula_drone", "cockpit_hum",
                                   "afterburner", "radio_squelch", "laser_player", "laser_enemy", "ui_confirm"]) {
                    loadBuffer(key);
                }
            }
            if (ctx.state === "suspended") {
                ctx.resume().catch(() => {});
            }
            return ctx;
        }

        function playSample(key, volume, rate, cutoffHz, pan, wet, delaySec, highpassHz) {
            const c = ensure();
            const buf = buffers[key];
            if (!c || volume <= 0.01 || buf === null) return;
            if (!buf) {
                // Einen noch ladenden Einschlag einmal nachholen, ohne Feuerstöße aufzustauen.
                const tag = key + ":" + (delaySec || 0);
                if (!pendingSamples.has(tag)) {
                    const generation = audioGeneration;
                    pendingSamples.set(tag, generation);
                    loadBuffer(key).then(() => {
                        if (pendingSamples.get(tag) !== generation) return;
                        pendingSamples.delete(tag);
                        if (buffers[key]) playSample(key, volume, rate, cutoffHz, pan, wet, delaySec, highpassHz);
                    });
                }
                return;
            }
            const src = c.createBufferSource();
            src.buffer = buf;
            src.playbackRate.value = Math.max(0.25, Math.min(3.0, rate || 1.0));

            let tail = src;
            if (highpassHz && highpassHz > 20) {
                const hp = c.createBiquadFilter();
                hp.type = "highpass";
                hp.frequency.value = highpassHz;
                tail.connect(hp);
                tail = hp;
            }
            if (cutoffHz && cutoffHz < 14000) {
                const flt = c.createBiquadFilter();
                flt.type = "lowpass";
                flt.frequency.value = cutoffHz;
                tail.connect(flt);
                tail = flt;
            }

            const gain = c.createGain();
            gain.gain.value = volume;
            tail.connect(gain);

            let outNode = gain;
            if (pan && Math.abs(pan) > 0.01 && typeof c.createStereoPanner === "function") {
                const panner = c.createStereoPanner();
                panner.pan.value = Math.max(-0.9, Math.min(0.9, pan));
                gain.connect(panner);
                outNode = panner;
            }

            outNode.connect(sfxBus || master);
            if (wet && echoSend) {
                const sendGain = c.createGain();
                sendGain.gain.value = wet * vol.sfx;
                outNode.connect(sendGain);
                sendGain.connect(echoSend);
            }
            src.start(c.currentTime + (delaySec || 0));
        }

        function spatialize(x, y, z, maxDist, minAtten) {
            const dx = x - listener.px;
            const dy = y - listener.py;
            const dz = z - listener.pz;
            const dist = Math.hypot(dx, dy, dz);
            const atten = dist <= 0.001 ? 1.0 : Math.max(minAtten || 0.0, 1.0 - dist / maxDist);
            if (dist <= 0.5) return { atten, pan: 0.0, cutoffMul: 1.0 };
            const inv = 1.0 / dist;
            const nx = dx * inv, ny = dy * inv, nz = dz * inv;
            const dotR = nx * listener.rx + ny * listener.ry + nz * listener.rz;
            const dotF = nx * listener.fx + ny * listener.fy + nz * listener.fz;
            const pan = Math.max(-0.88, Math.min(0.88, dotR));
            const cutoffMul = 0.70 + 0.30 * (dotF * 0.5 + 0.5);
            return { atten, pan, cutoffMul };
        }

        function jitter(span) {
            return 1.0 + (Math.random() * 2 - 1) * span;
        }

        function setReceiver(signal) {
            if (!ctx) return;
            const now = ctx.currentTime;
            if (signal < 0) {
                if (!receiverChannels) return;
                for (const {src, gain} of receiverChannels) {
                    gain.gain.setTargetAtTime(0, now, 0.018);
                    src.stop(now + 0.08);
                }
                receiverChannels = null;
                radioLevel.gain.setTargetAtTime(0.92, now, 0.12);
                return;
            }
            if (!receiverChannels) {
                if (!buffers.drift_tug || !buffers.drift_handset) {
                    loadBuffer("drift_tug");
                    loadBuffer("drift_handset");
                    return;
                }
                receiverChannels = ["drift_tug", "drift_handset"].map((key, i) => {
                    const src = ctx.createBufferSource();
                    src.buffer = buffers[key];
                    src.loop = true;
                    const gain = ctx.createGain();
                    gain.gain.value = 0;
                    src.connect(gain);
                    if (typeof ctx.createStereoPanner === "function") {
                        const pan = ctx.createStereoPanner();
                        pan.pan.value = i ? 0.24 : -0.36;
                        gain.connect(pan);
                        pan.connect(radioOut || master);
                    } else gain.connect(radioOut || master);
                    src.start(now);
                    return {src, gain};
                });
                radioLevel.gain.setTargetAtTime(0.19, now, 0.12);
            }
            receiverChannels[0].gain.gain.setTargetAtTime(0.62 - 0.59 * signal, now, 0.10);
            receiverChannels[1].gain.gain.setTargetAtTime(0.26 + 0.46 * signal, now, 0.10);
        }

        function stopContactBeat(beat = -1) {
            if (contactSource && (beat < 0 || activeBeat === beat)) {
                interruptedBeats.add(activeBeat);
                contactSource.stop();
                contactSource = null;
                activeBeat = -1;
            }
        }

        function clearRadio() {
            audioGeneration++;
            pendingSamples.clear();
            pendingCapital = -1;
            stopContactBeat();
            if (radioSource) radioSource.stop();
            radioSource = null;
            radioVoiceEndsAt = 0;
            lastRadioAt = -99;
        }

        function playRadioClip(key, vol, pan, squelchVol, beat = -1) {
            const c = ensure();
            const buf = buffers[key];
            if (!c || !radioBus || buf === null) return 0;
            if (!buf) {
                loadBuffer(key);
                return 0;
            }
            const now = c.currentTime;
            if (now - lastRadioAt < 5.0 || now < radioVoiceEndsAt) return 0;
            if (contactSource) {
                if (key === "comm_shield_down" || key === "comm_hull_critical" || key === "comm_pirate_ambush") {
                    stopContactBeat();
                } else {
                    return 0;
                }
            }

            if (beat < 0) {
                lastRadioAt = now;
                radioVoiceEndsAt = Math.max(radioVoiceEndsAt, now + 0.14 + buf.duration);
                duckUntil = Math.max(duckUntil, now + Math.min(6.0, buf.duration + 0.30));
            }
            if (spaceGain) spaceGain.gain.setTargetAtTime(0.022, now, 0.06);

            playSample("radio_squelch", squelchVol, jitter(0.04), 3600, pan || 0.08, 0.12, 0.0);

            const src = c.createBufferSource();
            src.buffer = buf;
            const gain = c.createGain();
            gain.gain.value = vol;
            src.connect(gain);

            if (pan && Math.abs(pan) > 0.01 && typeof c.createStereoPanner === "function") {
                const panner = c.createStereoPanner();
                panner.pan.value = Math.max(-0.7, Math.min(0.7, pan));
                gain.connect(panner);
                panner.connect(radioBus);
            } else {
                gain.connect(radioBus);
            }
            src.start(now + 0.14);
            if (beat >= 0) {
                activeBeat = beat;
                interruptedBeats.delete(beat);
                contactSource = src;
                src.onended = () => {
                    if (contactSource === src) {
                        contactSource = null;
                        activeBeat = -1;
                    }
                };
            } else {
                radioSource = src;
                src.onended = () => {
                    if (radioSource === src) radioSource = null;
                };
            }
            return 0.14 + buf.duration;
        }

        function requestRadioClip(key, vol, pan, squelchVol) {
            const c = ensure();
            if (!c || buffers[key] === null) return;
            if (buffers[key]) {
                playRadioClip(key, vol, pan, squelchVol);
            } else {
                const generation = audioGeneration;
                loadBuffer(key).then(() => {
                    if (generation === audioGeneration) playRadioClip(key, vol, pan, squelchVol);
                });
            }
        }

        function playCapitalCall(kind) {
            const key = CAPITAL_KEYS[kind];
            if (!key || !playRadioClip(key, kind === 2 ? 0.84 : 0.80, 0.0, 0.16)) return false;
            if (kind === 3) playSample("capital_alarm", 0.18, 0.88, 2600, 0.0, 0.35, 0.0);
            return true;
        }

        function maybePlayChatter(t) {
            if (receiverChannels) return;
            if (nextChatterAt < 0) {
                nextChatterAt = t + 14.0 + Math.random() * 8.0;
                return;
            }
            if (t < nextChatterAt || t - lastRadioAt < 9.0) return;
            nextChatterAt = t + 26.0 + Math.random() * 22.0;
            let idx = Math.floor(Math.random() * 16);
            if (idx === lastChatterIdx) idx = (idx + 1) % 16;
            const key = "chatter_" + idx;
            lastChatterIdx = idx;
            const pan = (Math.random() * 2 - 1) * 0.34;
            requestRadioClip(key, 0.46, pan, 0.11);
        }

        const HULL_ENGINE = [
            { rateBase: 0.84, throttleRate: 0.22, boostRate: 0.30, freqBase: 150, freqThrottle: 260, freqBoost: 560, gainScale: 1.0 },
            { rateBase: 0.68, throttleRate: 0.16, boostRate: 0.22, freqBase: 118, freqThrottle: 195, freqBoost: 410, gainScale: 1.12 },
            { rateBase: 1.02, throttleRate: 0.28, boostRate: 0.36, freqBase: 205, freqThrottle: 330, freqBoost: 680, gainScale: 0.94 },
            { rateBase: 0.93, throttleRate: 0.24, boostRate: 0.32, freqBase: 177, freqThrottle: 294, freqBoost: 610, gainScale: 0.98 }
        ];
        const SECTOR_AMBIENCE = [
            { space: 0.075, nebula: 0.025, cap: 0.0 },
            { space: 0.064, nebula: 0.038, cap: 0.020 },
            { space: 0.068, nebula: 0.044, cap: 0.0 },
            { space: 0.070, nebula: 0.028, cap: 0.024 },
            { space: 0.054, nebula: 0.062, cap: 0.0 },
            { space: 0.060, nebula: 0.046, cap: 0.028 },
            { space: 0.058, nebula: 0.042, cap: 0.0 },
            { space: 0.085, nebula: 0.037, cap: 0.0 },
            { space: 0.046, nebula: 0.080, cap: 0.0 }
        ];

        function playLaser(isPlayer, atten, pan, cutoffMul, overdrive, laserLevel, kit, enemyRole) {
            if (atten <= 0.03) return;
            const key = isPlayer ? "laser_player" : "laser_enemy";
            let rate = 1.0;
            let vol = (isPlayer ? 0.27 : 0.18) * Math.min(1.0, atten);
            let cutoff = isPlayer ? 15000 : (1800 + Math.min(1.0, atten) * 9000) * cutoffMul;
            if (isPlayer) {
                const lvl = Math.max(0, Math.min(3, laserLevel || 0));
                rate = (overdrive ? 1.12 : 1.0) * (kit === 2 ? 1.07 : 1.0) * (1.0 - lvl * 0.025) * jitter(0.05);
                vol *= (1.0 + lvl * 0.05);
            } else {
                const role = enemyRole === undefined ? -1 : enemyRole;
                if (role === 0) rate = 1.24;
                else if (role === 1) rate = 1.06;
                else if (role === 2) { rate = 0.82; vol *= 1.12; }
                else if (role === 3) { rate = 0.70; vol *= 1.22; }
                else if (role === 5) { rate = 0.96; vol *= 1.08; }
                rate *= jitter(0.06);
            }
            if (isPlayer && pan === 0) {
                gunSide = -gunSide;
                pan = gunSide * 0.22;
            }
            playSample(key, vol, rate, cutoff, pan, isPlayer ? 0.22 : 0.14, 0.0);
            if (isPlayer && (overdrive || (laserLevel || 0) >= 2 || kit === 2)) {
                const hidx = Math.floor(Math.random() * 3);
                const heavyGain = overdrive ? 0.44 : (kit === 2 ? 0.26 : 0.18 + (laserLevel - 2) * 0.08);
                playSample("laser_heavy_" + hidx, vol * heavyGain, (kit === 2 ? 1.04 : 0.92) * jitter(0.05), kit === 2 ? 2600 : 1850, pan, 0.18, 0.0);
            } else if (!isPlayer && (enemyRole === 2 || enemyRole === 3) && atten > 0.25) {
                const hidx = Math.floor(Math.random() * 3);
                playSample("laser_heavy_" + hidx, vol * 0.30, (enemyRole === 3 ? 0.74 : 0.86) * jitter(0.05), 1450 * cutoffMul, pan, 0.16, 0.0);
            }
        }

        function playHit(shield, isPlayer, pan, cutoffMul) {
            if (shield) {
                const idx = Math.floor(Math.random() * 5);
                playSample("shield_" + idx, isPlayer ? 0.34 : 0.18, jitter(0.05), 14000 * cutoffMul, pan, 0.30, 0.0);
            } else {
                const midx = Math.floor(Math.random() * 5);
                playSample("hull", isPlayer ? 0.36 : 0.18, (isPlayer ? 0.82 : 0.94) * jitter(0.06), 4500 * cutoffMul, pan, 0.20, 0.0);
                playSample("metal_" + midx, isPlayer ? 0.30 : 0.16, (isPlayer ? 0.78 : 0.96) * jitter(0.07), 5200 * cutoffMul, pan, 0.24, 0.012);
                if (isPlayer) {
                    const aidx = Math.floor(Math.random() * 2);
                    playSample("hull_arc_" + aidx, 0.22, jitter(0.06), 3900, (Math.random() * 2 - 1) * 0.25, 0.22, 0.025);
                    playSample("hull_creak", 0.20, 0.85 * jitter(0.06), 2400, pan, 0.24, 0.045);
                }
                if (isPlayer && ctx && ctx.currentTime - lastShieldDownAt >= 18.0) {
                    lastShieldDownAt = ctx.currentTime;
                    requestRadioClip("comm_shield_down", 0.72, 0.0, 0.16);
                }
            }
        }

        function playExplosion(scale, atten, pan, cutoffMul) {
            if (atten <= 0.03) return;
            const impact = scale * atten;
            if (ctx && impact > 0.45) {
                duckUntil = Math.max(duckUntil, ctx.currentTime + 0.36);
                if (spaceGain) spaceGain.gain.setTargetAtTime(0.022, ctx.currentTime, 0.04);
            }
            const rate = Math.max(0.65, Math.min(1.18, 1.12 - scale * 0.22)) * jitter(0.05);
            const vol = Math.min(0.62, (0.26 + scale * 0.22) * atten);
            const cutoff = (1200 + Math.min(1.0, atten) * 11000) * cutoffMul;
            const cidx = Math.floor(Math.random() * 5);
            playSample("explosion", vol, rate, cutoff, pan, 0.34, 0.0);
            playSample("exp_crunch_" + cidx, vol * 0.62, rate * jitter(0.06), cutoff * 0.95, pan * 0.85, 0.32, 0.028);
            if (impact > 0.35) {
                const sidx = Math.floor(Math.random() * 2);
                playSample("exp_sub_" + sidx, Math.min(0.55, vol * 0.72), 0.92 * jitter(0.05), 950, pan * 0.35, 0.18, 0.015);
            }
            if (scale > 1.05) {
                const midx = Math.floor(Math.random() * 5);
                playSample("metal_" + midx, vol * 0.40, 0.70 * jitter(0.06), 3400 * cutoffMul, pan * 0.6, 0.28, 0.065);
            }
        }

        return {
            ensure,
            setReceiver,
            clearRadio,
            setVolumes(m, s, r, a) {
                vol.master = clamp01(m);
                vol.sfx = clamp01(s);
                vol.radio = clamp01(r);
                vol.ambience = clamp01(a);
                try { win.localStorage && win.localStorage.setItem(AUDIO_KEY, JSON.stringify(vol)); } catch {}
                applyVolumesNow();
            },
            getVolumes() {
                return [vol.master, vol.sfx, vol.radio, vol.ambience];
            },
            setListener(px, py, pz, rx, ry, rz, fx, fy, fz) {
                listener.px = px; listener.py = py; listener.pz = pz;
                listener.rx = rx; listener.ry = ry; listener.rz = rz;
                listener.fx = fx; listener.fy = fy; listener.fz = fz;
            },
            setFlightContext(hull, kit, sector, cockpit, outpostKind, gateProx, cruiserNear) {
                flightCtx.hull = Math.max(0, Math.min(HULL_ENGINE.length - 1, hull || 0));
                flightCtx.kit = Math.max(0, Math.min(3, kit || 0));
                flightCtx.sector = Math.max(0, Math.min(SECTOR_AMBIENCE.length - 1, sector || 0));
                flightCtx.cockpit = !!cockpit;
                flightCtx.outpostKind = Math.max(0, Math.min(5, outpostKind || 0));
                flightCtx.gateProx = Math.max(0, Math.min(1, gateProx || 0));
                flightCtx.cruiserNear = !!cruiserNear;
            },
            setSceneMode(mode) {
                flightCtx.mode = mode;
            },
            setMuted(m) {
                muted = !!m;
                if (muted) clearRadio();
                else ensure();
                applyVolumesNow();
            },
            setEngine(active, throttle, boost) {
                if (!ctx || !engGain) return;
                if (pendingCapital >= 0 && (buffers[CAPITAL_KEYS[pendingCapital]] === null || playCapitalCall(pendingCapital))) {
                    pendingCapital = -1;
                }
                const t = ctx.currentTime;
                const ducked = t < duckUntil;
                const amb = SECTOR_AMBIENCE[flightCtx.sector] || SECTOR_AMBIENCE[0];
                const cockpitSpaceScale = flightCtx.cockpit ? 0.84 : 1.0;
                const mode = flightCtx.mode;
                const inSpace = active || mode === 2;
                const pausedScale = (!active && mode === 2) ? 0.35 : 1.0;
                const inHangar = !active && (mode === 3 || mode < 0);
                if (spaceGain) spaceGain.gain.setTargetAtTime(inSpace ? (ducked ? 0.022 : amb.space * cockpitSpaceScale * pausedScale) : 0.0, t, ducked ? 0.05 : 0.28);
                if (nebulaGain) nebulaGain.gain.setTargetAtTime(inSpace ? (ducked ? 0.012 : amb.nebula * pausedScale) : 0.0, t, ducked ? 0.06 : 0.35);
                if (cockpitGain) cockpitGain.gain.setTargetAtTime(active ? (flightCtx.cockpit ? 0.036 : 0.018) : 0.0, t, 0.28);

                const capWanted = active && (amb.cap > 0 || flightCtx.cruiserNear);
                if (capWanted && !buffers.capital_engine_0) loadBuffer("capital_engine_0");
                if (capitalAmbGain) {
                    const capTarget = capWanted ? (ducked ? 0.008 : Math.max(amb.cap, flightCtx.cruiserNear ? 0.024 : 0.0)) : 0.0;
                    capitalAmbGain.gain.setTargetAtTime(capTarget, t, 0.35);
                }
                const gateWanted = active && flightCtx.gateProx > 0.05;
                if (gateWanted && !buffers.capital_engine_1) loadBuffer("capital_engine_1");
                if (gateHumGain && gateHumFilter) {
                    gateHumGain.gain.setTargetAtTime(gateWanted ? flightCtx.gateProx * 0.042 : 0.0, t, 0.16);
                    gateHumFilter.frequency.setTargetAtTime(420 + flightCtx.gateProx * 480, t, 0.14);
                    if (gateHumSource) gateHumSource.playbackRate.setTargetAtTime(1.25 + flightCtx.gateProx * 0.45, t, 0.14);
                }

                const quietRelay = flightCtx.outpostKind === 3;
                const hangarTarget = inHangar ? (contactSource ? 0.018 : (quietRelay ? 0.034 : 0.050)) : 0.0;
                const hangarCompTarget = inHangar ? (contactSource ? 0.010 : (quietRelay ? 0.036 : 0.028)) : 0.0;
                if (hangarGain) hangarGain.gain.setTargetAtTime(hangarTarget, t, 0.28);
                if (hangarCompGain) hangarCompGain.gain.setTargetAtTime(hangarCompTarget, t, 0.30);

                if (!active || !engSource) {
                    wasBoosting = false;
                    prevThrottle = 0;
                    engGain.gain.setTargetAtTime(0.0, t, 0.08);
                    if (boostGain) boostGain.gain.setTargetAtTime(0.0, t, 0.08);
                    return;
                }
                const boosting = boost > 0.35;
                if (boosting && !wasBoosting) {
                    const tid = Math.floor(Math.random() * 3);
                    playSample("thruster_" + tid, flightCtx.kit === 1 ? 0.33 : 0.28, jitter(0.06), 2600, 0.0, 0.25, 0.0);
                }
                wasBoosting = boosting;

                if (prevThrottle > 0.25 && throttle < 0.06 && t - lastBrakeAt >= 2.2) {
                    lastBrakeAt = t;
                    playSample("retro_thrust", 0.18, jitter(0.05), 2900, 0.0, 0.20, 0.0);
                }
                prevThrottle = throttle;

                if (boostGain && boostSource) {
                    const boostAmp = flightCtx.kit === 1 ? 0.175 : 0.14;
                    const boostPitch = (flightCtx.kit === 1 ? 1.02 : 0.95) + boost * 0.22;
                    boostGain.gain.setTargetAtTime(boosting ? boostAmp : 0.0, t, boosting ? 0.09 : 0.14);
                    boostSource.playbackRate.setTargetAtTime(boostPitch, t, 0.10);
                }

                const hp = HULL_ENGINE[flightCtx.hull] || HULL_ENGINE[0];
                const filterTone = flightCtx.cockpit ? 0.84 : 1.08;
                const power = Math.min(1.6, throttle * 0.85 + boost * 0.95);
                engSource.playbackRate.setTargetAtTime(hp.rateBase + throttle * hp.throttleRate + boost * hp.boostRate, t, 0.08);
                engFilter.frequency.setTargetAtTime((hp.freqBase + throttle * hp.freqThrottle + boost * hp.freqBoost) * filterTone, t, 0.07);
                engGain.gain.setTargetAtTime((0.05 + power * 0.14) * hp.gainScale, t, 0.07);
                maybePlayChatter(t);
            },
            laser(isPlayer, atten, overdrive) {
                playLaser(isPlayer, atten, 0, 1.0, overdrive, 0, 0, -1);
            },
            laserAt(isPlayer, x, y, z, overdrive, laserLevel, kit, enemyRole) {
                if (isPlayer) {
                    playLaser(true, 1.0, 0, 1.0, overdrive, laserLevel, kit, -1);
                } else {
                    const sp = spatialize(x, y, z, 520.0, 0.0);
                    playLaser(false, sp.atten, sp.pan, sp.cutoffMul, false, 0, 0, enemyRole);
                }
            },
            hit(shield, isPlayer) {
                const pan = isPlayer ? 0.0 : (Math.random() * 2 - 1) * 0.35;
                playHit(shield, isPlayer, pan, 1.0);
            },
            hitAt(shield, isPlayer, x, y, z) {
                if (isPlayer) {
                    playHit(shield, true, 0.0, 1.0);
                } else {
                    const sp = spatialize(x, y, z, 560.0, 0.15);
                    playHit(shield, false, sp.pan, sp.cutoffMul);
                }
            },
            explosion(scale, atten) {
                const pan = (Math.random() * 2 - 1) * 0.52;
                playExplosion(scale, atten, pan, 1.0);
            },
            explosionAt(scale, x, y, z) {
                const sp = spatialize(x, y, z, 680.0, 0.05);
                playExplosion(scale, sp.atten, sp.pan, sp.cutoffMul);
            },
            cue(kind) {
                // Lieber gar kein UI-Ton als 08/15-Piepsen:
                // Zielwechsel (0) bleibt stumm; Sektor-Abschluss (2) nutzt sattes Docking-Einrasten.
                if (kind === 1) {
                    playSample("shield_0", 0.18, 0.78, 3200, 0.0, 0.35, 0.0);
                    requestRadioClip("comm_wave_start", 0.74, 0.0, 0.15);
                } else if (kind === 2) {
                    playSample("docking", 0.40, 1.0, 14000, 0.0, 0.45, 0.0);
                    requestRadioClip("comm_wave_clear", 0.74, 0.0, 0.15);
                } else if (kind === 3) {
                    playSample("explosion", 0.38, 0.62, 1600, 0.0, 0.40, 0.0);
                }
            },
            stationComm(stationId, kind) {
                const key =
                    kind === 0 ? STATION_COMM_KEYS[stationId] :
                    kind === 1 ? "comm_ready" :
                    kind === 2 ? "comm_blocked" : null;
                if (!key) return;
                const tag = stationId + ":" + kind;
                if (stationAnnounced[tag]) return;
                if (playRadioClip(key, 0.78, 0.06, 0.16)) {
                    stationAnnounced[tag] = true;
                }
            },
            radioEvent(kind) {
                if (kind === 0) {
                    requestRadioClip("comm_pirate_ambush", 0.80, 0.0, 0.18);
                } else if (kind === 1) {
                    requestRadioClip("comm_sector_clear", 0.76, 0.0, 0.16);
                } else if (kind === 2) {
                    let idx = Math.floor(Math.random() * 16);
                    if (idx === lastChatterIdx) idx = (idx + 1) % 16;
                    lastChatterIdx = idx;
                    requestRadioClip("chatter_" + idx, 0.50, (Math.random() * 2 - 1) * 0.32, 0.12);
                } else if (kind === 3) {
                    requestRadioClip("comm_shield_down", 0.76, 0.0, 0.16);
                } else if (kind === 4) {
                    const c = ensure();
                    if (!c || c.currentTime - lastHullCriticalAt < 25.0) return;
                    lastHullCriticalAt = c.currentTime;
                    requestRadioClip("comm_hull_critical", 0.78, 0.0, 0.18);
                } else if (kind === 5) {
                    requestRadioClip("comm_upgrade_ready", 0.76, 0.0, 0.15);
                } else if (kind === 6) {
                    requestRadioClip("comm_trade_profit", 0.74, 0.0, 0.15);
                } else if (kind === 7) {
                    requestRadioClip("comm_jump_ready", 0.76, 0.0, 0.16);
                } else if (kind === 8) {
                    requestRadioClip("comm_auto_turret", 0.76, 0.0, 0.15);
                }
            },
            storyBeat(beat) {
                const key = STORY_KEYS[beat];
                if (!key) return;
                requestRadioClip(key, 0.82, 0.0, 0.16);
            },
            tryContactBeat(beat) {
                const key = STORY_KEYS[beat];
                if (!key || !ctx || ctx.state === "closed" || buffers[key] === null) return -1;
                if (ctx.state !== "running") return 0;
                return playRadioClip(key, 0.82, 0.0, 0.16, beat);
            },
            stopContactBeat,
            contactInterrupted(beat) { return interruptedBeats.has(beat); },
            resetStationComm(stationId) {
                const prefix = stationId + ":";
                for (const k of Object.keys(stationAnnounced)) {
                    if (k.startsWith(prefix)) delete stationAnnounced[k];
                }
            },
            turret(atten) {
                if (atten <= 0.03) return;
                const tid = Math.floor(Math.random() * 3);
                const pan = (Math.random() * 2 - 1) * 0.28;
                playSample("turret_" + tid, 0.16 * Math.min(1.0, atten), 1.08 * jitter(0.06), 6500, pan, 0.15, 0.0);
            },
            turretAt(x, y, z) {
                const sp = spatialize(x, y, z, 460.0, 0.0);
                if (sp.atten <= 0.03) return;
                const tid = Math.floor(Math.random() * 3);
                playSample("turret_" + tid, 0.16 * Math.min(1.0, sp.atten), 1.08 * jitter(0.06), 6500 * sp.cutoffMul, sp.pan, 0.15, 0.0);
            },
            fx(kind) {
                if (kind === 0) {
                    // Zielaufschaltung (crisp, dezent, kein 08/15-Piepsen)
                    playSample("target_lock", 0.15, jitter(0.03), 4200, 0.0, 0.18, 0.0);
                } else if (kind === 1) {
                    // Fracht / Erz geborgen
                    playSample("cargo_pickup", 0.30, jitter(0.04), 5600, 0.0, 0.25, 0.0);
                } else if (kind === 2) {
                    // Handel / Upgrade bestaetigt
                    playSample("trade_confirm", 0.28, jitter(0.03), 6800, 0.0, 0.20, 0.0);
                } else if (kind === 3) {
                    // Fehler / Dock gesperrt / Credits fehlen
                    playSample("ui_error", 0.24, 0.96, 3800, 0.0, 0.15, 0.0);
                } else if (kind === 4) {
                    // Hangar-Schleuse oeffnen (Andocken)
                    playSample("docking", 0.32, 1.0, 12000, 0.0, 0.35, 0.0);
                    playSample("airlock_open", 0.28, 0.95, 5200, 0.0, 0.30, 0.14);
                } else if (kind === 5) {
                    // Abdocken (Entriegeln + Schleuse zu)
                    playSample("undock", 0.30, 1.0, 5800, 0.0, 0.28, 0.0);
                    playSample("airlock_close", 0.24, 0.96, 4800, 0.0, 0.25, 0.12);
                } else if (kind === 6) {
                    // Sprungtor / Sektor-Transit
                    playSample("jump_charge", 0.34, 1.02, 7500, 0.0, 0.45, 0.0);
                } else if (kind === 7) {
                    // Niedrige Huelle Warnung (mit 6s Cooldown)
                    const c = ensure();
                    if (!c || c.currentTime - lastHullWarnAt < 6.0) return;
                    lastHullWarnAt = c.currentTime;
                    playSample("low_hull_warn", 0.20, 0.94, 2900, 0.0, 0.32, 0.0);
                } else if (kind === 8) {
                    // Sektor-Scanner Ping
                    playSample("scanner_ping", 0.22, jitter(0.03), 4800, 0.0, 0.28, 0.0);
                } else if (kind === 9) {
                    // Cockpit-Bestaetigung
                    playSample("ui_confirm", 0.20, jitter(0.03), 5500, 0.0, 0.16, 0.0);
                } else if (kind === 10) {
                    // Torpedo-Abschuss: Auswurfklammer + Triebwerksschub + Tiefbass-Impuls
                    playSample("docking", 0.29, 1.18, 4600, 0.0, 0.18, 0.0);
                    playSample("thruster_1", 0.34, 0.72, 5600, 0.0, 0.28, 0.06);
                    playSample("exp_sub_0", 0.30, 0.88, 720, 0.0, 0.22, 0.05);
                } else if (kind === 11) {
                    // Werft-Einfahrt / Schwere Dockklammern & Kranhydraulik
                    playSample("docking", 0.36, 0.74, 4800, 0.0, 0.42, 0.0);
                    playSample("shipyard_crane_0", 0.28, 0.78, 2200, -0.22, 0.45, 0.08);
                    playSample("capital_engine_" + Math.floor(Math.random() * 2), 0.22, 0.56, 650, 0.15, 0.35, 0.18);
                } else if (kind === 12) {
                    // Werft-Kran / Mechanik-Impuls
                    const cid = Math.floor(Math.random() * 2);
                    playSample("shipyard_crane_" + cid, 0.26, 0.80 * jitter(0.05), 2400, (Math.random() * 2 - 1) * 0.35, 0.38, 0.0);
                } else if (kind === 13) {
                    // Thermodrucker / Archiv-Log-Tick
                    playSample("printer_tick", 0.22, jitter(0.04), 4600, 0.0, 0.14, 0.0);
                } else if (kind === 14) {
                    // ECM / Geister-Protokoll Stoerimpuls
                    const eid = Math.floor(Math.random() * 2);
                    playSample("ecm_jam_" + eid, 0.34, 0.94 * jitter(0.05), 4200, 0.0, 0.42, 0.0);
                } else if (kind === 15) {
                    // Mechanisches Dock-Blockieren / Schleuse klemmt
                    const c = ensure();
                    if (!c || c.currentTime - lastDockJamAt < 3.6) return;
                    lastDockJamAt = c.currentTime;
                    playSample("dock_jam", 0.30, 0.92 * jitter(0.04), 3800, 0.0, 0.26, 0.0);
                } else if (kind === 16) {
                    // Overdrive aktiviert (3x Kombo-Schub)
                    playSample("thruster_2", 0.26, 1.24, 5200, 0.0, 0.32, 0.0, 320);
                    playSample("shield_2", 0.22, 1.28, 7200, 0.0, 0.36, 0.04, 480);
                } else if (kind === 17) {
                    // Eigener Schild zusammengebrochen
                    playSample("shield_4", 0.28, 0.74, 3400, 0.0, 0.34, 0.0);
                    playSample("hull_arc_0", 0.20, 0.88, 3600, 0.0, 0.25, 0.03);
                    const c = ensure();
                    if (c && c.currentTime - lastShieldDownAt >= 18.0) {
                        lastShieldDownAt = c.currentTime;
                        requestRadioClip("comm_shield_down", 0.72, 0.0, 0.16);
                    }
                } else if (kind === 18) {
                    // Schild-Regeneration setzt wieder ein (Schildrelais-Kit mit zusaetzlichem Relais-Impuls)
                    playSample("shield_1", 0.13, flightCtx.kit === 3 ? 1.44 : 1.32, 5200, 0.0, 0.30, 0.0, 420);
                    if (flightCtx.kit === 3) playSample("ui_confirm", 0.11, 1.38, 6200, 0.0, 0.22, 0.03, 520);
                } else if (kind === 19) {
                    // Booster leer / ueberhitzt
                    playSample("retro_thrust", 0.22, 0.68, 2200, 0.0, 0.18, 0.0);
                    playSample("ui_error", 0.15, 0.74, 2400, 0.0, 0.12, 0.04);
                } else if (kind === 20) {
                    // Booster wieder einsatzbereit
                    playSample("ui_confirm", 0.15, 1.18, 4800, 0.0, 0.16, 0.0);
                } else if (kind === 21) {
                    // Schwere Gegner-Salve laedt auf (Sovereign / Bomber)
                    const c = ensure();
                    if (!c || c.currentTime - lastMacroChargeAt < 6.5) return;
                    lastMacroChargeAt = c.currentTime;
                    playSample("jump_charge", 0.18, 0.68, 1650, 0.0, 0.44, 0.0);
                } else if (kind === 22) {
                    // Schiffswechsel / Kit-Umbau in der Werft
                    const cid = Math.floor(Math.random() * 2);
                    playSample("shipyard_crane_" + cid, 0.25, 0.86, 2600, -0.18, 0.34, 0.0);
                    playSample("docking", 0.24, 1.04, 6200, 0.16, 0.25, 0.08);
                    playSample("trade_confirm", 0.24, 0.98, 6500, 0.0, 0.20, 0.16);
                } else if (kind === 23) {
                    // Torpedo-Panzerungstreffer auf Grossschiff
                    playSample("explosion", 0.50, 0.68, 4200, 0.0, 0.45, 0.0);
                    playSample("exp_sub_1", 0.46, 0.80, 760, 0.0, 0.30, 0.02);
                    playSample("metal_3", 0.38, 0.64, 3400, 0.18, 0.40, 0.05);
                    playSample("hull_creak", 0.28, 0.72, 2200, -0.15, 0.32, 0.12);
                } else if (kind === 24) {
                    // Dezenter Cockpit-/Tab-Klick
                    playSample("ui_confirm", 0.11, 1.16 * jitter(0.03), 4600, 0.0, 0.10, 0.0, 260);
                }
            },
            collisionImpact(x, y, z, intensity, isPlayer) {
                const c = ensure();
                if (!c || c.currentTime - lastCollisionAt < 0.18) return;
                lastCollisionAt = c.currentTime;
                const sp = isPlayer ? { atten: 1.0, pan: 0.0, cutoffMul: 1.0 } : spatialize(x, y, z, 420.0, 0.0);
                if (sp.atten <= 0.05) return;
                const norm = Math.max(0.2, Math.min(1.0, intensity / 32.0));
                const vol = (isPlayer ? 0.32 : 0.16) * norm * sp.atten;
                const midx = Math.floor(Math.random() * 5);
                playSample("metal_" + midx, vol, (isPlayer ? 0.68 : 0.84) * jitter(0.07), 3200 * sp.cutoffMul, sp.pan, 0.24, 0.0);
                if (isPlayer) {
                    playSample("hull_creak", vol * 0.75, 0.82 * jitter(0.06), 2100, sp.pan, 0.26, 0.03);
                    if (norm > 0.55) playSample("exp_sub_0", vol * 0.65, 0.85, 580, 0.0, 0.16, 0.0);
                }
            },
            mineralCrack(x, y, z) {
                const c = ensure();
                if (!c || c.currentTime - lastMineralCrackAt < 0.12) return;
                lastMineralCrackAt = c.currentTime;
                const sp = spatialize(x, y, z, 420.0, 0.1);
                if (sp.atten <= 0.05) return;
                playSample("cargo_pickup", 0.22 * sp.atten, 1.44 * jitter(0.05), 7800 * sp.cutoffMul, sp.pan, 0.30, 0.0, 420);
                playSample("hull_arc_0", 0.12 * sp.atten, 1.35, 6200 * sp.cutoffMul, sp.pan, 0.24, 0.02, 700);
            },
            capitalHit(x, y, z, armour, gunDestroyed) {
                const c = ensure();
                const sp = spatialize(x, y, z, 720.0, 0.12);
                if (gunDestroyed) {
                    const cidx = Math.floor(Math.random() * 5);
                    playSample("explosion", 0.42 * sp.atten, 0.92, 5200 * sp.cutoffMul, sp.pan, 0.36, 0.0);
                    playSample("exp_crunch_" + cidx, 0.34 * sp.atten, 0.88, 4400 * sp.cutoffMul, sp.pan, 0.32, 0.02);
                    return;
                }
                if (!c || c.currentTime - lastCapitalHitAt < 0.09) return;
                lastCapitalHitAt = c.currentTime;
                const midx = Math.floor(Math.random() * 5);
                playSample("metal_" + midx, (armour ? 0.24 : 0.30) * sp.atten, (armour ? 0.66 : 0.86) * jitter(0.06), (armour ? 2800 : 4800) * sp.cutoffMul, sp.pan, 0.28, 0.0);
            },
            capitalHorn(atten) {
                const c = ensure();
                if (!c || atten <= 0.03) return;
                if (c.currentTime - lastHornAt < 8.0) return;
                lastHornAt = c.currentTime;
                const vol = Math.min(0.62, 0.48 * Math.min(1.0, atten));
                const cutoff = 900 + Math.min(1.0, atten) * 3200;
                playSample("capital_horn", vol, 0.96 * jitter(0.03), cutoff, 0.0, 0.65, 0.0);
                playSample("deep_thunder", vol * 0.42, 0.78, 720, 0.0, 0.55, 0.35);
            },
            capitalHornAt(x, y, z) {
                const c = ensure();
                const sp = spatialize(x, y, z, 1400.0, 0.12);
                if (!c || sp.atten <= 0.03 || c.currentTime - lastHornAt < 8.0) return;
                lastHornAt = c.currentTime;
                const vol = Math.min(0.62, 0.48 * Math.min(1.0, sp.atten));
                const cutoff = (900 + Math.min(1.0, sp.atten) * 3200) * sp.cutoffMul;
                playSample("capital_horn", vol, 0.96 * jitter(0.03), cutoff, sp.pan * 0.65, 0.65, 0.0);
                playSample("deep_thunder", vol * 0.42, 0.78, 720, sp.pan * 0.4, 0.55, 0.35);
            },
            macroCannon(atten) {
                if (atten <= 0.03) return;
                const mid = Math.floor(Math.random() * 2);
                const pan = (Math.random() * 2 - 1) * 0.38;
                const vol = Math.min(0.60, 0.46 * Math.min(1.0, atten));
                playSample("macro_cannon_" + mid, vol, 0.58 * jitter(0.05), 2400, pan, 0.42, 0.0);
                playSample("exp_sub_0", vol * 0.72, 0.82, 780, pan * 0.5, 0.28, 0.015);
            },
            macroCannonAt(x, y, z) {
                const sp = spatialize(x, y, z, 950.0, 0.05);
                if (sp.atten <= 0.03) return;
                const mid = Math.floor(Math.random() * 2);
                const vol = Math.min(0.60, 0.46 * Math.min(1.0, sp.atten));
                playSample("macro_cannon_" + mid, vol, 0.58 * jitter(0.05), 2400 * sp.cutoffMul, sp.pan, 0.42, 0.0);
                playSample("exp_sub_0", vol * 0.72, 0.82, 780, sp.pan * 0.5, 0.28, 0.015);
            },
            capitalExplosion(atten) {
                if (atten <= 0.03) return;
                const c = ensure();
                if (c) {
                    duckUntil = Math.max(duckUntil, c.currentTime + 1.6);
                    if (spaceGain) spaceGain.gain.setTargetAtTime(0.014, c.currentTime, 0.04);
                }
                const vol = Math.min(0.75, 0.62 * Math.min(1.0, atten));
                const cidx = Math.floor(Math.random() * 5);
                const midx = Math.floor(Math.random() * 5);
                playSample("explosion", vol, 0.55, 2800, 0.0, 0.55, 0.0);
                playSample("exp_sub_0", vol * 0.85, 0.78, 680, 0.0, 0.35, 0.02);
                playSample("deep_thunder", vol * 0.65, 0.72, 950, 0.0, 0.60, 0.18);
                playSample("exp_crunch_" + cidx, vol * 0.70, 0.66, 3200, -0.25, 0.48, 0.36);
                playSample("metal_" + midx, vol * 0.52, 0.55, 2600, 0.28, 0.45, 0.44);
                playSample("capital_horn", vol * 0.45, 0.68, 1200, 0.0, 0.68, 0.48);
                playSample("explosion", vol * 0.55, 0.68, 2200, 0.18, 0.50, 0.78);
            },
            capitalExplosionAt(x, y, z) {
                const sp = spatialize(x, y, z, 1600.0, 0.15);
                if (sp.atten <= 0.03) return;
                const c = ensure();
                if (c) {
                    duckUntil = Math.max(duckUntil, c.currentTime + 1.6);
                    if (spaceGain) spaceGain.gain.setTargetAtTime(0.014, c.currentTime, 0.04);
                }
                const vol = Math.min(0.75, 0.62 * Math.min(1.0, sp.atten));
                const cidx = Math.floor(Math.random() * 5);
                const midx = Math.floor(Math.random() * 5);
                playSample("explosion", vol, 0.55, 2800 * sp.cutoffMul, sp.pan * 0.7, 0.55, 0.0);
                playSample("exp_sub_0", vol * 0.85, 0.78, 680, sp.pan * 0.3, 0.35, 0.02);
                playSample("deep_thunder", vol * 0.65, 0.72, 950, sp.pan * 0.5, 0.60, 0.18);
                playSample("exp_crunch_" + cidx, vol * 0.70, 0.66, 3200 * sp.cutoffMul, Math.max(-0.88, Math.min(0.88, sp.pan - 0.22)), 0.48, 0.36);
                playSample("metal_" + midx, vol * 0.52, 0.55, 2600 * sp.cutoffMul, Math.max(-0.88, Math.min(0.88, sp.pan + 0.22)), 0.45, 0.44);
                playSample("capital_horn", vol * 0.45, 0.68, 1200, sp.pan * 0.5, 0.68, 0.48);
                playSample("explosion", vol * 0.55, 0.68, 2200 * sp.cutoffMul, sp.pan * 0.75, 0.50, 0.78);
            },
            capitalComm(kind) {
                // Die Feuerwarnung darf nur während eines echten Ladevorgangs beginnen.
                if (kind === 3) return pendingCapital < 0 && playCapitalCall(kind);
                pendingCapital = playCapitalCall(kind) ? -1 : kind;
            }
        };
    })()""")>]
    let private createBackend () : obj = jsNative

    [<Emit("$0 && $0.ensure()")>]
    let private emitEnsure (_h: obj) : unit = jsNative

    [<Emit("$0 && $0.setMuted($1)")>]
    let private emitMuted (_h: obj) (_m: bool) : unit = jsNative

    [<Emit("$0 && $0.setEngine($1, $2, $3)")>]
    let private emitEngine (_h: obj) (_active: bool) (_throttle: float) (_boost: float) : unit = jsNative

    [<Emit("$0 && $0.laser($1, $2, $3)")>]
    let private emitLaser (_h: obj) (_isPlayer: bool) (_atten: float) (_overdrive: bool) : unit = jsNative

    [<Emit("$0 && $0.hit($1, $2)")>]
    let private emitHit (_h: obj) (_shield: bool) (_isPlayer: bool) : unit = jsNative

    [<Emit("$0 && $0.explosion($1, $2)")>]
    let private emitExplosion (_h: obj) (_scale: float) (_atten: float) : unit = jsNative

    [<Emit("$0 && $0.cue($1)")>]
    let private emitCue (_h: obj) (_kind: int) : unit = jsNative

    [<Emit("$0 && $0.stationComm($1, $2)")>]
    let private emitStationComm (_h: obj) (_stationId: string) (_kind: int) : unit = jsNative

    [<Emit("$0 && $0.resetStationComm($1)")>]
    let private emitResetStationComm (_h: obj) (_stationId: string) : unit = jsNative

    [<Emit("$0 && $0.turret($1)")>]
    let private emitTurret (_h: obj) (_atten: float) : unit = jsNative

    [<Emit("$0 && $0.fx($1)")>]
    let private emitFx (_h: obj) (_kind: int) : unit = jsNative

    [<Emit("$0 && $0.radioEvent($1)")>]
    let private emitRadioEvent (_h: obj) (_kind: int) : unit = jsNative

    [<Emit("$0 && $0.storyBeat($1)")>]
    let private emitStoryBeat (_h: obj) (_beat: int) : unit = jsNative

    [<Emit("$0 ? $0.tryContactBeat($1) : -1")>]
    let private emitContactBeat (_h: obj) (_beat: int) : float = jsNative

    [<Emit("$0 && $0.stopContactBeat()")>]
    let private emitStopContact (_h: obj) : unit = jsNative

    [<Emit("!!($0 && $0.contactInterrupted($1))")>]
    let private emitInterrupted (_h: obj) (_beat: int) : bool = jsNative

    [<Emit("$0 && $0.stopContactBeat($1)")>]
    let private emitStopBeat (_h: obj) (_beat: int) : unit = jsNative

    [<Emit("$0 && $0.capitalHorn($1)")>]
    let private emitCapitalHorn (_h: obj) (_atten: float) : unit = jsNative

    [<Emit("$0 && $0.macroCannon($1)")>]
    let private emitMacroCannon (_h: obj) (_atten: float) : unit = jsNative

    [<Emit("$0 && $0.capitalExplosion($1)")>]
    let private emitCapitalExplosion (_h: obj) (_atten: float) : unit = jsNative

    [<Emit("$0 && $0.capitalComm($1)")>]
    let private emitCapitalComm (_h: obj) (_kind: int) : unit = jsNative

    [<Emit("!!($0 && $0.capitalComm(3))")>]
    let private emitMacroWarning (_h: obj) : bool = jsNative

    [<Emit("$0 && $0.clearRadio()")>]
    let private emitClearRadio (_h: obj) : unit = jsNative

    [<Emit("$0 && $0.setReceiver($1)")>]
    let private emitReceiver (_h: obj) (_signal: float) : unit = jsNative

    [<Emit("$0 && $0.setVolumes($1, $2, $3, $4)")>]
    let private emitSetVolumes (_h: obj) (_m: float) (_s: float) (_r: float) (_a: float) : unit = jsNative

    [<Emit("$0 ? $0.getVolumes() : [1, 1, 1, 1]")>]
    let private emitGetVolumes (_h: obj) : float[] = jsNative

    [<Emit("$0 && $0.setListener($1, $2, $3, $4, $5, $6, $7, $8, $9)")>]
    let private emitSetListener (_h: obj) (_px: float) (_py: float) (_pz: float) (_rx: float) (_ry: float) (_rz: float) (_fx: float) (_fy: float) (_fz: float) : unit = jsNative

    [<Emit("$0 && $0.setFlightContext($1, $2, $3, $4, $5, $6, $7)")>]
    let private emitSetFlightContext (_h: obj) (_hull: int) (_kit: int) (_sector: int) (_cockpit: bool) (_outpostKind: int) (_gateProx: float) (_cruiserNear: bool) : unit = jsNative

    [<Emit("$0 && $0.setSceneMode($1)")>]
    let private emitSetSceneMode (_h: obj) (_mode: int) : unit = jsNative

    [<Emit("$0 && $0.laserAt($1, $2, $3, $4, $5, $6, $7, $8)")>]
    let private emitLaserAt (_h: obj) (_isPlayer: bool) (_x: float) (_y: float) (_z: float) (_overdrive: bool) (_laserLevel: int) (_kit: int) (_enemyRole: int) : unit = jsNative

    [<Emit("$0 && $0.hitAt($1, $2, $3, $4, $5)")>]
    let private emitHitAt (_h: obj) (_shield: bool) (_isPlayer: bool) (_x: float) (_y: float) (_z: float) : unit = jsNative

    [<Emit("$0 && $0.explosionAt($1, $2, $3, $4)")>]
    let private emitExplosionAt (_h: obj) (_scale: float) (_x: float) (_y: float) (_z: float) : unit = jsNative

    [<Emit("$0 && $0.turretAt($1, $2, $3)")>]
    let private emitTurretAt (_h: obj) (_x: float) (_y: float) (_z: float) : unit = jsNative

    [<Emit("$0 && $0.macroCannonAt($1, $2, $3)")>]
    let private emitMacroCannonAt (_h: obj) (_x: float) (_y: float) (_z: float) : unit = jsNative

    [<Emit("$0 && $0.capitalHornAt($1, $2, $3)")>]
    let private emitCapitalHornAt (_h: obj) (_x: float) (_y: float) (_z: float) : unit = jsNative

    [<Emit("$0 && $0.capitalExplosionAt($1, $2, $3)")>]
    let private emitCapitalExplosionAt (_h: obj) (_x: float) (_y: float) (_z: float) : unit = jsNative

    [<Emit("$0 && $0.collisionImpact($1, $2, $3, $4, $5)")>]
    let private emitCollisionImpact (_h: obj) (_x: float) (_y: float) (_z: float) (_intensity: float) (_isPlayer: bool) : unit = jsNative

    [<Emit("$0 && $0.mineralCrack($1, $2, $3)")>]
    let private emitMineralCrack (_h: obj) (_x: float) (_y: float) (_z: float) : unit = jsNative

    [<Emit("$0 && $0.capitalHit($1, $2, $3, $4, $5)")>]
    let private emitCapitalHit (_h: obj) (_x: float) (_y: float) (_z: float) (_armour: bool) (_gunDestroyed: bool) : unit = jsNative

    let receiver (s: SoundEngine) signal =
        try emitReceiver s.Handle (if s.Muted then -1.0 else signal) with _ -> ()

    let create () : SoundEngine =
        let h =
            try
                createBackend ()
            with _ ->
                null

        { Handle = h; Muted = false }

    let unlock (s: SoundEngine) =
        if not s.Muted then
            try
                emitEnsure s.Handle
            with _ ->
                ()

    let toggleMute (s: SoundEngine) =
        s.Muted <- not s.Muted

        try
            emitMuted s.Handle s.Muted
        with _ ->
            ()

    let updateEngine (s: SoundEngine) (active: bool) (throttle: float) (boost: float) =
        try
            emitEngine s.Handle (active && not s.Muted) throttle boost
        with _ ->
            ()

    let laser (s: SoundEngine) (isPlayer: bool) (distance: float) (overdrive: bool) =
        if not s.Muted then
            let atten = if isPlayer then 1.0 else Math.Max(0.0, 1.0 - distance / 520.0)

            try
                emitLaser s.Handle isPlayer atten overdrive
            with _ ->
                ()

    let hit (s: SoundEngine) (shieldAbsorbed: bool) (isPlayer: bool) =
        if not s.Muted then
            try
                emitHit s.Handle shieldAbsorbed isPlayer
            with _ ->
                ()

    let explosion (s: SoundEngine) (scale: float) (distance: float) =
        if not s.Muted then
            let atten = Math.Max(0.05, 1.0 - distance / 680.0)

            try
                emitExplosion s.Handle scale atten
            with _ ->
                ()

    let targetLock (s: SoundEngine) =
        if not s.Muted then
            try
                emitFx s.Handle 0
            with _ ->
                ()

    let waveStart (s: SoundEngine) =
        if not s.Muted then
            try
                emitCue s.Handle 1
            with _ ->
                ()

    let waveCleared (s: SoundEngine) =
        if not s.Muted then
            try
                emitCue s.Handle 2
            with _ ->
                ()

    let gameOver (s: SoundEngine) =
        if not s.Muted then
            try
                emitCue s.Handle 3
            with _ ->
                ()

    let stationComm (s: SoundEngine) (stationId: string) (kind: int) =
        if not s.Muted then
            try
                emitStationComm s.Handle stationId kind
            with _ ->
                ()

    let resetStationComm (s: SoundEngine) (stationId: string) =
        try
            emitResetStationComm s.Handle stationId
        with _ ->
            ()

    let cargoPickup (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 1 with _ -> ()

    let tradeConfirm (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 2 with _ -> ()

    let uiError (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 3 with _ -> ()

    let dockOpen (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 4 with _ -> ()

    let undock (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 5 with _ -> ()

    let jumpGate (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 6 with _ -> ()

    let lowHullWarn (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 7 with _ -> ()

    let turretFire (s: SoundEngine) (distance: float) =
        if not s.Muted then
            let atten = Math.Max(0.0, 1.0 - distance / 460.0)
            try emitTurret s.Handle atten with _ -> ()

    let pirateAmbush (s: SoundEngine) =
        if not s.Muted then
            try emitRadioEvent s.Handle 0 with _ -> ()

    let sectorClear (s: SoundEngine) =
        if not s.Muted then
            try emitRadioEvent s.Handle 1 with _ -> ()

    let aetherChatter (s: SoundEngine) =
        if not s.Muted then
            try emitRadioEvent s.Handle 2 with _ -> ()

    let shieldDown (s: SoundEngine) =
        if not s.Muted then
            try emitRadioEvent s.Handle 3 with _ -> ()

    let hullCritical (s: SoundEngine) =
        if not s.Muted then
            try emitRadioEvent s.Handle 4 with _ -> ()

    let upgradeReady (s: SoundEngine) =
        if not s.Muted then
            try emitRadioEvent s.Handle 5 with _ -> ()

    let tradeProfit (s: SoundEngine) =
        if not s.Muted then
            try emitRadioEvent s.Handle 6 with _ -> ()

    let jumpReady (s: SoundEngine) =
        if not s.Muted then
            try emitRadioEvent s.Handle 7 with _ -> ()

    let autoTurretOnline (s: SoundEngine) =
        if not s.Muted then
            try emitRadioEvent s.Handle 8 with _ -> ()

    let scannerPing (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 8 with _ -> ()

    let torpedoLaunch (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 10 with _ -> ()

    let uiConfirm (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 9 with _ -> ()

    let storyBeat (s: SoundEngine) (beat: int) =
        if not s.Muted then
            try emitStoryBeat s.Handle beat with _ -> ()

    /// Zeit bis zum Ende inklusive Funkauftakt. 0 bedeutet Laden oder belegter Kanal, -1 stumm oder nicht verfügbar.
    let tryContactBeat (s: SoundEngine) (beat: int) =
        if s.Muted then -1.0
        else
            try emitContactBeat s.Handle beat with _ -> -1.0

    let stopContactBeat (s: SoundEngine) =
        try emitStopContact s.Handle with _ -> ()

    let stopBeat (s: SoundEngine) (beat: int) =
        try emitStopBeat s.Handle beat with _ -> ()

    let contactInterrupted (s: SoundEngine) (beat: int) =
        try emitInterrupted s.Handle beat with _ -> false

    let capitalHorn (s: SoundEngine) (distance: float) =
        if not s.Muted then
            let atten = Math.Max(0.12, 1.0 - distance / 1400.0)
            try emitCapitalHorn s.Handle atten with _ -> ()

    let macroCannon (s: SoundEngine) (distance: float) =
        if not s.Muted then
            let atten = Math.Max(0.05, 1.0 - distance / 950.0)
            try emitMacroCannon s.Handle atten with _ -> ()

    let capitalExplosion (s: SoundEngine) (distance: float) =
        if not s.Muted then
            let atten = Math.Max(0.15, 1.0 - distance / 1600.0)
            try emitCapitalExplosion s.Handle atten with _ -> ()

    let shipyardEnter (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 11 with _ -> ()

    let shipyardCrane (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 12 with _ -> ()

    let capitalComm (s: SoundEngine) (kind: int) =
        if not s.Muted then
            try emitCapitalComm s.Handle kind with _ -> ()

    let printerTick (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 13 with _ -> ()

    let ecmJam (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 14 with _ -> ()

    let dockJam (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 15 with _ -> ()

    let tryMacroWarning (s: SoundEngine) =
        if s.Muted then false
        else try emitMacroWarning s.Handle with _ -> false

    let clearRadio (s: SoundEngine) =
        try emitClearRadio s.Handle with _ -> ()

    let setVolumes (s: SoundEngine) (master: float) (sfx: float) (radio: float) (ambience: float) =
        try emitSetVolumes s.Handle master sfx radio ambience with _ -> ()

    let getVolumes (s: SoundEngine) =
        try emitGetVolumes s.Handle with _ -> [| 1.0; 1.0; 1.0; 1.0 |]

    let setListener (s: SoundEngine) (pos: Vec3) (rot: Quat) =
        let r = Quat.right rot
        let f = Quat.forward rot
        try emitSetListener s.Handle pos.X pos.Y pos.Z r.X r.Y r.Z f.X f.Y f.Z with _ -> ()

    let setFlightContext (s: SoundEngine) (hull: int) (kit: int) (sector: int) (cockpit: bool) (outpostKind: int) (gateProx: float) (cruiserNear: bool) =
        try emitSetFlightContext s.Handle hull kit sector cockpit outpostKind gateProx cruiserNear with _ -> ()

    let setSceneMode (s: SoundEngine) (mode: int) =
        try emitSetSceneMode s.Handle mode with _ -> ()

    let laserAt (s: SoundEngine) (isPlayer: bool) (pos: Vec3) (overdrive: bool) (laserLevel: int) (kit: int) (enemyRole: int) =
        if not s.Muted then
            try emitLaserAt s.Handle isPlayer pos.X pos.Y pos.Z overdrive laserLevel kit enemyRole with _ -> ()

    let hitAt (s: SoundEngine) (shieldAbsorbed: bool) (isPlayer: bool) (pos: Vec3) =
        if not s.Muted then
            try emitHitAt s.Handle shieldAbsorbed isPlayer pos.X pos.Y pos.Z with _ -> ()

    let explosionAt (s: SoundEngine) (scale: float) (pos: Vec3) =
        if not s.Muted then
            try emitExplosionAt s.Handle scale pos.X pos.Y pos.Z with _ -> ()

    let turretFireAt (s: SoundEngine) (pos: Vec3) =
        if not s.Muted then
            try emitTurretAt s.Handle pos.X pos.Y pos.Z with _ -> ()

    let macroCannonAt (s: SoundEngine) (pos: Vec3) =
        if not s.Muted then
            try emitMacroCannonAt s.Handle pos.X pos.Y pos.Z with _ -> ()

    let capitalHornAt (s: SoundEngine) (pos: Vec3) =
        if not s.Muted then
            try emitCapitalHornAt s.Handle pos.X pos.Y pos.Z with _ -> ()

    let capitalExplosionAt (s: SoundEngine) (pos: Vec3) =
        if not s.Muted then
            try emitCapitalExplosionAt s.Handle pos.X pos.Y pos.Z with _ -> ()

    let collisionImpact (s: SoundEngine) (pos: Vec3) (intensity: float) (isPlayer: bool) =
        if not s.Muted then
            try emitCollisionImpact s.Handle pos.X pos.Y pos.Z intensity isPlayer with _ -> ()

    let mineralCrack (s: SoundEngine) (pos: Vec3) =
        if not s.Muted then
            try emitMineralCrack s.Handle pos.X pos.Y pos.Z with _ -> ()

    let capitalHit (s: SoundEngine) (pos: Vec3) (armour: bool) (gunDestroyed: bool) =
        if not s.Muted then
            try emitCapitalHit s.Handle pos.X pos.Y pos.Z armour gunDestroyed with _ -> ()

    let overdriveOn (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 16 with _ -> ()

    let shieldBreak (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 17 with _ -> ()

    let shieldRecharge (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 18 with _ -> ()

    let boostEmpty (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 19 with _ -> ()

    let boostReady (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 20 with _ -> ()

    let macroCharge (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 21 with _ -> ()

    let refit (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 22 with _ -> ()

    let torpedoImpact (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 23 with _ -> ()

    let uiClick (s: SoundEngine) =
        if not s.Muted then
            try emitFx s.Handle 24 with _ -> ()
