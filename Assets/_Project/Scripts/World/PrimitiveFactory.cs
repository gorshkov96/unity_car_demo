using UnityEngine;

namespace CarDemo.World
{
    /// <summary>
    /// Helpers for building geometry out of Unity primitives.
    /// The demo deliberately uses primitives: no art pipeline, no binary assets in git,
    /// and every shape is described by readable code.
    /// </summary>
    public static class PrimitiveFactory
    {
        /// <summary>Creates a box with a collider, parented and placed in one call.</summary>
        public static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Quaternion rotation, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.SetLocalPositionAndRotation(position, rotation);
            go.transform.localScale = size;
            ApplyMaterial(go, material);
            return go;
        }

        public static GameObject Cylinder(string name, Transform parent, Vector3 position, Vector3 scale, Quaternion rotation, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.SetLocalPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
            ApplyMaterial(go, material);
            return go;
        }

        public static void ApplyMaterial(GameObject go, Material material)
        {
            if (material == null) return;
            if (go.TryGetComponent(out MeshRenderer renderer))
            {
                renderer.sharedMaterial = material;
            }
        }

        /// <summary>Removes the collider a primitive is created with (for pure decoration).</summary>
        public static void StripCollider(GameObject go)
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider == null) return;

            if (Application.isPlaying)
            {
                Object.Destroy(collider);
            }
            else
            {
                Object.DestroyImmediate(collider);
            }
        }

        /// <summary>
        /// Marks generated geometry as static for lighting and culling.
        ///
        /// Deliberately does NOT set BatchingStatic: the project runs the GPU Resident Drawer,
        /// and static batching conflicts with it — it merges meshes on the CPU and defeats the
        /// GPU-side instancing and occlusion culling the drawer provides.
        /// </summary>
        public static void MarkStatic(GameObject go)
        {
#if UNITY_EDITOR
            const UnityEditor.StaticEditorFlags flags =
                UnityEditor.StaticEditorFlags.ContributeGI
                | UnityEditor.StaticEditorFlags.OccluderStatic
                | UnityEditor.StaticEditorFlags.OccludeeStatic
                | UnityEditor.StaticEditorFlags.ReflectionProbeStatic;

            // Recursive on purpose: SetStaticEditorFlags only touches the object it is given,
            // and every piece of generated geometry is a child of the root it is called on.
            UnityEditor.GameObjectUtility.SetStaticEditorFlags(go, flags);
            Transform transform = go.transform;
            for (int i = 0; i < transform.childCount; i++)
            {
                MarkStatic(transform.GetChild(i).gameObject);
            }
#endif
        }
    }
}
