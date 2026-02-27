from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy import func
from sqlalchemy.orm import Session

from config import MUSIC_ID_START
from database import get_db
from models import Mix, Song
from schemas import SongCreate, SongResponse

router = APIRouter(prefix="/mixes/{mix_id}/songs", tags=["songs"])


def _next_music_id(db: Session, mix: Mix) -> int:
    # Start from the mix's configured starting ID, but never collide globally
    floor = mix.music_id_start
    max_id = db.query(func.max(Song.music_id)).scalar()
    return max(floor, (max_id or floor - 1) + 1)


@router.post("", response_model=SongResponse, status_code=201)
def add_song(mix_id: int, body: SongCreate, db: Session = Depends(get_db)):
    mix = db.query(Mix).filter(Mix.id == mix_id).first()
    if not mix:
        raise HTTPException(status_code=404, detail="Mix not found")

    # Reuse existing song if the same URL was added before (preserves music_id)
    song = db.query(Song).filter(Song.url == body.url).first()
    if not song:
        song = Song(
            music_id=_next_music_id(db, mix),
            url=body.url,
            title=body.title,
            artist=body.artist,
        )
        db.add(song)
        db.flush()
    else:
        # Update metadata if it was missing
        if body.title and not song.title:
            song.title = body.title
        if body.artist and not song.artist:
            song.artist = body.artist

    if song in mix.songs:
        raise HTTPException(status_code=409, detail="Song already in this mix")

    mix.songs.append(song)
    db.commit()
    db.refresh(song)
    return song


@router.delete("/{song_id}", status_code=204)
def remove_song(mix_id: int, song_id: int, db: Session = Depends(get_db)):
    mix = db.query(Mix).filter(Mix.id == mix_id).first()
    if not mix:
        raise HTTPException(status_code=404, detail="Mix not found")

    song = db.query(Song).filter(Song.id == song_id).first()
    if not song or song not in mix.songs:
        raise HTTPException(status_code=404, detail="Song not found in this mix")

    mix.songs.remove(song)
    db.commit()
