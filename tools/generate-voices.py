"""Batch-generate German neural voiceovers for Space 2601AE via edge-tts.

Generates:
  - 5 cinematic intro trailer voiceovers (public/assets/sfx/intro_vo_*.mp3)
  - 12 story beats 18..29 (public/assets/sfx/story_*.mp3)
  - 28 faction radio chatter clips (public/assets/sfx/faction_00..27.mp3)
"""

import asyncio
import sys
from pathlib import Path
import edge_tts

OUT_DIR = Path(__file__).resolve().parent.parent / "public" / "assets" / "sfx"

# (filename, voice, pitch, rate, text)
CLIPS = [
    # --- Intro-Film Trailer Voiceover (deep, punchy cinema narrator) ---
    ("intro_vo_01_2034.mp3", "de-DE-ChristophNeural", "-14Hz", "+10%",
     "14. März 2034 – ein einziger Lizenzfehler, und zwei Menschen verschwinden im Sturm."),
    ("intro_vo_02_2601.mp3", "de-DE-ChristophNeural", "-14Hz", "+12%",
     "567 Jahre später gehört AETHER der ganze Gürtel. Keine SIN – kein Sauerstoff."),
    ("intro_vo_03_action.mp3", "de-DE-ChristophNeural", "-13Hz", "+14%",
     "Neun Sektoren, fünf Schiffe. Berge Erz, brich die Blockade – und lass dich nicht löschen!"),
    ("intro_vo_04_threat.mp3", "de-DE-ChristophNeural", "-16Hz", "+4%",
     "AETHER Grace Period. Sie nennen es Ordnung – wir nennen es Krieg."),
    ("intro_vo_05_finale.mp3", "de-DE-ChristophNeural", "-14Hz", "+8%",
     "Space 2601 AE – keine Lizenz, kein Backup. Flieg, solange es rauscht."),

    # --- Story Beats 18..29 (1:1 from src/StoryRadio.fs) ---
    ("story_act2_kestrel_voss.mp3", "de-DE-ChristophNeural", "-11Hz", "-4%",
     "Vance hat heute Nacht vier Kennungen aus dem Dockregister gestrichen. Die Leute stehen noch an ihren Greifern. Laut Liste war die Schicht nie besetzt."),
    ("story_act2_helios_orlov.mp3", "de-CH-JanNeural", "-12Hz", "-6%",
     "Sie haben Schacht 9 neu ausgeschrieben. 38 Stellen. Vorbesitzer unbekannt. Bring mir keine Blumen. Bring Filter."),
    ("story_act2_drift_kipper.mp3", "de-AT-JonasNeural", "+4Hz", "+8%",
     "Schlechte Nachricht, Lizenzleiche. Deine Kennung hat vor einer Stunde bei Kepler getankt. Du warst nicht dabei. Wenn dir das Ding begegnet: zuerst schießen, dann rechnen."),
    ("story_act2_kepler_voss.mp3", "de-DE-ChristophNeural", "-11Hz", "-4%",
     "Im Trockendock B liegt ein Kolonieschiff, das seit sechshundert Jahren auf Startfreigabe wartet. Die Werft zählt seitdem ihre eigenen Jahre. Hier draußen wartet jeder auf eine Bodenstation, die nicht mehr antwortet."),
    ("story_act2_theta_lind.mp3", "de-DE-TanjaNeural", "-2Hz", "+2%",
     "Ich habe deinen Bordfunk gegen das Band von 2034 gelegt. Deine Stimme rauscht. Die von Frachter Neun nicht mehr. Vertrau keiner Stimme ohne Rauschen. Rauschen heißt, da atmet noch jemand."),
    ("story_act2_bastion_vance.mp3", "de-AT-JonasNeural", "-6Hz", "-2%",
     "Du schon wieder. Weißt du, warum niemand nach Frachter Neun sucht? Mein Puffer funkt höflicher als ein erstickender Schürfer. Du wirst ein hervorragender Puffer."),
    ("story_kaltstrom_tamm.mp3", "de-AT-IngridNeural", "-4Hz", "+2%",
     "Raureif, Kühlkreis. Tamm am Rohr. Wir brennen Kälte für Morgenlicht. Die Pufferbänke laufen heiß, wenn so viele Tote weitersenden. Zahl in Zellen. Credits frieren hier nicht."),
    ("story_wracksaum_karst.mp3", "de-CH-JanNeural", "-9Hz", "-3%",
     "Karst, Schlepper Vierundvierzig. Jedes Wrack hier funkt noch. Wir kappen den Sender, bevor wir die Presse bergen. Sera nennt das Löschen. Ich nenn es Feierabend."),
    ("story_schleierpass_aders.mp3", "de-DE-ChristophNeural", "-4Hz", "-1%",
     "Graue Wacht, Aders. Wir haben die Spur durch den Nebel vermessen und verschenkt. Seit gestern verkauft sie jemand als Geleitschutz. Unter unserer Kennung."),
    ("story_act3_kestrel_voss.mp3", "de-DE-ChristophNeural", "-11Hz", "-3%",
     "Vance hat meine Schlepperkennung gestrichen. Laut Register gibt es mich seit heute früh nicht mehr. Gut. Dann sieht mich auch keiner kommen, Partner."),
    ("story_act3_theta_lind.mp3", "de-DE-TanjaNeural", "-1Hz", "+4%",
     "Ich speise das Band von 2034 auf deinen Transponder. Zwei Körper, eine Kennung. Sein Schild kann das nicht auseinanderhalten. Flieg, solange es rauscht."),
    ("story_act3_bastion_vance.mp3", "de-AT-JonasNeural", "-5Hz", "+4%",
     "Wer hat euch Analogsäcken die RT-9-Schlüssel gegeben? Ich halte diesen ganzen verdammten Sektor zusammen. Sleeve Nummer sieben wird sich an dich erinnern. Nummer acht auch."),

    # --- Faction Radio Chatter 00..27 (1:1 from RadioChatter.exchanges in src/StoryRadio.fs) ---
    # AETHER two-ship derogatory dialogues (distinct alternating voices for L/R stereo)
    ("faction_00.mp3", "de-AT-JonasNeural", "-4Hz", "+4%",
     "Schon wieder ein Schürfer mit geflicktem Transponder. Riecht bis hierher nach billigem Kühlmittel."),
    ("faction_01.mp3", "de-AT-IngridNeural", "-2Hz", "+3%",
     "Lass den Kutter durch. Wenn sein Stack reißt, spart das wenigstens die Hafensteuer."),

    ("faction_02.mp3", "de-DE-TanjaNeural", "-3Hz", "+2%",
     "Schichtwechsel auf Helios. Dreiundzwanzig unversicherte Lungen mehr auf der Liste."),
    ("faction_03.mp3", "de-DE-ChristophNeural", "-5Hz", "+3%",
     "Solange der Schmelzofen brennt, zahlt die Konzernkasse keinen einzigen Filter."),

    ("faction_04.mp3", "de-AT-IngridNeural", "-3Hz", "+4%",
     "Die Graue Wacht markiert wieder Nebelspuren für umsonst. Unzertifizierte Amateure."),
    ("faction_05.mp3", "de-AT-JonasNeural", "-5Hz", "+2%",
     "Ab morgen verkaufen wir dieselbe Spur als Platin-Geleitschutz. Mit Stempel."),

    ("faction_06.mp3", "de-AT-JonasNeural", "+0Hz", "+0%",
     "Mein letzter Sleeve hatte einen Kratzer am Handgelenk. Hab ihn auf Morgenlicht tauschen lassen."),
    ("faction_07.mp3", "de-DE-TanjaNeural", "-2Hz", "+2%",
     "Dekadent. Meine Police deckt erst ab Totalverlust – oder Gürtelkontakt."),

    ("faction_08.mp3", "de-DE-ChristophNeural", "-3Hz", "+6%",
     "Kapper auf Sektorband. Sollen die Drohnen eingreifen?"),
    ("faction_09.mp3", "de-AT-IngridNeural", "-4Hz", "+1%",
     "Negativ. Solange sie nur Gürtelfrachter plündern, sind das unbezahlte Marktregulatoren."),

    ("faction_10.mp3", "de-AT-JonasNeural", "+2Hz", "+8%",
     "Leitstand, die gestrichene Kestrel-Kennung funkt schon wieder auf dem Nebenband."),
    ("faction_11.mp3", "de-DE-ChristophNeural", "-7Hz", "+2%",
     "Ignorieren. Und erwähnen Sie das Analogband nicht noch einmal – die Direktion hört mit."),

    ("faction_12.mp3", "de-DE-ChristophNeural", "-6Hz", "+0%",
     "Schlepper ohne Platin-Bürgschaft halten fünfzig Meter Abstand zur Sperrlinie."),
    ("faction_13.mp3", "de-DE-TanjaNeural", "-3Hz", "+5%",
     "Verstanden. Wer die Linie kratzt, wird als Schrottgebühr verbucht."),

    # Civilian, Belt, Grey Watch & Kapper radio
    ("faction_14.mp3", "de-CH-JanNeural", "-7Hz", "+1%",
     "AETHER Tariff sieben steht wieder quer vor dem Tor. Für jeden Scan verlierst du zwei Stunden Sauerstoff."),
    ("faction_15.mp3", "de-DE-ChristophNeural", "-8Hz", "+2%",
     "Auf Drift-9 löten sie dir eine neue SIN aus Altmetall. Hält genau bis zur ersten Drohne."),
    ("faction_16.mp3", "de-AT-IngridNeural", "-4Hz", "+3%",
     "Zahlen in Zellen, nicht in Credits. Auf Raureif taut dir kein Konzernscheck den Verdampfer auf."),
    ("faction_17.mp3", "de-CH-JanNeural", "-9Hz", "-2%",
     "Wenn dir im Wracksaum ein Notruf zu sauber klingt, kapp die Leitung. Tote atmen nicht so ruhig."),
    ("faction_18.mp3", "de-CH-LeniNeural", "-2Hz", "+2%",
     "Baken sind gesetzt. Lasst euch am Schleierpass kein AETHER-Geleit aufschwatzen – die Spur ist unsere."),
    ("faction_19.mp3", "de-DE-ChristophNeural", "-4Hz", "+4%",
     "Achtung im Nebel: Zwei Drohnen ohne Transponder patrouillieren abseits der Baken."),
    ("faction_20.mp3", "de-AT-JonasNeural", "+3Hz", "+9%",
     "Kein AETHER-Stern in Reichweite. Der Erzfrachter da drüben gehört schon zur Hälfte uns."),
    ("faction_21.mp3", "de-CH-JanNeural", "-4Hz", "+7%",
     "Wirf die Ladung ab, Lizenzleiche, dann lassen wir dir genug Treibstoff bis Kestrel."),
    ("faction_22.mp3", "de-DE-TanjaNeural", "-1Hz", "+3%",
     "Analogband RT-9 meldet zwei identische Kennungen im selben Sektor. Prüft eure Transponder."),

    # Extra AETHER scorn dialogues & cruiser hail
    ("faction_23.mp3", "de-AT-IngridNeural", "-2Hz", "+3%",
     "Warum riecht jeder Schlepper aus dem Wracksaum nach verbranntem Isolierband?"),
    ("faction_24.mp3", "de-AT-JonasNeural", "-5Hz", "+3%",
     "Weil ihre SIN-Lizenzen genauso geflickt sind wie ihre Hüllen."),

    ("faction_25.mp3", "de-DE-TanjaNeural", "-3Hz", "+3%",
     "Kepler hat wieder einen Erzfrachter ohne Platin-Stempel abgewiesen."),
    ("faction_26.mp3", "de-DE-ChristophNeural", "-5Hz", "+2%",
     "Gut so. Sollen sie ihr Altmetall draußen im Gürtel rosten lassen."),

    ("faction_27.mp3", "de-DE-ChristophNeural", "-8Hz", "-1%",
     "Unzertifizierte Einheiten räumen den Anflugkorridor. Schäden an Lackierungen werden in Rechnung gestellt."),
]


async def synth_one(sem: asyncio.Semaphore, filename: str, voice: str, pitch: str, rate: str, text: str):
    async with sem:
        out_path = OUT_DIR / filename
        comm = edge_tts.Communicate(text, voice=voice, pitch=pitch, rate=rate)
        last_end = 0.0
        with out_path.open("wb") as f:
            async for chunk in comm.stream():
                if chunk["type"] == "audio":
                    f.write(chunk["data"])
                elif chunk["type"] == "WordBoundary":
                    end_sec = (chunk["offset"] + chunk["duration"]) / 1e7
                    if end_sec > last_end:
                        last_end = end_sec
        return filename, out_path.stat().st_size, last_end


async def main():
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    prefixes = sys.argv[1:]
    selected = [r for r in CLIPS if not prefixes or any(r[0].startswith(p) for p in prefixes)]
    sem = asyncio.Semaphore(4)
    results = await asyncio.gather(*(synth_one(sem, *row) for row in selected))
    for name, size, _ in results:
        print(f"{name:34s} {size:7d} B  ~{size / 6000:4.2f}s")
    print(f"Generated {len(results)} clips in {OUT_DIR}")


if __name__ == "__main__":
    asyncio.run(main())
