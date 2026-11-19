"""Maximum-quality Gemini API intro & soundtrack production pipeline for Space 2601AE.

Produces studio-grade assets using the current Gemini Interactions REST API
(zero extra Python dependencies — pure Python standard library):

  1. --video   gemini-omni-1.2-flash (16:9, 1080p or 4k, 15s + 10s multi-turn
               extension = 25s cinema intro guided by real game renders)
               -> public/assets/intro/space2601-omni.mp4
               (automatically preferred by public/intro-film.js; ruecklauf.mp4
                remains intact as fallback)

  2. --music   lyria-3.5 ($0.08/song, strictly instrumental — zero vocals —
               Nordic war-drum & Tagelharpa dark sci-fi calm + combat loops)
               -> public/assets/sfx/music_calm_lyria.mp3
               -> public/assets/sfx/music_combat_lyria.mp3

  3. --voices  gemini-3.8-flash-tts (delegates to tools/generate-voices-gemini.py
               for all 40 acted German voiceovers: story beats 18..29 + faction 00..27)

Usage:
  1) Set your Gemini API key:
       $env:GEMINI_API_KEY = "AIza..."

  2) Inspect the full production plan offline (no API key needed):
       python tools/generate-intro-gemini.py --dry-run

  3) Render the 1080p (or 4k) cinema intro video only:
       python tools/generate-intro-gemini.py --video --resolution 1080p
       python tools/generate-intro-gemini.py --video --resolution 4k

  4) Generate studio Nordic instrumental music & wire into src/Sound.fs:
       python tools/generate-intro-gemini.py --music --update-sound

  5) Run the full maximum-quality pipeline (4K/1080p video + Lyria music + Gemini TTS):
       python tools/generate-intro-gemini.py --all --resolution 4k --update-sound
"""

from __future__ import annotations

import argparse
import base64
import json
import mimetypes
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
INTRO_DIR = ROOT / "public" / "assets" / "intro"
SFX_DIR = ROOT / "public" / "assets" / "sfx"
SOUND_FS = ROOT / "src" / "Sound.fs"
VOICES_SCRIPT = ROOT / "tools" / "generate-voices-gemini.py"

INTERACTIONS_URL = "https://generativelanguage.googleapis.com/v1beta/interactions"
FILES_API_BASE = "https://generativelanguage.googleapis.com/v1beta"
FILES_DOWNLOAD_BASE = "https://generativelanguage.googleapis.com/download/v1beta"
LYRIA_API_REVISION = "2026-05-20"

DEFAULT_VIDEO_MODEL = "gemini-omni-1.2-flash"
DEFAULT_MUSIC_MODEL = "lyria-3.5"

# Real game renders passed as visual references to keep ships, stations, and
# the 2034 RT-9 analog relay bunker consistent with the playable game.
VIDEO_REF_IMAGES = [
    ("RT-9 Relay Bunker (2034)", ROOT / "public" / "assets" / "research" / "rt9-labor.webp"),
    ("Salvage Cutter (Player Ship)", ROOT / "public" / "assets" / "ships" / "kutter.webp"),
    ("AETHER Belt Traffic & Cruisers", ROOT / "public" / "assets" / "raumverkehr.webp"),
    ("Bastion Morgenlicht (Orbital Ring)", ROOT / "public" / "assets" / "docks" / "bastion.webp"),
]

INTRO_PART1_PROMPT = (
    "Ultra-cinematic hard science-fiction game trailer in 16:9 widescreen, shot on anamorphic "
    "65mm film with grounded industrial realism inspired by The Expanse and Blade Runner 2049, "
    "crisp high-contrast lighting, zero cartoon look, no on-screen subtitles or watermarks. "
    "Use the four reference images for consistent hardware design: image 1 for the 2034 analog "
    "radio bunker, image 2 for the weathered player salvage cutter, image 3 for the sleek "
    "white-and-crimson AETHER corporate cruisers, and image 4 for the dark orbital bastion. "
    "Seconds 0 to 4: Tight tracking shot inside the dim 2034 RT-9 underground relay room — "
    "spinning amber reel-to-reel magnetic tape decks, glowing vacuum tubes, and a dusty green-amber "
    "CRT oscilloscope pulsing with an analog human voice waveform while real magnetic tape hiss "
    "fills the air. "
    "Seconds 4 to 10: Hard smash-cut across 567 years of deep space silence into the icy orbital "
    "asteroid belt of 2601 AE. A scarred, weathered industrial salvage cutter drifts past tumbling "
    "ice boulders illuminated by a cold distant sun, tiny attitude-thruster puffs catching ice dust. "
    "Seconds 10 to 15: Slow ominous camera sweep as two gleaming white-and-crimson AETHER corporate "
    "interceptor cruisers slide out of shadow, locking blinding searchlights onto a stalled commuter "
    "ferry clamped to an orbital docking gantry. Through the gantry glass, an amber digital registry "
    "counter flickers from 32 down to 00. "
    "Audio: tactile mechanical tape-deck clicks, deep sub-bass orbital vacuum rumble, distant metallic "
    "hull stress, and a slow, brooding instrumental Nordic hide war-drum pulse with bowed dark strings "
    "— strictly no vocals or singing."
)

