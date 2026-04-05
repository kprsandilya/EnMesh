"""Loads sentence-transformers once; swap model_id for different backbones."""

from __future__ import annotations

import logging

import numpy as np

logger = logging.getLogger(__name__)


class SentenceEmbeddingRuntime:
    def __init__(self, model_id: str) -> None:
        self._model_id = model_id
        self._model = None

    @property
    def model_id(self) -> str:
        return self._model_id

    @property
    def is_loaded(self) -> bool:
        return self._model is not None

    def load(self) -> None:
        if self._model is not None:
            return
        from sentence_transformers import SentenceTransformer

        logger.info("Loading sentence-transformers model %r …", self._model_id)
        self._model = SentenceTransformer(self._model_id)
        logger.info("Embedding model ready")

    def encode(self, texts: list[str]) -> np.ndarray:
        if self._model is None:
            raise RuntimeError("Embedding model not loaded; call load() at startup.")
        vec = self._model.encode(
            texts,
            normalize_embeddings=True,
            show_progress_bar=False,
        )
        return np.asarray(vec, dtype=np.float64)
