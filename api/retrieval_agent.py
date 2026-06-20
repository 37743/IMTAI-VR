from __future__ import annotations
import math
import hashlib
import json
import logging
import os
import time
import uuid
from typing import Any, Dict, List, Optional
import re
from sqlalchemy.orm import Session
from database import SessionLocal, write_log
from document_handler import (ElementType,
    get_machine_parts_dict,
    persist_query_result,
    process_pdf_folder,
    register_chunks,
    register_document,
)

logger = logging.getLogger(__name__)
from table_schemas import DocumentChunk, Machine

logger = logging.getLogger(__name__)


def sigmoid(x: float) -> float:
    return 1 / (1 + math.exp(-x))


def multiple_replace(replacements: Dict[re.Pattern, str], text: str) -> str:

    for pattern, replacement in replacements.items():
        text = pattern.sub(replacement, text)
    return text

class EmbeddingManager:
    def __init__(self, model_name: str = "all-MiniLM-L6-v2", device: str | None = None):
        self.model_name = model_name
        from sentence_transformers import SentenceTransformer

        if device is None:
            device = os.getenv("EMBEDDING_DEVICE")

        if device is None:
            try:
                import torch
                device = "cuda" if torch.cuda.is_available() else "cpu"
            except ImportError:
                device = "cpu"

        if device == "cuda":
            try:
                import torch
                gpu_name = torch.cuda.get_device_name(0)
                logger.info("EmbeddingManager: loading '%s' on GPU (%s)", model_name, gpu_name)
            except Exception:
                logger.info("EmbeddingManager: loading '%s' on CUDA", model_name)
        else:
            logger.info("EmbeddingManager: loading '%s' on CPU" ,model_name)

        self.device = device
        self.model = SentenceTransformer(model_name, device=device)

    def gen_embeds(self, texts: List[str]):
        import numpy as np
        return np.asarray(self.model.encode(texts, show_progress_bar=True))


class VectorStore:
    def __init__(self, collection_name: str = "documents",
                 persist_directory: str | None = None):
        import chromadb
        self.collection_name = collection_name
        if persist_directory is None:
            persist_directory = os.path.join(os.path.dirname(__file__), "data", "vector_store")
        self.persist_directory = persist_directory
        os.makedirs(persist_directory, exist_ok=True)
        self.client = chromadb.PersistentClient(path=persist_directory)
        self.collection = self.client.get_or_create_collection(
            name=collection_name,
            metadata={"description": f"{collection_name} embeddings"},)

    def add_documents(self, elements: List[Any], embeddings) -> List[str]:

        ids, metadatas, texts, embed_list = [], [], [], []
        for i, (el, emb) in enumerate(zip(elements, embeddings)):
            doc_id = f"{uuid.uuid4().hex[:8]}_{i}"
            ids.append(doc_id)
            meta = {
                "source_file": el.source_file,
                "page": el.page_number,
                "element_type": el.element_type.value,
                "doc_index": i,
                "content_length": len(el.summary),
            }
            metadatas.append(meta)
            texts.append(el.summary)
            embed_list.append(emb.tolist())

        self.collection.add(ids=ids, embeddings=embed_list,
                            metadatas=metadatas, documents=texts)
        return ids

    def upsert_documents(self,ids: List[str],texts: List[str],metadatas: List[Dict[str, Any]],embeddings) -> List[str]:

        embed_list = [(emb.tolist() if hasattr(emb, "tolist") else list(emb))for emb in embeddings]
        self.collection.upsert(ids=ids, embeddings=embed_list,metadatas=metadatas, documents=texts)
        return ids


class BM25Index:
    def __init__(self) -> None:
        self._corpus: List[str] = []
        self._ids: List[str] = []
        self._metadatas: List[Dict[str, Any]] = []
        self._bm25 = None

    def build(self,ids: List[str],documents: List[str],metadatas: List[Dict[str, Any]]) -> None:
        try:
            from rank_bm25 import BM25Okapi
        except ImportError as exc:
            raise RuntimeError("rank_bm25 is not installed.") from exc

        self._ids = ids
        self._corpus = documents
        self._metadatas = metadatas
        tokenised = [doc.lower().split() for doc in documents]
        self._bm25 = BM25Okapi(tokenised)
        logger.info("BM25Index: built index over %d documents.", len(ids))

    def query(self, query: str, top_k: int) -> List[Dict[str, Any]]:
        if self._bm25 is None:
            return []

        import numpy as np

        tokens = query.lower().split()
        scores: "np.ndarray" = self._bm25.get_scores(tokens)
        top_indices = scores.argsort()[::-1][:top_k]

        results = []
        for rank, idx in enumerate(top_indices):
            if scores[idx] <= 0:
                continue
            results.append({
                "id": self._ids[idx],
                "content": self._corpus[idx],
                "metadata": self._metadatas[idx],
                "bm25_score": float(scores[idx]),
                "rank": rank + 1,
            })
        return results

    def update(self,ids: List[str],documents: List[str],metadatas: List[Dict[str, Any]]) -> None:
        self.build(self._ids + ids,self._corpus + documents,self._metadatas + metadatas)


