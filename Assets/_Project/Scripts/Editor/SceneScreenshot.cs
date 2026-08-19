using System.IO;
using CarDemo.Vehicle;
using CarDemo.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarDemo.EditorTools
{
    /// <summary>
    /// Renders the generated scenes to PNG files from a few fixed viewpoints.
    ///
    /// Numeric audits catch geometry that is buried, missing or mismatched, but they say
    /// nothing about proportion — a ramp four times too large passes every one of them. This
    /// exists so the scene can actually be looked at without launching the game.
    /// </summary>
    public static class SceneScreenshot
    {
        private const string OutputFolder = "Builds/Screenshots";
        private const int Width = 1280;
        private const int Height = 720;

        [MenuItem("CarDemo/Capture Scene Screenshots")]
        public static void Capture()
        {
            Directory.CreateDirectory(OutputFolder);

            CaptureDescent();
            CaptureRingMap();

            Debug.Log($"[SHOT] screenshots written to {Path.GetFullPath(OutputFolder)}");
        }

        private static void CaptureDescent()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Descent.unity", OpenSceneMode.Single);

            var streamer = Object.FindFirstObjectByType<DescentStreamer>();
            var car = Object.FindFirstObjectByType<CarController>();
            if (streamer == null || car == null) return;

            streamer.EnsureInitialised();
            streamer.PreloadAroundStart();

            Transform slope = streamer.SlopeRoot;
            Transform carTransform = car.transform;

            // Chase view, the angle the player actually drives from.
            Shoot("descent_chase",
                carTransform.position + slope.forward * -9f + Vector3.up * 4f,
                carTransform.position + slope.forward * 12f);

            // Down the slope from above: shows the shape and scale of the whole course.
            Shoot("descent_overview",
                slope.TransformPoint(new Vector3(0f, 45f, 40f)),
                slope.TransformPoint(new Vector3(0f, 0f, 150f)));

            // Ground level alongside the track, where proportions read best.
            Shoot("descent_side",
                slope.TransformPoint(new Vector3(46f, 6f, 90f)),
                slope.TransformPoint(new Vector3(0f, 1f, 105f)));

            // Close on the car itself.
            Shoot("car_closeup",
                carTransform.position + carTransform.right * 5.5f + Vector3.up * 2f + carTransform.forward * 3f,
                carTransform.position + Vector3.up * 0.6f);
        }

        private static void CaptureRingMap()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Demo.unity", OpenSceneMode.Single);

            var car = Object.FindFirstObjectByType<CarController>();
            if (car == null) return;

            Transform carTransform = car.transform;
            Shoot("ring_chase",
                carTransform.position - carTransform.forward * 9f + Vector3.up * 4f,
                carTransform.position + carTransform.forward * 14f);

            Shoot("ring_overview", new Vector3(0f, 120f, -140f), new Vector3(0f, 0f, 20f));
        }

        private static void Shoot(string name, Vector3 position, Vector3 lookAt)
        {
            var cameraObject = new GameObject("ScreenshotCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = position;
            camera.transform.LookAt(lookAt);
            camera.fieldOfView = 60f;
            camera.farClipPlane = 900f;

            var texture = new RenderTexture(Width, Height, 24);
            camera.targetTexture = texture;
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;

            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();

            RenderTexture.active = previous;
            camera.targetTexture = null;

            File.WriteAllBytes($"{OutputFolder}/{name}.png", image.EncodeToPNG());

            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(image);
        }
    }
}
