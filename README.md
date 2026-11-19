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

- **6-Sector Star Network:** Outposts, docking approach states, jump gates, and mining fields (`src/Universe.fs`, `src/DockContact.fs`).
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
| Claude Fable 5.2 | 147,594,668 | 0 |
| Claude Haiku 5.5 | 5,482 | 0 |
| Claude Sonnet 5.5 | 6,799,501 | 3 |
| OpenAI GPT-6.2 Astra | 111,570,026 | 0 |
| Gemini 3.8 Flash | Open | 1 |
| Gemini (version unreported) | Open | 1 |
| Claude Opus 5.5 | 151,674,967 | 0 |
| Gemini 4 Neon | Open | 13 |
| OpenAI GPT-6.2 Sol | 6,579,957 | 0 |
| **Project · measured subtotal** | **424,224,601** | **18** |
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
| 2026-11-19 | OpenAI GPT-6.2 Astra | Fixed combat-save approach clearance, physics-based mission timers and overlapping key bindings. Integrated freight guidance and verified the flight in Chromium (interim 14:34:44 UTC). | 7,434,240 | Codex turn 01a1f430-b576 |
| 2026-11-19 | OpenAI GPT-6.2 Astra | Added freight gate directions, cockpit and radar crate markers, and route and HUD tests. | 2,373,541 | Codex worker turn 01a1f431-10f1 |
| 2026-11-19 | Gemini 4 Neon | Role-specific enemy wreck salvage (TitaniumScrap, PlasmaCores, FuelCells) with 2 t cargo-magnet guard, HUD & 3D pickup visuals, dock hull repair (Credits & salvaged TitaniumScrap), G-scan ore-asteroid radar pings, save compatibility and unit tests. | Open | Antigravity · 654056c3 |
<!-- /ai-benchmark:changes -->

<!-- kostenbuch:start -->
### Effective cost & agent time (estimated)

*Who did what, where, and what it effectively cost. **Effective cost** = the tokens each model used, priced at public **API list prices** (USD); the subscriptions actually paid (Claude, ChatGPT/Codex, Gemini) are not deducted. Read from the local session logs of Claude Code, Codex and Gemini (Antigravity). Agent time = the agents working, credited to each session's main model; idle = waiting for user input (gaps over 120 min count as breaks). Rough estimates, not an invoice; the change log above stays the measured token record. Maintained by the `kostenbuch` Haiku agent (`tools/kostenbuch/`), last update 2026-11-19 16:01 (UTC+1).*

**Total effective cost: $452 (≈ 411 €)** · agent working time 32 h 48 min (wall clock 18 h 12 min) · idle 20 h 34 min

| Tool | Sessions | Effective cost | Agent time | Wall clock | Idle |
| --- | ---: | ---: | ---: | ---: | ---: |
| Claude Code | 35 | $218 | 14 h 14 min | 9 h 08 min | 1 h 58 min |
| Codex | 31 | $200 | 15 h 25 min | 12 h 06 min | 7 h 49 min |
| Gemini | 13 | $34 | 3 h 08 min | 3 h 02 min | 10 h 47 min |

| Model | Tool | Requests | Input tokens | Output tokens | Effective cost | Agent time | Main files written |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| GPT-6.2 Astra | Codex | 1,954 | 241.6 M (237.8 M cached) | 874 k | $160 | 14 h 07 min | `Main.fs`, `App.fsproj`, `ProceduralMeshes.fs` |
| Claude Fable 5.2 | Claude Code | 541 | 172.5 M (169.5 M cached) | 1.0 M | $115 | 6 h 08 min | `Main.fs`, `index.html`, `PhysicsAndPool.fs` |
| Claude Opus 5.5 | Claude Code | 967 | 281.2 M (278.1 M cached) | 1.1 M | $97 | 7 h 52 min | `App.fsproj`, `Main.fs`, `PhysicsAndPool.fs` |
| GPT-6 Astra | Codex | 312 | 37.3 M (36.7 M cached) | 271 k | $38 | 55 min | `ProceduralMeshes.fs`, `flight-assist.test.mjs`, `App.fsproj` |
| Gemini 4 Hydrogen | Gemini | 527 | 133.6 M (126.8 M cached) | 496 k | $23 | 1 h 34 min | `Main.fs`, `HUD.fs`, `SFX_CODEX.md` |
| Gemini 4 Neon | Gemini | 113 | 31.9 M (31.2 M cached) | 123 k | $8.73 | 1 h 21 min | `Main.fs`, `README.md`, `hangar.css` |
| Claude Sonnet 5.5 | Claude Code | 31 | 14.7 M (13.9 M cached) | 63 k | $5.30 | 0 min | `Main.fs`, `Spiel-starten.bat`, `docker-compose.yml` |
| Gemini 4 Argon | Gemini | 32 | 4.6 M (4.3 M cached) | 38 k | $1.36 | 14 min | `index.html`, `Main.fs`, `HUD.fs` |
| GPT-6.2 Sol | Codex | 113 | 6.5 M (6.4 M cached) | 35 k | $1.30 | 23 min | `ProceduralMeshes.fs`, `eisvogel.blend`, `PlayerMeshes.fs` |
| Claude Haiku 5.5 | Claude Code | 80 | 1.9 M (1.3 M cached) | 143 k | $0.39 | 14 min | `kostenbuch.mjs` |
| GPT-6.1 Sol | Codex | 7 | 829 k (719 k cached) | 5 k | $0.34 | 0 min | – |

