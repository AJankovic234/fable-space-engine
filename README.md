# Space 2601AE — Vibe-Coding Model Benchmark (`Fable 5.2` + `WebGL2`)

> **About this project:**  
> This repository is a **pure vibe-coding project** created strictly as a **personal benchmark to test and compare current AI coding models** (including Claude, Codex, and Gemini) on a cohesive, multi-system codebase—covering architecture, 3D rendering, physics, economy, UI, audio, and automated tests rather than isolated throwaway snippets.

The in-game UI, radio dialogue, and design/lore documents inside the repo are written in German, while the codebase itself is organized as a modular **F# / Fable 5.2** project targeting browser **ES modules** and **native WebGL2**.

[Token totals and model contributions](#personal-benchmark)

---

## Tech Stack

- **Language & Compiler:** F# compiled to JavaScript via [Fable 5.2.0](https://fable.io/) (`.NET SDK 10.0.100`, 40 source modules under `src/`)
- **Rendering:** Native WebGL2 instanced renderer with 3-level HDR bloom, directional sun shadows, sector-tinted starfield/sky dome, procedural ships/structures, and glTF `.glb` loading (`src/WebGLRenderer.fs`, `src/SkyBackdrop.fs`, `src/GlbLoader.fs`)
- **Audio:** WebAudio API 3D spatial sound engine with 4-channel volume mixer (`master`, `sfx`, `radio`, `ambience`), hull/kit flight curves, scene-mode separation, and local SFX samples (`src/Sound.fs`)
- **Bundler & Runtime:** Vite `8.3.8`, Node.js (`>= 22.12.0`)
- **Tests:** Automated Node.js test runner (`tests/*.test.mjs`, 127 tests covering rendering shaders, glTF loading, sound engine, physics, flight controls, economy, freight, blockade combat, upgrades, and save-slot recovery)
- **Containerization:** Nginx static build via `Dockerfile` and `docker-compose.yml`

---

## Implemented Gameplay & Systems

- **9-Sector Star Network:** Outposts, physical jump gates, mining fields, and a star map with saved destinations, waypoints, and next-leg trade forecasts (`src/Universe.fs`, `src/StarMap.fs`).
- **Combat & Helios Blockade:** Interceptor AI, turret auto-targeting, capital-ship cruiser encounters with destructible batteries, escort bombers, and torpedoes (`src/AI.fs`, `src/Turret.fs`, `src/CapitalFlight.fs`, `src/Blockade.fs`, `src/PhysicsAndPool.fs`).
- **Trading, Freight & Economy:** Live supply/demand prices, courier & bulk freight missions, escort fees, shielded cargo, and contraband mechanics (`src/TradeSystem.fs`, `src/TradeRoutes.fs`, `src/Freight.fs`, `src/Economy.fs`).
- **Ships, Kits & Visible Upgrades:** Four playable hulls (`Bergungskutter`, `Lastkahn`, `Sperber`, `Eisvogel`), four tuning kits (including `ShieldRelay`), pilot skills, and external hull upgrade meshes (`src/ShipCatalog.fs`, `src/UpgradeMeshes.fs`, `src/OutfitVisuals.fs`, `src/PilotSkills.fs`).
- **Save Persistence:** Multi-slot `localStorage` persistence with automatic primary/backup slot recovery and JSON export (`src/SaveStore.fs`, `src/GameSave.fs`).

---

## Quick Start

### Prerequisites
- [Node.js](https://nodejs.org/) `>= 22.12.0`
- [.NET SDK](https://dotnet.microsoft.com/) `10.0` (matches `global.json`)

### Commands

```bash
# Install JS dependencies and restore Fable local tool
npm install
npm run restore

# Start dev server (Fable watch + Vite)
npm run dev

# Compile F# -> JS and run all tests
npm test

# Production build (outputs to dist/)
npm run build
```

### Docker

```bash
docker compose up -d --build
# Served at http://localhost:8088 (override with WEB_PORT=9000)
```

---

## Further Documentation (German)

- [`GAMEPLAY.md`](GAMEPLAY.md) — Detailed mechanics, controls, flight model, trade, and blockade overview
- [`AUSBAU_PLAN.md`](AUSBAU_PLAN.md) — Architecture & feature expansion plan (including AI model role notes)
- [`KOSTEN.md`](KOSTEN.md) — Benchmark session cost breakdown
- [`SFX_CODEX.md`](SFX_CODEX.md) — Audio buses, spatialization, and local SFX asset inventory
- [`LORE_2601AE.md`](LORE_2601AE.md) / [`STORY_2601AE.md`](STORY_2601AE.md) / [`PILOT_SYSTEM_2601AE.md`](PILOT_SYSTEM_2601AE.md) — Worldbuilding & campaign narrative

## Personal benchmark

**100% vibe coding, without human-provided structure proposals.** Adrian sets the goals and judges the playable result. The models decide how to structure and implement the work. This is a personal benchmark across one evolving game.

### Recorded token usage

The subtotal covers the measured entries below. Historical coverage starts with the original build and includes the later work for which a change and a usage record could be matched. An **Open** entry identifies a known contribution whose count is still missing. An **interim** entry includes tokens through the stated checkpoint.

<!-- ai-benchmark:totals -->
| Model | Recorded tokens | Entries awaiting counts |
| --- | ---: | ---: |
| Claude Fable 5.2 | 212,339,680 | 2 |
| Claude Haiku 5.5 | 178,692 | 0 |
| Claude Sonnet 5.5 | 8,560,672 | 3 |
| OpenAI GPT-6.2 Astra | 202,460,585 | 0 |
| Gemini 3.8 Flash | Open | 1 |
| Gemini (version unreported) | Open | 1 |
| Claude Opus 5.5 | 369,386,057 | 0 |
| Gemini 4 Neon | Open | 17 |
| OpenAI GPT-6.2 Sol | 78,732,559 | 0 |
| Gemini 4 Hydrogen | Open | 1 |
| OpenAI ImageGen (version unreported) | Open | 3 |
| Microsoft Neural TTS (version unreported) | Open | 1 |
| **Project · measured subtotal** | **871,658,245** | **29** |
<!-- /ai-benchmark:totals -->

### Change log

Record one row **per change and contributing model**, including reviews, delegated work and documentation. Give the exact model version, its own contribution, total tokens and a stable run or turn reference. When a version or count is unavailable, mark it open and reconcile the same row once the counter is available. Refresh the model and project totals with `node tools/benchmark-totals.mjs --write`.

Use the provider's total for the task. For cumulative counters, take the difference between its start and end. Input includes cache reads and writes once. Output includes reasoning once. For Claude request logs, count each request ID once. Attribute each worker's own usage separately and exclude it from the orchestrator's row if the parent counter includes it. For an active session, date its checkpoint and update that row when the final counter arrives.

<!-- ai-benchmark:changes -->
| Date (UTC) | Responsible model | Change or contribution | Total tokens | Measurement reference |
| --- | --- | --- | ---: | --- |
| 2026-11-17 | Claude Fable 5.2 | Original build, milestones 1–4. Project setup, renderer, flight, combat, targeting and HUD. | 47,609,456 | KOSTEN.md · Fable counter |
| 2026-11-17 | Claude Haiku 5.5 | Background session titles and metadata for the original build. | 5,482 | KOSTEN.md · Haiku counter |
| 2026-11-17 | Claude Sonnet 5.5 | Cost analysis for the original build, first 14 report requests documented in KOSTEN.md. | 6,799,501 | 78b22d56 · Sonnet requests 1–14 |
| 2026-11-18 | OpenAI GPT-6.2 Astra | Production plan, campaign milestones and model roles. | 611,014 | Codex turn 01a1f024 |
| 2026-11-18 | OpenAI GPT-6.2 Astra | Trade-route margins, hold expansion feedback and freight waypoint integration. | 5,635,308 | Codex turn 01a1f02c |
| 2026-11-18 | OpenAI GPT-6.2 Astra | Shipyard, playable hull and kit integration, purchases and save compatibility. | 8,577,099 | Codex turn 01a1f039 |
| 2026-11-18 | OpenAI GPT-6.2 Astra | Repeatable freight contracts, trade forecasts and delivery flow. | 9,597,620 | Codex turn 01a1f049 |
| 2026-11-18 | OpenAI GPT-6.2 Astra | Visible upgrade integration, shield relay, Blender export and gameplay tests. | 5,621,695 | Codex turn 01a1f059 |
| 2026-11-18 | OpenAI GPT-6.2 Astra | Handoff of orchestration to Opus. | 97,882 | Codex turn 01a1f093 |
| 2026-11-18 | OpenAI GPT-6.2 Astra | Universe map design, sector proposals and interactive route preview. | 1,501,193 | Codex turn 01a1f094 |
| 2026-11-18 | OpenAI GPT-6.2 Astra | Acceptance and handoff of the map design. | 125,949 | Codex turn 01a1f0a0 |
| 2026-11-18 | OpenAI GPT-6.2 Astra | README benchmark, attribution and token reconciliation. | 3,315,862 | Codex turn 01a1f0b1 |
| 2026-11-18 | OpenAI GPT-6.2 Astra | Trade design research on Freelancer, Star Citizen and Wing Commander: Armada. | 248,558 | Codex worker 01a1f02e |
| 2026-11-18 | OpenAI GPT-6.2 Astra | Shipyard asset work for the new hulls and their Blender previews. | 1,213,892 | Codex worker 01a1f039 |
| 2026-11-18 | Claude Sonnet 5.5 | Shipyard pricing against early trade earnings. | Open | Shipyard · pricing review |
| 2026-11-18 | Claude Sonnet 5.5 | Freight tariffs and risk premiums. | Open | Freight desk · tariff review |
| 2026-11-18 | Claude Sonnet 5.5 | Geometry for the nine ranked ship upgrades and the shield relay. | Open | Visible upgrades · mesh generation |
| 2026-11-18 | Gemini 3.8 Flash | Visual review of the three shipyard silhouettes at card size. | Open | Shipyard · visual review |
| 2026-11-18 | Gemini (version unreported) | README rewrite, project overview and setup documentation. | Open | README · Gemini editing session |
| 2026-11-18 | Claude Opus 5.5 | Capital-ship visual overhaul and scale. | 18,712,297 | Claude 76786965 · capital ships |
| 2026-11-18 | Claude Opus 5.5 | Graphics, gameplay and controls orchestration, through 22:08:40 UTC. | 20,437,391 | Claude 76786965 · orchestration to cutoff |
| 2026-11-18 | Claude Opus 5.5 | Flight controls, camera and combat feel work package: chase framing, speed feel, hit direction, kill marks, nearest-target key. | 17,169,499 | Claude Code worker · flight package |
| 2026-11-18 | Claude Opus 5.5 | Enemy fleet work package: five new enemy models at readable size, command corvette, role beacons and charge glow. | 44,552,553 | Claude Code worker · enemy package |
| 2026-11-18 | OpenAI GPT-6.2 Astra | Selected the jump-gate art task and wrote its acceptance brief for Sol. | 1,294,669 | Codex turn 01a1f0bd |
| 2026-11-18 | OpenAI GPT-6.2 Astra | Verified the missing gate transit and revised Sol's brief for fly-through travel and gate visuals. | 844,643 | Codex turn 01a1f0c0 |
| 2026-11-18 | Claude Opus 5.5 | Reviewed, built, tested and merged the station and flight work packages (22:08–22:10 UTC). | 3,524,877 | Claude Code · package merges, part 2 |
| 2026-11-18 | Claude Opus 5.5 | Effective cost and agent time ledger: Haiku agent, Claude Code hooks, list-price estimates and per-model attribution of files for Claude Code, Codex and Gemini (interim 22:34 UTC). | 37,292,511 | Claude Code · cost ledger |
| 2026-11-18 | Gemini 4 Neon | WebGL2 hull panel bevel normals, solar grazing rim lighting, hull-scaled shield flash light, kit/overdrive muzzle & thruster lighting cohesion, and shader tests. | Open | Antigravity · 9e9d50d2 |
| 2026-11-18 | Gemini 4 Neon | Freelancer market intel (galactic average & best neighbor arbitrage), optional Wing Commander: Armada fleet visibility (route tags + live 3D escort/transport livery), Starlancer Δv target closing telemetry, review fixes and unit tests. | Open | Antigravity · 9e9d50d2-b |
| 2026-11-18 | Gemini 4 Neon | Multimedia audit, three custom WebP character portraits (Orlov, Sera, J. Halden) matching cast art direction, and live radio transmission scanline/signal styling. | Open | Antigravity · 9e9d50d2-c |
| 2026-11-18 | OpenAI GPT-6.2 Astra | Regional asteroid models and materials, switchable flight optics, steering precision and in-game comparisons. | 16,992,842 | Codex turn 01a1f0c7 |
| 2026-11-18 | Gemini 4 Neon | Three custom Kepler-07 shipyard order artworks (Transporter, Eskorte, Torpedobomber) in WebP, auto-turret WebP optimization (-90%), and animated live-radio equalizer indicator. | Open | Antigravity · 9e9d50d2-d |
| 2026-11-18 | Gemini 4 Neon | Custom SVG favicon and three isometric low-poly tuning-kit artworks (Serientrimmung, Kurierdüsen, Impulsfokus) completing the shipyard kit cards. | Open | Antigravity · 9e9d50d2-e |
| 2026-11-18 | OpenAI GPT-6.2 Astra | Verified the current flight build and selected the physical jump-gate acceptance task. | 721,086 | Codex turn 01a1f0d8 |
| 2026-11-18 | OpenAI GPT-6.2 Sol | Swept gate collision and transfer, reciprocal exit, transit visuals, HUD guidance and four regression tests. | 2,601,851 | Codex worker 01a1f0dd |
| 2026-11-18 | OpenAI GPT-6.2 Astra | Freight, blockade and gate acceptance in the release browser, cockpit comparisons, flight recording and gameplay notes. | 4,028,645 | Codex turn 01a1f0dc |
| 2026-11-19 | Gemini 4 Neon | Borderlands-inspired sarcastic intro cinema (7 beat-synced freeze-frame title cards) with procedural WebAudio D-minor slide-blues-rock soundtrack ("No Rest in the Belt") and main-menu launcher. | Open | Antigravity · 9e9d50d2-f |
| 2026-11-19 | Gemini 4 Neon | Reworked intro cinema tone from goofy jokes to epic deadpan noir (AETHER Sovereign / Helios blockade stakes, Orlov feature), added 3-act orchestral-drone to blues-rock arc and clean widescreen framing. | Open | Antigravity · 9e9d50d2-g |
| 2026-11-19 | Gemini 4 Neon | Added real-time intro particle/ember canvas, beat-reactive anamorphic lens streak, live WebAudio AnalyserNode oscilloscope, staggered title-card animations, menu orbital drift and hangar panel/card motion polish. | Open | Antigravity · 9e9d50d2-h |
| 2026-11-19 | Gemini 4 Neon | Upgraded intro cinema with real radio-filtered character voice cameos (Orlov, Kipper, Vance) + auto-ducking, stereo reverb/slapback guitar bus, dual-layer backdrop crossfades, camera slam shake, live REC timecode, chapter scrubber and direct cockpit launch CTA. | Open | Antigravity · 9e9d50d2-i |
| 2026-11-19 | Gemini 4 Neon | Grounded intro cinema in the Signal Lost trilogy canon (14.03.2034 HERMES 2 / ORPHEUS 7 / RT-9 bridge -> 567-year jump to 2601 AE SIN/Sleeve era), adding Dr. Mara Lind & Halden blackbox voice cameos (8 scenes, 5 recorded voices). | Open | Antigravity · 9e9d50d2-j |
| 2026-11-19 | Gemini 4 Neon | Added live 3D WebGL2 intro camera flyby & real 3D freeze-frame halftone effect (Main.fs + intro-film.js), sharper Borderlands-style corporate satire, and drop-in intro_theme.mp3/.ogg soundtrack support. | Open | Antigravity · 9e9d50d2-k |
| 2026-11-18 | OpenAI GPT-6.2 Sol | Eisvogel twin-hull ship geometry, six freight mounts, Blender scene and matching shipyard artwork. | 1,374,254 | Codex worker 01a1f0fc |
| 2026-11-18 | OpenAI GPT-6.2 Astra | Eisvogel shipyard purchase, freight and equipment integration, engine profile, save and browser flight checks. | 4,527,693 | Codex turn 01a1f0fb |
| 2026-11-19 | Gemini 4 Neon | Expanded 3D WebGL2 intro director to 3 distinct camera shots (Kestrel station flyby, ship close-up freeze-frame, finale orbit), added intro image preloading and Signal Lost 2034 trilogy dossier cards in main menu. | Open | Antigravity · 9e9d50d2-l |
| 2026-11-18 | OpenAI GPT-6.2 Sol | Role-specific player hull details, four matching Blender ship portraits and updated Eisvogel scene. | 2,603,852 | Codex worker turn 01a1f10b |
| 2026-11-18 | OpenAI GPT-6.2 Astra | Selectable trade starts, opening balances and ownership, hangar UI, save integration, release flight gallery and gameplay verification. | 6,083,620 | Codex turn 01a1f10a |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Freight desk hold and escort budget previews, first-trade guidance, four-hull Helios route verification and benchmark reconciliation (interim 00:33:16 UTC). | 1,971,750 | Codex turn 01a1f133 |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Station work package (delegated via CLI): six distinct, larger station models with a shipyard dry dock. | 7,748,973 | Codex CLI worker · station package |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Landmark work package (delegated via CLI): AETHER Sovereign backdrop and a first jump-gate model (gates later replaced by the fly-through version). | 4,921,710 | Codex CLI worker · landmark package |
| 2026-11-19 | OpenAI GPT-6.2 Astra | World work package (delegated via CLI): asteroid variants per sector, station collision, civilian shuttle traffic. | 6,825,006 | Codex CLI worker · world package |
| 2026-11-19 | OpenAI GPT-6.2 Astra | HUD work package (delegated via CLI): label layout without overlaps, combat priorities, target instrument, radar symbols. | 6,272,604 | Codex CLI worker · HUD package |
| 2026-11-19 | Claude Fable 5.2 | Player ship work package: remodelled cutter, hauler, Sperber and Kepler bomber with navigation lights and retro thrusters. | 9,179,564 | Claude Code worker · ship package |
| 2026-11-19 | Claude Fable 5.2 | Combat effects work package: bolt styles per source, shield and hull hits, destruction tiers by ship size. | 8,409,402 | Claude Code worker · effects package |
| 2026-11-19 | Claude Fable 5.2 | Combat gameplay work package: balance audit by simulation, role behaviours, three-phase corvette, converging enemy fire, pirate look. | 21,324,509 | Claude Code worker · combat package |
| 2026-11-19 | Claude Fable 5.2 | Orchestration of both work-package rounds: activity tracking (lotse agent, watcher, prompt hook), audits, briefs, reviews, merges with the live tree and integration. | 54,253,714 | Claude Code · orchestration rounds 1–2 |
| 2026-11-19 | Claude Fable 5.2 | Eisvogel work package: courier remodelled in the new hull style with chin lasers under the skid bows, own lights, retro nozzles and upgrade sockets; legacy mesh and workarounds removed; shipyard card re-rendered from the game mesh with a reusable Blender script (includes the interrupted first worker). | 6,818,023 | Claude Code · Eisvogel package |
| 2026-11-19 | Claude Opus 5.5 | Eisvogel follow-up: shipyard card colour corrected for the Blender scene's AgX view transform, game mesh saved into the Eisvogel Blender scene (`--save-scene`), three GAMEPLAY screenshots re-shot with the new hull, combat frame-time check on a desktop GPU (1440p, MSAA and shadows on; p99 ≤ 6 ms during explosions); Claude benchmark rows of the previous night reconciled (interim 12:30 UTC). | 9,985,839 | Claude Code · Eisvogel follow-up |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Reviewed combat-save restoration, freight guidance, simulation timing and keyboard bindings. | 2,982,932 | Codex turn 01a1f420-0392 |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Fixed combat-save approach clearance, physics-based mission timers and overlapping key bindings. Integrated freight guidance and verified the flight in Chromium. | 8,013,770 | Codex turn 01a1f430-b576 |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Added freight gate directions, cockpit and radar crate markers, and route and HUD tests. | 2,373,541 | Codex worker turn 01a1f431-10f1 |
| 2026-11-19 | Gemini 4 Neon | Role-specific enemy wreck salvage (TitaniumScrap, PlasmaCores, FuelCells) with 2 t cargo-magnet guard, HUD & 3D pickup visuals, dock hull repair (Credits & salvaged TitaniumScrap), G-scan ore-asteroid radar pings, save compatibility and unit tests. | Open | Antigravity · 654056c3 |
| 2026-11-19 | OpenAI GPT-6.2 Sol | Rebuilt Kestrel, Helios and Drift-9, integrated the other three station models, updated collisions and the Kepler shuttle route, rendered map portraits and verified all six arrivals. | 9,007,855 | Codex turn 01a1f459-bfa4 |
| 2026-11-19 | OpenAI GPT-6.2 Sol | Built the Kepler yard gantry, Theta solar wings and Morgenlicht shield towers with collision geometry. | 2,746,986 | Codex worker turn 01a1f45a-412d |
| 2026-11-19 | OpenAI GPT-6.2 Sol | Built the star map and saved courses, added cockpit and station course markings, rendered two port portraits and tested both new corridors in Chromium. | 12,464,817 | Codex turn 01a1f463-8352 |
| 2026-11-19 | OpenAI GPT-6.2 Sol | Added Kaltstrom and Wracksaum, four reciprocal connections, new markets, salvage patrol, freight, archive entries and backward-compatible sector saves. | 8,797,148 | Codex worker turn 01a1f464-4c1b |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Quality work package (delegated via CLI): quality tiers Low–Ultra with automatic selection, dynamic resolution, graphics menu and in-game benchmark (`?bench`). | 6,990,835 | Codex CLI worker · quality package |
| 2026-11-19 | Claude Opus 5.5 | Lighting work package: physically based shading (GGX, Fresnel), material classes, sky-derived ambient per sector, up to 32 prioritized point lights including explosion, muzzle and bolt lights. | 57,827,736 | Claude Code worker · lighting package |
| 2026-11-19 | Claude Opus 5.5 | GPU particle work package: GPU ring-buffer particle system, engine trails, combat sparks, embers and smoke, manoeuvring thrusters, belt dust. | 40,671,484 | Claude Code worker · particles package |
| 2026-11-19 | Claude Opus 5.5 | Engine round 3 orchestration: engine audit, briefs and interface contracts, GPU frame-time tooling, reviews, merges, budget wiring to quality tiers, live apply and docs. | 25,880,859 | Claude Code · engine round 3 |
| 2026-11-19 | Claude Fable 5.2 | Graphics audit on the GPU across all six sectors; sky bodies baked per sector at screen resolution with 4x supersampling (sharp planet limbs, rings and lava veins) and a per-sector shader, cutting blocked start-up time from 2.1 s to 0.9 s; SSAO prototyped and dropped as not worth its pass on convex low-poly hulls. | 25,147,841 | Claude Code · graphics audit |
| 2026-11-19 | Gemini 4 Neon | Homing micro-missiles on J (4-shot rechargeable rack, 3D flight/trails, HUD indicator), dynamic market stock drift & live sector shortage/surplus events on travel (respecting Helios blockade), ×5 bulk trade buttons at dock markets, and unit tests. | Open | Antigravity · 654056c3-b |
| 2026-11-19 | OpenAI GPT-6.2 Sol | Added Schleierpass, trade, archive and freight, saved waypoints, market migration and patrol integration. Verified the route and encounter in Chromium. | 13,822,014 | Codex turn 01a1f471-ea53 |
| 2026-11-19 | OpenAI GPT-6.2 Sol | Built the Blocker enemy profile, mesh, close-range fan attack, projectile effects and focused regression tests. | 7,611,269 | Codex worker turn 01a1f472-5f1e |
| 2026-11-19 | OpenAI GPT-6.2 Sol | Authored and integrated two persistent wreck fields, titanium and missile salvage, HUD and save support, varied belt densities and ore shares, and verified both fields in Chromium. | 12,701,406 | Codex turn 01a1f47d-d0ce |
| 2026-11-19 | OpenAI GPT-6.2 Sol | Added eight procedural asteroid silhouettes and materials, retained all legacy variants, and verified mesh limits and seams. | 5,001,107 | Codex worker turn 01a1f47f-9987 |
| 2026-11-19 | Claude Fable 5.2 | World pass: own skies for the three rim sectors (ringed ice giant, broken moon with debris arc, nebula veil), lore-based sector and station names with call-sign gate labels, redesigned universe map (station symbols, danger marks, untangled layout, legend, sector blurbs) and docs (interim). | 15,764,229 | Claude Code · world pass |
| 2026-11-19 | Gemini 4 Neon | Added 6 new purchasable weapon/specialty kits (RapidBlaster, shield-piercing PlasmaCannon, 4-bolt ScatterLaser, MissilePod Mk II, TractorRig, OverdriveCap) plus 4 permanent stackable ship addons (MissileLoader, SalvageScanner, Emergency Countermeasures, Hull Nanites) with full UI, HUD, save compatibility and unit tests. | Open | Antigravity · 654056c3-c |
| 2026-11-19 | Claude Fable 5.2 | Story pass: three acts driven by pilot progress with nine new station calls, voices for the three rim stations, a logbook in the find archive, tests and story bible update. | 11,448,411 | Claude Code · story pass |
| 2026-11-19 | Claude Fable 5.2 | Wreck distress calls: every wreck still transmits, the pilot secures the voice by waiting in range or cuts the sender for extra scrap; a wreck in each of the nine sectors, voice archive with Sera's and Karst's verdicts, save field, tests and docs (interim). | 12,384,531 | Claude Code · wreck voices |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Built two civilian ship models, integrated sector traffic and AETHER formations, drive effects, shipping lanes and HUD identification. Verified flight and ship imagery in Chromium. | 5,283,718 | Codex turn 01a1f48a-c74d |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Modelled the AETHER patrol frigate and escort corvette, including drive sockets, navigation lights and faction detailing. | 479,010 | Codex worker turn 01a1f48b-d88a |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Detailed civilian grapple machinery, passenger gallery, boarding hatches and lifeboats, integrated military ship revisions, verified geometry and shipping lanes, and captured all four ships in Chromium. | 3,567,250 | Codex turn 01a1f4b3-6a6b |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Modelled stepped twin turrets, radar and armour for the patrol frigate, paired impulse pods for the escort and shared AETHER fleet insignia. | 287,397 | Codex worker turn 01a1f4b4-00d5 |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Remodelled the cutter, hauler and kingfisher, updated Blender materials and framing, rendered the shipyard cards and verified all five flyable hulls and loaded courier in Chromium. | 10,457,655 | Codex turn 01a1f4c1-7c4a |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Modelled the Sparrow escort wings, fins and nozzles, and the Kepler bomber observation cabin, torpedo outriggers and engine shoulders. | 517,225 | Codex worker turn 01a1f4c2-2adb |
| 2026-11-19 | Gemini 4 Neon | Implemented exclusive RT-9 onboard research lab/AI (Relais Theta unlock) and 15-node 3-branch skill-tree research system (foundations + exotic weapons Cryo-Disruptor, Phase-Railgun, Swarm Missiles, Shield-Siphon, Phase-Reflector, Quantum Hold, Tractor Field, Trade Matrix) with instant hub unlock (Theta/Kepler-07), Research UI tab, save compatibility and unit tests. | Open | Antigravity · 654056c3-d |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Start hull selection and shipyard purchases in both flight modes, fleet save compatibility and browser purchase/reload checks (interim 17:23:37 UTC). | 2,993,840 | Codex turn 01a1f4ce-0a0b |
| 2026-11-19 | Claude Fable 5.2 | Reworked the research tree around the 2601 AE lore: renamed all 15 nodes with a source and a band fragment that becomes readable on install, cross-branch prerequisites for the three apex nodes, archive finds as apex seals, breakthroughs bound to their home dock, research panel states and tests. | Open | Claude Code · research tree lore pass |
| 2026-11-19 | Gemini 4 Hydrogen | Added contextual faction radio chatter (including two-ship AETHER derogatory L/R stereo exchanges, civilian, Grey Watch and pirate lines), clean/analog radio filter profiles, in-flight HUD radio ticker, StoryRadio beat 18..29 audio slots in Sound.fs, and unit tests. | Open | Antigravity · 0140bd25 |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Art direction and integration for the RT-9 lab and 15 research illustrations, responsive design and in-game verification of installation and unlocks. | 1,365,179 | Codex turn 01a1f4e4-62cf |
| 2026-11-19 | OpenAI ImageGen (version unreported) | Generated the research relay tape-reader scene and a 15-cell research equipment atlas. | Open | ImageGen via Codex turn 01a1f4e4-62cf |
| 2026-11-19 | Gemini 4 Neon | Wired faction_00..27 audio slots and three additional AETHER/faction exchanges; applied corporate-cynic English ship names and callsigns (AETHER Grace Period, AETHER Tariff VII, AUDIT/CLAIM/ESCROW/PREMIUM/ACTUARY/LIEN/UNDERWRITER, Tug Deadweight, Hauler Cold Iron, Clipper Null-Sign), renamed sector 6 to Sperrzone Morgenlicht across code and lore docs, built tools/generate-music.py and upgraded Sound.fs cockpit radio mastering, built Gemini intro/soundtrack/TTS pipelines (tools/generate-intro-gemini.py, tools/generate-voices-gemini.py), rendered the 25s 4K Gemini Omni 1.2 Flash cinema intro (space2601-omni-part1.mp4 + space2601-omni.mp4) wired into a seamless two-reel player in public/intro-film.js with fallback to ruecklauf.mp4, generated two strictly instrumental Nordic war-drum tracks via Lyria 3.5 (music_calm_lyria.mp3, music_combat_lyria.mp3) wired into Sound.fs, composed and wired a dark, oppressive 102 BPM D-Phrygian/tritone AETHER hegemony march (music_aether.wav with contrabass BRAAAM swells, iron bulkhead strikes, darker parade snares and low trombones) triggered by AETHER cruiser/patrol proximity and Bastion Morgenlicht presence (Sound.fs, Main.fs, tests/sound.test.mjs), ran full naturalness and AETHER-darkening passes across all 51 radio dialogues, 38 station-approach/cockpit/harbor VHF clips (comm_*.mp3, chatter_0..15.mp3), 11 wreck distress calls, 15 research node logs/branch names, 9 dock bulletins, 9 relic archive entries, sector subtitles, pilot profile origins, logbook entries and RT-9 lab/menu copy (StoryRadio.fs, WreckFields.fs, Research.fs, Shop.fs, Universe.fs, PilotSkills.fs, DockContact.fs, pilot-profile.js, index.html), and restored all 14 Gemini 3.8 Flash TTS .wav bindings in Sound.fs alongside freshly re-rendered native German (+0Hz) MP3s for all remaining station/flight clips. | Open | Antigravity · 0140bd25-b |
| 2026-11-19 | Claude Fable 5.2 | Dystopian art pass on the ship models: weathered palettes, per-plate wear with primer and bare replacement plates, soot towards the stern, rust and oil runs, blacked-out canopies and a struck SIN registration barcode on the four player hulls; cold platinum AETHER frigate and corvette with red scanners; grimy tug, liner and port launch; night-dock lighting for the shipyard cards, re-rendered cards and documentation images, and tests that guard the look. | Open | Claude Code · ship model art pass |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Integrated rim dock interiors and bulletins, clandestine onboard lab and research availability, model gallery, fleet inspection optics and restored dock-time shipyard refresh. Checked remote main, tests and browser flows. | 5,959,495 | Codex turn 01a1f55b-e4b7 |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Modelled five hull-specific inspection and repair details, rendered shipyard cards from the game meshes and synchronized the Eisvogel Blender scene. | 1,846,435 | Codex worker turn 01a1f55c-a7e7 |
| 2026-11-19 | OpenAI ImageGen (version unreported) | Generated three rim-port interiors and the installed RT-9 aboard the player ship. | Open | ImageGen via Codex turn 01a1f55b-e4b7 |
| 2026-11-19 | Claude Opus 5.5 | Token accounting fix and in-game token info: the cost ledger subtracted Gemini cache reads a second time from input that already excludes them, dropped Gemini steps without a model label and counted session work from before the project start; Claude and Codex counts verified (no duplicate requests, reasoning inside output). The ledger now totals tokens, effective cost and Ø cost per 1M tokens per AI and model, writes `public/token-info.json`, and a new **ⓘ KI-Token & Kosten** button in the main menu shows them per AI; shell-write heuristic no longer mistakes `=>` for a redirect; recount logged as its own ledger entry; tests. | 13,121,526 | Claude Code · token info panel |
| 2026-11-19 | Claude Haiku 5.5 | Cost ledger entry with the previous counting, recorded right before the token recount (delegated by Opus). | 173,210 | Claude Code · ledger entry before recount |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Integrated clock-driven AETHER seizures of civilian shift ferries, boarding bridge, registry transfer, scoped broadcasts and shipping-lane clearance. Added regression tests, captured a gameplay clip and verified the encounter in Chromium. | 9,645,971 | Codex turn 01a1f56e-244e |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Remodelled the AETHER frigate and escort corvette with an open command crown, sensor iris, flank insignia, sickle wings and scanner pods. | 299,673 | Codex worker turn 01a1f56e-81c3 |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Directed and edited Rücklauf, captured the ferry seizure from the game, composited the interiors and replaced the old intro with a keyboard-accessible film player. Verified playback, failure recovery and entry into the game. | 2,828,784 | Codex turn 01a1f584-860e |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Composed and mixed the 26-second stereo soundtrack for Rücklauf, including the flyby, docking latch and radio cutoff. | 395,797 | Codex worker turn 01a1f585-17f9 |
| 2026-11-19 | OpenAI ImageGen (version unreported) | Generated the occupied ferry cabin and the courier cockpit inserts for Rücklauf. | Open | ImageGen via Codex turn 01a1f584-860e |
| 2026-11-19 | Claude Sonnet 5.5 | Rebuilt and restarted the game container; worked around Docker Compose's failing bake step with a plain image build. | 1,761,171 | Claude Code · container rebuild |
| 2026-11-19 | Claude Opus 5.5 | Sector variety after playtest feedback: each sector gets its own asteroid landscape (belt, slag stream, drift pockets, swept space, rock rampart, ice nests, debris stream, nebula boulders) with its own rock colours; ore only in named nests at least 450 m from the station, under a crust that takes 15–20 laser hits, and fewer crystals; nest markers with remaining veins; a sector operations director with distress calls, bounties and flotsam far from the station, fights spawned on site and paid on completion; tests and GAMEPLAY notes. | 43,013,602 | Claude Code · sector variety |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Built nine harbour installations and a paid local courier route at each. Added timed randomized dispatches, recoverable lifeboats, pirate interceptions, and saved encounter state. Verified payouts, collision clearance and combat reload in tests and in Chromium. | 6,568,038 | Codex turn 01a1f599-b5c9 |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Reworked automatic flight radio into positional receiver cues with subtitles, added a persistent opt-in for recorded voice, and fixed queued playback on first load. Verified audio and story tests. | 2,377,270 | Codex worker turn 01a1f59a-0665 |
| 2026-11-19 | Claude Opus 5.5 | Sector sights after "sectors are empty and boring": procedural landmarks with blinking beacons and point lights in every sector (drill rigs, the twelve distress buoys, hull skeletons, a listening array, defence platforms, a comet with a soft sprite tail, a broken cruiser, customs and slag masts), placed at the operation sites clear of station, gates and shipping lanes; two mining ships with drilling beams at every ore nest that move on when their rock is gone; HUD names within 700 m; container rebuilt; tests and GAMEPLAY notes (interim 21:33 UTC). | 37,195,883 | Claude Code · sector sights |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Integrated the single roaming Doppel-A cruiser, saved eight-port itinerary, chart trace, tracking camera and optical scan. Wrote both German radio exchanges, integrated eight voice clips, and verified the encounter in Chromium and regression tests (interim 21:27:28 UTC). | 6,766,741 | Codex turn 01a1f5aa-bc75 |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Modelled the 100.5 m register cruiser, its offset camera, registry markings, office decks, and articulated underbody optic. | 1,705,461 | Codex worker turn 01a1f5ab-1dc6 |
| 2026-11-19 | Microsoft Neural TTS (version unreported) | Synthesized eight optional German Doppel-A radio clips with KatjaNeural and JonasNeural. | Open | Edge TTS via Codex turn 01a1f5aa-bc75 |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Nine exterior expeditions, distinct sector fields and traffic, staged scans, three alien artifacts, ambushes, payouts, dock archive and save compatibility. Implemented and checked the trade flight, browser views and regression coverage. | 9,241,096 | Codex turn 01a1f5bc-e16b |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Modeled and animated all nine exterior sites and their guide ships and beacons, including reactive ring segments and the twin tow. Tested geometry, instancing and light cues. | 2,033,253 | Codex worker 01a1f5bd-3819 |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Added 18 sector points of interest, freight collection, receiver surveys, service couplers, calibrated ring crossings, selectable map and HUD bearings, save compatibility and flight checks (interim 22:05:00 UTC). | 7,867,695 | Codex turn 01a1f5d0-0e0b |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Built and tested animated 3D geometry for the four POI families with sector variants, receiver lights and a clear flight aperture. | 833,211 | Codex worker 01a1f5d0-ad88 |
<!-- /ai-benchmark:changes -->

<!-- kostenbuch:start -->
### Effective cost & agent time (estimated)

*Who did what, where, and what it effectively cost. **Effective cost** = the tokens each model used, priced at public **API list prices** (USD); the subscriptions actually paid (Claude, ChatGPT/Codex, Gemini) are not deducted. **Tokens** = input (new, cache reads and cache writes) + output (including reasoning), counted as in the change log; **Ø per 1M** = effective cost ÷ tokens, an average over all token kinds (far below the input list price because most input is read from the prompt cache). Read from the local session logs of Claude Code, Codex and Gemini (Antigravity) from the project start on. Agent time = the agents working, credited to each session's main model; idle = waiting for user input (gaps over 120 min count as breaks). Rough estimates, not an invoice; the change log above stays the measured token record. The **ⓘ KI-Token & Kosten** button in the game's main menu shows the same figures (`public/token-info.json`). Maintained by the `kostenbuch` Haiku agent (`tools/kostenbuch/`), last update 2026-11-19 21:26 (UTC+1).*

**Total effective cost: $585 (≈ 531 €)** for 1.26 B tokens (Ø $0.46 per 1M) · agent working time 40 h 49 min (wall clock 22 h 28 min) · idle 27 h 08 min

| AI | Models | Tokens | Token share | Effective cost | Cost share | Ø per 1M tokens |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Claude (Anthropic) | 4 | 631.6 M | 50 % | $290 | 50 % | $0.46 |
| GPT (OpenAI) | 4 | 410.2 M | 33 % | $241 | 41 % | $0.59 |
| Gemini (Google) | 3 | 218.1 M | 17 % | $54 | 9 % | $0.25 |

| Tool | Sessions | Effective cost | Agent time | Wall clock | Idle |
| --- | ---: | ---: | ---: | ---: | ---: |
| Claude Code | 52 | $290 | 17 h 42 min | 12 h 28 min | 2 h 52 min |
| Codex | 40 | $241 | 19 h 21 min | 15 h 21 min | 9 h 15 min |
| Gemini | 14 | $54 | 3 h 47 min | 3 h 41 min | 15 h 01 min |

| Model | Tool | Requests | Input tokens | Output tokens | Total tokens | Effective cost | Ø per 1M | Agent time | Main files written |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| GPT-6.2 Astra | Codex | 2,306 | 284.5 M (280.0 M cached) | 1.0 M | 285.6 M | $190 | $0.67 | 17 h 39 min | `Main.fs`, `game-save.test.mjs`, `PhysicsAndPool.fs` |
| Claude Fable 5.2 | Claude Code | 863 | 313.5 M (309.3 M cached) | 1.5 M | 314.9 M | $180 | $0.57 | 11 h 15 min | `Main.fs`, `GAMEPLAY.md`, `index.html` |
| Claude Opus 5.5 | Claude Code | 1,044 | 294.2 M (290.9 M cached) | 1.2 M | 295.4 M | $103 | $0.35 | 6 h 04 min | `App.fsproj`, `Main.fs`, `PhysicsAndPool.fs` |
| GPT-6 Astra | Codex | 312 | 37.3 M (36.7 M cached) | 271 k | 37.6 M | $38 | $1.02 | 55 min | `ProceduralMeshes.fs`, `flight-assist.test.mjs`, `App.fsproj` |
| Gemini 4 Hydrogen | Gemini | 582 | 140.9 M (132.0 M cached) | 545 k | 141.5 M | $27 | $0.19 | 1 h 13 min | `Sound.fs`, `Main.fs`, `HUD.fs` |
| Gemini 4 Neon | Gemini | 276 | 71.5 M (68.3 M cached) | 312 k | 71.8 M | $24 | $0.34 | 2 h 20 min | `README.md`, `index.html`, `hangar.css` |
| GPT-6.2 Sol | Codex | 401 | 86.0 M (84.8 M cached) | 187 k | 86.2 M | $13 | $0.15 | 46 min | `App.fsproj`, `Main.fs`, `GAMEPLAY.md` |
| Claude Sonnet 5.5 | Claude Code | 40 | 16.4 M (15.4 M cached) | 67 k | 16.5 M | $6.17 | $0.37 | 0 min | `index.html`, `Spiel-starten.bat`, `KOSTEN.md` |
| Gemini 4 Argon | Gemini | 32 | 4.8 M (4.3 M cached) | 38 k | 4.8 M | $1.66 | $0.34 | 14 min | `index.html`, `Main.fs`, `HUD.fs` |
| Claude Haiku 5.5 | Claude Code | 153 | 4.5 M (3.2 M cached) | 258 k | 4.7 M | $0.80 | $0.17 | 23 min | – |
| GPT-6.1 Sol | Codex | 7 | 829 k (719 k cached) | 5 k | 834 k | $0.34 | $0.41 | 0 min | – |

**Who did what, where** (latest entries)

- **2026-11-19 21:26 (UTC+1)** · Sound engine, main menu, gameplay notes and captured media changed since last entry.
  - **GPT-6.2 Astra** (Codex) +$0.96
  - **Gemini 4 Neon** (Gemini) +$0.70 — Sample-based sound engine: radio, ambience and capital-ship cues · `Sound.fs`
  - **Claude Sonnet 5.5** (Claude Code) +$0.09 — Main menu markup: audio mixer, graphics and ship emblem settings · `index.html`
  - **Claude Haiku 5.5** (Claude Code) +$0.04
- **2026-11-19 21:24 (UTC+1)** · AETHER gate seizure encounter, traffic lanes, engine trails, intro film music, tests.
  - **GPT-6.2 Astra** (Codex) +$2.64 — AETHER ferry seizure encounter at two gates, traffic lanes, engine trails, tests · `Seizure.fs`, `GAMEPLAY.md`, `README.md`, `Traffic.fs` +6
  - **Gemini 4 Neon** (Gemini) +$0.68 — Intro film scenes and procedural war-drum soundtrack · `README.md`, `intro-film.js`
  - **Claude Sonnet 5.5** (Claude Code) +$0.77
  - **Claude Haiku 5.5** (Claude Code) +$0.14
- **2026-11-19 21:16 (UTC+1)** · Civilian and AETHER ship meshes, a seizure encounter timetable, and a main-menu token dialog.
  - **GPT-6.2 Astra** (Codex) +$1.98 — Civilian tug and liner meshes, AETHER warship details, seizure encounter timing. · `AetherTrafficMeshes.fs`, `CivilTrafficMeshes.fs`, `App.fsproj`, `Seizure.fs`
  - **Claude Opus 5.5** (Claude Code) +$2.62 — Main-menu token and cost dialog with styles, tests, and ledger updates. · `README.md`, `MEMORY.md`, `index.html`, `token-info.test.mjs` +3
  - **Gemini 4 Neon** (Gemini) +$0.06
  - **Claude Haiku 5.5** (Claude Code) +$0.02
- **2026-11-19 21:10 (UTC+1)** · Token recount: Gemini input was undercounted (cache subtracted twice, steps without a model label skipped); tokens are now totalled per AI.
  - **GPT-6.2 Astra** (Codex) +$0.45
  - **Claude Opus 5.5** (Claude Code) +$1.13 — Token accounting fix, per-AI token totals and in-game token info panel · `kostenbuch.mjs`, `token-info.js`
  - **Gemini 4 Hydrogen** (Gemini) +$3.28 — Recount only: uncached input and unlabelled steps now counted
  - **Gemini 4 Neon** (Gemini) +$2.82 — Recount only: uncached input tokens now counted
  - **Gemini 4 Argon** (Gemini) +$0.30 — Recount only: uncached input tokens now counted
- **2026-11-19 21:07 (UTC+1)** · Helios blockade, start hangar, sky and mesh rendering, radio voiceovers, intro film, and benchmark tooling.
  - **GPT-6.2 Astra** (Codex) +$5.58 — Helios blockade clearance and commissions, start hangar, ship catalog tests · `blockade.test.mjs`, `Main.fs`, `ResearchUi.fs`, `GAMEPLAY.md` +8
  - **Claude Fable 5.2** (Claude Code) +$17 — Sky backdrop, wreck fields, landmark and station mesh updates, benchmark totals · `kostenbuch.mjs`, `LandmarkMeshes.fs`, `GAMEPLAY.md`, `StationMeshes.fs` +8
  - **Claude Opus 5.5** (Claude Code) +$2.11 — Start page markup and inline styles, package scripts · `index.html`, `kostenbuch.mjs`, `package.json`
  - **Gemini 4 Neon** (Gemini) +$2.49 — Radio voiceovers, intro film, sound engine, story radio and hangar styling · `README.md`, `hangar.css`, `Main.fs`, `index.html` +4
  - **Claude Haiku 5.5** (Claude Code) +$0.01 — Cost ledger script maintenance · `kostenbuch.mjs`
- **2026-11-19 19:07 (UTC+1)** · Save games, starter ships, story radio, research and cruiser sorties changed since last entry.
  - **GPT-6.2 Astra** (Codex) +$1.50 — Save game storage, save UI, graphics settings and tests · `index.html`, `hangar.css`, `README.md`, `rt9-am-relais.webp` +8
  - **Claude Fable 5.2** (Claude Code) +$14 — Starter ship art, start hangar, traffic and mesh tests, benchmark tooling · `README.md`, `benchmark-totals.mjs`, `kostenbuch.mjs`, `Main.fs` +8
  - **Gemini 4 Hydrogen** (Gemini) +$0.25 — Sound effects and story radio transmissions with tests · `Sound.fs`, `StoryRadio.fs`, `Main.fs`, `story-radio.test.mjs` +1
  - **Gemini 4 Neon** (Gemini) +$2.00 — Story and lore texts, HUD, universe and research tree updates · `Main.fs`, `README.md`, `hangar.css`, `index.html` +5
  - **GPT-6.2 Sol** (Codex) +$0.36 — Capital cruiser sorties, camera work and ship emblems · `App.fsproj`, `Main.fs`, `GAMEPLAY.md`, `index.html` +5
  - **Claude Haiku 5.5** (Claude Code) +$0.01
- **2026-11-19 18:31 (UTC+1)** · Research tree, starting hangar, radio transmissions, ship emblem and save validation updated since last entry.
  - **GPT-6.2 Astra** (Codex) +$0.00 — Starting ship selection, shipyard UI and save-game validation · `Main.fs`, `index.html`, `GAMEPLAY.md`, `hangar.css` +8
  - **Claude Fable 5.2** (Claude Code) +$4.19 — Research tree UI, lab installation and docking price terms · `GAMEPLAY.md`, `README.md`, `research.test.mjs`, `TradeSystem.fs` +6
  - **Gemini 4 Hydrogen** (Gemini) +$0.39 — Story radio transmissions and audio mix settings · `story-radio.test.mjs`, `Main.fs`, `StoryRadio.fs`, `HUD.fs` +1
  - **GPT-6.2 Sol** (Codex) +$0.92 — Ship emblem, WebGL renderer, procedural meshes and sky backdrop · `GAMEPLAY.md`, `ShipEmblem.fs`, `README.md`, `WebGLRenderer.fs` +8
  - **Claude Haiku 5.5** (Claude Code) +$0.04
- **2026-11-19 18:24 (UTC+1)** · Trade terms, research tree, turret, mission loadouts, input and saves changed; docs updated.
  - **GPT-6.2 Astra** (Codex) +$2.89 — Trade terms, sector gates, input controls and start-ship selection. · `Main.fs`, `index.html`, `GAMEPLAY.md`, `hangar.css` +8
  - **Gemini 4 Neon** (Gemini) +$1.43 — Research lab and tech tree, turret and mission loadout changes. · `research.test.mjs`, `README.md`, `Main.fs`, `Shop.fs` +8
  - **Claude Haiku 5.5** (Claude Code) +$0.02

<sub>Prices per 1M tokens and sources: `tools/kostenbuch/preise.json`. Agent time sums parallel agents; wall clock counts overlapping work once. Files are attributed from each tool's edit calls (shell edits by heuristic).</sub>
<!-- kostenbuch:end -->