INTRO_PART2_EXTENSION_PROMPT = (
    "Extend this video seamlessly from the final frame for 10 more seconds with the same 65mm "
    "anamorphic hard sci-fi realism, lighting, and color grade. "
    "Cut smoothly inside the dark, worn, lived-in cockpit of the salvage cutter: scratched glass "
    "canopy, amber instrument glow, and an old bolted-on RT-9 analog radio transponder snapping to "
    "life with warm crackling static. Through authentic analog belt-radio hiss, a calm, gravelly "
    "veteran German voice (male, unhurried, weathered) speaks clearly: "
    "'Rauschen heißt, da atmet noch jemand. Dock zwei ist noch offen — los, Partner.' "
    "Immediately, the cutter's main drive ignites with a fierce ice-blue plasma plume; the exterior "
    "camera pulls wide as the ship banks sharply away from the AETHER searchlights and streaks "
    "through the asteroid belt toward the colossal, backlit orbital ring of Bastion Morgenlicht "
    "(matching reference image 4), then cuts decisively to black on the final drum hit. "
    "Audio: authentic radio static behind the spoken German line, roaring plasma ignition, and a "
    "driving crescendo of instrumental Nordic war drums and bowed Tagelharpa strings — strictly "
    "no singing, chanting, or choir vocals."
)

LYRIA_TRACKS = [
    {
        "key": "music_calm",
        "stem": "music_calm_lyria",
        "title": "Nordic Dark Sci-Fi Exploration (86 BPM, D Dorian, Instrumental)",
        "prompt": (
            "Strictly instrumental atmospheric dark Nordic folk meets deep-space hard sci-fi "
            "soundtrack, Scandinavian ritual percussion at 86 BPM in D Dorian. Driven by steady, "
            "resonant low hide drums and hand-played frame drums with crisp wooden rim accents, "
            "layered with a haunting, earthy bowed Tagelharpa melody, hypnotic plucked Kravik-lyre "
            "arpeggios, subtle birch rattles, and a deep cavernous dark ambient orbital synth drone. "
            "Expansive, cinematic, driving yet meditative and loop-friendly. Absolutely NO vocals, "
            "NO singing, NO chanting, NO choir, NO throat singing, NO spoken words — 100% pure instrumental."
        ),
    },
    {
        "key": "music_combat",
        "stem": "music_combat_lyria",
        "title": "Nordic Dark Sci-Fi Action (124 BPM, D Dorian, Instrumental)",
        "prompt": (
            "Strictly instrumental high-energy, fast-paced Nordic folk percussion meets dark "
            "cinematic sci-fi action score at a relentless 124 BPM in D Dorian. Driven by booming "
            "ensemble low hide drums, brisk syncopated frame drums, crisp wooden rim accents, a "
            "fast propulsive bowed Tagelharpa string ostinato, plucked Kravik-lyre bassline, deep "
            "resonant bronze Lur horn swells, and pulsing low analog sci-fi synth bass. Driving, "
            "urgent, heroic, gritty, and seamlessly loop-friendly without quiet fades. Absolutely "
            "NO vocals, NO singing, NO chanting, NO choir, NO humming, NO spoken words — 100% pure instrumental."
        ),
    },
    {
        "key": "music_aether",
        "stem": "music_aether_lyria",
        "title": "AETHER Dark Hegemony March (102 BPM, D Phrygian / Tritone, Instrumental)",
        "prompt": (
            "Strictly instrumental crushing, sinister dark sci-fi totalitarian march at a heavy "
            "102 BPM in D Phrygian with eerie tritone dissonance. Combines disciplined, dry low "
            "military snare corps with buzz rolls, pounding gran cassa bass drum, heavy industrial "
            "iron bulkhead strikes, menacing sub-octave contrabass brass swells, growling bass "
            "trombones and dark French horns, and an icy oppressive deep-space sub-bass dread. "
            "Bleak, suffocating, cold, authoritarian and loop-friendly — zero major-key brightness. "
            "Absolutely NO vocals, NO singing, NO choir, NO chanting — 100% pure instrumental."
        ),
    },
]


