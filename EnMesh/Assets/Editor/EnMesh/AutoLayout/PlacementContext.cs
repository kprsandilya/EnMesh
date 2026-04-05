using System;
using System.Collections.Generic;
using EnMesh;
using UnityEngine;

namespace EnMesh.Editor.AutoLayout
{
    /// <summary>Shared state for placement phases (grid, occupancy, RNG).</summary>
    public sealed class PlacementContext
    {
        public PlacementContext(
            Transform environmentRoot,
            IReadOnlyList<Vector3> shuffledGridWorld,
            System.Random rng,
            float cellSize,
            float minSeparation,
            bool usePhysicsOverlap,
            LayerMask physicsMask,
            float physicsProbeYOffset)
        {
            EnvironmentRoot = environmentRoot;
            ShuffledGridWorld = shuffledGridWorld;
            Rng = rng;
            CellSize = cellSize;
            MinSeparation = minSeparation;
            UsePhysicsOverlap = usePhysicsOverlap;
            PhysicsMask = physicsMask;
            PhysicsProbeYOffset = physicsProbeYOffset;
        }

        public Transform EnvironmentRoot { get; }
        public IReadOnlyList<Vector3> ShuffledGridWorld { get; }
        public System.Random Rng { get; }
        public float CellSize { get; }
        public float MinSeparation { get; }
        public bool UsePhysicsOverlap { get; }
        public LayerMask PhysicsMask { get; }
        public float PhysicsProbeYOffset { get; }

        /// <summary>World positions already claimed (placed items).</summary>
        public List<OccupiedSlot> Occupied { get; } = new List<OccupiedSlot>();

        public bool CanPlace(Vector3 worldPosition, float clearanceRadius)
        {
            foreach (OccupiedSlot o in Occupied)
            {
                float need = o.Radius + clearanceRadius + MinSeparation;
                float dx = worldPosition.x - o.Position.x;
                float dz = worldPosition.z - o.Position.z;
                if (dx * dx + dz * dz < need * need)
                    return false;
            }

            if (UsePhysicsOverlap)
            {
                Vector3 probe = worldPosition + Vector3.up * PhysicsProbeYOffset;
                if (Physics.CheckSphere(
                        probe,
                        clearanceRadius,
                        PhysicsMask,
                        QueryTriggerInteraction.Ignore))
                    return false;
            }

            return true;
        }

        public void Register(PlaceableItem item, Vector3 worldPosition, float clearanceRadius, string slotKind)
        {
            Occupied.Add(new OccupiedSlot(worldPosition, clearanceRadius, slotKind, item));
        }

        /// <summary>Snap a world point onto the environment root XZ grid at local Y = 0.</summary>
        public Vector3 SnapWorldToGridWorld(Vector3 world)
        {
            Vector3 local = EnvironmentRoot.InverseTransformPoint(world);
            float cs = Mathf.Max(0.05f, CellSize);
            float x = Mathf.Round(local.x / cs) * cs;
            float z = Mathf.Round(local.z / cs) * cs;
            return EnvironmentRoot.TransformPoint(new Vector3(x, 0f, z));
        }

        /// <summary>Slots whose <see cref="OccupiedSlot.Kind"/> satisfies <paramref name="predicate"/>.</summary>
        public IReadOnlyList<OccupiedSlot> GetSlotsMatchingKind(Func<string, bool> predicate)
        {
            var list = new List<OccupiedSlot>();
            foreach (OccupiedSlot o in Occupied)
            {
                if (predicate(o.Kind))
                    list.Add(o);
            }

            return list;
        }

        /// <summary>Convenience: all slots registered with a given kind string (e.g. <see cref="PlacementSlotKinds.Anchor"/>).</summary>
        public IReadOnlyList<OccupiedSlot> GetSlotsOfKind(string kind) =>
            GetSlotsMatchingKind(k => k == kind);
    }

    public readonly struct OccupiedSlot
    {
        public OccupiedSlot(Vector3 position, float radius, string kind, PlaceableItem item)
        {
            Position = position;
            Radius = radius;
            Kind = kind;
            Item = item;
        }

        public Vector3 Position { get; }
        public float Radius { get; }
        public string Kind { get; }
        public PlaceableItem Item { get; }
    }
}
