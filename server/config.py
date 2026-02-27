import os

DATABASE_URL   = os.getenv("DATABASE_URL", "sqlite:///./voxcharger.db")
MUSIC_ID_START = int(os.getenv("MUSIC_ID_START", "2000"))
