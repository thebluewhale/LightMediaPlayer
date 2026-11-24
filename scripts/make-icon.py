"""Generate the Windows application icon from the project artwork."""
from pathlib import Path

from PIL import Image


assets = Path(__file__).resolve().parents[1] / "src/LightMediaPlayer/Assets"
sizes = [(n, n) for n in (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)]
with Image.open(assets / "mediaplayericon.png") as source:
    source.convert("RGBA").save(assets / "player.ico", sizes=sizes)
