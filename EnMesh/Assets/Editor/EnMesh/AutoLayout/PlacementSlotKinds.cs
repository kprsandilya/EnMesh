namespace EnMesh.Editor.AutoLayout
{
    /// <summary>
    /// Stored on <see cref="OccupiedSlot.Kind"/> after placement. Phases and custom rules use these
    /// to query anchors vs supports vs fill.
    /// </summary>
    public static class PlacementSlotKinds
    {
        public const string Anchor = "anchor";
        public const string Support = "support";
        public const string Fill = "fill";
    }
}
