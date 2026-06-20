from __future__ import annotations
import asyncio
import json
import logging
import os
import shutil
from contextlib import asynccontextmanager
from typing import List, Optional
import chromadb
from fastapi import Depends, FastAPI, HTTPException, status
from sqlalchemy.orm import Session
from database import Base, engine, get_db, write_log
from table_schemas import UserQuery, EvalMetric
from pydantic_schemas import ProbeRequest, EvalRequest
from retrieval_agent import (
    get_llm,
    get_rag_retriever,
    ingest_pdfs,
    ingest_probe_components,
    rag_query
)

logging.basicConfig(level=logging.INFO,format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",datefmt="%H:%M:%S",force=True)

for _mod in ("main", "document_handler", "retrieval_agent", "database"):
    logging.getLogger(_mod).setLevel(logging.INFO)

logger = logging.getLogger(__name__)

PDF_FOLDER = os.path.join(os.path.dirname(__file__), "data", "pdfs")
JSON_FOLDER = os.path.join(os.path.dirname(__file__), "data", "json")

def run_startup_ingest() -> None:

    logger.info("Startup: running initial PDF ingestion from %s …", PDF_FOLDER)
    db = next(get_db())
    try:
        result = ingest_pdfs(db, pdf_folder=PDF_FOLDER)
        write_log(
            db,
            event_type="startup_ingest",
            level="info",
            payload={
                "total": result["ingested_documents"],
                "documents": [
                    {"title": d["title"], "status": d["status"]}
                    for d in result["documents"]
                ],
            },
        )
        logger.info("Startup ingestion complete: %d file(s) processed.",result["ingested_documents"])
    except Exception as exc:
        logger.error("Startup ingestion failed: %s", exc, exc_info=True)
    finally:
        db.close()


@asynccontextmanager
async def lifespan(app: FastAPI):
    Base.metadata.create_all(bind=engine)
    os.makedirs(PDF_FOLDER, exist_ok=True)
    os.makedirs(JSON_FOLDER, exist_ok=True)

    if os.getenv("STARTUP_INGEST_ENABLED", "false").strip().lower() == "true":
        asyncio.create_task(asyncio.to_thread(run_startup_ingest))
        logger.info("Startup ingestion launched in background.")
    else:
        logger.info("Startup ingestion disabled.")

    yield


app = FastAPI(title="RAG System API", version="1.0", lifespan=lifespan)


# ask GET
@app.get("/ask")
async def ask(question: str,machine: str | None = None,db: Session = Depends(get_db)):
    result = rag_query(question, get_rag_retriever(), get_llm(),top_k=3, min_score=0.1,user_id=None,machine_name=machine,db=db)
    with open(os.path.join(JSON_FOLDER, "output.json"), "w", encoding="utf-8") as f:
        json.dump(result, f, indent=2)
    return result


# probe POST
@app.post("/probe")
def probe_machine(data: ProbeRequest, db: Session = Depends(get_db)):
    from table_schemas import Machine

    logger.info("/probe: received probe for machine=%r with %d component(s).",
                data.machine, len(data.components))

    probe_data = {
        "machine": data.machine,
        "components": [c.model_dump() for c in data.components],
    }
    with open(os.path.join(JSON_FOLDER, "probe_data.json"), "w", encoding="utf-8") as f:
        json.dump(probe_data, f, indent=2)
    logger.info("/probe: wrote probe_data.json for %r.", data.machine)

    try:
        ingest_result = ingest_probe_components(db, json.dumps(probe_data), machine_name=data.machine, sync_machine_parts=True)
    except Exception as exc:
        logger.error("/probe: ingest_probe_components raised for machine=%r: %s",data.machine, exc, exc_info=True)
        raise

    logger.info("/probe: ingest_probe_components returned status=%r, components_embedded=%d.",ingest_result.get("status"), ingest_result.get("component_count", 0))

    machine = db.query(Machine).filter(Machine.name == data.machine).first()

    write_log(db, event_type="probe_updated", level="info",
              payload={
                  "machine": data.machine,
                  "parts": len(data.components),
                  "components_embedded": ingest_result.get("component_count", 0),
              })
    logger.info("/probe: done for machine=%r (machine_id=%s, total_parts_in_db=%d).",data.machine, machine.id if machine else None,len(machine.parts) if machine else 0)
    return {
        "machine": machine.name,
        "machine_id": machine.id,
        "components_received": probe_data["components"],
        "total_parts_in_db": len(machine.parts),
        "components_embedded": ingest_result.get("component_count", 0),
        "ingest_status": ingest_result.get("status"),
    }

# eval POST
@app.post("/eval", status_code=status.HTTP_201_CREATED)
def submit_eval(data: EvalRequest, db: Session = Depends(get_db)):

    if data.query_id:
        query_exists = db.get(UserQuery, data.query_id)
        if not query_exists:
            raise HTTPException(status_code=status.HTTP_404_NOT_FOUND,detail=f"UserQuery '{data.query_id}' not found.")

    existing: EvalMetric | None = None
    if data.query_id:
        existing = (db.query(EvalMetric).filter(EvalMetric.query_id == data.query_id).first())

    metric_fields = data.model_dump(exclude={"query_id"}, exclude_none=True)

    if existing:
        for field, value in metric_fields.items():
            setattr(existing, field, value)
        db.commit()
        db.refresh(existing)
        record = existing
    else:
        record = EvalMetric(query_id=data.query_id, **metric_fields)
        db.add(record)
        db.commit()
        db.refresh(record)

    write_log(db,event_type="eval_submitted",level="info",query_id=data.query_id,payload={"eval_id": record.id})

    return {
        "eval_id": record.id,
        "query_id": record.query_id,
        "created_at": record.created_at.isoformat(),
    }


if __name__ == "__main__":
    import uvicorn
    uvicorn.run("main:app", host="0.0.0.0", port=8000, reload=False)