class CrossEncoderReranker:

    def __init__(self, model_name: str = "cross-encoder/ms-marco-MiniLM-L-6-v2") -> None:
        self.model_name = model_name
        self._model = None
        try:
            from sentence_transformers import CrossEncoder
            self._model = CrossEncoder(model_name)
            logger.info("CrossEncoderReranker: loaded '%s'.", model_name)
        except Exception as exc:
            logger.warning("CrossEncoderReranker: could not load '%s' (%s). ""Reranking will be skipped.",model_name, exc)

    def rerank(self,query: str,candidates: List[Dict[str, Any]],top_k: int) -> List[Dict[str, Any]]:
        if self._model is None or not candidates:
            return candidates[:top_k]

        pairs = [(query, c["content"]) for c in candidates]
        scores = self._model.predict(pairs)

        for cand, score in zip(candidates, scores):
            cand["rerank_score"] = float(score)

        reranked = sorted(candidates, key=lambda x: x["rerank_score"], reverse=True)

        for i, doc in enumerate(reranked):
            doc["rank"] = i + 1

        return reranked[:top_k]


class RAGRetriever:

    def __init__(self,vector_store: VectorStore,embedding_manager: EmbeddingManager,bm25_candidates: int = 20,vector_candidates: int = 20,rrf_k: int = 60,reranker_model: str | None = "cross-encoder/ms-marco-MiniLM-L-6-v2") -> None:

        self.vector_store = vector_store
        self.embedding_manager = embedding_manager
        self.bm25_candidates = bm25_candidates
        self.vector_candidates = vector_candidates
        self.rrf_k = rrf_k

        self._bm25_index = BM25Index()
        self._bm25_built = False

        self._reranker = (CrossEncoderReranker(reranker_model) if reranker_model else None)

    def _ensure_bm25(self) -> None:
        if self._bm25_built:
            return
        collection = self.vector_store.collection
        count = collection.count()
        if count == 0:
            logger.warning("RAGRetriever: vector store is empty; BM25 index not built.")
            return
        
        all_docs = collection.get(include=["documents", "metadatas"])
        self._bm25_index.build(
            ids=all_docs["ids"],
            documents=all_docs["documents"],
            metadatas=all_docs["metadatas"],
        )
        self._bm25_built = True

    def _vector_search(self, query: str, n: int, score_threshold: float) -> List[Dict[str, Any]]:
        query_embedding = self.embedding_manager.gen_embeds([query])[0]
        results = self.vector_store.collection.query(query_embeddings=[query_embedding.tolist()],n_results=n)
        retrieved: List[Dict[str, Any]] = []
        if results["documents"] and results["documents"][0]:
            for i, (doc_id, document, metadata, distance) in enumerate(zip(results["ids"][0], results["documents"][0],results["metadatas"][0], results["distances"][0])):
                sim = 1.0 - distance
                if sim >= score_threshold:
                    retrieved.append({
                        "id": doc_id,
                        "content": document,
                        "metadata": metadata,
                        "vector_score": sim,
                        "rank": i + 1,
                    })
        return retrieved

    @staticmethod
    def _reciprocal_rank_fusion(ranked_lists: List[List[Dict[str, Any]]],k: int = 60) -> List[Dict[str, Any]]:

        scores: Dict[str, float] = {}
        doc_store: Dict[str, Dict[str, Any]] = {}

        for ranked in ranked_lists:
            for doc in ranked:
                doc_id = doc["id"]
                rank = doc["rank"]
                scores[doc_id] = scores.get(doc_id, 0.0) + 1.0 / (k + rank)
                if doc_id not in doc_store:
                    doc_store[doc_id] = doc

        merged = sorted(doc_store.values(), key=lambda d: scores[d["id"]], reverse=True)
        for i, doc in enumerate(merged):
            doc["rrf_score"] = scores[doc["id"]]
            doc["similarity_score"] = scores[doc["id"]]
            doc["rank"] = i + 1
        return merged
    
    def invalidate_bm25(self) -> None:
        self._bm25_built = False

    def retrieve(self,query: str,top_k: int = 5,score_threshold: float = 0.0) -> List[Dict[str, Any]]:
        
        self._ensure_bm25()
        bm25_results = self._bm25_index.query(query, top_k=self.bm25_candidates)
        logger.debug("BM25 returned %d candidates.", len(bm25_results))

        vector_results = self._vector_search(query, n=self.vector_candidates, score_threshold=score_threshold)
        logger.debug("Vector search returned %d candidates.", len(vector_results))

        fused = self._reciprocal_rank_fusion([bm25_results, vector_results], k=self.rrf_k)
        logger.debug("After RRF fusion: %d unique candidates.", len(fused))

        if self._reranker is not None:
            rerank_pool = min(len(fused), max(top_k * 3, 10))
            fused = self._reranker.rerank(query, fused[:rerank_pool], top_k=top_k)
            logger.debug("After reranking: %d results.", len(fused))
        else:
            fused = fused[:top_k]

        return fused


