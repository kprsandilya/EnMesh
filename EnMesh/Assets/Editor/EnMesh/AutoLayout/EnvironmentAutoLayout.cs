using System;
using System.Collections.Generic;
using EnMesh;
using EnMesh.Editor.AutoLayout.Phases;
using UnityEngine;

namespace EnMesh.Editor.AutoLayout
{
    /// <summary>Optional parameters for <see cref="EnvironmentAutoLayout.Run"/>.</summary>
    public readonly struct LayoutSettings
    {
        public LayoutSettings(
            float cellSize,
            float areaWidth,
            float areaDepth,
            float minSeparation,
            bool usePhysicsOverlap,
            LayerMask physicsMask,
            float physicsProbeYOffset,
            int? randomSeed)
        {
            CellSize = Mathf.Max(0.05f, cellSize);
            AreaWidth = Mathf.Max(CellSize, areaWidth);
            AreaDepth = Mathf.Max(CellSize, areaDepth);
            MinSeparation = Mathf.Max(0f, minSeparation);
            UsePhysicsOverlap = usePhysicsOverlap;
            PhysicsMask = physicsMask;
            PhysicsProbeYOffset = physicsProbeYOffset;
            RandomSeed = randomSeed;
        }

        public float CellSize { get; }
        public float AreaWidth { get; }
        public float AreaDepth { get; }
        public float MinSeparation { get; }
        public bool UsePhysicsOverlap { get; }
        public LayerMask PhysicsMask { get; }
        public float PhysicsProbeYOffset { get; }
        /// <summary>If null, a time-based seed is used so each run differs slightly.</summary>
        public int? RandomSeed { get; }
    }

    public readonly struct LayoutResult
    {
        public LayoutResult(bool ok, string message, int placedCount, int totalItems)
        {
            Ok = ok;
            Message = message;
            PlacedCount = placedCount;
            TotalItems = totalItems;
        }

        public bool Ok { get; }
        public string Message { get; }
        public int PlacedCount { get; }
        public int TotalItems { get; }
    }

    /// <summary>Entry point for environment auto-layout; phases are pluggable.</summary>
    public static class EnvironmentAutoLayout
    {
        /// <summary>Default ordered pipeline: anchors → supports (near anchors) → fill remaining.</summary>
        public static IReadOnlyList<IPlacementPhase> CreateDefaultPipeline() =>
            new IPlacementPhase[]
            {
                new AnchorPlacementPhase(),
                new SupportNearAnchorPhase(),
                new FillRemainingPhase()
            };

        /// <summary>
        /// Optional hook: assign to supply a custom ordered phase list (e.g. insert rules before fill).
        /// If null, <see cref="CreateDefaultPipeline"/> is used.
        /// </summary>
        public static Func<IReadOnlyList<IPlacementPhase>> CustomPipelineFactory { get; set; }

        public static LayoutResult Run(Transform environmentRoot, LayoutSettings settings)
        {
            if (environmentRoot == null)
                return new LayoutResult(false, "Environment root is null.", 0, 0);

            PlaceableItem[] items = environmentRoot.GetComponentsInChildren<PlaceableItem>(true);
            if (items.Length == 0)
                return new LayoutResult(
                    false,
                    "No PlaceableItem components found under the environment root.",
                    0,
                    0);

            int seed = settings.RandomSeed ?? Environment.TickCount;
            var rng = new System.Random(seed);

            List<Vector3> grid = BuildGridWorld(environmentRoot, settings, rng);
            Shuffle(grid, rng);

            IReadOnlyList<IPlacementPhase> phases =
                CustomPipelineFactory?.Invoke() ?? CreateDefaultPipeline();

            var ctx = new PlacementContext(
                environmentRoot,
                grid,
                rng,
                settings.CellSize,
                settings.MinSeparation,
                settings.UsePhysicsOverlap,
                settings.PhysicsMask,
                settings.PhysicsProbeYOffset);

            var placed = new HashSet<PlaceableItem>();
            var all = new List<PlaceableItem>(items);

            foreach (IPlacementPhase phase in phases)
            {
                var candidates = new List<PlaceableItem>();
                foreach (PlaceableItem c in phase.SelectCandidates(all))
                {
                    if (!placed.Contains(c))
                        candidates.Add(c);
                }

                IReadOnlyList<PlaceableItem> newly = phase.Execute(ctx, candidates);
                foreach (PlaceableItem p in newly)
                    placed.Add(p);
            }

            string msg =
                $"Auto layout ({phases.Count} phase(s)): placed {placed.Count}/{items.Length}. Seed={seed}.";
            return new LayoutResult(true, msg, placed.Count, items.Length);
        }

        private static List<Vector3> BuildGridWorld(
            Transform root,
            LayoutSettings settings,
            System.Random rng)
        {
            float halfW = settings.AreaWidth * 0.5f;
            float halfD = settings.AreaDepth * 0.5f;
            float cell = settings.CellSize;

            var list = new List<Vector3>();
            for (float x = -halfW; x <= halfW + 0.0001f; x += cell)
            {
                for (float z = -halfD; z <= halfD + 0.0001f; z += cell)
                {
                    float jx = (float)(rng.NextDouble() * cell * 0.4 - cell * 0.2);
                    float jz = (float)(rng.NextDouble() * cell * 0.4 - cell * 0.2);
                    Vector3 local = new Vector3(x + jx, 0f, z + jz);
                    list.Add(root.TransformPoint(local));
                }
            }

            if (list.Count == 0)
            {
                list.Add(root.TransformPoint(Vector3.zero));
            }

            return list;
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
