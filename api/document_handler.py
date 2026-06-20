from __future__ import annotations
import base64
import hashlib
import io
import json
import logging
import os
import time
import uuid
from dataclasses import dataclass, field
from enum import Enum
from typing import Any, Dict, List, Optional, Tuple
from sqlalchemy.orm import Session
from table_schemas import (
    Document as DocumentModel,
    DocumentChunk,
    Log,Machine,
    MachinePart,
    QueryComponent,
    QuerySource,
    UserQuery
)

logger = logging.getLogger(__name__)

class ElementType(str, Enum):
    TEXT      = "text"
    TABLE     = "table"
    IMAGE     = "image"
    COMPONENT = "component"


@dataclass
class ParsedElement:
    element_type: ElementType
    raw_content:  str      
    summary:      str        = "" 
    page_number:  int        = 0
    source_file:  str        = ""
    metadata:     Dict[str, Any] = field(default_factory=dict)


def image_element_from(oe: Any, file_path: str, extract_images: bool) -> ParsedElement:

    page_num = (oe.metadata.page_number or 0) if getattr(oe, "metadata", None) else 0
    b64 = getattr(oe.metadata, "image_base64", None) if extract_images else None
    return ParsedElement(element_type=ElementType.IMAGE,raw_content=b64 or "",page_number=page_num,source_file=file_path,metadata={"mime": "image/png"})

def partition_with_strategy(partition_pdf,file_path: str,strategy: str,extract_images: bool) -> list:
    kwargs: Dict[str, Any] = {
        "filename": file_path,
        "strategy": strategy,
        "infer_table_structure": True,
        "chunking_strategy": "by_title",
        "max_characters": 10000,
        "combine_text_under_n_chars": 2000,
        "new_after_n_chars": 6000,
        "include_orig_elements": True}
    
    if extract_images:
        kwargs.update(extract_image_block_types=["Image", "Table"], extract_image_block_to_payload=True)
    return partition_pdf(**kwargs)


def parse_pdf_with_unstructured(file_path: str,extract_images: bool = True,) -> List[ParsedElement]:
    from unstructured.partition.pdf import partition_pdf

    logger.info("Partitioning %s with unstructured …", file_path)

    raw_elements = partition_with_strategy(partition_pdf, file_path,strategy="hi_res",extract_images=extract_images)
    elements: List[ParsedElement] = []
    images_recovered = 0
    tables_recovered = 0

    for el in raw_elements:
        category = getattr(el, "category", "").lower()
        page_num = (el.metadata.page_number or 0) if el.metadata else 0
        orig_elements = list(getattr(el.metadata, "orig_elements", None) or [])

        if category in ("table", "tablechunk"):
            html = getattr(el.metadata, "text_as_html", None) or str(el)
            elements.append(ParsedElement(element_type=ElementType.TABLE,raw_content=html,page_number=page_num,source_file=file_path,metadata={"html": html}))

            for oe in orig_elements:
                if getattr(oe, "category", "").lower() == "image":
                    elements.append(image_element_from(oe, file_path, extract_images))
                    images_recovered += 1
            continue

        if category == "image":
            elements.append(image_element_from(el, file_path, extract_images))
            continue

        for oe in orig_elements:
            oe_category = getattr(oe, "category", "").lower()
            if oe_category == "image":
                elements.append(image_element_from(oe, file_path, extract_images))
                images_recovered += 1
            elif oe_category in ("table", "tablechunk"):
                html = getattr(oe.metadata, "text_as_html", None) or str(oe)
                elements.append(ParsedElement(
                    element_type=ElementType.TABLE,
                    raw_content=html,
                    page_number=(oe.metadata.page_number or page_num) if getattr(oe, "metadata", None) else page_num,
                    source_file=file_path,
                    metadata={"html": html}))
                tables_recovered += 1

            text = str(el).strip()
            if text:
                elements.append(ParsedElement(
                    element_type=ElementType.TEXT,
                    raw_content=text,
                    page_number=page_num,
                    source_file=file_path,
                ))

    logger.info("Extracted %d elements from %s (recovered from orig_elements: %d image(s), %d table(s))", len(elements), file_path, images_recovered, tables_recovered)
    return elements

