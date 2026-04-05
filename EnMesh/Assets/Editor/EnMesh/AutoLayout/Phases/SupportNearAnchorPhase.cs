using System;
using System.Collections.Generic;
using EnMesh;
using UnityEngine;
using EnMesh.Editor.AutoLayout;

namespace EnMesh.Editor.AutoLayout.Phases
{
    /// <summary>Phase 2: place <see cref="PlacementRole.Support"/> items in a ring around anchor slots.</summary>
    public sealed class SupportNearAnchorPhase : IPlacementPhase
    {
        private const int MaxAttemptsPerItem = 48;
        private const float MinRing = 0.65f;
        private const float MaxRing = 2.25f;

        public string PhaseName => "Place supports near anchors";

        public IEnumerable<PlaceableItem> SelectCandidates(IReadOnlyList<PlaceableItem> all)
        {
            foreach (PlaceableItem p in all)
            {
                if (p.Role == PlacementRole.Support)
                    yield return p;
            }
        }

        public IReadOnlyList<PlaceableItem> Execute(PlacementContext ctx, IReadOnlyList<PlaceableItem> candidates)
        {
            var placed = new List<PlaceableItem>();
            IReadOnlyList<OccupiedSlot> anchors = ctx.GetSlotsOfKind(PlacementSlotKinds.Anchor);
            if (anchors.Count == 0)
            {
                Debug.Log("[EnMesh AutoLayout] No anchors placed — skipping support phase.");
                return placed;
            }

            System.Random rng = ctx.Rng;

            foreach (PlaceableItem item in candidates)
            {
                float r = item.GetEffectiveClearanceRadius();
                bool success = false;

                for (int attempt = 0; attempt < MaxAttemptsPerItem && !success; attempt++)
                {
                    OccupiedSlot anchor = anchors[rng.Next(anchors.Count)];
                    float dist = Mathf.Lerp(MinRing, MaxRing, (float)rng.NextDouble());
                    float ang = (float)(rng.NextDouble() * Math.PI * 2.0);
                    Vector3 offset = new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
                    Vector3 raw = anchor.Position + offset;
                    Vector3 pos = ctx.SnapWorldToGridWorld(raw);

                    if (!ctx.CanPlace(pos, r))
                        continue;

                    Vector3 toAnchor = anchor.Position - pos;
                    toAnchor.y = 0f;
                    Quaternion rot = toAnchor.sqrMagnitude > 0.0001f
                        ? Quaternion.LookRotation(toAnchor.normalized, Vector3.up)
                        : Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);

                    rot *= Quaternion.Euler(0f, (float)(rng.NextDouble() * 20.0 - 10.0), 0f);

                    item.transform.SetPositionAndRotation(pos, rot);
                    ctx.Register(item, pos, r, PlacementSlotKinds.Support);
                    placed.Add(item);
                    success = true;
                }

                if (!success)
                    Debug.LogWarning($"[EnMesh AutoLayout] Could not place support '{item.name}' near an anchor.");
            }

            return placed;
        }
    }
}