_embedding_manager: EmbeddingManager | None = None
_vector_store: VectorStore | None = None
_probe_vector_store: VectorStore | None = None
_rag_retriever: RAGRetriever | None = None
_LLM: Any | None = None
_DOCUMENT_LLM: Any | None = None


def get_embedding_manager() -> EmbeddingManager:
    global _embedding_manager
    if _embedding_manager is None:
        _embedding_manager = EmbeddingManager()
    return _embedding_manager


def get_vector_store() -> VectorStore:
    global _vector_store
    if _vector_store is None:
        _vector_store = VectorStore()
    return _vector_store


def get_probe_vector_store() -> VectorStore:

    global _probe_vector_store
    if _probe_vector_store is None:
        _probe_vector_store = VectorStore(collection_name="probe_components")
    return _probe_vector_store


def get_rag_retriever(bm25_candidates: int = 20,vector_candidates: int = 20,rrf_k: int = 60,reranker_model: str | None = "cross-encoder/ms-marco-MiniLM-L-6-v2") -> RAGRetriever:
    global _rag_retriever
    if _rag_retriever is None:
        _rag_retriever = RAGRetriever(vector_store=get_vector_store(),embedding_manager=get_embedding_manager(),
                                      bm25_candidates=bm25_candidates,vector_candidates=vector_candidates,rrf_k=rrf_k,reranker_model=reranker_model)
    return _rag_retriever


def get_llm() -> Any:
    global _LLM

    if _LLM is None:
        from langchain_ollama import ChatOllama
        _LLM = ChatOllama(
            model=os.getenv("OLLAMA_MODEL"),
            base_url=os.getenv("OLLAMA_BASE_URL"),
            temperature=0.3,
            num_ctx=8192,
            max_tokens=4096)
    return _LLM


def get_document_llm() -> Any:
    global _DOCUMENT_LLM

    if _DOCUMENT_LLM is None:
        from langchain_ollama import ChatOllama
        _DOCUMENT_LLM = ChatOllama(
            model=os.getenv("DOCUMENT_LLM_MODEL"),
            base_url=os.getenv("OLLAMA_BASE_URL"),
            temperature=0.3,
            num_ctx=8192,
            max_tokens=4096,
        )
    return _DOCUMENT_LLM


def ingest_pdfs(db: Session,pdf_folder: str = "./data/pdfs",chunk_size: int = 1200,chunk_overlap: int = 150,embedding_model: str = "all-MiniLM-L6-v2") -> Dict[str, Any]:

    absolute_folder = os.path.abspath(pdf_folder)
    if not os.path.isdir(absolute_folder):
        raise FileNotFoundError(f"PDF folder not found: {absolute_folder}")

    llm = get_document_llm()
    file_elements_map = process_pdf_folder(absolute_folder, llm,chunk_size=chunk_size,chunk_overlap=chunk_overlap,use_vision=True)
    ingested: List[Dict[str, Any]] = []

    for file_path, elements in file_elements_map.items():
        try:
            document_row = register_document(db,file_path=file_path,title=os.path.basename(file_path),)

            existing_chunks = (db.query(DocumentChunk).filter(DocumentChunk.document_id == document_row.id).count())
            if existing_chunks:
                ingested.append({
                    "document_id": document_row.id,
                    "title": document_row.title,
                    "status": "skipped",
                    "existing_chunks": existing_chunks,
                })
                continue

            if not elements:
                ingested.append({
                    "document_id": document_row.id,
                    "title": document_row.title,
                    "status": "no_chunks",
                })
                continue
            logger.info("Embedding %d summaries for %s ...", len(elements), document_row.title)
            t0 = time.time()
            texts = [el.summary for el in elements]
            embeddings = get_embedding_manager().gen_embeds(texts)
            vector_ids = get_vector_store().add_documents(elements, embeddings)
            logger.info("Embedded and stored %d chunks for %s in %.1fs",len(elements), document_row.title, time.time() - t0)

            register_chunks(db, document_row.id, elements, vector_ids,model_name=embedding_model)
            get_rag_retriever().invalidate_bm25()

            type_counts = {t.value: 0 for t in ElementType}
            for el in elements:
                type_counts[el.element_type.value] += 1

            ingested.append({
                "document_id": document_row.id,
                "title": document_row.title,
                "status": "ingested",
                "chunk_count": len(elements),
                "element_types": type_counts,
            })

        except Exception as exc:
            logger.error("Ingestion failed for %s: %s", file_path, exc, exc_info=True)
            ingested.append({
                "title": os.path.basename(file_path),
                "status": "error",
                "error": str(exc),
            })
    return {"ingested_documents": len(ingested), "documents": ingested}