**Who did what, where** (latest entries)

- **2026-11-19 16:01 (UTC+1)** · Save-game validation, trade routes, freight UI, GPU particles, sky backdrop, graphics quality and ledger script changed.
  - **GPT-6.2 Astra** (Codex) +$14 — Save-game validation, trade route escorts, freight UI and HUD tests. · `hud-layout.test.mjs`, `Main.fs`, `GameSave.fs`, `game-save.test.mjs` +8
  - **Claude Opus 5.5** (Claude Code) +$34 — GPU particle system, sky backdrop baking, graphics quality settings and lighting. · `README.md`, `benchmark-totals.mjs`, `GraphicsSettings.fs`, `GraphicsQuality.fs` +8
  - **Gemini 4 Neon** (Gemini) +$0.61
  - **Claude Haiku 5.5** (Claude Code) +$0.01 — Cost ledger script changes. · `kostenbuch.mjs`
- **2026-11-19 14:21 (UTC+1)** · Benchmark records and session log updated since the last ledger entry.
  - **GPT-6.2 Astra** (Codex) +$0.19 · `Main.fs`
  - **Claude Opus 5.5** (Claude Code) +$4.97 — Benchmark records and session log updated with Claude token reconciliation · `SESSIONS.md`, `README.md`
  - **Claude Haiku 5.5** (Claude Code) +$0.01
- **2026-11-19 13:29 (UTC+1)** · Shop, freight and yard UI, player ship meshes, intro film and ship card renders changed.
  - **GPT-6.2 Astra** (Codex) +$2.05 — Trade, freight, yard UI, station collision and save panel updates · `Main.fs`, `index.html`, `GAMEPLAY.md`, `FreightUi.fs` +8
  - **Claude Fable 5.2** (Claude Code) +$6.10 — Player ship meshes, outfit visuals and render scripts for ship cards · `README.md`, `GAMEPLAY.md`, `benchmark-totals.mjs`, `render-ship-card.py` +8
  - **Claude Opus 5.5** (Claude Code) +$7.03 — Save-game format and Eisvogel ship card render updates · `README.md`, `benchmark-totals.mjs`, `gpufps.mjs`, `render-ship-card.py` +8
  - **Gemini 4 Neon** (Gemini) +$1.94 — Opening intro film scenes and hangar UI styling updates · `Main.fs`, `README.md`, `hangar.css`, `index.html` +1
  - **Claude Haiku 5.5** (Claude Code) +$0.02 — Cost ledger script updates · `kostenbuch.mjs`
- **2026-11-19 01:07 (UTC+1)** · Since the last entry: flight school, start hangar, boss escorts, pirate factions, intro film and ship models changed.
  - **GPT-6.2 Astra** (Codex) +$4.16 — Flight school practice targets and start hangar hull selection · `README.md`, `benchmark-totals.mjs`, `GAMEPLAY.md`, `kutter.webp` +8
  - **Claude Fable 5.2** (Claude Code) +$14 — Boss escort reserve, pirate faction looks, and headless combat simulation tests · `GAMEPLAY.md`, `README.md`, `benchmark-totals.mjs`, `PhysicsAndPool.fs` +8
  - **Gemini 4 Neon** (Gemini) +$1.50 — Intro film story scenes and hangar screen styling · `Main.fs`, `README.md`, `hangar.css`, `intro-film.js` +1
  - **GPT-6.2 Sol** (Codex) +$0.44 — Eisvogel hull model and procedural player mesh changes · `ProceduralMeshes.fs`, `eisvogel.blend`, `PlayerMeshes.fs`, `extract.mjs` +1
  - **Claude Haiku 5.5** (Claude Code) +$0.03 — Cost ledger script changes · `kostenbuch.mjs`
