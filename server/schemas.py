from datetime import datetime
from typing import List, Optional

from pydantic import BaseModel


class SongCreate(BaseModel):
    url: str
    title: Optional[str] = None
    artist: Optional[str] = None


class SongResponse(BaseModel):
    id: int
    music_id: int
    url: str
    title: Optional[str]
    artist: Optional[str]

    model_config = {"from_attributes": True}


class MixCreate(BaseModel):
    name: str
    music_id_start: int = 2000


class MixResponse(BaseModel):
    id: int
    name: str
    music_id_start: int
    created_at: datetime
    songs: List[SongResponse]

    model_config = {"from_attributes": True}


class MixListItem(BaseModel):
    id: int
    name: str
    music_id_start: int
    created_at: datetime
    song_count: int
