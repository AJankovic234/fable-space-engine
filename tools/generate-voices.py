"""Batch-generate natural German MP3 voiceovers for Space 2601AE via edge-tts.

1. Pulls the exact rewritten natural German story & faction scripts from
   tools/generate-voices-gemini.py (single source of truth) and renders MP3s
   for any clip that doesn't already have a Gemini 3.8 Flash TTS .wav file.
2. With --comms (or --all), also renders all 22 station-approach/event comms
   (comm_*.mp3) and 16 ambient harbor VHF chatter clips (chatter_0..15.mp3)
   in terse, natural spoken German using native German/Austrian/Swiss voices
   strictly at +0Hz pitch (zero phase-vocoder artifacts).
"""

from __future__ import annotations

import argparse
import asyncio
import importlib.util
from pathlib import Path
import edge_tts

ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "public" / "assets" / "sfx"
GEMINI_SCRIPT = ROOT / "tools" / "generate-voices-gemini.py"

# Native German voice mapping by Gemini voice persona (strictly +0Hz pitch).
VOICE_MAP = {
    "Algenib": ("de-CH-JanNeural", "-4%"),        # Voss (older weathered dispatcher)
    "Umbriel": ("de-DE-ChristophNeural", "-5%"),  # Orlov / Karst / Hauler (tired, gruff)
    "Puck":    ("de-AT-JonasNeural", "+4%"),      # Kipper / Pirates (quick, casual)
    "Aoede":   ("de-DE-TanjaNeural", "+1%"),      # Mara Lind / Grey Watch (clear, focused)
    "Charon":  ("de-DE-ChristophNeural", "-1%"),  # Vance / AETHER Dispatch (calm, authoritative)
    "Orus":    ("de-CH-JanNeural", "+1%"),        # AETHER patrol male (dry, matter-of-fact)
    "Fenrir":  ("de-AT-JonasNeural", "+0%"),      # AETHER officer casual
    "Kore":    ("de-AT-IngridNeural", "+1%"),     # AETHER officer female (cool, crisp)
    "Leda":    ("de-CH-LeniNeural", "+2%"),       # Tamm / Raureif (blunt, practical)
    "Schedar": ("de-CH-JanNeural", "-1%"),        # Aders / Halden / Grey Watch
    "Zephyr":  ("de-CH-LeniNeural", "-2%"),       # Sera / Theta Relay (quiet, clear)
}

