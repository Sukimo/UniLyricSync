using System;
using UnityEngine;

namespace UniLyricSync
{
    /// <summary>
    /// One time-coded trigger that activates a single word in the lyrics.
    /// Stored as a plain serializable struct so Unity can serialize it
    /// directly inside a List on the UniLyricData ScriptableObject.
    /// </summary>
    [Serializable]
    public struct UniLyricMarker
    {
        [Tooltip("Time in seconds from the start of the AudioClip when this word activates.")]
        public float time;

        [Tooltip("Index of the word inside UniLyricData.Words (the split lyrics array).")]
        public int wordIndex;

        [Tooltip("Cached copy of the word string - used by the editor for display only.")]
        public string word;

        [Tooltip("When true, this marker uses overrideColor instead of UniLyricData.highlightColor.")]
        public bool useOverrideColor;

        [Tooltip("Per-marker highlight color. Only active when useOverrideColor is true.")]
        public Color overrideColor;

        // Convenience constructor used by the editor when placing a marker
        public UniLyricMarker(float time, int wordIndex, string word)
        {
            this.time = time;
            this.wordIndex = wordIndex;
            this.word = word;
            this.useOverrideColor = false;
            this.overrideColor = Color.white;
        }
    }
}