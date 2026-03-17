import sys, json
from pathlib import Path
import numpy as np
import scipy.io.wavfile
from transformers import pipeline
from transformers.utils import logging

logging.set_verbosity_error()

if len(sys.argv) < 3:
    print("Usage: python musicgen.py <input_mapped_json> <output_wav_base>")
    sys.exit(1)

in_path = Path(sys.argv[1])
out_wav_base = Path(sys.argv[2])

data = json.loads(in_path.read_text(encoding="utf-8"))
caption = (data.get("caption") or "").strip()

music = data.get("music") or {}
mood = music.get("mood", "neutral")
genre = music.get("genre", "ambient")
tempo = music.get("tempo_bpm", 100)
instruments = music.get("instruments", ["pads"])

inst_text = ", ".join(instruments) if isinstance(instruments, list) else str(instruments)
control_text = (
    f"{genre} music, {mood} mood, tempo {tempo} bpm, instruments: {inst_text}. "
    f"Scene: {caption}."
)

# MusicGen laden
synth = pipeline("text-to-audio", model="facebook/musicgen-small")

out_wav_base.parent.mkdir(parents=True, exist_ok=True)

# base name ohne Endung
base_stem = out_wav_base.stem
base_dir = out_wav_base.parent

N_VERSIONS = 3

for i in range(1, N_VERSIONS + 1):
    out = synth(
        control_text,
        forward_params={
            "do_sample": True,
            "max_new_tokens": 256
        }
    )

    sr = int(out["sampling_rate"])
    audio = np.asarray(out["audio"], dtype=np.float32)

    out_path = base_dir / f"{base_stem}_v{i}.wav"
    scipy.io.wavfile.write(out_path.as_posix(), sr, audio)

    print("OK WAV:", out_path)

print("Control:", control_text)