def ingest_single_pdf(db: Session,file_path: str,chunk_size: int = 1200,chunk_overlap: int = 150,embedding_model: str = "all-MiniLM-L6-v2") -> Dict[str, Any]:

    if not os.path.isfile(file_path):
        raise FileNotFoundError(f"PDF not found: {file_path}")

    from document_handler import process_pdf

    llm = get_llm()

    try:
        elements = process_pdf(file_path, llm, chunk_size=chunk_size, chunk_overlap=chunk_overlap, use_vision=True)
    except Exception as exc:
        logger.error("process_pdf failed for %s: %s", file_path, exc, exc_info=True)
        return {"title": os.path.basename(file_path), "status": "error", "error": str(exc)}

    try:
        document_row = register_document(db,file_path=file_path,title=os.path.basename(file_path))

        existing_chunks = (db.query(DocumentChunk).filter(DocumentChunk.document_id == document_row.id).count())
        if existing_chunks:
            return {
                "document_id": document_row.id,
                "title": document_row.title,
                "status": "skipped",
                "existing_chunks": existing_chunks,
            }

        if not elements:
            return {
                "document_id": document_row.id,
                "title": document_row.title,
                "status": "no_chunks",
            }

        logger.info("Embedding %d summaries for %s ...", len(elements), document_row.title)
        t0 = time.time()
        texts = [el.summary for el in elements]
        embeddings = get_embedding_manager().gen_embeds(texts)
        vector_ids = get_vector_store().add_documents(elements, embeddings)
        logger.info("Embedded and stored %d chunks for %s in %.1fs",len(elements), document_row.title, time.time() - t0)

        register_chunks(db, document_row.id, elements, vector_ids, model_name=embedding_model)
        get_rag_retriever().invalidate_bm25()

        type_counts = {t.value: 0 for t in ElementType}
        for el in elements:
            type_counts[el.element_type.value] += 1

        return {
            "document_id": document_row.id,
            "title": document_row.title,
            "status": "ingested",
            "chunk_count": len(elements),
            "element_types": type_counts,
        }

    except Exception as exc:
        logger.error("DB ingestion failed for %s: %s", file_path, exc, exc_info=True)
        return {"title": os.path.basename(file_path), "status": "error", "error": str(exc)}


def slugify_for_id(text: str) -> str:
    slug = re.sub(r"[^a-zA-Z0-9_-]+", "-", text.strip().lower())
    return re.sub(r"-{2,}", "-", slug).strip("-") or "unnamed"


def probe_component_doc_id(machine_name: str, component_name: str) -> str:
    return f"probe::{slugify_for_id(machine_name)}::{slugify_for_id(component_name)}"


def probe_component_content_hash(default_state: dict, possible_states: dict) -> str:
    payload = json.dumps({"default_state": default_state, "possible_states": possible_states},sort_keys=True)
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def probe_component_text(machine_name: str, name: str, default_state: dict, possible_states: dict) -> str:

    lines = [f"Component: {name}", f"Machine: {machine_name}"]
    if default_state:
        lines.append(f"Default state: {json.dumps(default_state, sort_keys=True)}")
    if possible_states:
        lines.append(f"Possible states: {json.dumps(possible_states, sort_keys=True)}")
    return "\n".join(lines)


def ingest_probe_components(db: Session,probe_data_json: str,machine_name: str,sync_machine_parts: bool = True) -> Dict[str, Any]:
    try:
        payload = json.loads(probe_data_json)
    except json.JSONDecodeError as exc:
        logger.error("ingest_probe_components: invalid probe_data_json: %s", exc)
        return {"status": "error", "error": f"invalid JSON: {exc}", "component_count": 0}

    raw_components = payload.get("components", [])
    if not raw_components:
        logger.warning("ingest_probe_components: no components in payload for machine=%r", machine_name)
        return {"status": "no_components", "component_count": 0}

    store = get_probe_vector_store()

    existing_hash_by_id: Dict[str, str] = {}
    try:
        existing = store.collection.get(
            where={"machine_name": machine_name},
            include=["metadatas"],
        )
        for doc_id, meta in zip(existing.get("ids", []), existing.get("metadatas", [])):
            existing_hash_by_id[doc_id] = (meta or {}).get("content_hash", "")
    except Exception as exc:
        logger.warning("ingest_probe_components: could not fetch existing entries for ""machine=%r (%s) will re-embed all submitted components.",machine_name, exc)

    to_embed_ids: List[str] = []
    to_embed_texts: List[str] = []
    to_embed_metadatas: List[Dict[str, Any]] = []
    skipped = 0
    component_names: List[str] = []

    for comp in raw_components:
        name = str(comp.get("name", "")).strip()
        if not name:
            logger.warning("ingest_probe_components: skipping component with empty name for machine=%r", machine_name)
            continue
        component_names.append(name)

        default_state = comp.get("default_state") or {}
        possible_states = comp.get("possible_states") or {}
        content_hash = probe_component_content_hash(default_state, possible_states)
        doc_id = probe_component_doc_id(machine_name, name)

        if existing_hash_by_id.get(doc_id) == content_hash:
            skipped += 1
            continue

        to_embed_ids.append(doc_id)
        to_embed_texts.append(probe_component_text(machine_name, name, default_state, possible_states))
        to_embed_metadatas.append({
            "machine_name": machine_name,
            "component_name": name,
            "default_state": json.dumps(default_state, sort_keys=True),
            "possible_states": json.dumps(possible_states, sort_keys=True),
            "content_hash": content_hash,
        })

    embedded = 0
    if to_embed_texts:
        embeddings = get_embedding_manager().gen_embeds(to_embed_texts)
        store.upsert_documents(
            ids=to_embed_ids, texts=to_embed_texts,
            metadatas=to_embed_metadatas, embeddings=embeddings,
        )
        embedded = len(to_embed_ids)
        logger.info(
            "ingest_probe_components: embedded/updated %d component(s) for "
            "machine=%r (%d skipped).",embedded, machine_name, skipped)
    else:
        logger.info("ingest_probe_components: all %d component(s) for machine=%r",skipped, machine_name)

    if sync_machine_parts and component_names:
        from document_handler import upsert_machine_from_probe
        upsert_machine_from_probe(db, machine_name, component_names)

    return {
        "status": "ok",
        "component_count": embedded + skipped,
        "embedded": embedded,
        "skipped_unchanged": skipped,
    }


