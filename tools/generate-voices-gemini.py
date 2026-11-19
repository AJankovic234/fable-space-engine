"""Generate natural, understated German radio voiceovers for Space 2601AE via Gemini 3.8 Flash TTS.

Covers ALL 51 voiced dialogue clips in src/StoryRadio.fs:
  - 23 Dock & Archive Story Beats (Beats 0, 3, 4, 6, 7, 12..29)
  - 28 In-Flight Faction Radio Lines (faction_00..27)

Uses the current Gemini Interactions REST API (POST /v1beta/interactions) with
gemini-3.8-flash-tts and natural colloquial German directing cues — zero extra
Python packages required.

Usage:
  1) Set your Gemini API key:
       $env:GEMINI_API_KEY = "AIza..."
  2) Generate all 51 clips (parallel workers) and switch src/Sound.fs paths to .wav:
       python tools/generate-voices-gemini.py --force --update-sound
  3) Preview cast & count offline:
       python tools/generate-voices-gemini.py --dry-run
"""

from __future__ import annotations

import argparse
import base64
from concurrent.futures import ThreadPoolExecutor, as_completed
import json
import os
import sys
import time
import urllib.error
import urllib.request
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "public" / "assets" / "sfx"
SOUND_FS = ROOT / "src" / "Sound.fs"
DEFAULT_MODEL = "gemini-3.8-flash-tts"
INTERACTIONS_URL = "https://generativelanguage.googleapis.com/v1beta/interactions"

