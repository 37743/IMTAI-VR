from __future__ import annotations
import uuid
from datetime import datetime
from typing import List, Optional
from sqlalchemy import (
    BigInteger,
    Boolean,
    DateTime,
    Float,
    ForeignKey,
    Integer,
    JSON,
    String,
    Text,
    func,
)
from sqlalchemy.orm import Mapped, DeclarativeBase, mapped_column, relationship
from database import Base


class User(Base):
    __tablename__ = "users"

    id:           Mapped[str]            = mapped_column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    username:     Mapped[str]            = mapped_column(String(64), unique=True, nullable=False)
    email:        Mapped[str]            = mapped_column(String(255), unique=True, nullable=False)
    password_hash: Mapped[str]           = mapped_column(String(255), nullable=False)
    salt:         Mapped[str]            = mapped_column(String(64), nullable=False)
    role:         Mapped[str]            = mapped_column(String(32), default="user")
    is_active:    Mapped[bool]           = mapped_column(Boolean, default=True)
    last_login:   Mapped[Optional[datetime]] = mapped_column(DateTime, nullable=True)
    created_at:   Mapped[datetime]       = mapped_column(DateTime, default=func.now())
    updated_at:   Mapped[datetime]       = mapped_column(DateTime, default=func.now(), onupdate=func.now())

    sessions:     Mapped[List["UserSession"]]  = relationship(back_populates="user", cascade="all, delete-orphan")
    queries:      Mapped[List["UserQuery"]]    = relationship(back_populates="user")
    documents:    Mapped[List["Document"]]     = relationship(back_populates="uploaded_by_user")
    logs:         Mapped[List["Log"]]          = relationship(back_populates="user")

    def __repr__(self) -> str:
        return f"<User {self.username}>"


class UserSession(Base):
    __tablename__ = "sessions"

    id:         Mapped[str]      = mapped_column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    user_id:    Mapped[str]      = mapped_column(ForeignKey("users.id", ondelete="CASCADE"), nullable=False)
    token_hash: Mapped[str]      = mapped_column(String(255), nullable=False, unique=True)
    ip_address: Mapped[Optional[str]] = mapped_column(String(45), nullable=True)
    user_agent: Mapped[Optional[str]] = mapped_column(String(512), nullable=True)
    is_active:  Mapped[bool]     = mapped_column(Boolean, default=True)
    expires_at: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    created_at: Mapped[datetime] = mapped_column(DateTime, default=func.now())

    user:    Mapped["User"]            = relationship(back_populates="sessions")
    queries: Mapped[List["UserQuery"]] = relationship(back_populates="session")


class Machine(Base):
    __tablename__ = "machines"

    id:           Mapped[str]            = mapped_column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    name:         Mapped[str]            = mapped_column(String(255), nullable=False)
    model_number: Mapped[Optional[str]]  = mapped_column(String(128), nullable=True)
    manufacturer: Mapped[Optional[str]]  = mapped_column(String(255), nullable=True)
    category:     Mapped[Optional[str]]  = mapped_column(String(128), nullable=True)
    description:  Mapped[Optional[str]]  = mapped_column(Text, nullable=True)
    status:       Mapped[str]            = mapped_column(String(32), default="active")
    created_at:   Mapped[datetime]       = mapped_column(DateTime, default=func.now())

    parts:     Mapped[List["MachinePart"]] = relationship(back_populates="machine", cascade="all, delete-orphan")
    documents: Mapped[List["Document"]]    = relationship(back_populates="machine")
    queries:   Mapped[List["UserQuery"]]   = relationship(back_populates="machine")

    def __repr__(self) -> str:
        return f"<Machine {self.name}>"


class MachinePart(Base):
    __tablename__ = "machine_parts"

    id:              Mapped[str]           = mapped_column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    machine_id:      Mapped[str]           = mapped_column(ForeignKey("machines.id", ondelete="CASCADE"), nullable=False)
    name:            Mapped[str]           = mapped_column(String(255), nullable=False)
    part_code:       Mapped[Optional[str]] = mapped_column(String(64), nullable=True, unique=True)
    category:        Mapped[Optional[str]] = mapped_column(String(128), nullable=True)
    description:     Mapped[Optional[str]] = mapped_column(Text, nullable=True)
    position_index:  Mapped[int]           = mapped_column(Integer, default=0)
    default_state:   Mapped[Optional[dict]] = mapped_column(JSON, nullable=True)
    possible_states: Mapped[Optional[dict]] = mapped_column(JSON, nullable=True)
    created_at:      Mapped[datetime]      = mapped_column(DateTime, default=func.now())

    machine:            Mapped["Machine"]              = relationship(back_populates="parts")
    query_components:   Mapped[List["QueryComponent"]] = relationship(back_populates="part")


