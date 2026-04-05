using System.Collections.Generic;
using EnMesh;
using UnityEngine;
using EnMesh.Editor.AutoLayout;

namespace EnMesh.Editor.AutoLayout.Phases
{
    /// <summary>Phase 1: place <see cref="PlacementRole.Anchor"/> items on shuffled grid cells.</summary>
    public sealed class AnchorPlacementPhase : IPlacementPhase
    {
        public string PhaseName => "Place anchors";

        public IEnumerable<PlaceableItem> SelectCandidates(IReadOnlyList<PlaceableItem> all)
        {
            foreach (PlaceableItem p in all)
            {
                if (p.Role == PlacementRole.Anchor)
                    yield return p;
            }
        }

        public IReadOnlyList<PlaceableItem> Execute(PlacementContext ctx, IReadOnlyList<PlaceableItem> candidates)
        {
            var placed = new List<PlaceableItem>();
            System.Random rng = ctx.Rng;

            foreach (PlaceableItem item in candidates)
            {
                float r = item.GetEffectiveClearanceRadius();
                bool success = false;

                foreach (Vector3 gridWorld in ctx.ShuffledGridWorld)
                {
                    Vector3 pos = ctx.SnapWorldToGridWorld(gridWorld);
                    if (!ctx.CanPlace(pos, r))
                        continue;

                    float yaw = rng.Next(0, 4) * 90f + (float)(rng.NextDouble() * 16.0 - 8.0);
                    Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                    item.transform.SetPositionAndRotation(pos, rot);
                    ctx.Register(item, pos, r, PlacementSlotKinds.Anchor);
                    placed.Add(item);
                    success = true;
                    break;
                }

                if (!success)
                    Debug.LogWarning($"[EnMesh AutoLayout] No grid slot for anchor '{item.name}'.");
            }

            return placed;
        }
    }
}
