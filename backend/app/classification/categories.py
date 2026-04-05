"""Semantic layout categories (extend enum + descriptions to add categories)."""

from enum import Enum


class LayoutCategory(str, Enum):
    ANCHOR = "Anchor"
    SUPPORT = "Support"
    FILL = "Fill"


CATEGORY_ORDER: tuple[LayoutCategory, ...] = (
    LayoutCategory.ANCHOR,
    LayoutCategory.SUPPORT,
    LayoutCategory.FILL,
)

CATEGORY_DESCRIPTIONS: dict[LayoutCategory, str] = {
    LayoutCategory.ANCHOR: (
        "Large central objects that define the environment, such as a bed, mountain, "
        "river, house, main building, island, or primary terrain feature."
    ),
    LayoutCategory.SUPPORT: (
        "Objects that complement anchor objects, such as trees near a house, dressers "
        "beside a bed, bushes, animals, lamps, or furniture grouped with a main prop."
    ),
    LayoutCategory.FILL: (
        "Small decorative or filler objects such as grass, flowers, pebbles, small "
        "animals, clutter, or scattered minor details."
    ),
}
