using UnityEngine;

namespace EnMesh
{
    /// <summary>
    /// How this object participates in auto-layout. Table/chair/decor are examples of sizes —
    /// anchors are large primary props, supports attach to anchors, fill uses leftover grid.
    /// </summary>
    public enum PlacementRole
    {
        /// <summary>Large / primary props that define the scene (islands, counters, beds, …).</summary>
        Anchor = 0,
        /// <summary>Props placed relative to anchors (seats, lamps on a desk, …).</summary>
        Support = 1,
        /// <summary>Everything else: scattered on free grid cells.</summary>
        Fill = 2,
    }

    /// <summary>
    /// Marks an object as participating in EnMesh auto-layout.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlaceableItem : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Anchor → grid first; Support → near anchors; Fill → remaining cells.")]
        private PlacementRole role = PlacementRole.Fill;

        [SerializeField]
        [Tooltip("Optional tag for custom IPlacementPhase rules (e.g. \"kitchen\", \"outdoor\").")]
        private string category = "";

        [SerializeField]
        [Min(0f)]
        [Tooltip("Horizontal clearance for overlap checks. 0 = estimate from renderers/colliders on this object.")]
        private float clearanceRadius;

        public PlacementRole Role
        {
            get => role;
            set => role = value;
        }

        public string Category
        {
            get => category;
            set => category = value;
        }

        public float ClearanceRadius => clearanceRadius;

        /// <summary>World-space horizontal radius used for spacing (XZ).</summary>
        public float GetEffectiveClearanceRadius()
        {
            if (clearanceRadius > 0.001f)
                return clearanceRadius;

            float fromGeometry = EstimateRadiusFromGeometry();
            return Mathf.Max(0.2f, fromGeometry);
        }

        private float EstimateRadiusFromGeometry()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                var col = GetComponentInChildren<Collider>();
                if (col == null) return 0.35f;
                Vector3 ext = col.bounds.extents;
                return 0.5f * Mathf.Max(ext.x, ext.z);
            }

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);
            return 0.5f * Mathf.Max(b.size.x, b.size.z);
        }
    }
}