def build_summariser(llm: Any,use_vision: bool = True,min_text_chars_for_llm: int = 30,min_image_b64_chars_for_vision: int = 2000):

    from langchain_core.messages import HumanMessage

    min_text_chars_for_llm = int(os.getenv("SUMMARY_MIN_TEXT_CHARS", min_text_chars_for_llm))
    min_image_b64_chars_for_vision = int(os.getenv("SUMMARY_MIN_IMAGE_B64_CHARS", min_image_b64_chars_for_vision))

    image_summary_cache: Dict[str, str] = {}

    def summarise(el: ParsedElement) -> str:
        try:
            if el.element_type == ElementType.TEXT:
                stripped = el.raw_content.strip()
                if len(stripped) < min_text_chars_for_llm:
                    logger.info(
                        "  -- skipping LLM for TEXT element, page %s "
                        "(%d chars < %d threshold): %r",el.page_number, len(stripped), min_text_chars_for_llm, stripped)
                    return stripped
                prompt = (
                    f"""
                    You are an assistant tasked with summarizing text.
                    Give a concise summary of the text.
                    Preserve numbers and information, keep the summary very detailed.

                    Respond only with the summary, no additionnal comment.
                    Do not start your message by saying "Here is a summary" or "This text" or anything like that.
                    Just give the summary as it is.

                    Text chunk: {el.raw_content}
                    """
                )
                msg = HumanMessage(content=prompt)
                logger.info("  -> LLM call: TEXT element, page %s, %d chars of source text",el.page_number, len(el.raw_content))
                t0 = time.time()
                response = llm.invoke([msg])
                logger.info("  <- LLM responded in %.1fs", time.time() - t0)
                return response.content.strip()

            # table handler
            elif el.element_type == ElementType.TABLE:
                prompt = (
                    f"""
                    You are an assistant tasked with summarizing tables.
                    Give a concise summary of the table.
                    Preserve numbers and information, keep the summary very detailed.

                    Respond only with the summary, no additionnal comment.
                    Do not start your message by saying "Here is a summary" or "This table" or anything like that.
                    Just give the summary as it is.

                    Table chunk: {el.raw_content}

                    """
                )
                msg = HumanMessage(content=prompt)
                logger.info("  -> LLM call: TABLE element, page %s, %d chars of HTML",el.page_number, len(el.raw_content))
                t0 = time.time()
                response = llm.invoke([msg])
                logger.info("  <- LLM responded in %.1fs", time.time() - t0)
                return response.content.strip()

            # image handler
            elif el.element_type == ElementType.IMAGE:
                page_info = f"page {el.page_number}" if el.page_number else "unknown page"
                src_name  = os.path.basename(el.source_file) if el.source_file else "document"

                img_hash = (hashlib.md5(el.raw_content.encode()).hexdigest() if el.raw_content else None)

                if img_hash and img_hash in image_summary_cache:
                    logger.info(
                        "duplicate image (hash %s…) on %s of %s"
                        "skip LLM call",img_hash[:10], page_info, src_name)
                    return image_summary_cache[img_hash]

                if el.raw_content and len(el.raw_content) < min_image_b64_chars_for_vision:
                    logger.info("skipping vision LLM for IMAGE element, page %s "
                        "(%d base64 chars < %d threshold)",el.page_number, len(el.raw_content), min_image_b64_chars_for_vision)
                    
                    result = (f"[Small decorative image on {page_info} of {src_name} "
                        "(below size threshold for vision summarisation).]")
                    if img_hash:
                        image_summary_cache[img_hash] = result
                    return result

                if use_vision:
                    prompt_parts = [
                        {
                            "type": "image_url",
                            "image_url": {
                                "url": (
                                    f"data:{el.metadata.get('mime', 'image/png')}"
                                    f";base64,{el.raw_content}"
                                )
                            },
                        },
                        {
                            "type": "text",
                            "text": (
                                """Describe the image in detail. For context,
                                the image is part of a user manual explaining the lathe machine parts and
                                architecture. Be very specific about graphs and machine schematics. 
                                Do not start with "This image" or add additional comments"""
                            ),
                        },
                    ]
                    msg = HumanMessage(content=prompt_parts)
                    logger.info("  -> LLM call: IMAGE element, page %s, ~%d base64 chars ",el.page_number, len(el.raw_content))
                    t0 = time.time()
                    response = llm.invoke([msg])
                    logger.info("  <- LLM responded in %.1fs", time.time() - t0)
                    result = response.content.strip()
                else:
                    result = (f"(Image extracted from {src_name}, {page_info}. "
                        "No vision model available)")

                if img_hash:
                    image_summary_cache[img_hash] = result
                return result

        except Exception as exc:
            logger.error("LLM summarisation failed for %s element on page %d of %s: %s",el.element_type, el.page_number, el.source_file, exc, exc_info=True)
            if el.element_type == ElementType.TEXT:
                return el.raw_content[:1000]
            elif el.element_type == ElementType.TABLE:
                return el.raw_content[:1000]
            page_info = f"page {el.page_number}" if el.page_number else "unknown page"
            return f"[Image on {page_info} summarisation failed: {exc}]"
    return summarise

