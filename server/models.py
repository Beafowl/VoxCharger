from datetime import datetime, timezone

from sqlalchemy import Column, Integer, String, DateTime, ForeignKey, Table
from sqlalchemy.orm import relationship

from database import Base

mix_songs = Table(
    "mix_songs",
    Base.metadata,
    Column("mix_id", Integer, ForeignKey("mixes.id", ondelete="CASCADE"), primary_key=True),
    Column("song_id", Integer, ForeignKey("songs.id", ondelete="CASCADE"), primary_key=True),
)


class Mix(Base):
    __tablename__ = "mixes"

    id             = Column(Integer, primary_key=True, index=True)
    name           = Column(String, unique=True, nullable=False)
    music_id_start = Column(Integer, nullable=False, default=2000)
    created_at     = Column(DateTime, default=lambda: datetime.now(timezone.utc))

    songs = relationship("Song", secondary=mix_songs, back_populates="mixes", lazy="joined")


class Song(Base):
    __tablename__ = "songs"

    id         = Column(Integer, primary_key=True, index=True)
    music_id   = Column(Integer, unique=True, nullable=False)
    url        = Column(String, unique=True, nullable=False)
    title      = Column(String, nullable=True)
    artist     = Column(String, nullable=True)
    created_at = Column(DateTime, default=lambda: datetime.now(timezone.utc))

    mixes = relationship("Mix", secondary=mix_songs, back_populates="songs")
