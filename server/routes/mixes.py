from typing import List

from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy.orm import Session

from database import get_db
from models import Mix
from schemas import MixCreate, MixResponse, MixListItem

router = APIRouter(prefix="/mixes", tags=["mixes"])


@router.get("", response_model=List[MixListItem])
def list_mixes(db: Session = Depends(get_db)):
    mixes = db.query(Mix).all()
    return [
        MixListItem(
            id=m.id,
            name=m.name,
            music_id_start=m.music_id_start,
            created_at=m.created_at,
            song_count=len(m.songs),
        )
        for m in mixes
    ]


@router.post("", response_model=MixResponse, status_code=201)
def create_mix(body: MixCreate, db: Session = Depends(get_db)):
    if db.query(Mix).filter(Mix.name == body.name).first():
        raise HTTPException(status_code=409, detail="Mix already exists")

    mix = Mix(name=body.name, music_id_start=body.music_id_start)
    db.add(mix)
    db.commit()
    db.refresh(mix)
    return mix


@router.get("/{mix_id}", response_model=MixResponse)
def get_mix(mix_id: int, db: Session = Depends(get_db)):
    mix = db.query(Mix).filter(Mix.id == mix_id).first()
    if not mix:
        raise HTTPException(status_code=404, detail="Mix not found")
    return mix


@router.delete("/{mix_id}", status_code=204)
def delete_mix(mix_id: int, db: Session = Depends(get_db)):
    mix = db.query(Mix).filter(Mix.id == mix_id).first()
    if not mix:
        raise HTTPException(status_code=404, detail="Mix not found")
    db.delete(mix)
    db.commit()
