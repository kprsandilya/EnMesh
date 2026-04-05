using System.Collections.Generic;
using EnMesh;
using UnityEngine;
using EnMesh.Editor.AutoLayout;

namespace EnMesh.Editor.AutoLayout.Phases
{
    /// <summary>Phase 3: fills still-unplaced items (typically <see cref="PlacementRole.Fill"/>) into free grid cells.</summary>
    public sealed class FillRemainingPhase : IPlacementPhase
    {
        public string PhaseName => "Fill remaining";

        public IEnumerable<PlaceableItem> SelectCandidates(IReadOnlyList<PlaceableItem> all)
        {
            foreach (PlaceableItem p in all)
                yield return p;
        }

        public IReadOnlyList<PlaceableItem> Execute(PlacementContext ctx, IReadOnlyList<PlaceableItem> candidates)
        {
            var placed = new List<PlaceableItem>();
            System.Random rng = ctx.Rng;

            var order = new List<PlaceableItem>(candidates);
            Shuffle(order, rng);

            foreach (PlaceableItem item in order)
            {
                float clearance = item.GetEffectiveClearanceRadius();
                bool success = false;

                foreach (Vector3 gridWorld in ctx.ShuffledGridWorld)
                {
                    Vector3 pos = ctx.SnapWorldToGridWorld(gridWorld);
                    if (!ctx.CanPlace(pos, clearance))
                        continue;

                    float yaw = (float)(rng.NextDouble() * 360.0);
                    Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                    item.transform.SetPositionAndRotation(pos, rot);
                    ctx.Register(item, pos, clearance, PlacementSlotKinds.Fill);
                    placed.Add(item);
                    success = true;
                    break;
                }

                if (!success)
                    Debug.LogWarning($"[EnMesh AutoLayout] No free cell for '{item.name}'.");
            }

            return placed;
        }

        private static void Shuffle<T>(IList<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