# Terse, realistic German VHF station-approach, cockpit & ambient harbor clips (strictly +0Hz pitch).
COMM_AND_CHATTER_CLIPS = [
    # Station approach & docking clearances
    ("comm_kestrel",          "de-CH-JanNeural",       "-2%", "Kestrel Kontrolle. Hab dich auf dem Schirm, Bucht zwei ist frei."),
    ("comm_helios",           "de-DE-ChristophNeural", "-3%", "Helios Vier, Anflug erfasst. Tempo rausnehmen und Abstand zum Tor halten."),
    ("comm_bazaar",           "de-AT-JonasNeural",     "+2%", "Drift-9 hier. Such dir eine freie Klammer, kein Zollscan heute."),
    ("comm_kepler",           "de-AT-IngridNeural",    "+0%", "Werftkontrolle Kepler. Transponder erkannt, Einflugkorridor freigegeben."),
    ("comm_aether",           "de-DE-TanjaNeural",     "+0%", "Relais Theta, wir hören dich. Halte dich aus dem Antennenkegel raus."),
    ("comm_bastion",          "de-DE-ChristophNeural", "-4%", "Sperrzone Morgenlicht. Anflug ohne Platin-Freigabe wird als Verstoß gewertet. Drehen Sie ab."),
    ("comm_ready",            "de-CH-JanNeural",       "+0%", "Klammern bereit. Kannst andocken."),
    ("comm_blocked",          "de-CH-JanNeural",       "+1%", "Dock bleibt zu, solange draußen geschossen wird."),

    # Flight & combat status callouts (calm, grounded, zero game-show announcer drama)
    ("comm_pirate_ambush",    "de-CH-LeniNeural",      "+2%", "Achtung, zwei Kontakte ohne Kennung direkt im Anflug."),
    ("comm_sector_clear",     "de-CH-LeniNeural",      "+0%", "Radar wieder sauber."),
    ("comm_shield_down",      "de-DE-TanjaNeural",     "+2%", "Schild ist runter."),
    ("comm_hull_critical",    "de-DE-TanjaNeural",     "+4%", "Hülle im roten Bereich, sofort aus dem Feuer."),
    ("comm_wave_start",       "de-CH-JanNeural",       "+1%", "Neue Signale am Sektorrand."),
    ("comm_wave_clear",       "de-CH-JanNeural",       "+0%", "Alle Kontakte weg."),
    ("comm_upgrade_ready",    "de-AT-JonasNeural",     "+0%", "Einbau abgeschlossen, Systeme stehen."),
    ("comm_trade_profit",     "de-CH-LeniNeural",      "+0%", "Fracht ist verbucht."),
    ("comm_jump_ready",       "de-AT-IngridNeural",    "+0%", "Sprungtor offen."),
    ("comm_auto_turret",      "de-AT-JonasNeural",     "+0%", "Geschützturm läuft."),
    ("comm_capital_inbound",  "de-DE-TanjaNeural",     "+1%", "Schweres Signal am Tor. Ein AETHER-Kreuzer kommt rein."),
    ("comm_shipyard_bay",     "de-AT-IngridNeural",    "+0%", "Werftbucht meldet Fertigstellung. Schiff liegt am Dock."),
    ("comm_sovereign_arrive", "de-DE-ChristophNeural", "-5%", "Hier AETHER Grace Period. Korridor sofort räumen. Schiffe ohne Freigabe werden aufgebracht."),
    ("comm_macro_salvo",      "de-DE-TanjaNeural",     "+3%", "Achtung, schwere Batterien aufgeschaltet. Ausweichen."),

    # Ambient VHF harbor & belt background chatter (chatter_0..15)
    ("chatter_0",  "de-CH-JanNeural",       "+0%", "Schlepper vier, bin an Bake zwei vorbei."),
    ("chatter_1",  "de-DE-TanjaNeural",     "+0%", "Verstanden, Bucht drei wird gleich frei."),
    ("chatter_2",  "de-DE-ChristophNeural", "-2%", "Frachter zwölf, wir brauchen noch zehn Minuten zum Entladen."),
    ("chatter_3",  "de-AT-JonasNeural",     "+1%", "Kein Problem, ich dreh noch eine Runde."),
    ("chatter_4",  "de-CH-LeniNeural",      "+1%", "Hat jemand Druckabfall auf Leitung B gemeldet?"),
    ("chatter_5",  "de-CH-JanNeural",       "-1%", "Negativ, hier läuft alles normal."),
    ("chatter_6",  "de-AT-IngridNeural",    "+0%", "Zollkontrolle Helios, bitte Kennung noch einmal durchgeben."),
    ("chatter_7",  "de-DE-ChristophNeural", "-1%", "Kutter sieben, Anflug verzögert sich, viel Verkehr am Tor."),
    ("chatter_8",  "de-AT-JonasNeural",     "+2%", "Eisbrocken auf Sektorbahn Ost, bisschen aufpassen."),
    ("chatter_9",  "de-DE-TanjaNeural",     "+0%", "Danke für die Info, geh zwanzig Meter höher."),
    ("chatter_10", "de-CH-JanNeural",       "+0%", "Leitstand, wer hat gerade das Licht an Dock vier ausgemacht?"),
    ("chatter_11", "de-CH-LeniNeural",      "+1%", "War nur die Umschaltung auf Reserve, kommt gleich wieder."),
    ("chatter_12", "de-DE-ChristophNeural", "-1%", "Kipper, bist du noch in der Werkstatt?"),
    ("chatter_13", "de-AT-JonasNeural",     "+3%", "Ja, aber mach schnell, ich mach gleich zu."),
    ("chatter_14", "de-AT-IngridNeural",    "+0%", "Schichtwechsel in zwanzig Minuten, dann übernimmt Gruppe zwei."),
    ("chatter_15", "de-DE-TanjaNeural",     "+0%", "Alles klar, wir lassen den Kanal offen."),
]


def _load_gemini_clips() -> list[tuple[str, str, str, str]]:
    spec = importlib.util.spec_from_file_location("gen_voices_gemini", GEMINI_SCRIPT)
    mod = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(mod)
    return list(mod.CLIPS)


async def generate_one(stem: str, voice: str, rate: str, text: str) -> None:
    out_path = OUT_DIR / f"{stem}.mp3"
    comm = edge_tts.Communicate(text=text, voice=voice, pitch="+0Hz", rate=rate)
    await comm.save(str(out_path))
    kb = out_path.stat().st_size / 1024.0
    print(f"OK   {out_path.name:32s} [{voice:22s}] {kb:5.1f} KB")


async def run(render_all: bool, comms_only: bool, include_comms: bool) -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    todo: list[tuple[str, str, str, str]] = []

    if not comms_only:
        clips = _load_gemini_clips()
        for stem, gemini_voice, _style, text in clips:
            if not render_all and (OUT_DIR / f"{stem}.wav").exists():
                print(f"SKIP {stem}.mp3 (Gemini TTS .wav already present)")
                continue
            edge_voice, rate = VOICE_MAP.get(gemini_voice, ("de-DE-ChristophNeural", "+0%"))
            todo.append((stem, edge_voice, rate, text))

    if comms_only or include_comms or render_all:
        todo.extend(COMM_AND_CHATTER_CLIPS)

    print(f"Generating {len(todo)} natural German MP3 clips via edge-tts...")
    sem = asyncio.Semaphore(4)

    async def bounded(item):
        async with sem:
            await generate_one(*item)

    await asyncio.gather(*(bounded(item) for item in todo))
    print(f"Done ({len(todo)} clips written).")


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate natural German MP3 voiceovers via edge-tts.")
    ap.add_argument("--all", action="store_true", help="Regenerate all story, faction, comm and chatter MP3s.")
    ap.add_argument("--comms", action="store_true", help="Regenerate only the 38 station/cockpit comm & chatter MP3s.")
    ap.add_argument("--with-comms", action="store_true", help="Include the 38 comm & chatter MP3s alongside story/faction.")
    args = ap.parse_args()
    asyncio.run(run(args.all, args.comms, args.with_comms))


if __name__ == "__main__":
    main()