def _get_api_key() -> str:
    return (os.environ.get("GEMINI_API_KEY") or os.environ.get("GOOGLE_API_KEY") or "").strip()


def _sanitize(text: str, api_key: str) -> str:
    if api_key:
        text = text.replace(api_key, "[REDACTED]")
    return re.sub(r"AIza[0-9A-Za-z\-_]{20,}", "[REDACTED]", text)


def _post_json(url: str, payload: dict, api_key: str, extra_headers: dict[str, str] | None = None, timeout: int = 600) -> dict:
    headers = {
        "Content-Type": "application/json",
        "x-goog-api-key": api_key,
    }
    if extra_headers:
        headers.update(extra_headers)
    req = urllib.request.Request(
        url,
        data=json.dumps(payload).encode("utf-8"),
        headers=headers,
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        detail = _sanitize(e.read().decode("utf-8", errors="replace")[:700], api_key)
        raise RuntimeError(f"HTTP {e.code} from {url}: {detail}") from None


def _get_json(url: str, api_key: str, timeout: int = 60) -> dict:
    req = urllib.request.Request(url, headers={"x-goog-api-key": api_key}, method="GET")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        detail = _sanitize(e.read().decode("utf-8", errors="replace")[:500], api_key)
        raise RuntimeError(f"HTTP {e.code} from {url}: {detail}") from None


def _download_bytes(url: str, api_key: str, timeout: int = 300) -> bytes:
    req = urllib.request.Request(url, headers={"x-goog-api-key": api_key}, method="GET")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return resp.read()
    except urllib.error.HTTPError as e:
        detail = _sanitize(e.read().decode("utf-8", errors="replace")[:500], api_key)
        raise RuntimeError(f"HTTP {e.code} downloading media: {detail}") from None


def _extract_media_part(interaction: dict, media_type: str) -> dict | None:
    """Locate the generated video or audio content part from Interactions REST JSON."""
    for container_key in ("steps", "outputs"):
        items = interaction.get(container_key)
        if not isinstance(items, list):
            continue
        for item in items:
            if not isinstance(item, dict):
                continue
            if item.get("type") == media_type and (item.get("data") or item.get("uri")):
                return item
            for part in item.get("content", []) or []:
                if isinstance(part, dict) and part.get("type") == media_type and (part.get("data") or part.get("uri")):
                    return part
    return None


def _extract_file_id(uri: str) -> str | None:
    match = re.search(r"files/([a-zA-Z0-9_-]+)", uri or "")
    return match.group(1) if match else None


def _wait_and_download_video(part: dict, out_path: Path, api_key: str) -> None:
    """Save a video part returned either inline (base64) or via Files API URI."""
    out_path.parent.mkdir(parents=True, exist_ok=True)
    if part.get("data"):
        raw = base64.b64decode(part["data"])
        out_path.write_bytes(raw)
        return

    uri = part.get("uri")
    if not uri:
        raise RuntimeError("Video part contained neither inline data nor a file URI.")

    file_id = _extract_file_id(uri)
    if file_id:
        meta_url = f"{FILES_API_BASE}/files/{file_id}"
        for attempt in range(60):
            info = _get_json(meta_url, api_key)
            state = info.get("state")
            if isinstance(state, dict):
                state = state.get("name")
            if state in (None, "ACTIVE"):
                break
            if state == "FAILED":
                raise RuntimeError(f"Gemini Files processing FAILED for files/{file_id}.")
            if attempt % 4 == 0:
                print(f"    waiting for files/{file_id} (state={state})...")
            time.sleep(3.0)
        dl_url = f"{FILES_DOWNLOAD_BASE}/files/{file_id}:download?alt=media"
        raw = _download_bytes(dl_url, api_key)
        out_path.write_bytes(raw)
        return

    sep = "&" if "?" in uri else "?"
    raw = _download_bytes(f"{uri}{sep}alt=media", api_key)
    out_path.write_bytes(raw)


def _encode_ref_images() -> list[dict]:
    parts: list[dict] = []
    for label, path in VIDEO_REF_IMAGES:
        if not path.exists():
            raise FileNotFoundError(f"Missing reference image ({label}): {path}")
        mime = mimetypes.guess_type(str(path))[0] or "image/webp"
        b64 = base64.b64encode(path.read_bytes()).decode("ascii")
        parts.append({"type": "image", "data": b64, "mime_type": mime})
    return parts


def run_video(args: argparse.Namespace, api_key: str) -> None:
    final_path = INTRO_DIR / "space2601-omni.mp4"
    part1_path = INTRO_DIR / "space2601-omni-part1.mp4"
    total_sec = 15 if args.single_shot else 25

    print(f"\n=== [1/3] VIDEO: {args.video_model} ({args.resolution}, 16:9, {total_sec}s) ===")
    for label, path in VIDEO_REF_IMAGES:
        size_kb = path.stat().st_size / 1024 if path.exists() else -1
        status = f"{size_kb:.1f} KB" if size_kb >= 0 else "MISSING"
        print(f"  ref: {label:<36s} -> {path.relative_to(ROOT)} ({status})")
    print(f"  out: {final_path.relative_to(ROOT)}")

    if args.dry_run:
        print(f"  turn 1: 15s image-referenced intro ({len(INTRO_PART1_PROMPT)} chars)")
        if not args.single_shot:
            print(f"  turn 2: +10s seamless multi-turn extension via previous_interaction_id ({len(INTRO_PART2_EXTENSION_PROMPT)} chars)")
            print("  note:   multi-turn extension works in EEA/DE (no external video upload needed).")
        return

    if final_path.exists() and not args.force:
        print(f"  skip: {final_path.relative_to(ROOT)} already exists (pass --force to regenerate).")
        return

    input_parts = _encode_ref_images()
    input_parts.append({"type": "text", "text": INTRO_PART1_PROMPT})

    turn1_payload = {
        "model": args.video_model,
        "input": input_parts,
        "response_format": {
            "type": "video",
            "aspect_ratio": "16:9",
            "duration": "15s",
            "resolution": args.resolution,
            "delivery": "uri",
        },
    }

    print(f"  Turn 1/2: rendering 15s base cinema intro at {args.resolution} (may take 1–4 min)...")
    t0 = time.time()
    resp1 = _post_json(INTERACTIONS_URL, turn1_payload, api_key, timeout=900)
    interaction_id = resp1.get("id")
    vid1 = _extract_media_part(resp1, "video")
    if not vid1:
        raise RuntimeError(f"Turn 1 returned no video block. Response keys: {list(resp1.keys())}")

    target1 = final_path if args.single_shot else part1_path
    _wait_and_download_video(vid1, target1, api_key)
    mb1 = target1.stat().st_size / (1024 * 1024)
    print(f"  Turn 1 saved: {target1.relative_to(ROOT)} ({mb1:.2f} MB in {time.time() - t0:.1f}s, id={interaction_id})")

    if args.single_shot:
        return

    if not interaction_id:
        raise RuntimeError("Turn 1 did not return an interaction id; cannot run Turn 2 extension.")

    turn2_payload = {
        "model": args.video_model,
        "previous_interaction_id": interaction_id,
        "input": [{"type": "text", "text": INTRO_PART2_EXTENSION_PROMPT}],
        "response_format": {
            "type": "video",
            "duration": "10s",
            "resolution": args.resolution,
            "delivery": "uri",
        },
    }

    print(f"  Turn 2/2: seamlessly extending to 25s via previous_interaction_id={interaction_id}...")
    t1 = time.time()
    resp2 = _post_json(INTERACTIONS_URL, turn2_payload, api_key, timeout=900)
    vid2 = _extract_media_part(resp2, "video")
    if not vid2:
        raise RuntimeError(f"Turn 2 returned no video block. Part 1 is safely kept at {part1_path}.")

    _wait_and_download_video(vid2, final_path, api_key)
    mb2 = final_path.stat().st_size / (1024 * 1024)
    print(f"  Turn 2 saved: {final_path.relative_to(ROOT)} ({mb2:.2f} MB in {time.time() - t1:.1f}s)")


def _update_sound_music(ext: str) -> int:
    text = SOUND_FS.read_text(encoding="utf-8")
    updated = 0
    for track in LYRIA_TRACKS:
        key = track["key"]
        stem = track["stem"]
        pattern = rf'({re.escape(key)}:\s*)"/assets/sfx/[^"]+"'
        replacement = rf'\1"/assets/sfx/{stem}.{ext}"'
        new_text, count = re.subn(pattern, replacement, text, count=1)
        if count:
            text = new_text
            updated += 1
    if updated:
        SOUND_FS.write_text(text, encoding="utf-8")
    return updated


def run_music(args: argparse.Namespace, api_key: str) -> None:
    ext = args.music_format
    print(f"\n=== [2/3] MUSIC: {args.music_model} ({ext.upper()}, strictly instrumental, ~$0.08/track) ===")
    for track in LYRIA_TRACKS:
        out_path = SFX_DIR / f"{track['stem']}.{ext}"
        print(f"  track: {track['key']:<14s} -> {out_path.relative_to(ROOT)} ({track['title']})")
        if args.dry_run:
            continue
        if out_path.exists() and not args.force:
            print(f"    skip: already exists (pass --force to regenerate).")
            continue

        payload: dict = {
            "model": args.music_model,
            "input": track["prompt"],
        }
        if ext == "wav":
            payload["response_format"] = {"type": "audio"}

        t0 = time.time()
        resp = _post_json(
            INTERACTIONS_URL,
            payload,
            api_key,
            extra_headers={"Api-Revision": LYRIA_API_REVISION},
            timeout=300,
        )
        audio_part = _extract_media_part(resp, "audio")
        if not audio_part or not audio_part.get("data"):
            raise RuntimeError(f"Lyria 3.5 returned no audio data for {track['key']}.")

        out_path.parent.mkdir(parents=True, exist_ok=True)
        raw = base64.b64decode(audio_part["data"])
        out_path.write_bytes(raw)
        mb = len(raw) / (1024 * 1024)
        print(f"    saved: {out_path.relative_to(ROOT)} ({mb:.2f} MB in {time.time() - t0:.1f}s)")

    if not args.dry_run and args.update_sound:
        changed = _update_sound_music(ext)
        print(f"  Updated {changed} music path(s) in {SOUND_FS.relative_to(ROOT)} -> .{ext}")


def run_voices(args: argparse.Namespace) -> None:
    print("\n=== [3/3] VOICES: gemini-3.8-flash-tts (40 German voiceovers via generate-voices-gemini.py) ===")
    cmd = [sys.executable, str(VOICES_SCRIPT)]
    if args.dry_run:
        cmd.append("--dry-run")
    if args.force:
        cmd.append("--force")
    if args.update_sound and not args.dry_run:
        cmd.append("--update-sound")
    print("  exec:", " ".join(cmd))
    res = subprocess.run(cmd, cwd=str(ROOT))
    if res.returncode != 0:
        raise RuntimeError(f"generate-voices-gemini.py failed with exit code {res.returncode}")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Maximum-quality Gemini API intro video (Omni 1.2 Flash), music (Lyria 3.5) & voice (3.8 Flash TTS) pipeline."
    )
    parser.add_argument("--all", action="store_true", help="Run video + music + voices")
    parser.add_argument("--video", action="store_true", help="Generate 16:9 cinema intro via gemini-omni-1.2-flash")
    parser.add_argument("--music", action="store_true", help="Generate instrumental Nordic calm + combat tracks via lyria-3.5")
    parser.add_argument("--voices", action="store_true", help="Generate all 40 German voiceovers via gemini-3.8-flash-tts")
    parser.add_argument("--resolution", choices=["720p", "1080p", "4k"], default="1080p",
                        help="Output video resolution (default: 1080p; use 4k for cinema master)")
    parser.add_argument("--single-shot", action="store_true",
                        help="Generate only Turn 1 (15s) instead of 15s + 10s extension (25s)")
    parser.add_argument("--music-format", choices=["mp3", "wav"], default="mp3",
                        help="Lyria 3.5 output format (default: mp3; wav requests uncompressed audio)")
    parser.add_argument("--video-model", default=DEFAULT_VIDEO_MODEL, help=f"Video model (default: {DEFAULT_VIDEO_MODEL})")
    parser.add_argument("--music-model", default=DEFAULT_MUSIC_MODEL, help=f"Music model (default: {DEFAULT_MUSIC_MODEL})")
    parser.add_argument("--update-sound", action="store_true",
                        help="Update music and/or voice paths in src/Sound.fs after generation")
    parser.add_argument("--force", action="store_true", help="Regenerate even if output files already exist")
    parser.add_argument("--dry-run", action="store_true", help="Validate assets and print plan without calling Gemini API")
    args = parser.parse_args()

    do_video = args.all or args.video
    do_music = args.all or args.music
    do_voices = args.all or args.voices
    if not (do_video or do_music or do_voices):
        # Default to full intro package preview/execution when no stage flag is passed
        do_video = do_music = do_voices = True

    api_key = _get_api_key()
    if not args.dry_run and not api_key:
        print(
            "ERROR: GEMINI_API_KEY is not set.\n"
            "Set it in PowerShell first:\n"
            '  $env:GEMINI_API_KEY = "AIza..."\n'
            "Then re-run this script (or pass --dry-run to inspect the plan offline).",
            file=sys.stderr,
        )
        return 2

    if do_video:
        run_video(args, api_key)
    if do_music:
        run_music(args, api_key)
    if do_voices:
        run_voices(args)

    print("\nDone.")
    if not args.dry_run and do_video:
        print("public/intro-film.js automatically prefers /assets/intro/space2601-omni.mp4 when present.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
