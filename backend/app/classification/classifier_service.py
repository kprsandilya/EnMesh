"""
Context-aware Auto Layout classification.

Layers (for extension):
  - Category embeddings from text (init_category_embeddings)
  - Per-mesh name embeddings (encode via runtime)
  - Global context + quotas (_assign_with_context) — replace with spatial heuristics later
"""

from __future__ import annotations

import logging
import re
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from .categories import CATEGORY_DESCRIPTIONS, CATEGORY_ORDER, LayoutCategory
from .embedding_runtime import SentenceEmbeddingRuntime

logger = logging.getLogger(__name__)

_SUFFIX_RE = re.compile(r"_[a-f0-9]{8}$", re.IGNORECASE)


@dataclass(frozen=True)
class ClassifiedMesh:
    mesh_name: str
    absolute_path: str
    category: LayoutCategory


class AutoLayoutClassifierService:
    def __init__(self, embedding_runtime: SentenceEmbeddingRuntime) -> None:
        self._emb = embedding_runtime
        self._category_matrix: np.ndarray | None = None

    @property
    def category_embeddings_ready(self) -> bool:
        return self._category_matrix is not None

    def init_category_embeddings(self) -> None:
        texts = [CATEGORY_DESCRIPTIONS[c] for c in CATEGORY_ORDER]
        self._category_matrix = self._emb.encode(texts)
        logger.info("Cached %d category description embeddings", len(texts))

    def text_for_embedding(self, mesh_name: str) -> str:
        base = mesh_name.strip()
        base = _SUFFIX_RE.sub("", base)
        return base.replace("_", " ").strip() or mesh_name

    def resolve_server_path(self, mesh_name: str, outputs_dir: Path) -> Path | None:
        tail = mesh_name.replace("\\", "/").split("/")[-1]
        if not tail or tail.startswith(".") or "/" in tail or "\\" in mesh_name:
            return None
        base = Path(tail).stem if "." in tail else tail
        if not base or ".." in base:
            return None
        root = outputs_dir.resolve()
        for ext in ("obj", "glb"):
            p = (root / f"{base}.{ext}").resolve()
            try:
                p.relative_to(root)
            except ValueError:
                return None
            if p.is_file():
                return p
        return None

    def classify_meshes(
        self,
        mesh_names: list[str],
        outputs_dir: Path,
    ) -> list[ClassifiedMesh]:
        if self._category_matrix is None:
            raise RuntimeError("Category embeddings not initialized.")
        if not mesh_names:
            return []

        labels = [self.text_for_embedding(n) for n in mesh_names]
        E = self._emb.encode(labels)
        C = self._category_matrix
        sims = E @ C.T
        categories = self._assign_with_context(sims, E)

        out: list[ClassifiedMesh] = []
        for name, cat in zip(mesh_names, categories, strict=True):
            path = self.resolve_server_path(name, outputs_dir)
            out.append(
                ClassifiedMesh(
                    mesh_name=name,
                    absolute_path=str(path) if path else "",
                    category=cat,
                )
            )
        return out

    def _assign_with_context(
        self,
        sims: np.ndarray,
        name_embeddings: np.ndarray,
    ) -> list[LayoutCategory]:
        """Z-scored anchor similarity + centroid prominence; quota for Anchor/Support/Fill."""
        n = sims.shape[0]
        if n == 0:
            return []

        centroid = name_embeddings.mean(axis=0)
        cnorm = np.linalg.norm(centroid) + 1e-12
        centroid = centroid / cnorm
        prom = name_embeddings @ centroid

        def _z(x: np.ndarray) -> np.ndarray:
            s = x.std()
            if s < 1e-8:
                return np.zeros_like(x)
            return (x - x.mean()) / s

        z_anchor_sim = _z(sims[:, 0])
        z_prom = _z(prom)
        anchor_score = z_anchor_sim + z_prom

        order = np.argsort(-anchor_score)
        n_anchor = max(1, min(n, max(1, int(round(0.15 * n)))))
        n_anchor = min(n_anchor, max(1, (n + 2) // 3))
        anchor_set = set(order[:n_anchor].tolist())

        remain = [i for i in range(n) if i not in anchor_set]
        categories: list[LayoutCategory | None] = [None] * n
        for i in anchor_set:
            categories[i] = LayoutCategory.ANCHOR

        if not remain:
            return list(categories)

        rem = np.array(remain, dtype=int)
        support_vs_fill = sims[rem, 1] - sims[rem, 2]
        sup_order = rem[np.argsort(-support_vs_fill)]

        m = len(remain)
        if m == 1:
            categories[remain[0]] = LayoutCategory.FILL
            return list(categories)

        n_support = max(1, min(m - 1, int(round(0.38 * n))))
        support_set = set(sup_order[:n_support].tolist())
        for i in remain:
            categories[i] = (
                LayoutCategory.SUPPORT if i in support_set else LayoutCategory.FILL
            )

        return list(categories)
