using System.Collections.Generic;
using EnMesh;

namespace EnMesh.Editor.AutoLayout
{
    /// <summary>
    /// One ordered step in auto-layout. Add new implementations and register them on the pipeline to extend rules.
    /// </summary>
    public interface IPlacementPhase
    {
        string PhaseName { get; }

        /// <summary>Items this phase should try to place (caller filters unplaced).</summary>
        IEnumerable<PlaceableItem> SelectCandidates(IReadOnlyList<PlaceableItem> all);

        /// <summary>Place as many candidates as possible. Return items successfully placed.</summary>
        IReadOnlyList<PlaceableItem> Execute(PlacementContext ctx, IReadOnlyList<PlaceableItem> candidates);
    }
}
