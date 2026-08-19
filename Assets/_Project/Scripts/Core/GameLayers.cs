using UnityEngine;

namespace CarDemo.Core
{
    /// <summary>
    /// Named layers used by the demo. Physics queries must never use "everything":
    /// a suspension ray that accepts any collider will happily rest the car on a crate,
    /// on a moving prop, or on the car's own chassis.
    /// </summary>
    public static class GameLayers
    {
        public const string GroundName = "Ground";
        public const string VehicleName = "Vehicle";
        public const string PropName = "Prop";

        public static int Ground => LayerMask.NameToLayer(GroundName);
        public static int Vehicle => LayerMask.NameToLayer(VehicleName);
        public static int Prop => LayerMask.NameToLayer(PropName);

        /// <summary>Layers the suspension is allowed to stand on.</summary>
        public static LayerMask DrivableMask => LayerMask.GetMask(GroundName, PropName);

        /// <summary>Assigns a layer to an object and its whole hierarchy.</summary>
        public static void ApplyRecursively(GameObject root, int layer)
        {
            if (root == null || layer < 0) return;

            root.layer = layer;
            Transform transform = root.transform;
            for (int i = 0; i < transform.childCount; i++)
            {
                ApplyRecursively(transform.GetChild(i).gameObject, layer);
            }
        }
    }
}
