from dotenv import load_dotenv
load_dotenv()

from fastapi import FastAPI

from database import Base, engine
from routes import mixes_router, songs_router

Base.metadata.create_all(bind=engine)

app = FastAPI(title="VoxCharger Mix Server")
app.include_router(mixes_router)
app.include_router(songs_router)
