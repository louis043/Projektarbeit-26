import sys, json
from pathlib import Path
from transformers import pipeline
from transformers.utils import logging

logging.set_verbosity_error()

if len(sys.argv) < 3:
    print("Usage: python map_caption.py <input_json> <output_json>")
    sys.exit(1)

in_path = Path(sys.argv[1])
out_path = Path(sys.argv[2])

data = json.loads(in_path.read_text(encoding="utf-8"))
caption = (data.get("caption") or "").strip()

# Zero-shot classifier
clf = pipeline("zero-shot-classification", model="facebook/bart-large-mnli")

MOODS = ["calm", "happy", "sad", "dark", "energetic", "dramatic", "neutral"]
GENRES = ["ambient", "cinematic", "electronic", "pop", "rock", "jazz", "classical", "lofi", "folk"]

mood_res = clf(caption, MOODS, multi_label=False)
genre_res = clf(caption, GENRES, multi_label=False)

mood = mood_res["labels"][0]
genre = genre_res["labels"][0]

# Tempo + Instrumente
TEMPO_BY_MOOD = {
    "calm": 75,
    "happy": 110,
    "sad": 70,
    "dark": 75,
    "energetic": 130,
    "dramatic": 85,
    "neutral": 100,
}
INSTR_BY_GENRE = {
    "ambient": ["pads", "soft synth"],
    "cinematic": ["strings", "pads", "low brass"],
    "electronic": ["drums", "synth", "bass"],
    "pop": ["drums", "bass", "keys"],
    "rock": ["drums", "electric guitar", "bass"],
    "jazz": ["piano", "upright bass", "drums"],
    "classical": ["strings", "piano"],
    "lofi": ["vinyl noise", "piano", "soft drums"],
    "folk": ["acoustic guitar", "light percussion"],
}

tempo_bpm = TEMPO_BY_MOOD.get(mood, 100)
instruments = INSTR_BY_GENRE.get(genre, ["pads"])

data["music"] = {
    "mood": mood,
    "genre": genre,
    "tempo_bpm": tempo_bpm,
    "instruments": instruments,
    "debug": {
        "mood_scores": dict(zip(mood_res["labels"], mood_res["scores"])),
        "genre_scores": dict(zip(genre_res["labels"], genre_res["scores"])),
    }
}

out_path.parent.mkdir(parents=True, exist_ok=True)
out_path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
print("Mapped music:", data["music"]["mood"], data["music"]["genre"], data["music"]["tempo_bpm"])
