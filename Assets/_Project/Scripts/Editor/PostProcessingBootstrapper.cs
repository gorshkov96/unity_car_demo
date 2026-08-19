using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CarDemo.EditorTools
{
    /// <summary>
    /// Builds the scene's Global Volume: tonemapping, colour grading, bloom and vignette.
    ///
    /// Without a tonemapper the renderer writes linear values straight to the screen, and the
    /// result is exactly what flat, washed-out lighting looks like — no matter how saturated
    /// the materials are. This is the single largest step from "grey test scene" to something
    /// that reads as a game.
    ///
    /// The profile is created on the Volume itself rather than shared: the docs warn that the
    /// default and quality-level profiles are cached, so editing them from a script does
    /// nothing at all.
    /// </summary>
    public static class PostProcessingBootstrapper
    {
        public static void Create(bool speedEffects = true)
        {
            var volumeObject = new GameObject("Global Volume");
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "GlobalVolumeProfile";
            volume.profile = profile;

            // Tonemapping first: it maps the render's high dynamic range into displayable
            // colour. Neutral keeps the palette's hues honest; ACES would push everything
            // towards a filmic, desaturated look that fights a low-poly style.
            Tonemapping tonemapping = profile.Add<Tonemapping>();
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.Neutral;

            // Grading: a little more contrast and saturation than the materials carry on
            // their own, which is what separates neighbouring flat facets.
            ColorAdjustments grading = profile.Add<ColorAdjustments>();
            grading.postExposure.overrideState = true;
            grading.postExposure.value = 0.55f;
            grading.contrast.overrideState = true;
            grading.contrast.value = 18f;
            grading.saturation.overrideState = true;
            grading.saturation.value = 28f;
            grading.colorFilter.overrideState = true;
            grading.colorFilter.value = new Color(1f, 0.99f, 0.96f);

            // Cool shadows against warm light: the cheapest way to make flat shading read as
            // three-dimensional.
            ShadowsMidtonesHighlights split = profile.Add<ShadowsMidtonesHighlights>();
            split.shadows.overrideState = true;
            split.shadows.value = new Vector4(0.86f, 0.92f, 1.15f, 0f);
            split.highlights.overrideState = true;
            split.highlights.value = new Vector4(1.08f, 1.02f, 0.9f, 0f);

            // Bloom with the threshold near 1 so only genuinely bright things glow —
            // headlights and hazard markings, not the whole car.
            Bloom bloom = profile.Add<Bloom>();
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 1.05f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.55f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.6f;

            Vignette vignette = profile.Add<Vignette>();
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.22f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.5f;

            if (!speedEffects) return;

            // Motion blur in camera-only mode: the docs single it out as the strongest
            // contributor to a sense of speed, and the object mode costs a motion-vector pass
            // for an effect a chase camera barely shows.
            MotionBlur motionBlur = profile.Add<MotionBlur>();
            motionBlur.mode.overrideState = true;
            motionBlur.mode.value = MotionBlurMode.CameraOnly;
            motionBlur.intensity.overrideState = true;
            motionBlur.intensity.value = 0.18f;
            motionBlur.clamp.overrideState = true;
            motionBlur.clamp.value = 0.05f;
        }
    }
}