def parse_components_safely(comp_raw: str) -> List[dict]:
    candidates = [comp_raw]

    candidates.append(re.sub(r",\s*([\]}])", r"\1", comp_raw))
    missing_braces = comp_raw.count("{") - comp_raw.count("}")
    missing_brackets = comp_raw.count("[") - comp_raw.count("]")
    if missing_braces > 0 or missing_brackets > 0:
        candidates.append(comp_raw + "}" * max(missing_braces, 0) + "]" * max(missing_brackets, 0))

    for cand in candidates:
        try:
            parsed = json.loads(cand)
            if isinstance(parsed, list):
                return parsed
        except json.JSONDecodeError:
            continue
    salvaged = []
    for obj_match in re.finditer(r"\{[^{}]*\}", comp_raw):
        try:
            salvaged.append(json.loads(obj_match.group(0)))
        except json.JSONDecodeError:
            continue
    return salvaged

def fetch_probe_components(machine_name: str) -> tuple[list[str], Dict[str, Dict[str, Any]], list[str]]:

    try:
        store = get_probe_vector_store()
        raw = store.collection.get(
            where={"machine_name": machine_name},
            include=["metadatas", "documents"],
        )
    except Exception as exc:
        logger.warning("_fetch_probe_components: get() failed for machine=%r: %s",machine_name, exc)
        return [], {}, []

    metadatas_list = raw.get("metadatas") or []
    documents_list = raw.get("documents") or []

    if not metadatas_list:
        return [], {}, []

    names: list[str] = []
    states_by_name: Dict[str, Dict[str, Any]] = {}
    documents_by_name: Dict[str, str] = {}

    for meta, doc in zip(metadatas_list, documents_list):
        name = (meta or {}).get("component_name")
        if not name or name in states_by_name:
            continue
        try:
            default_state = json.loads(meta.get("default_state") or "{}")
        except json.JSONDecodeError:
            default_state = {}
        try:
            possible_states = json.loads(meta.get("possible_states") or "{}")
        except json.JSONDecodeError:
            possible_states = {}
        names.append(name)
        states_by_name[name] = {
            "default_state": default_state,
            "possible_states": possible_states,
        }
        documents_by_name[name] = doc or ""

    ordered_docs = [documents_by_name[n] for n in names]
    return names, states_by_name, ordered_docs



def normalize_step_key(key: str) -> str:

    if not isinstance(key, str):
        key = str(key)
    k = key.strip()
    k = re.sub(r"(?i)^\**\s*step\s*", "", k)
    k = k.strip(" *")
    k = k.rstrip(":").strip()
    return k


def attribute_state_fields(raw_state: Any,matched: list[str],probe_component_states: Dict[str, Dict[str, Any]]) -> Optional[dict]:

    if not isinstance(raw_state, dict) or not raw_state:
        return None

    matched_set = set(matched)
    result: dict[str, Any] = {}

    def _attach(owner: str, field: Optional[str], value: Any) -> None:
        existing = result.get(owner)
        if field is None:
            result[owner] = value if existing is None else existing
            return
        if isinstance(existing, dict):
            existing[field] = value
        elif existing is not None:
            result[owner] = {"_state": existing, field: value}
        else:
            result[owner] = {field: value}

    for key, value in raw_state.items():
        if value is None:
            continue
        key_str = str(key).strip()

        if key_str in matched_set:
            _attach(key_str, None, value)
            continue

        owners = [
            c for c in matched
            if key_str in (probe_component_states.get(c, {}).get("possible_states") or {})
        ]
        if len(owners) == 1:
            _attach(owners[0], key_str, value)
            continue

        if len(matched) == 1:
            _attach(matched[0], key_str, value)
            continue

    return result or None