def merge_text_elements(elements: List[ParsedElement],target_chars: int = 1500,overlap_chars: int = 200) -> List[ParsedElement]:

    if overlap_chars >= target_chars:
        overlap_chars = max(0, target_chars // 4)

    merged: List[ParsedElement] = []
    buffer_parts: List[str] = []
    buffer_pages: List[int] = []
    buffer_source = ""
    buffer_len = 0
    has_new_content = False

    def flush() -> None:
        nonlocal buffer_parts, buffer_pages, buffer_len, has_new_content
        if not has_new_content or not buffer_parts:
            buffer_parts, buffer_pages, buffer_len, has_new_content = [], [], 0, False
            return

        combined = "\n\n".join(buffer_parts)
        merged.append(ParsedElement(
            element_type=ElementType.TEXT,
            raw_content=combined,
            page_number=buffer_pages[0],
            source_file=buffer_source,
            metadata={"merged_pages": sorted(set(buffer_pages))}
            )
        )

        if overlap_chars > 0 and len(combined) > overlap_chars:
            tail = combined[-overlap_chars:]
            buffer_parts, buffer_pages, buffer_len = [tail], [buffer_pages[-1]], len(tail)
        else:
            buffer_parts, buffer_pages, buffer_len = [], [], 0
        has_new_content = False

    for el in elements:
        if el.element_type == ElementType.TEXT:
            text = el.raw_content.strip()
            if not text:
                continue
            buffer_parts.append(text)
            buffer_pages.append(el.page_number)
            buffer_source = el.source_file
            buffer_len += len(text)
            has_new_content = True
            if buffer_len >= target_chars:
                flush()
        else:
            flush()
            merged.append(el)

    flush()
    return merged


def process_pdf(file_path: str,llm: Any,chunk_size: int = 1200,chunk_overlap: int = 150,use_vision: bool = True,merge_target_chars: int = 1500,merge_overlap_chars: int = 200) -> List[ParsedElement]:

    chunk_size = int(os.getenv("CHUNK_SIZE", chunk_size))
    chunk_overlap = int(os.getenv("CHUNK_OVERLAP", chunk_overlap))
    merge_target_chars = int(os.getenv("MERGE_TARGET_CHARS", merge_target_chars))
    merge_overlap_chars = int(os.getenv("MERGE_OVERLAP_CHARS", merge_overlap_chars))

    if not os.path.isfile(file_path):
        raise FileNotFoundError(f"PDF not found: {file_path}")

    raw_elements = parse_pdf_with_unstructured(file_path, extract_images=use_vision)
    if not raw_elements:
        logger.warning("No elements extracted from %s", file_path)
        return []

    elements = merge_text_elements(raw_elements,target_chars=merge_target_chars,overlap_chars=merge_overlap_chars)
    logger.info("Merged %d raw elements into %d elements for %s ""(merge_target_chars=%d, merge_overlap_chars=%d)",len(raw_elements), len(elements), os.path.basename(file_path),merge_target_chars, merge_overlap_chars)

    summarise = build_summariser(llm, use_vision=use_vision)
    chunked: List[ParsedElement] = []

    total_elements = len(elements)
    type_counts_in = {t.value: 0 for t in ElementType}
    for el in elements:
        type_counts_in[el.element_type.value] += 1
    logger.info("Starting summarisation of %d elements from %s ""(text=%d, table=%d, image=%d, use_vision=%s)",total_elements, os.path.basename(file_path),type_counts_in["text"], type_counts_in["table"], type_counts_in["image"],use_vision)

    for i, el in enumerate(elements, start=1):
        logger.info("[%d/%d] Summarising %s element (page %s) from %s",i, total_elements, el.element_type.value, el.page_number,os.path.basename(file_path))
        el_t0 = time.time()
        el.summary = summarise(el)
        logger.info("[%d/%d] Finished %s element in %.1fs (summary length=%d)",i, total_elements, el.element_type.value, time.time() - el_t0,len(el.summary or ""))
        if not el.summary:
            logger.debug("Empty summary for %s element.", el.element_type)
            continue

        if el.element_type in (ElementType.IMAGE, ElementType.TABLE):
            chunked.append(el)
            continue

        if len(el.summary) <= chunk_size:
            chunked.append(el)
        else:
            try:
                from langchain_text_splitters import RecursiveCharacterTextSplitter
                splitter = RecursiveCharacterTextSplitter(chunk_size=chunk_size,chunk_overlap=chunk_overlap,length_function=len,separators=["\n\n", "\n", " ", ""])
                sub_texts = splitter.split_text(el.summary)
                for sub in sub_texts:
                    chunked.append(ParsedElement(element_type=ElementType.TEXT,raw_content=el.raw_content,summary=sub,page_number=el.page_number,source_file=el.source_file,metadata=el.metadata))
            except Exception as exc:
                logger.error("Chunking failed for element on page %d: %s", el.page_number, exc)
                chunked.append(el)

    logger.info("process_pdf: %d raw elements -> %d merged elements -> %d embeddable chunks for %s",len(raw_elements), len(elements), len(chunked), file_path)
    return chunked


def process_pdf_folder(folder: str,llm: Any,chunk_size: int = 1200,chunk_overlap: int = 150,use_vision: bool = True,) -> Dict[str, List[ParsedElement]]:
    
    if not os.path.isdir(folder):
        raise FileNotFoundError(f"PDF folder not found: {folder}")

    pdf_files = [
        os.path.join(root, fname)
        for root, _, files in os.walk(folder)
        for fname in files
        if fname.lower().endswith(".pdf")]

    if not pdf_files:
        logger.warning("No PDF files found in %s", folder)
        return {}

    total_files = len(pdf_files)
    logger.info("process_pdf_folder: found %d PDF file(s) in %s", total_files, folder)

    results: Dict[str, List[ParsedElement]] = {}
    for i, path in enumerate(pdf_files, start=1):
        logger.info("File %d/%d: %s", i, total_files, os.path.basename(path))
        file_t0 = time.time()
        try:
            results[path] = process_pdf(path, llm, chunk_size, chunk_overlap, use_vision=use_vision)
            logger.info("File %d/%d: %s done in %.1fs (%d chunks)",i, total_files, os.path.basename(path), time.time() - file_t0, len(results[path]))
        except Exception as exc:
            logger.error("process_pdf failed for %s: %s", path, exc, exc_info=True)

    return results

def register_document(db: Session,file_path: str,title: str,machine_id: Optional[str] = None,uploaded_by: Optional[str] = None,total_pages: Optional[int] = None) -> DocumentModel:
    try:
        doc = db.query(DocumentModel).filter(DocumentModel.file_path == file_path).first()
        if doc:
            return doc
        size = os.path.getsize(file_path) if os.path.exists(file_path) else None
        doc = DocumentModel(machine_id=machine_id,uploaded_by=uploaded_by,title=title,file_path=file_path,file_size_bytes=size,total_pages=total_pages)
        db.add(doc)
        db.commit()
        db.refresh(doc)
        return doc
    except Exception as exc:
        db.rollback()
        logger.error("register_document failed for %s: %s", file_path, exc, exc_info=True)
        raise


def register_chunks(db: Session,document_id: str,elements: List[ParsedElement],vector_ids: List[str],model_name: str = "all-MiniLM-L6-v2") -> List[DocumentChunk]:

    chunk_rows: List[DocumentChunk] = []
    try:
        for idx, (el, vid) in enumerate(zip(elements, vector_ids)):
            embeddable_text = el.summary or el.raw_content
            row = DocumentChunk(document_id=document_id,chunk_index=idx,content=embeddable_text,char_count=len(embeddable_text),page_number=el.page_number,embedding_model=model_name,vector_store_id=vid)
            db.add(row)
            chunk_rows.append(row)
        db.commit()
        return chunk_rows
    except Exception as exc:
        db.rollback()
        logger.error("register_chunks failed for document %s: %s", document_id, exc, exc_info=True)
        raise

def parse_probe_data(probe_data_json: str | Dict[str, Any],machine_name: Optional[str] = None,max_summary_chars: int = 2000) -> List[ParsedElement]:

    if isinstance(probe_data_json, str):
        try:
            data = json.loads(probe_data_json)
        except json.JSONDecodeError as exc:
            raise ValueError(f"probe_data_json is not valid JSON: {exc}") from exc
    else:
        data = probe_data_json

    resolved_machine = machine_name or data.get("machine") or "unknown"
    raw_components = data.get("components", [])

    elements: List[ParsedElement] = []
    for idx, comp in enumerate(raw_components):
        if isinstance(comp, str):
            comp = {"name": comp}
        if not isinstance(comp, dict):
            continue

        name = comp.get("name", "")
        if not name:
            continue

        description     = comp.get("description", "") or ""
        default_state    = comp.get("default_state") or {}
        possible_states  = comp.get("possible_states") or {}

        summary_lines = [f"Component: {name}"]
        if description:
            summary_lines.append(f"Description: {description}")
        if possible_states:
            summary_lines.append(f"Possible states: {json.dumps(possible_states)}")
        if default_state:
            summary_lines.append(f"Default state: {json.dumps(default_state)}")
        summary = "\n".join(summary_lines)

        if len(summary) > max_summary_chars:
            logger.debug("parse_probe_data: truncating embedding text for component "
                "'%s' (%d chars > %d cap)",name, len(summary), max_summary_chars)
            summary = summary[:max_summary_chars].rstrip() + " …"

        elements.append(ParsedElement(
            element_type=ElementType.COMPONENT,
            raw_content=json.dumps(comp),
            summary=summary,
            page_number=idx,
            source_file=resolved_machine,
            metadata={
                "name": name,
                "machine": resolved_machine,
                "description": description,
                "default_state": json.dumps(default_state),
                "possible_states": json.dumps(possible_states),
            },
        ))

    logger.info("parse_probe_data: parsed %d component(s) for machine '%s'",len(elements), resolved_machine)
    return elements


def machine_part_code_prefix(machine_name: str, machine_id: str) -> str:

    slug = "".join(ch for ch in machine_name.upper() if ch.isalnum())[:6] or "MACH"
    suffix = hashlib.sha1(str(machine_id).encode("utf-8")).hexdigest()[:4].upper()
    return f"{slug}-{suffix}"


def upsert_machine_from_probe(db: Session,machine_name: str,components: List[Any]) -> Machine:

    try:
        machine = db.query(Machine).filter(Machine.name == machine_name).first()
        if not machine:
            machine = Machine(name=machine_name)
            db.add(machine)
            db.flush()

        existing_by_name = {p.name: p for p in machine.parts}
        part_code_prefix = machine_part_code_prefix(machine_name, machine.id)

        for idx, comp in enumerate(components):
            if isinstance(comp, str):
                comp_name = comp
                default_state: dict = {}
                possible_states: dict = {}
            elif isinstance(comp, dict):
                comp_name = comp.get("name", "")
                default_state = comp.get("default_state") or {}
                possible_states = comp.get("possible_states") or {}
            else:
                comp_name = getattr(comp, "name", "")
                default_state = getattr(comp, "default_state", {}) or {}
                possible_states = getattr(comp, "possible_states", {}) or {}

            if not comp_name:
                continue

            existing_part = existing_by_name.get(comp_name)
            if existing_part is None:
                db.add(MachinePart(
                    machine_id=machine.id,
                    name=comp_name,
                    part_code=f"{part_code_prefix}-{idx:03d}",
                    position_index=idx,
                    default_state=default_state,
                    possible_states=possible_states,
                ))
            else:
                existing_part.default_state = default_state
                existing_part.possible_states = possible_states
                existing_part.position_index = idx

        db.commit()
        db.refresh(machine)
        return machine
    except Exception as exc:
        db.rollback()
        logger.error("upsert_machine_from_probe failed for %s: %s", machine_name, exc, exc_info=True)
        raise


def get_machine_parts_dict(db: Session, machine_name: str) -> Dict[str, str]:
    try:
        machine = db.query(Machine).filter(Machine.name == machine_name).first()
        if not machine:
            return {}
        return {p.name: p.id for p in machine.parts}
    except Exception as exc:
        logger.error("get_machine_parts_dict failed for %s: %s", machine_name, exc, exc_info=True)
        return {}


def persist_query_result(db: Session,result: Dict[str, Any],response_time_ms: int,user_id: Optional[str] = None,session_id: Optional[str] = None,machine_id: Optional[str] = None,parts_map: Optional[Dict[str, str]] = None,chunk_map: Optional[Dict[str, str]] = None) -> UserQuery:
    try:
        query_row = UserQuery(
            id=result.get("message_id", str(uuid.uuid4())),
            user_id=user_id,
            machine_id=machine_id,
            session_id=session_id,
            question=result["question"],
            question_type=result.get("question_type", "misc"),
            response=result.get("response", ""),
            top_k=result.get("_top_k", 3),
            min_score=result.get("_min_score", 0.1),
            response_time_ms=response_time_ms)
        db.add(query_row)
        db.flush()

        for src in result.get("information", []):
            try:
                vid = src.get("doc_id", "")
                chunk_id = (chunk_map or {}).get(vid)
                db.add(QuerySource(
                    query_id=query_row.id,
                    chunk_id=chunk_id,
                    similarity_score=src.get("confidence_score", 0.0),
                    rank=src.get("rank", 0),
                    page_number=src.get("page") or None,
                    source_path=src.get("source"),
                    content_preview=src.get("content_preview"),
                ))
            except Exception as exc:
                logger.warning("Skipping wrong source entry: %s - %s", src, exc)

        for comp in result.get("component", []):
            try:
                step = comp.get("step", "")
                for part_name in (comp.get("index") or []):
                    part_id = (parts_map or {}).get(part_name)
                    db.add(QueryComponent(query_id=query_row.id,part_id=part_id,step_number=str(step),part_name=part_name))
            except Exception as exc:
                logger.warning("Skipping wrong component entry: %s - %s", comp, exc)

        db.add(Log(
            user_id=user_id,
            query_id=query_row.id,
            event_type="rag_query",
            level="info",
            payload=json.dumps({
                "question_type": result.get("question_type"),
                "sources_count": len(result.get("information", [])),
                "components_count": len(result.get("component", [])),
            }),
            duration_ms=response_time_ms,
        ))

        db.commit()
        db.refresh(query_row)
        return query_row

    except Exception as exc:
        db.rollback()
        logger.error("persist_query_result failed: %s", exc, exc_info=True)
        raise


def get_query_history(db: Session,user_id: str,limit: int = 50) -> List[Dict[str, Any]]:
    try:
        rows = (db.query(UserQuery).filter(UserQuery.user_id == user_id).order_by(UserQuery.created_at.desc()).limit(limit).all())
        return [
            {
                "message_id": r.id,
                "question": r.question,
                "question_type": r.question_type,
                "response": r.response,
                "created_at": r.created_at.isoformat(),
                "response_time_ms": r.response_time_ms,
            }
            for r in rows
        ]
    except Exception as exc:
        logger.error("get_query_history failed for user %s: %s", user_id, exc, exc_info=True)
        return []