# (stem, gemini_voice, speech_metadata_style, verbatim_german_text)
# Natural spoken German directing cues — understated, grounded, zero theatrical announcer tone.
CLIPS = [
    # --- Act 1 Dock Calls & Core Archive Beats (0, 3, 4, 6, 7, 12..17) ---
    ("story_act1_voss", "Algenib",
     "Natural colloquial spoken German, tired older harbor dispatcher talking casually into a headset mic while checking his radar screen, understated, no melodrama",
     "Voss hier, Disposition Kestrel. Du, ich hab deine Kennung gerade zweimal auf dem Schirm. Ich weiß nicht, wer da draußen noch mit deiner Nummer fliegt – aber verlass dich heute lieber aufs Radar, nicht aufs Register. Ja?"),
    ("story_helios_orlov", "Umbriel",
     "Natural colloquial spoken German, overworked raspy refinery shift foreman speaking matter-of-factly and a bit worn down over ship radio, understated",
     "Orlov, Schichtbüro Helios. Wenn du was Medizinisches oder Filter geladen hast – sag's am Tor nicht an. Der Zoll hält die Kisten seit drei Tagen fest, und unten in Schacht neun gehen uns langsam die Kartuschen aus."),
    ("story_kipper_drift", "Puck",
     "Natural colloquial spoken German, slightly hurried workshop mechanic talking casually while putting down a tool, candid, street-smart, realistic",
     "Ja, Kipper hier, Drift-9. Hab dir die neue Kennung draufgelötet – sieht bisschen wild aus unter der Klappe, ich weiß. Aber für die alten Zolldrohnen reicht das erstmal. Solange du nicht direkt neben 'nem Kreuzer parkst."),
    ("story_act2_voss", "Algenib",
     "Natural colloquial spoken German, experienced veteran dispatcher giving a plain, calm practical warning over long-range radio, low-key",
     "Voss nochmal, über Fernleitung. Bei Kepler musst du aufpassen – da sind noch die alten Werftdrohnen im Netz. Die fragen nicht nach, die gleichen nur die Lizenzliste ab. Wenn dein Stempel fehlt, machen die einfach auf."),
    ("story_act3_lind", "Aoede",
     "Natural colloquial spoken German, focused female archivist checking mic connection then explaining an alarming technical discovery plainly and urgently, no theatrics",
     "Hallo? Kannst du mich hören? Hier ist Mara Lind auf Theta. Ich sitz seit Stunden über dem alten Band von vierunddreißig. Das ist kein kaputter Transponder – die haben damals bei ORPHEUS-7 genau dieselbe Schleife gefahren. Nur diesmal hängt der halbe Sektor drin."),
    ("story_vance_intro", "Charon",
     "Natural high-register German, calm polite corporate executive speaking like an HR or insurance director closing an account, chillingly matter-of-fact, zero cartoon villainy",
     "Hier spricht Kaelen Vance. Ihre Kennung taucht in unseren Anfluglisten gar nicht mehr auf. Sie können gern noch ein paar Runden Fracht fahren, wenn Ihnen das hilft. Aber bitte halten Sie Abstand zur Station – ich hab die Bereinigung für Ihren Datensatz schon freigegeben."),
    ("story_letter_orlov", "Umbriel",
     "Natural colloquial spoken German, quiet tired foreman recording a short pragmatic note late at night, understated and worn out",
     "Ich hab die ganze Schichtliste gestern Nacht noch auf Papier gezogen, bevor sie den Server geleert haben. Liegt bei mir unten im Schreibtisch. Irgendwer muss ja was in der Hand haben, wenn nächste Woche wieder die Familien am Tor stehen."),
    ("story_letter_voss", "Algenib",
     "Natural colloquial spoken German, older man speaking very quietly with a brief hesitant pause, understated grief, almost embarrassed to admit he replayed the tape",
     "Das... ja, das ist meine Tochter. Mika. Ich hab das Band heute früh dreimal zurückgespult. Bescheuert, ich weiß – aber man wartet trotzdem jedes Mal, dass sie noch irgendwas hinterherschickt."),
    ("story_letter_lind", "Aoede",
     "Natural colloquial spoken German, researcher realizing a detail while flipping through old invoices, conversational and sharp",
     "Warte mal – jetzt seh ich's erst in den alten Abrechnungen. AETHER hat für beide Schiffe zusammen nur eine einzige Lizenz verbucht. Die wussten von Anfang an, dass die Nummer doppelt läuft. Ich leg dir 'ne Kopie davon direkt zum Band."),
    ("story_sera_ghosts", "Zephyr",
     "Natural colloquial spoken German, quiet intimate young woman speaking softly on a sideband radio channel, careful and sincere",
     "Hey... nicht auflegen, ich bin's nur, Sera auf dem Nebenband. Ich schreib hier oben die ganzen alten Puffer-Echos mit, bevor sie überschrieben werden. Deine Kennung war auch schon mal dabei. Die gehörte früher 'nem Kurier namens Halden – dachte, das solltest du vielleicht wissen."),
    ("story_halden_blackbox", "Schedar",
     "Natural colloquial spoken German, rattled courier pilot recording an emergency blackbox log while checking ship instruments, tense and urgent",
     "Äh, Halden, Kestrel vier – falls das hier überhaupt jemand findet. Ich weiß nicht, was die gemacht haben, aber meine Kennung antwortet mir gerade von 'ner Drohne aus. Wenn du irgendwann meine Nummer übernimmst – flieg damit bloß nicht nach Morgenlicht hoch, hörst du?"),

    # --- Act 2, Rim Ports & Act 3 Story Beats (18..29) ---
    ("story_act2_kestrel_voss", "Algenib",
     "Natural colloquial spoken German, weary veteran dispatcher passing along troubling dock news plainly and quietly",
     "Du, ich wollt dir nur kurz Bescheid geben – heute Nacht sind bei uns wieder vier Leute aus dem Dockregister geflogen. Die stehen gerade noch draußen an ihren Greifern, und oben auf dem Terminal steht einfach 'Schicht unbesetzt'. Keine Meldung, nix."),
    ("story_act2_helios_orlov", "Umbriel",
     "Natural colloquial spoken German, exhausted raspy shift boss speaking heavily after a long shift, blunt and worried",
     "Du, die haben heute früh Schacht neun neu ausgeschrieben. Achtunddreißig Stellen. Von der alten Schicht steht kein Name mehr am Brett. Wenn du irgendwo Filter auftreibst, bring sie hoch."),
    ("story_act2_drift_kipper", "Puck",
     "Natural colloquial spoken German, nervous quick-talking mechanic calling with bad news, candid and realistic",
     "Äh, schlechte Nachrichten. Deine neue Kennung hat vor 'ner knappen Stunde bei Kepler getankt. Und da du gerade hier stehst, warst du das wohl eher nicht. Wenn dir das Schiff draußen über den Weg läuft, warte nicht erst ab, was es funkt."),
    ("story_act2_kepler_voss", "Algenib",
     "Natural colloquial spoken German, older dispatcher reflecting quietly and casually over a long-range channel, unhurried, grounded",
     "Hast du im Anflug das große Ding in Trockendock B gesehen? Das Kolonieschiff liegt da angeblich schon seit ein paar hundert Jahren. Die alten Uhren in der Werft laufen einfach weiter, als käme jeden Moment noch die Startfreigabe von unten. Na ja... irgendwann hört man auf zu warten."),
    ("story_act2_theta_lind", "Aoede",
     "Natural colloquial spoken German, focused female archivist explaining an audio comparison quietly and plainly, zero melodrama",
     "Hör mal kurz. Ich hab deinen Funk neben Frachter Neun gelegt. Bei dir knackt die Leitung, man hört dich atmen. Bei denen ist gar nichts. Kein Rauschen, nichts. Da sitzt keiner mehr."),
    ("story_act2_bastion_vance", "Charon",
     "Natural high-register German, composed cool corporate director speaking with matter-of-fact certainty, quietly chilling, no melodrama",
     "Sie fliegen ja immer noch. Wissen Sie, warum seit einem halben Jahr niemand nach Frachter Neun fragt? Weil er sich jeden Dienstag pünktlich meldet. Den Leuten reicht es, wenn am anderen Ende wer abnimmt."),
    ("story_kaltstrom_tamm", "Leda",
     "Natural colloquial spoken German, blunt practical female cryo-plant engineer talking briskly over humming machinery, direct and everyday",
     "Tamm, Kühlkreis Raureif. Warte kurz, hier läuft gerade Pumpe drei an. Morgenlicht zieht heute alles weg für ihre Bänke. Wenn du handeln willst, bring Zellen. Mit Credits heizt hier keiner."),
    ("story_wracksaum_karst", "Umbriel",
     "Natural colloquial spoken German, gruff pragmatic salvage tug captain explaining everyday procedure plainly, zero philosophy",
     "Karst, Schlepper vierundvierzig. Wenn du da draußen in die Wracks gehst, erschrick nicht. Die alten Notsender laufen fast alle noch. Wir klemmen die vorher ab. Sonst dudelt dir das die ganze Schicht ins Ohr."),
    ("story_schleierpass_aders", "Schedar",
     "Natural colloquial spoken German, steady tired nebula beacon keeper giving a straightforward warning with quiet annoyance",
     "Aders, Graue Wacht. Kurz zur Info: im Nebel funkt seit gestern jemand mit unserer Kennung und will Geld für Geleitschutz. Ignorier das. Die Baken eins bis sechs stehen offen, dafür zahlt hier keiner was."),
    ("story_act3_kestrel_voss", "Algenib",
     "Natural colloquial spoken German, dry calm older dispatcher taking bad news in stride with quiet resolve",
     "Jetzt haben sie meine Schlepperkennung auch rausgenommen. Seit heute früh. Dann hab ich wohl frei. Wenn du nach Morgenlicht hochgehst, sag Bescheid. Ich komm mit der alten Kiste hintenrum mit."),
    ("story_act3_theta_lind", "Aoede",
     "Natural colloquial spoken German, focused female engineer uploading a signal under time pressure, crisp and practical",
     "Ich spiel dir das alte Band von vierunddreißig auf den Transponder. Nicht bewegen, dauert drei Sekunden. Damit sendest du beide Kennungen gleichzeitig. Die Schilde vor Morgenlicht verschlucken sich daran. Flieg direkt rein."),
    ("story_act3_bastion_vance", "Charon",
     "Natural German, polished corporate executive whose composure cracks under real stress on an open channel, sharp and tense, not cartoonish",
     "Woher haben Sie die alten Schlüssel? Hat Lind Ihnen das Band gegeben? Lassen Sie die Finger von den Puffern. Wenn die hier ausfallen, stehen morgen früh drei Stationen still. Drehen Sie ab."),

    # --- In-Flight Faction Radio Chatter 00..27 (1:1 from RadioChatter.exchanges) ---
    ("faction_00", "Orus",
     "Cold clinical spoken German, unfeeling corporate patrol officer reporting an expired ship over intercom",
     "Kutter auf zwölf Uhr. Kennung steht seit sechs Wochen auf Abgang."),
    ("faction_01", "Kore",
     "Icy clinical spoken German, cold female corporate officer ordering clearance without any empathy",
     "Markier ihn für die nächste Räumung. Ich will den Rumpf nicht vor den Schleusen liegen haben."),

    ("faction_02", "Kore",
     "Cold flat spoken German, unfeeling female corporate patrol officer reporting oxygen failures like routine numbers",
     "Auf Helios melden die schon wieder Ausfälle in Schacht neun. Drei Frachter hängen noch vorm Tor."),
    ("faction_03", "Charon",
     "Icy indifferent spoken German, ruthless corporate officer dismissing human lives casually",
     "Freigabe bleibt gesperrt. Wenn die Schicht kippt, schreiben wir die Plätze neu aus."),

    ("faction_04", "Kore",
     "Cold clipped spoken German, irritated female corporate officer reporting unauthorized beacons",
     "Graue Wacht setzt schon wieder eigene Baken im Nebel."),
    ("faction_05", "Charon",
     "Cold authoritarian spoken German, quiet ruthless corporate officer issuing an order",
     "Koordinaten übernehmen, Baken morgen abschalten. Durchflug gibt's nur noch gegen Stempel."),

    ("faction_06", "Charon",
     "Quiet chilling matter-of-fact spoken German, corporate officer confirming a cleared commuter ferry",
     "Fähre vier ist durch. Zweiunddreißig Namen aus dem Rücklauf gestrichen."),
    ("faction_07", "Kore",
     "Icy clinical female German, calm chilling instruction about storing voice buffers before opening the airlock",
     "Gut. Speicher die Stimmproben im Puffer, bevor wir die Schleuse öffnen."),

    ("faction_08", "Orus",
     "Cold professional spoken German, security pilot checking protocol during a sector distress",
     "Leitstand, auf Sektorband zwei wird ein Frachter aufgebracht. Eingreifen?"),
    ("faction_09", "Kore",
     "Icy flat female German, corporate dispatcher shutting down a distress channel without hesitation",
     "Kein Platin-Vertrag, kein Einsatz. Kanal stummschalten und weiterfliegen."),

    ("faction_10", "Orus",
     "Tense quiet spoken German, corporate patrol pilot reporting a forbidden signal on the old band",
     "Leitstand, die gelöschte Kestrel-Kennung sendet schon wieder über das alte Band."),
    ("faction_11", "Charon",
     "Sharp cold authoritative German, corporate dispatcher cutting off open radio and summoning security",
     "Kein Wort mehr über offenen Kanal. Frequenz dichten, Zugriffsteam informieren."),

    ("faction_12", "Charon",
     "Cold stern German, heavy customs cruiser warning broadcast",
     "Unregistrierte Schlepper halten fünfzig Meter Abstand zur Sperrlinie. Zuwiderhandlung führt zur Beschlagnahme."),
    ("faction_13", "Kore",
     "Icy calm female German, escort pilot confirming weapon lock",
     "Verstanden. Zielerfassung steht."),

    ("faction_14", "Algenib",
     "Natural colloquial spoken German, annoyed tired tug pilot grumbling over open belt VHF radio",
     "Tariff sieben blockiert schon wieder das Tor. Bis die durch sind, steh ich hier zwei Stunden."),
    ("faction_15", "Umbriel",
     "Natural colloquial spoken German, casual freighter trucker sharing a practical tip over open radio",
     "Falls einer 'nen Transponder braucht, Kipper auf Drift-9 hat heute offen."),
    ("faction_16", "Leda",
     "Natural colloquial spoken German, straightforward female dock dispatcher giving a quick operational notice",
     "Raureif an alle im Anflug: heute keine Credits. Nur Zellen oder Fracht."),
    ("faction_17", "Umbriel",
     "Natural colloquial spoken German, seasoned salvage pilot warning other ships casually on open belt channel",
     "Kanal vier im Wracksaum könnt ihr ausmachen, das ist nur wieder die alte Schleife."),
    ("faction_18", "Aoede",
     "Natural colloquial spoken German, clear steady female beacon-runner broadcasting a practical navigation update",
     "Graue Wacht, Baken eins bis sechs sind an. Und nochmal, unsere Spur kostet nichts."),
    ("faction_19", "Schedar",
     "Natural colloquial spoken German, alert quiet nebula scout giving a low-key hazard warning",
     "Vorsicht im Nordnebel, da hängen zwei Drohnen ohne Kennung neben Bake vier."),
    ("faction_20", "Puck",
     "Natural colloquial spoken German, tense opportunistic pirate pilot speaking quickly to wingman",
     "Kein Kreuzer auf dem Schirm. Der Frachter da vorn fliegt allein."),
    ("faction_21", "Puck",
     "Natural colloquial spoken German, blunt impatient hijacker hailing a freighter over open radio without theatrics",
     "Kutter, mach keinen Ärger. Klink die Container aus, dann lassen wir dich."),
    ("faction_22", "Zephyr",
     "Natural colloquial spoken German, focused female relay technician issuing a calm technical broadcast",
     "Relais Theta an alle: wir kriegen hier doppelte Echos rein. Schaut mal auf eure Transponder."),

    ("faction_23", "Kore",
     "Cold clipped female German, corporate sensor officer flagging a duplicate transponder",
     "Wracksaum-Schlepper im Anflug. Transponder gibt zwei verschiedene Namen aus."),
    ("faction_24", "Charon",
     "Icy bureaucratic German, corporate officer denying dock access without emotion",
     "Dann zählt keiner davon. Sperr ihm die Dockfreigabe."),

    ("faction_25", "Kore",
     "Cold matter-of-fact female German, reporting low oxygen levels on rejected ships with zero concern",
     "Kepler hat eben wieder zwei Erzfrachter abgewiesen. Luftvorrat laut Scan unter zwanzig Prozent."),
    ("faction_26", "Charon",
     "Icy heartless German, corporate officer closing the doors calmly",
     "Nicht unser Sektor, nicht unsere Police. Schleusen bleiben zu."),

    ("faction_27", "Charon",
     "Cold heavy authoritarian German, flagship flight control issuing a final warning",
     "Flugkontrolle Grace Period. Alle nicht angemeldeten Schiffe räumen den Korridor. Keine zweite Warnung."),
]