def load_prompt_template(path: str, fallback: str = "context\n{query}") -> str:
    if not os.path.isabs(path):
        path = os.path.join(os.path.dirname(__file__), "instructions", os.path.basename(path))
    try:
        with open(path, "r", encoding="utf-8") as f:
            return f.read()
    except FileNotFoundError:
        logger.error("%s not found", path)
        return fallback


def fill_prompt(template: str, *, context: str = "", probe_components_json: str = "", query: str = "", step_answer: str = "", step_text: str = "") -> str:
    placeholders = {
        re.compile(re.escape(r"<probe_components>")): probe_components_json,
        re.compile(re.escape(r"<probe_components_json>")): probe_components_json,
        re.compile(re.escape(r"<context>")): context,
        re.compile(re.escape(r"<query>")): query,
        re.compile(re.escape(r"<step_answer>")): step_answer,
        re.compile(re.escape(r"<step_text>")): step_text,
    }
    return multiple_replace(placeholders, template)


def strip_code_fences(raw: str) -> str:
    raw = re.sub(r"^```[a-zA-Z]*\n?", "", raw.strip())
    raw = re.sub(r"\n?```$", "", raw.strip())
    return raw.strip()


def llm_classify_and_answer(prompt_template: str,llm: Any,*,context: str,probe_components_json: str,query: str) -> tuple[str, str, Any]:

    prompt = fill_prompt(prompt_template, context=context, probe_components_json=probe_components_json, query=query)
    response = llm.invoke([prompt])
    raw = strip_code_fences(response.content)

    if "~" not in raw:
        raise ValueError("Missing '~' separator in pass-1 (classify) response")

    q_type, body = raw.split("~", 1)
    q_type = q_type.strip().lower().lstrip("`").rstrip("`")
    body = body.strip()
    return q_type, body, getattr(response, "response_metadata", None)


def llm_generate_steps(prompt_template: str,llm: Any,*,context: str,probe_components_json: str,query: str) -> tuple[str, Any]:

    prompt = fill_prompt(prompt_template, context=context, probe_components_json=probe_components_json, query=query)
    response = llm.invoke([prompt])
    raw = strip_code_fences(response.content)

    raw = re.sub(r"^[a-zA-Z]+\s*~\s*", "", raw)
    raw = raw.split("#", 1)[0].strip()
    return raw, getattr(response, "response_metadata", None)


def filter_probe_components_for_text(text: str,probe_components: list[str],probe_component_states: Dict[str, Dict[str, Any]],probe_component_docs: list[str]) -> tuple[list[str], Dict[str, Dict[str, Any]], list[str]]:

    if not probe_components:
        return [], {}, []

    present = [name for name in probe_components if name in text]
    if not present:
        return probe_components, probe_component_states, probe_component_docs

    present_set = set(present)
    filtered_states = {k: v for k, v in (probe_component_states or {}).items() if k in present_set}

    name_to_doc = dict(zip(probe_components, probe_component_docs)) if probe_component_docs else {}
    filtered_docs = [name_to_doc[name] for name in present if name in name_to_doc]

    return present, filtered_states, filtered_docs


def deterministic_extract_step_components(step_answer: str,probe_components: list[str],probe_component_states: Dict[str, Dict[str, Any]]) -> list[dict]:

    step_pattern = re.compile(r"(\*\*Step\s+\d+[a-zA-Z]?:\*\*.*?)(?=\*\*Step\s+\d+[a-zA-Z]?:\*\*|\Z)", re.DOTALL)
    matches = step_pattern.findall(step_answer)
    if not matches:
        return []

    probe_set = probe_components or list((probe_component_states or {}).keys())

    cleaned: list[dict] = []
    for i, raw_line in enumerate(matches, start=1):
        line = raw_line.strip()
        num_match = re.match(r"\*\*Step\s+(\d+[a-zA-Z]?):\*\*", line)
        step_num = num_match.group(1) if num_match else str(i)

        present = [name for name in probe_set if name in line]
        index_val = present or None

        state_val = None
        if present and probe_component_states:
            collected: dict[str, Any] = {}
            for name in present:
                comp_state_info = probe_component_states.get(name, {}) or {}
                possible = comp_state_info.get("possible_states") or {}
                matched_field = False
                for field, allowed in possible.items():
                    if allowed == "binary":
                        collected[name] = {field: True}
                        matched_field = True
                        break
                    elif isinstance(allowed, list):
                        for val in allowed:
                            if isinstance(val, str) and val and val in line:
                                collected[name] = {field: val}
                                matched_field = True
                                break
                    if matched_field:
                        break
            if collected:
                state_val = collected

        cleaned.append({
            "step": step_num,
            "step_text": line,
            "index": index_val,
            "state": state_val,
        })
    return cleaned


