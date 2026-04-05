"""Canonical placement category strings (match Unity PlacementRole)."""

from __future__ import annotations

from typing import Final

ANCHOR: Final[str] = "Anchor"
SUPPORT: Final[str] = "Support"
FILL: Final[str] = "Fill"

ALL_CATEGORIES: Final[tuple[str, ...]] = (ANCHOR, SUPPORT, FILL)
