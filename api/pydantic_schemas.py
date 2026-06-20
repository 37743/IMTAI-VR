from __future__ import annotations
from typing import Any, Dict, List, Optional
from pydantic import BaseModel


class ProbeComponent(BaseModel):
    name: str
    default_state: Dict[str, Any] = {}
    possible_states: Dict[str, Any] = {}


class ProbeRequest(BaseModel):
    machine: str
    components: List[ProbeComponent]


class RegisterRequest(BaseModel):
    username: str
    email: str
    password: str
    role: str = "user"


class LoginRequest(BaseModel):
    username: str
    password: str


class RatingRequest(BaseModel):
    query_id: str
    rating: int
    feedback: str = ""


class EvalRequest(BaseModel):
    query_id: Optional[str] = None

    n_correct_steps: Optional[int] = None
    n_total_steps: Optional[int] = None

    n_safe_actions: Optional[int] = None
    n_total_actions: Optional[int] = None

    n_completed_tasks: Optional[int] = None
    n_assigned_tasks: Optional[int] = None

    t_start: Optional[float] = None
    t_end: Optional[float] = None

    n_errors_omission: Optional[int] = None
    n_errors_sequence: Optional[int] = None
    n_errors_unsafe: Optional[int] = None
    n_total_errors: Optional[int] = None

    score_t: Optional[float] = None
    score_t_minus_1: Optional[float] = None

    avg_fps: Optional[float] = None
    wer: Optional[float] = None

    tlx_score: Optional[float] = None
    sus_score: Optional[float] = None
    ipq_score: Optional[float] = None
    ssq_score: Optional[float] = None