def call_gemini_tts(api_key: str, model: str, voice: str, style: str, text: str) -> bytes:
    """Call Gemini 3.8 Flash TTS via the official Interactions REST API."""
    payload = {
        "model": model,
        "input": [
            {
                "type": "user_input",
                "content": [
                    {
                        "type": "text",
                        "text": text,
                        "annotations": [
                            {
                                "type": "speech_metadata",
                                "style": style,
                            }
                        ],
                    }
                ],
            }
        ],
        "response_format": {"type": "audio"},
        "generation_config": {
            "speech_config": [
                {"voice": voice},
            ]
        },
    }
    body = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        INTERACTIONS_URL,
        data=body,
        headers={
            "Content-Type": "application/json",
            "x-goog-api-key": api_key,
        },
        method="POST",
    )

    for attempt in range(7):
        try:
            with urllib.request.urlopen(req, timeout=60) as resp:
                data = json.loads(resp.read().decode("utf-8"))
            for step in reversed(data.get("steps", [])):
                if step.get("type") == "model_output":
                    for item in reversed(step.get("content", [])):
                        if item.get("type") == "audio" and item.get("data"):
                            return base64.b64decode(item["data"])
            raise RuntimeError(f"No audio output in interaction response: {list(data.keys())}")
        except urllib.error.HTTPError as e:
            err_text = e.read().decode("utf-8", errors="replace")[:500]
            if e.code in (429, 500, 503) and attempt < 6:
                wait = min(65.0, 5.0 * (2 ** attempt))
                print(f"  HTTP {e.code}, backing off {wait:.0f}s...", file=sys.stderr, flush=True)
                time.sleep(wait)
                continue
            raise RuntimeError(f"HTTP {e.code}: {err_text}") from None