def humanize_step_text(step_text: str) -> str:

    prefix_match = re.match(r"(\*\*Step\s+\d+[a-zA-Z]?:\*\*\s*)", step_text)
    if prefix_match:
        prefix = prefix_match.group(1)
        body = step_text[prefix_match.end():]
    else:
        prefix = ""
        body = step_text

    def _split_token(token: str) -> str:
        s = re.sub(r"([a-zA-Z])(\d)", r"\1 \2", token)
        s = re.sub(r"(\d)([a-zA-Z])", r"\1 \2", s)
        s = re.sub(r"([a-z])([A-Z])", r"\1 \2", s)
        s = re.sub(r"([A-Z]+)([A-Z][a-z])", r"\1 \2", s)
        return s.lower()

    humanized_body = " ".join(_split_token(t) for t in body.split())
    return prefix + humanized_body





def parse_single_step_object(raw: str, step_num: str, step_text: str,probe_set: set, probe_lookup: dict,narrowed_states: Dict[str, Dict[str, Any]]) -> dict:
    try:
        obj = json.loads(strip_code_fences(raw))
    except json.JSONDecodeError:
        obj = parse_components_safely(raw)

    if not isinstance(obj, dict):
        return {"step": step_num, "step_text": step_text, "index": None, "state": None}

    raw_index = obj.get("index")
    matched: Optional[list[str]] = None
    if isinstance(raw_index, list):
        resolved = []
        for c in raw_index:
            if not isinstance(c, str):
                continue
            candidate = c.strip()
            resolved.append(
                candidate if candidate in probe_set
                else probe_lookup.get(candidate.lower())
            )
        valid = [c for c in resolved if c]
        matched = valid if valid else None

    state: Optional[dict[str, Any]] = None
    if matched:
        raw_state = obj.get("state") or {}
        if narrowed_states:
            state = attribute_state_fields(raw_state, matched, narrowed_states)
            if state:
                matched = [c for c in matched if c in state] or None
                if not matched:
                    state = None
            else:
                matched = None
        elif isinstance(raw_state, dict) and raw_state:
            state = raw_state
        else:
            matched = None

    return {
        "step": step_num,
        "step_text": step_text,
        "index": matched,
        "state": state,
    }


def llm_extract_step_components(prompt_template: str,step_answer: str,probe_components: list[str],probe_component_states: Dict[str, Dict[str, Any]],probe_component_docs: list[str],llm: Any) -> tuple[list[dict], Any]:

    if not step_answer.strip():
        return [], None

    step_pattern = re.compile(r"(\*\*Step\s+\d+[a-zA-Z]?:\*\*.*?)(?=\*\*Step\s+\d+[a-zA-Z]?:\*\*|\Z)",re.DOTALL)
    step_matches = step_pattern.findall(step_answer)
    if not step_matches:
        return [], None

    narrowed_components, narrowed_states, narrowed_docs = filter_probe_components_for_text(step_answer, probe_components, probe_component_states, probe_component_docs,)
    components_context = "\n\n".join(doc for doc in narrowed_docs if doc)
    probe_components_json = components_context or json.dumps(narrowed_components)

    probe_set = set(narrowed_states.keys()) if narrowed_states else set(narrowed_components)
    probe_lookup = {name.lower(): name for name in probe_set}

    results: list[dict] = []
    last_metadata = None

    from langchain_core.messages import HumanMessage

    for raw_line in step_matches:
        line = raw_line.strip()
        humanized_line = humanize_step_text(line)
        num_match = re.match(r"\*\*Step\s+(\d+[a-zA-Z]?):\*\*", line)
        step_num = num_match.group(1) if num_match else str(len(results) + 1)

        prompt_text = fill_prompt(prompt_template,probe_components_json=probe_components_json,step_text=line)
        raw = ""
        try:
            try:
                response = llm.invoke([HumanMessage(content=prompt_text)], format="json")
            except TypeError:
                response = llm.invoke([HumanMessage(content=prompt_text)])
            last_metadata = getattr(response, "response_metadata", None)
            raw = response.content
            cleaned = parse_single_step_object(raw, step_num, humanized_line, probe_set, probe_lookup, narrowed_states)
        except Exception as exc:
            logger.warning("Pass-3 per-step LLM call failed for step %s (%s).",step_num, exc)
            det = deterministic_extract_step_components(line, narrowed_components, narrowed_states)
            if det:
                det[0]["step_text"] = humanized_line
                cleaned = det[0]
            else:
                cleaned = {"step": step_num, "step_text": humanized_line, "index": None, "state": None}

        results.append(cleaned)

    matched_count = sum(1 for v in results if v.get("index"))
    if matched_count == 0:
        logger.warning("Pass-3 per-step LLM matched 0/%d step(s).",len(results))
        fallback = deterministic_extract_step_components(step_answer, narrowed_components, narrowed_states)
        if fallback:
            for entry in fallback:
                entry["step_text"] = humanize_step_text(entry["step_text"])
            return fallback, last_metadata
    else:
        logger.info("Pass-3 per-step extraction: matched %d/%d step(s) to probe components.",matched_count, len(results))
    return results, last_metadata