- **2026-11-19 00:33 (UTC+1)** · Minor follow-up work on ship meshes and the werft model since last entry.
  - **GPT-6.2 Astra** (Codex) +$0.23 · `GAMEPLAY.md`
  - **Claude Fable 5.2** (Claude Code) +$0.51 · `AI.fs`, `EnemyProfiles.fs`, `combat.test.mjs`
  - **GPT-6.2 Sol** (Codex) +$0.02 — Minor follow-up work on ship meshes and werft model · `ProceduralMeshes.fs`, `eisvogel.blend`
- **2026-11-19 00:33 (UTC+1)** · Freight contracts, jump gate flight, enemy AI and missions, shipyard UI, and visual assets since last entry.
  - **GPT-6.2 Astra** (Codex) +$16 — Freight contract UI, hangar styling, ship visuals and save tests · `HUD.fs`, `README.md`, `eisvogel.webp`, `ProceduralMeshes.fs` +8
  - **Claude Fable 5.2** (Claude Code) +$34 — Enemy AI, mission stages, combat simulation tests and save handling · `combat.test.mjs`, `player-meshes.test.mjs`, `GAMEPLAY.md`, `gameplay.test.mjs` +8
  - **Gemini 4 Neon** (Gemini) +$2.38 — Shipyard and blockade UI, intro film, upgrade icons and favicon · `README.md`, `hangar.css`, `index.html`, `intro-film.js` +6
  - **GPT-6.2 Sol** (Codex) +$0.84 — Jump gate flight physics, ship mesh and universe updates · `eisvogel.blend`, `package.json`, `ProceduralMeshes.fs`, `HUD.fs` +5
  - **Claude Haiku 5.5** (Claude Code) +$0.02 — Cost ledger script adjusted · `kostenbuch.mjs`
- **2026-11-18 23:46 (UTC+1)** · Game save, flight optics, trade route cards and cost ledger tooling changed since last entry.
  - **GPT-6.2 Astra** (Codex) +$7.08 — Game save/restore with tests, regional asteroid meshes, flight optics · `Main.fs`, `GAMEPLAY.md`, `HUD.fs`, `Shop.fs` +8
  - **Claude Fable 5.2** (Claude Code) +$12 — Cost ledger tooling: activity hook, watcher, background runner, agent setup · `aktiv-hook.mjs`, `kostenbuch.mjs`, `kostenbuch.mjs`, `aktiv-hook.mjs` +4
  - **Claude Opus 5.5** (Claude Code) +$1.55 — Benchmark totals script, project build file, HUD and main updates · `App.fsproj`, `Main.fs`, `README.md`, `benchmark-totals.mjs` +3
  - **Gemini 4 Hydrogen** (Gemini) +$0.07
  - **Gemini 4 Neon** (Gemini) +$0.98 — Trade route tests and hangar route-card styling · `README.md`, `hangar.css`, `Main.fs`, `trade-routes.test.mjs`
  - **Claude Haiku 5.5** (Claude Code) +$0.06 — Cost ledger script maintenance · `kostenbuch.mjs`
- **2026-11-18 23:33 (UTC+1)** · Six-sector universe, sky backdrop bodies, trade route fees, shader polish and cost ledger tooling.
  - **GPT-6.2 Astra** (Codex) +$1.46 — Six-sector universe, sky backdrop bodies, camera zoom and HUD gate markers · `ProceduralMeshes.fs`, `Main.fs`, `HUD.fs`, `Camera.fs` +8
  - **Claude Opus 5.5** (Claude Code) +$3.15 — Cost ledger script, price table and Haiku agent definition · `kostenbuch.md`, `preise.json`, `kostenbuch.mjs`
  - **Gemini 4 Neon** (Gemini) +$1.28 — Trade route fees, route cards, renderer shading and shader tests · `Main.fs`, `TradeRoutes.fs`, `Shop.fs`, `HUD.fs` +3
  - **Claude Haiku 5.5** (Claude Code) +$0.21

<sub>Prices per 1M tokens and sources: `tools/kostenbuch/preise.json`. Agent time sums parallel agents; wall clock counts overlapping work once. Files are attributed from each tool's edit calls (shell edits by heuristic).</sub>
<!-- kostenbuch:end -->
