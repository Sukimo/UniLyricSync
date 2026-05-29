using System;
using System.Collections.Generic;
using UnityEngine;

namespace UniLyricSync
{
    public enum HighlightEffect
    {
        ColorOnly = 0,
        ScaleAndColor = 1,
        WaveAndColor = 2
    }

    [CreateAssetMenu(
        fileName = "NewUniLyricData",
        menuName = "UniLyricSync/Lyric Data",
        order = 0)]
    public class UniLyricData : ScriptableObject
    {
        [Header("Audio")]
        [Tooltip("AudioClip to play. Use Load Type: Decompress On Load for waveform baking.")]
        public AudioClip audioClip;

        [Header("Lyrics")]
        [TextArea(3, 10)]
        [Tooltip("Full lyrics. Words split on whitespace — each word = one marker.")]
        public string lyricsText = "";

        [Header("Trigger Markers")]
        [Tooltip("Maintained by the Editor Window. Each marker = one word activation time.")]
        public List<UniLyricMarker> markers = new List<UniLyricMarker>();

        // ── Colours ──────────────────────────────────────────────────────────
        [Header("Colours")]
        [Tooltip("Active (highlighted) word colour.")]
        public Color highlightColor = Color.white;

        [Tooltip("Words not yet reached.")]
        public Color defaultColor = new Color(0.55f, 0.55f, 0.55f, 1f);

        [Tooltip("Words already passed.")]
        public Color doneColor = new Color(0.35f, 0.35f, 0.35f, 1f);

        // ── Transition ───────────────────────────────────────────────────────
        [Header("Transition")]
        [Tooltip("0 = instant snap. Higher = slower lerp. Framerate-independent.")]
        [Range(0f, 1f)]
        public float transitionSmoothness = 0.08f;   // used as: pow(smoothness, deltaTime)

        // ── Effect ───────────────────────────────────────────────────────────
        [Header("Effect")]
        public HighlightEffect effect = HighlightEffect.ColorOnly;

        [Tooltip("Scale effect: how much the active word grows. 1.0 = no scale, 1.2 = 20% bigger.")]
        [Range(1f, 2f)]
        public float scaleAmount = 1.15f;

        [Tooltip("Scale effect: how fast the scale pulses (cycles per second).")]
        [Range(1f, 30f)]
        public float scaleSpeed = 8f;

        [Tooltip("Wave effect: vertical bounce height in pixels.")]
        [Range(0.5f, 20f)]
        public float waveAmplitude = 3f;

        [Tooltip("Wave effect: how fast the wave moves (cycles per second).")]
        [Range(1f, 30f)]
        public float waveSpeed = 12f;

        [Tooltip("Wave effect: phase offset between adjacent characters (radians).")]
        [Range(0f, 3f)]
        public float waveSpread = 0.9f;

        // ── End marker ───────────────────────────────────────────────────────
        [Header("End Marker")]
        [Tooltip("Time (seconds) when the last word fades back to doneColor.\n" +
                 "Set automatically when you place the end marker in the Editor Window.\n" +
                 "0 = disabled (last word stays highlighted until playback stops).")]
        public float endMarkerTime = 0f;

        // ── Waveform cache (Editor only) ─────────────────────────────────────
        [HideInInspector]
        public Texture2D waveformTexture;

        // ── Helpers ──────────────────────────────────────────────────────────
        public string[] Words =>
            string.IsNullOrWhiteSpace(lyricsText)
                ? Array.Empty<string>()
                : lyricsText.Split(
                    new[] { ' ', '\n', '\r', '\t' },
                    StringSplitOptions.RemoveEmptyEntries);

        public int GetActiveWordIndex(float time)
        {
            int active = -1;
            for (int i = 0; i < markers.Count; i++)
            {
                if (markers[i].time <= time) active = markers[i].wordIndex;
                else break;
            }
            return active;
        }

        public void SortMarkers() =>
            markers.Sort((a, b) => a.time.CompareTo(b.time));

        public Color GetHighlightColorForWord(int wordIndex)
        {
            for (int i = 0; i < markers.Count; i++)
            {
                if (markers[i].wordIndex == wordIndex && markers[i].useOverrideColor)
                {
                    Color c = markers[i].overrideColor;
                    c.a = Mathf.Max(c.a, 0.05f); // never fully transparent
                    return c;
                }
            }
            return highlightColor;
        }
    }
}