def rag_query(query: str,retriever: RAGRetriever,llm: Any,top_k: int = 3,min_score: float = 0.2,user_id: str | None = None,session_id: str | None = None,machine_name: str | None = None,db: Session | None = None) -> Dict[str, Any]:
    t_start = time.time()
    close_db = False

    if db is None:
        db = SessionLocal()
        close_db = True

    import re as _re
    query = _re.sub(r"\bthis machine\b", machine_name, query, flags=_re.IGNORECASE)

    try:
        result_docs = retriever.retrieve(query, top_k=top_k, score_threshold=min_score)
        context = "\n\n".join([d["content"] for d in result_docs])
        msg_id = uuid.uuid4().hex[:8]

        if not result_docs:
            output = {
                "message_id": msg_id,
                "question_type": "misc",
                "question": query,
                "response": "No relevant documents found to provide a sufficient answer.",
                "information": [],
                "component": [],
            }
            persist_query_result(db, output, response_time_ms=0,user_id=user_id, session_id=session_id)
            return output

        sources = [
            {
                "doc_id": d["id"],
                "source": d["metadata"].get("source_file", d["metadata"].get("source", "unknown")),
                "page": d["metadata"].get("page", "unknown"),
                "element_type": d["metadata"].get("element_type", "text"),
                "confidence_score": round(sigmoid(d.get("rerank_score", 0.0)), 4),
                "rrf_score": d.get("rrf_score"),
                "content_preview": d["content"][:500] + "...",
                "rank": d["rank"],
            }
            for d in result_docs
        ]
        probe_components: List[str] = []
        probe_component_states: Dict[str, Dict[str, Any]] = {}
        probe_component_docs: List[str] = []
        if machine_name:
            probe_components, probe_component_states, probe_component_docs = (fetch_probe_components(machine_name))
            if not probe_components:
                logger.warning(
                    "probe_components: no entries found in the probe_components for:", machine_name)
        else:
            logger.warning(
                "probe_components: no machine_name was passed to this query")

        probe_components_json = json.dumps(probe_components)

        pass1_template = load_prompt_template("pass1_query_handler.md")
        pass2_template = load_prompt_template("pass2_stepbystep_handler.md")
        pass3_template = load_prompt_template("pass3_json_array.md")

        response_metadata: Dict[str, Any] = {}
        component: List[dict] = []

        try:
            # PASS 1
            q_type, body, meta1 = llm_classify_and_answer(pass1_template, llm,context=context, probe_components_json=probe_components_json, query=query)
            if meta1:
                response_metadata["pass1_classify"] = meta1

            if q_type == "stepbystep":
                # PASS 2
                body, meta2 = llm_generate_steps(pass2_template, llm,context=context, probe_components_json=probe_components_json, query=query)
                if meta2:
                    response_metadata["pass2_steps"] = meta2

                # PASS 3
                if body.strip():
                    component, meta3 = llm_extract_step_components(pass3_template, body, probe_components, probe_component_states, probe_component_docs, llm)
                    if meta3:
                        response_metadata["pass3_json"] = meta3
                else:
                    component = []

            elif q_type not in ("summary", "qna", "misc"):
                logger.warning("Unrecognized question_type %r from pass 1.", q_type)
                q_type = "misc"

        except ValueError as exc:
            logger.warning("Response parsing failed (%s).", exc)
            q_type = "misc"
            body = "Information from context is insufficient to answer this question."
            component = []

        output = {
            "message_id": msg_id,
            "question_type": q_type,
            "question": query,
            "response": body.strip(),
            "information": sources,
            "component": component,
            "_top_k": top_k,
            "_min_score": min_score,
            "response_metadata": response_metadata,
        }

        response_time_ms = int((time.time() - t_start) * 1000)
        parts_map = get_machine_parts_dict(db, machine_name) if machine_name else {}

        vector_ids_in_result = [d["id"] for d in result_docs]
        chunk_rows = (db.query(DocumentChunk).filter(DocumentChunk.vector_store_id.in_(vector_ids_in_result)).all())

        chunk_map = {c.vector_store_id: c.id for c in chunk_rows}

        machine_id = None
        if machine_name:
            m = db.query(Machine).filter(Machine.name == machine_name).first()
            if m:
                machine_id = m.id

        persist_query_result(db, output, response_time_ms,user_id=user_id, session_id=session_id, machine_id=machine_id,parts_map=parts_map, chunk_map=chunk_map,)
        output.pop("_top_k", None)
        output.pop("_min_score", None)
        print(output)
        return output

    except Exception as exc:
        write_log(db, event_type="rag_error", level="error", user_id=user_id, error_message=str(exc))
        raise
    finally:
        if close_db:
            db.close()