def write_wav(path: Path, audio_bytes: bytes, sample_rate: int = 24000) -> float:
    path.parent.mkdir(parents=True, exist_ok=True)
    if audio_bytes[:4] == b"RIFF":
        path.write_bytes(audio_bytes)
        with wave.open(str(path), "rb") as wf:
            return wf.getnframes() / float(wf.getframerate() or sample_rate)
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(sample_rate)
        wf.writeframes(audio_bytes)
    return len(audio_bytes) / float(sample_rate * 2)


def update_sound_fs(stems: list[str]) -> None:
    src = SOUND_FS.read_text(encoding="utf-8")
    changed = 0
    for stem in stems:
        old = f'"/assets/sfx/{stem}.mp3"'
        new = f'"/assets/sfx/{stem}.wav"'
        if old in src:
            src = src.replace(old, new)
            changed += 1
    if changed:
        SOUND_FS.write_text(src, encoding="utf-8")
        print(f"Updated {changed} paths in {SOUND_FS.name} (.mp3 -> .wav).")


def _render_one(idx: int, total: int, clip: tuple[str, str, str, str], api_key: str, model: str, force: bool, pace: float) -> tuple[str, float, str]:
    stem, voice, style, text = clip
    out_path = OUT_DIR / f"{stem}.wav"
    if out_path.exists() and not force:
        with wave.open(str(out_path), "rb") as wf:
            dur = wf.getnframes() / float(wf.getframerate() or 24000)
        return stem, dur, f"[{idx:02d}/{total:02d}] SKIP {out_path.name:32s} [{voice:8s}] {dur:4.2f}s"
    audio = call_gemini_tts(api_key, model, voice, style, text)
    dur = write_wav(out_path, audio)
    if pace > 0:
        time.sleep(pace)
    return stem, dur, f"[{idx:02d}/{total:02d}] OK   {out_path.name:32s} [{voice:8s}] {dur:4.2f}s"


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate natural German voiceovers via Gemini 3.8 Flash TTS.")
    ap.add_argument("--key", default=os.environ.get("GEMINI_API_KEY") or os.environ.get("GOOGLE_API_KEY") or "",
                    help="Gemini API key (defaults to GEMINI_API_KEY env var).")
    ap.add_argument("--model", default=DEFAULT_MODEL,
                    help=f"TTS model (default: {DEFAULT_MODEL}; also supports gemini-3.8-flash-lite-tts).")
    ap.add_argument("--only", nargs="*", help="Filter by clip stem prefix (e.g. faction_00 story_act1).")
    ap.add_argument("--workers", type=int, default=1, help="Parallel API workers (default: 1 for RPM safety).")
    ap.add_argument("--pace", type=float, default=1.2, help="Seconds to pause after each rendered clip (default: 1.2).")
    ap.add_argument("--force", action="store_true", help="Overwrite existing .wav files.")
    ap.add_argument("--update-sound", action="store_true", help="Switch generated clips in src/Sound.fs to .wav.")
    ap.add_argument("--dry-run", action="store_true", help="Show selected clips and exit without calling API.")
    args = ap.parse_args()

    selected = [c for c in CLIPS if not args.only or any(c[0].startswith(p) for p in args.only)]
    total_chars = sum(len(c[3]) for c in selected)
    print(f"Selected {len(selected)} clips ({total_chars} spoken chars) using {args.model}.")

    if args.dry_run:
        for stem, voice, style, text in selected:
            print(f"  {stem:28s} [{voice:8s}]  {text[:72]}")
        return

    if not args.key:
        print("Error: Set GEMINI_API_KEY (or pass --key YOUR_KEY) first.", file=sys.stderr)
        sys.exit(2)

    generated: list[str] = []
    workers = max(1, min(8, args.workers))
    with ThreadPoolExecutor(max_workers=workers) as pool:
        futs = [
            pool.submit(_render_one, idx, len(selected), clip, args.key, args.model, args.force, args.pace)
            for idx, clip in enumerate(selected, 1)
        ]
        for fut in as_completed(futs):
            stem, _dur, msg = fut.result()
            print(msg, flush=True)
            generated.append(stem)

    if args.update_sound and generated:
        update_sound_fs(generated)


if __name__ == "__main__":
    main()
