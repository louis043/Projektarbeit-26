import json
from pathlib import Path
import re
import numpy as np
import pandas as pd
import librosa
import sys
from datetime import datetime

base_filter = sys.argv[1] if len(sys.argv) > 1 else None
run_id = sys.argv[2] if len(sys.argv) > 2 else datetime.now().strftime("%Y%m%d_%H%M%S")

ROOT = Path(__file__).resolve().parents[1]
OUT_DIR = ROOT / "data" / "outputs"
RES_DIR = ROOT / "data" / "results"

CSV_OUT = RES_DIR / f"analysis_{base_filter or 'all'}_{run_id}.csv"
comp_csv = RES_DIR / f"comparison_{base_filter or 'all'}_{run_id}.csv"
report_md = RES_DIR / f"comparison_report_{base_filter or 'all'}_{run_id}.md"


def safe_tempo(y, sr):
    try:
        tempo, _ = librosa.beat.beat_track(y=y, sr=sr)
        return float(np.squeeze(tempo))
    except Exception:
        return np.nan

rows = []

pattern = f"{base_filter}_v*.wav" if base_filter else "*.wav"
for wav_path in sorted(OUT_DIR.glob(pattern)):
    name = wav_path.stem
    mapped_json = RES_DIR / f"{name}_mapped.json"

    # Audio laden
    y, sr = librosa.load(wav_path, sr=None, mono=True)
    duration = float(librosa.get_duration(y=y, sr=sr))

    # Features
    rms = float(np.mean(librosa.feature.rms(y=y)))
    centroid = float(np.mean(librosa.feature.spectral_centroid(y=y, sr=sr)))
    tempo_est = safe_tempo(y, sr)

    # Optional: MFCC “Fingerprint”
    mfcc = librosa.feature.mfcc(y=y, sr=sr, n_mfcc=13)
    mfcc_mean = np.mean(mfcc, axis=1)

    # Mapped-Daten laden (falls vorhanden)
    mood = genre = None
    tempo_target = None
    if mapped_json.exists():
        data = json.loads(mapped_json.read_text(encoding="utf-8"))
        music = data.get("music", {})
        mood = music.get("mood")
        genre = music.get("genre")
        tempo_target = music.get("tempo_bpm")

    row = {
        "file": wav_path.name,
        "name": name,
        "sr": sr,
        "duration_s": duration,
        "rms": rms,
        "spectral_centroid": centroid,
        "tempo_est_bpm": tempo_est,
        "tempo_target_bpm": tempo_target,
        "mood": mood,
        "genre": genre,
    }

    # MFCC-Spalten hinzufügen
    for i, v in enumerate(mfcc_mean, start=1):
        row[f"mfcc{i}_mean"] = float(v)

    rows.append(row)

df = pd.DataFrame(rows)


def parse_version_from_filename(filename: str):
    # erwartet sowas wei test_v2.wav
    stem = Path(filename).stem
    m = re.match(r"^(.*)_v(\d+)$", stem)
    if m:
        return m.group(1), int(m.group(2))
    return stem, None

df[["base_name", "version"]] = df["file"].apply(
    lambda f: pd.Series(parse_version_from_filename(f))
)

RES_DIR.mkdir(parents=True, exist_ok=True)
df.to_csv(CSV_OUT, index=False)
print("Wrote:", CSV_OUT)

# Vergleich pro base_name
comp_rows = []
report_lines = ["# Version Comparison Report\n"]

for base, g in df.groupby("base_name"):
    g = g.sort_values("version")

    # nur vergleichen, wenn wirklich mehrere Versionen da sind
    if g["version"].notna().sum() < 2:
        continue

    # einfache, gut erklärbare Kennzahlen
    rms_min, rms_max = g["rms"].min(), g["rms"].max()
    cen_min, cen_max = g["spectral_centroid"].min(), g["spectral_centroid"].max()
    tmp_min, tmp_max = g["tempo_est_bpm"].min(), g["tempo_est_bpm"].max()

    # Gewinner pro Kennzahl
    loudest = g.loc[g["rms"].idxmax()]
    brightest = g.loc[g["spectral_centroid"].idxmax()]
    fastest = g.loc[g["tempo_est_bpm"].idxmax()] if g["tempo_est_bpm"].notna().any() else None

    comp_rows.append({
        "base_name": base,
        "versions_found": ",".join(str(int(v)) for v in g["version"].dropna().astype(int).tolist()),
        "rms_range": float(rms_max - rms_min),
        "centroid_range": float(cen_max - cen_min),
        "tempo_range": float(tmp_max - tmp_min) if np.isfinite(tmp_min) and np.isfinite(tmp_max) else np.nan,
        "loudest_version": int(loudest["version"]) if pd.notna(loudest["version"]) else None,
        "brightest_version": int(brightest["version"]) if pd.notna(brightest["version"]) else None,
        "fastest_version": int(fastest["version"]) if fastest is not None and pd.notna(fastest["version"]) else None,
    })

    # Lesbarer Abschnitt fürs Report
    report_lines.append(f"## {base}\n")
    report_lines.append("| file | version | rms (energy) | spectral_centroid (brightness) | tempo_est_bpm |\n")
    report_lines.append("|---|---:|---:|---:|---:|\n")
    for _, r in g.iterrows():
        report_lines.append(
            f"| {r['file']} | {int(r['version']) if pd.notna(r['version']) else ''} | "
            f"{r['rms']:.4f} | {r['spectral_centroid']:.1f} | "
            f"{'' if pd.isna(r['tempo_est_bpm']) else f'{r['tempo_est_bpm']:.1f}'} |\n"
        )

    report_lines.append("\n**Highlights:**\n")
    report_lines.append(f"- Loudest (highest RMS): **v{int(loudest['version'])}**\n")
    report_lines.append(f"- Brightest (highest centroid): **v{int(brightest['version'])}**\n")
    if fastest is not None and pd.notna(fastest["version"]):
        report_lines.append(f"- Fastest (highest tempo estimate): **v{int(fastest['version'])}**\n")
    report_lines.append("\n")
    report_lines.append(f"- RMS range (max-min): **{(rms_max - rms_min):.4f}**\n")
    report_lines.append(f"- Centroid range (max-min): **{(cen_max - cen_min):.1f} Hz**\n")
    if np.isfinite(tmp_min) and np.isfinite(tmp_max):
        report_lines.append(f"- Tempo range (max-min): **{(tmp_max - tmp_min):.1f} BPM**\n")
    report_lines.append("\n---\n\n")

# Dateien schreiben
comp_df = pd.DataFrame(comp_rows)

comp_df.to_csv(comp_csv, index=False)
report_md.write_text("".join(report_lines), encoding="utf-8")