class Document(Base):
    __tablename__ = "documents"

    id:              Mapped[str]            = mapped_column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    machine_id:      Mapped[Optional[str]]  = mapped_column(ForeignKey("machines.id"), nullable=True)
    uploaded_by:     Mapped[Optional[str]]  = mapped_column(ForeignKey("users.id"), nullable=True)
    title:           Mapped[str]            = mapped_column(String(512), nullable=False)
    file_path:       Mapped[str]            = mapped_column(String(1024), nullable=False)
    file_type:       Mapped[str]            = mapped_column(String(16), default="pdf")
    language:        Mapped[str]            = mapped_column(String(16), default="en")
    total_pages:     Mapped[Optional[int]]  = mapped_column(Integer, nullable=True)
    file_size_bytes: Mapped[Optional[int]]  = mapped_column(BigInteger, nullable=True)
    status:          Mapped[str]            = mapped_column(String(32), default="active")
    created_at:      Mapped[datetime]       = mapped_column(DateTime, default=func.now())
    updated_at:      Mapped[datetime]       = mapped_column(DateTime, default=func.now(), onupdate=func.now())

    machine:          Mapped[Optional["Machine"]] = relationship(back_populates="documents")
    uploaded_by_user: Mapped[Optional["User"]]    = relationship(back_populates="documents")
    chunks:           Mapped[List["DocumentChunk"]] = relationship(back_populates="document", cascade="all, delete-orphan")


class DocumentChunk(Base):
    __tablename__ = "document_chunks"

    id:              Mapped[str]           = mapped_column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    document_id:     Mapped[str]           = mapped_column(ForeignKey("documents.id", ondelete="CASCADE"), nullable=False)
    chunk_index:     Mapped[int]           = mapped_column(Integer, nullable=False)
    content:         Mapped[str]           = mapped_column(Text, nullable=False)
    char_count:      Mapped[int]           = mapped_column(Integer, nullable=False)
    page_number:     Mapped[Optional[int]] = mapped_column(Integer, nullable=True)
    embedding_model: Mapped[str]           = mapped_column(String(128), default="all-MiniLM-L6-v2")
    vector_store_id: Mapped[Optional[str]] = mapped_column(String(64), nullable=True)
    created_at:      Mapped[datetime]      = mapped_column(DateTime, default=func.now())

    document:      Mapped["Document"]          = relationship(back_populates="chunks")
    query_sources: Mapped[List["QuerySource"]] = relationship(back_populates="chunk")


class UserQuery(Base):
    __tablename__ = "user_queries"

    id:               Mapped[str]            = mapped_column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    user_id:          Mapped[Optional[str]]  = mapped_column(ForeignKey("users.id"), nullable=True)
    machine_id:       Mapped[Optional[str]]  = mapped_column(ForeignKey("machines.id"), nullable=True)
    session_id:       Mapped[Optional[str]]  = mapped_column(ForeignKey("sessions.id"), nullable=True)
    question:         Mapped[str]            = mapped_column(Text, nullable=False)
    question_type:    Mapped[str]            = mapped_column(String(32), default="misc")
    response:         Mapped[Optional[str]]  = mapped_column(Text, nullable=True)
    top_k:            Mapped[int]            = mapped_column(Integer, default=3)
    min_score:        Mapped[float]          = mapped_column(Float, default=0.1)
    response_time_ms: Mapped[Optional[int]]  = mapped_column(Integer, nullable=True)
    created_at:       Mapped[datetime]       = mapped_column(DateTime, default=func.now())

    user:       Mapped[Optional["User"]]         = relationship(back_populates="queries")
    machine:    Mapped[Optional["Machine"]]       = relationship(back_populates="queries")
    session:    Mapped[Optional["UserSession"]]   = relationship(back_populates="queries")
    sources:    Mapped[List["QuerySource"]]       = relationship(back_populates="query", cascade="all, delete-orphan")
    components: Mapped[List["QueryComponent"]]    = relationship(back_populates="query", cascade="all, delete-orphan")
    log:        Mapped[Optional["Log"]]           = relationship(back_populates="query", uselist=False)
    evaluation: Mapped[Optional["EvalMetric"]]    = relationship(back_populates="query", uselist=False)


