"""
Embedding-based placement classification using a local sentence-transformers model.

Category semantics are defined as natural-language descriptions; upgrade by changing
``CATEGORY_DESCRIPTIONS`` or subclassing ``EmbeddingPlacementClassifier``.
"""

from __future__ import annotations

import logging
from typing import Final

import numpy as np

from app.placement_labels import ANCHOR, FILL, SUPPORT

logger = logging.getLogger(__name__)

# Order must match ``_labels`` below (used for argmax index → label).
CATEGORY_DESCRIPTIONS: Final[dict[str, str]] = {
    ANCHOR: (
        "Large central objects of the environment like a bed, mountain, river, house, "
        "building, landscape feature, main furniture piece, or primary structure."
    ),
    SUPPORT: (
        "Objects that complement or support the anchor objects like trees, dressers, bushes, "
        "animals, secondary furniture, companion props, or contextual elements around a main subject."
    ),
    FILL: (
        "Small decorative objects to fill empty space in the environment like small animals, "
        "grass, flowers, pebbles, tiny props, clutter, or minor ornamental details."
    ),
}


class EmbeddingPlacementClassifier:
    """
    ``sentence-transformers`` cosine similarity vs fixed category description embeddings.

    Model is loaded once; category texts are embedded once at ``load()``.
    """

    def __init__(self, model_name: str) -> None:
        self._model_name = model_name
        self._model = None
        self._labels: list[str] = [ANCHOR, SUPPORT, FILL]
        self._category_matrix: np.ndarray | None = None  # (3, dim), L2-normalized rows

    def load(self) -> None:
        from sentence_transformers import SentenceTransformer

        logger.info("Loading sentence embedding model %r …", self._model_name)
        self._model = SentenceTransformer(self._model_name)

        texts = [CATEGORY_DESCRIPTIONS[label] for label in self._labels]
        mat = self._model.encode(
            texts,
            convert_to_numpy=True,
            normalize_embeddings=True,
            show_progress_bar=False,
        )
        self._category_matrix = np.asarray(mat, dtype=np.float32)
        logger.info(
            "Embedding classifier ready: %d category probes, dim=%d",
            self._category_matrix.shape[0],
            self._category_matrix.shape[1],
        )

    def classify_stem(self, stem: str) -> str:
        if self._model is None or self._category_matrix is None:
            raise RuntimeError("EmbeddingPlacementClassifier.load() was not called")

        query_text = (stem or "").strip().replace("_", " ")
        if not query_text:
            query_text = "generic environment asset"

        q = self._model.encode(
            query_text,
            convert_to_numpy=True,
            normalize_embeddings=True,
            show_progress_bar=False,
        )
        qv = np.asarray(q, dtype=np.float32).reshape(1, -1)
        # Rows of _category_matrix are unit vectors → dot = cosine similarity
        sims = (self._category_matrix @ qv.T).ravel()
        best = int(np.argmax(sims))
        label = self._labels[best]
        logger.debug(
            "classify_stem(%r) → %s scores=%s",
            stem,
            label,
            {self._labels[i]: float(sims[i]) for i in range(len(self._labels))},
        )
        return label
