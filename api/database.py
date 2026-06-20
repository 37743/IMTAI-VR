from __future__ import annotations

import json
import os

from dotenv import load_dotenv
from sqlalchemy import create_engine
from sqlalchemy.orm import DeclarativeBase, Session, sessionmaker

load_dotenv()

BASE_DIR = os.path.dirname(__file__)
DATABASE_DIR = os.path.join(BASE_DIR, "data", "database")
os.makedirs(DATABASE_DIR, exist_ok=True)

DATABASE_URL = os.getenv("DATABASE_URL", f"sqlite:///{os.path.join(DATABASE_DIR, 'rag_system.db')}")

engine = create_engine(DATABASE_URL, connect_args={"check_same_thread": False} if "sqlite" in DATABASE_URL else {},echo=False)
SessionLocal = sessionmaker(bind=engine, autoflush=False, autocommit=False)


class Base(DeclarativeBase):
    pass


def get_db() -> Session:
    db = SessionLocal()
    try:
        yield db
    finally:
        db.close()


from table_schemas import Log


def write_log(
    db: Session,
    event_type: str,
    level: str = "info",
    user_id: str | None = None,
    query_id: str | None = None,
    payload: dict | None = None,
    error_message: str | None = None,
    duration_ms: int | None = None,
):
    db.add(Log(
        user_id=user_id,
        query_id=query_id,
        event_type=event_type,
        level=level,
        payload=json.dumps(payload) if payload else None,
        error_message=error_message,
        duration_ms=duration_ms
    ))
    db.commit()
