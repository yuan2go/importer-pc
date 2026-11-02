"""Deterministic original PCM cues; no sampled/third-party sound assets."""
import math
import struct
import wave
from pathlib import Path

RATE = 22050
SCORES = {
    "read": [(880, .075), (0, .025)],
    "success": [(660, .09), (0, .04), (990, .15)],
    "rejected": [(330, .16), (0, .07), (330, .16), (0, .07), (220, .20)],
}
target = Path(__file__).resolve().parents[1] / "src/Ipc.Presentation.Wpf/Sounds"
target.mkdir(parents=True, exist_ok=True)
for name, score in SCORES.items():
    samples = []
    for frequency, seconds in score:
        count = round(RATE * seconds)
        for n in range(count):
            envelope = min(1, n / (RATE * .008), (count - 1 - n) / (RATE * .012))
            samples.append(round(14000 * envelope * math.sin(2 * math.pi * frequency * n / RATE)) if frequency else 0)
    with wave.open(str(target / f"ipc-{name}.wav"), "wb") as output:
        output.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        output.writeframes(struct.pack(f"<{len(samples)}h", *samples))