class QuerySource(Base):
    __tablename__ = "query_sources"

    id:               Mapped[str]           = mapped_column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    query_id:         Mapped[str]           = mapped_column(ForeignKey("user_queries.id", ondelete="CASCADE"), nullable=False)
    chunk_id:         Mapped[Optional[str]] = mapped_column(ForeignKey("document_chunks.id"), nullable=True)
    similarity_score: Mapped[float]         = mapped_column(Float, nullable=False)
    rank:             Mapped[int]           = mapped_column(Integer, nullable=False)
    page_number:      Mapped[Optional[int]] = mapped_column(Integer, nullable=True)
    source_path:      Mapped[Optional[str]] = mapped_column(String(1024), nullable=True)
    content_preview:  Mapped[Optional[str]] = mapped_column(Text, nullable=True)

    query: Mapped["UserQuery"]      = relationship(back_populates="sources")
    chunk: Mapped[Optional["DocumentChunk"]] = relationship(back_populates="query_sources")


class QueryComponent(Base):
    __tablename__ = "query_components"

    id:          Mapped[str]           = mapped_column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    query_id:    Mapped[str]           = mapped_column(ForeignKey("user_queries.id", ondelete="CASCADE"), nullable=False)
    part_id:     Mapped[Optional[str]] = mapped_column(ForeignKey("machine_parts.id"), nullable=True)
    step_number: Mapped[Optional[str]] = mapped_column(String(16), nullable=True)
    part_name:   Mapped[Optional[str]] = mapped_column(String(255), nullable=True)

    query: Mapped["UserQuery"]        = relationship(back_populates="components")
    part:  Mapped[Optional["MachinePart"]] = relationship(back_populates="query_components")


class Log(Base):
    __tablename__ = "logs"

    id:            Mapped[str]            = mapped_column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    user_id:       Mapped[Optional[str]]  = mapped_column(ForeignKey("users.id"), nullable=True)
    query_id:      Mapped[Optional[str]]  = mapped_column(ForeignKey("user_queries.id"), nullable=True)
    event_type:    Mapped[str]            = mapped_column(String(64), nullable=False)
    level:         Mapped[str]            = mapped_column(String(16), default="info")
    payload:       Mapped[Optional[str]]  = mapped_column(Text, nullable=True)
    ip_address:    Mapped[Optional[str]]  = mapped_column(String(45), nullable=True)
    duration_ms:   Mapped[Optional[int]]  = mapped_column(Integer, nullable=True)
    error_message: Mapped[Optional[str]]  = mapped_column(Text, nullable=True)
    created_at:    Mapped[datetime]       = mapped_column(DateTime, default=func.now())

    user:  Mapped[Optional["User"]]      = relationship(back_populates="logs")
    query: Mapped[Optional["UserQuery"]] = relationship(back_populates="log")



class EvalMetric(Base):

    __tablename__ = "eval_metrics"

    id:            Mapped[str]           = mapped_column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    query_id:      Mapped[Optional[str]] = mapped_column(ForeignKey("user_queries.id", ondelete="SET NULL"), nullable=True, unique=True)

    n_correct_steps:      Mapped[Optional[int]]   = mapped_column(Integer, nullable=True)
    n_total_steps:        Mapped[Optional[int]]   = mapped_column(Integer, nullable=True)

    n_safe_actions:       Mapped[Optional[int]]   = mapped_column(Integer, nullable=True)
    n_total_actions:      Mapped[Optional[int]]   = mapped_column(Integer, nullable=True)
    n_completed_tasks:    Mapped[Optional[int]]   = mapped_column(Integer, nullable=True)
    n_assigned_tasks:     Mapped[Optional[int]]   = mapped_column(Integer, nullable=True)
    t_end:                Mapped[Optional[float]] = mapped_column(Float, nullable=True)
    t_start:              Mapped[Optional[float]] = mapped_column(Float, nullable=True)
    n_errors_omission:    Mapped[Optional[int]]   = mapped_column(Integer, nullable=True)
    n_errors_sequence:    Mapped[Optional[int]]   = mapped_column(Integer, nullable=True)
    n_errors_unsafe:      Mapped[Optional[int]]   = mapped_column(Integer, nullable=True)
    n_total_errors:       Mapped[Optional[int]]   = mapped_column(Integer, nullable=True)
    score_t:              Mapped[Optional[float]] = mapped_column(Float, nullable=True)
    score_t_minus_1:      Mapped[Optional[float]] = mapped_column(Float, nullable=True)
    avg_fps:  Mapped[Optional[float]] = mapped_column(Float, nullable=True)
    wer:      Mapped[Optional[float]] = mapped_column(Float, nullable=True)
    tlx_score:  Mapped[Optional[float]] = mapped_column(Float, nullable=True)
    sus_score:  Mapped[Optional[float]] = mapped_column(Float, nullable=True)
    ipq_score:  Mapped[Optional[float]] = mapped_column(Float, nullable=True)
    ssq_score:  Mapped[Optional[float]] = mapped_column(Float, nullable=True)

    created_at: Mapped[datetime] = mapped_column(DateTime, default=func.now())

    query: Mapped[Optional["UserQuery"]] = relationship(back_populates="evaluation")