// This file is inside the Editor folder — it will NOT be included in player builds.
using UnityEngine;
using UnityEditor;

namespace UniLyricSync.Editor
{
    /// <summary>
    /// Bakes an AudioClip's sample data into a Texture2D waveform image.
    ///
    /// Called once by UniLyricSyncWindow when a new AudioClip is assigned.
    /// The result is stored as a hidden sub-asset inside the UniLyricData .asset file
    /// so it persists without re-baking on every Unity restart.
    ///
    /// Requirements:
    ///   AudioClip Load Type must be "Decompress On Load" or "Compressed In Memory".
    ///   Streaming clips return zeros from GetData() — we detect this and warn.
    /// </summary>
    public static class UniLyricWaveformBaker
    {
        // Texture resolution
        private const int TexWidth = 1024;
        private const int TexHeight = 64;

        // Waveform colours  (dark background, teal bars)
        private static readonly Color BackgroundColor = new Color(0.10f, 0.15f, 0.22f, 1f);
        private static readonly Color WaveColor = new Color(0.22f, 0.62f, 0.87f, 1f);
        private static readonly Color MidLineColor = new Color(0.20f, 0.30f, 0.38f, 1f);

        // ── Public API ─────────────────────────────────────────────────────────
        /// <summary>
        /// Bakes the waveform texture for <paramref name="data"/>.
        /// Saves it as a hidden sub-asset of <paramref name="data"/> via AssetDatabase.
        /// Safe to call multiple times — old sub-asset is replaced.
        /// </summary>
        public static void Bake(UniLyricData data)
        {
            if (data == null || data.audioClip == null) return;

            AudioClip clip = data.audioClip;

            // ── Guard: check Load Type ─────────────────────────────────────────
            string assetPath = AssetDatabase.GetAssetPath(clip);
            if (!string.IsNullOrEmpty(assetPath))
            {
                AudioImporter importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
                if (importer != null)
                {
                    AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                    if (settings.loadType == AudioClipLoadType.Streaming)
                    {
                        Debug.LogWarning(
                            $"[UniLyricSync] '{clip.name}' Load Type is Streaming — " +
                            "GetData() will return zeros. Change Load Type to " +
                            "'Decompress On Load' in the AudioClip Inspector, then re-assign.");
                        data.waveformTexture = MakeErrorTexture();
                        SaveSubAsset(data);
                        return;
                    }
                }
            }

            // ── Read sample data ───────────────────────────────────────────────
            int totalSamples = clip.samples * clip.channels;
            float[] samples = new float[totalSamples];

            if (!clip.GetData(samples, 0))
            {
                Debug.LogWarning(
                    $"[UniLyricSync] GetData() failed for '{clip.name}'. " +
                    "Make sure Load Type is not Streaming.");
                data.waveformTexture = MakeErrorTexture();
                SaveSubAsset(data);
                return;
            }

            // ── Downsample: one column per pixel ───────────────────────────────
            // For stereo: average L and R channels before peak detection.
            int channels = clip.channels;
            int samplesPerPx = Mathf.Max(1, clip.samples / TexWidth);

            float[] peaks = new float[TexWidth];

            for (int px = 0; px < TexWidth; px++)
            {
                int start = px * samplesPerPx * channels;
                int end = Mathf.Min(start + samplesPerPx * channels, totalSamples);
                float peak = 0f;

                for (int s = start; s < end; s += channels)
                {
                    // Average all channels into one mono value
                    float sum = 0f;
                    for (int c = 0; c < channels && s + c < totalSamples; c++)
                        sum += Mathf.Abs(samples[s + c]);

                    float mono = sum / channels;
                    if (mono > peak) peak = mono;
                }
                peaks[px] = peak;
            }

            // ── Build Texture2D ────────────────────────────────────────────────
            Texture2D tex = new Texture2D(TexWidth, TexHeight, TextureFormat.RGBA32, false)
            {
                name = "__WaveformCache",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[TexWidth * TexHeight];

            // Fill background
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = BackgroundColor;

            // Draw centre line
            int midY = TexHeight / 2;
            for (int px = 0; px < TexWidth; px++)
                pixels[midY * TexWidth + px] = MidLineColor;

            // Draw waveform bars (centred, symmetric)
            for (int px = 0; px < TexWidth; px++)
            {
                int halfHeight = Mathf.RoundToInt(peaks[px] * (TexHeight * 0.5f - 1));
                for (int dy = -halfHeight; dy <= halfHeight; dy++)
                {
                    int y = midY + dy;
                    if (y >= 0 && y < TexHeight)
                    {
                        // Slightly brighter at the edges of the bar for a nice look
                        float edge = 1f - Mathf.Abs(dy) / (float)(halfHeight + 1);
                        Color barCol = Color.Lerp(WaveColor * 0.7f, WaveColor, edge);
                        pixels[y * TexWidth + px] = barCol;
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();

            data.waveformTexture = tex;
            SaveSubAsset(data);

            Debug.Log($"[UniLyricSync] Waveform baked for '{clip.name}'.");
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        /// <summary>
        /// Saves <c>data.waveformTexture</c> as a hidden sub-asset of <c>data</c>.
        /// Replaces any previously baked texture.
        /// </summary>
        private static void SaveSubAsset(UniLyricData data)
        {
            string path = AssetDatabase.GetAssetPath(data);
            if (string.IsNullOrEmpty(path)) return; // asset not saved yet

            // Remove old waveform sub-asset if present
            UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (var sub in subAssets)
            {
                if (sub is Texture2D t && t.name == "__WaveformCache")
                {
                    AssetDatabase.RemoveObjectFromAsset(sub);
                    break;
                }
            }

            if (data.waveformTexture != null)
            {
                data.waveformTexture.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(data.waveformTexture, data);
            }

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }

        /// <summary>1×1 red texture shown when baking fails.</summary>
        private static Texture2D MakeErrorTexture()
        {
            Texture2D t = new Texture2D(TexWidth, TexHeight, TextureFormat.RGBA32, false)
            {
                name = "__WaveformCache"
            };
            Color[] px = new Color[TexWidth * TexHeight];
            Color bg = new Color(0.3f, 0.05f, 0.05f, 1f);
            Color err = new Color(0.8f, 0.15f, 0.15f, 1f);

            for (int i = 0; i < px.Length; i++) px[i] = bg;

            // Draw an X pattern to indicate error
            for (int i = 0; i < Mathf.Min(TexWidth, TexHeight); i++)
            {
                int y1 = Mathf.RoundToInt(i * (TexHeight / (float)Mathf.Min(TexWidth, TexHeight)));
                int x1 = Mathf.RoundToInt(i * (TexWidth / (float)Mathf.Min(TexWidth, TexHeight)));
                int x2 = TexWidth - 1 - x1;
                if (x1 < TexWidth && y1 < TexHeight) px[y1 * TexWidth + x1] = err;
                if (x2 >= 0 && y1 < TexHeight) px[y1 * TexWidth + x2] = err;
            }

            t.SetPixels(px);
            t.Apply();
            return t;
        }
    }
}