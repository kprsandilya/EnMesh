from __future__ import annotations

from typing import Protocol


class PlacementClassifier(Protocol):
    """Pluggable asset naming → Anchor / Support / Fill. Swap implementations in ``main``."""

    def load(self) -> None:
        """Load models and precompute any static embeddings (call once at startup)."""

    def classify_stem(self, stem: str) -> str:
        """Return one of ``Anchor``, ``Support``, ``Fill`` from filename stem (no extension)."""
