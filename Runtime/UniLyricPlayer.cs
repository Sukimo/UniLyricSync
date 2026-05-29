using System;
using TMPro;
using UnityEngine;

namespace UniLyricSync
{
    [AddComponentMenu("UniLyricSync/Lyric Player")]
    [RequireComponent(typeof(AudioSource))]
    public class UniLyricPlayer : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Data")]
        public UniLyricData data;
        public TMP_Text tmpText;

        [Header("Playback")]
        public bool playOnStart = true;
        public bool loop = false;

        [Header("Advanced")]
        [Tooltip("Disable to drive TMP vertex colours yourself via OnWordHighlighted.")]
        public bool useBuiltInEffect = true;

        // ── Events ────────────────────────────────────────────────────────────
        public event Action<int, string> OnWordHighlighted;
        public event Action OnPlaybackComplete;

        // ── Public read-only ──────────────────────────────────────────────────
        public bool IsPlaying => _audioSource != null && _audioSource.isPlaying;
        public int CurrentWordIndex => _currentWordIndex;
        public float CurrentTime => _audioSource != null && _audioSource.clip != null
                                        ? (float)_audioSource.timeSamples
                                          / Mathf.Max(1, _audioSource.clip.frequency)
                                        : 0f;

        // ── Private ───────────────────────────────────────────────────────────
        AudioSource _audioSource;
        int _currentWordIndex = -1;
        int _nextMarkerIndex = 0;
        bool _finished = false;

        // Per-word current colours (lerp state)
        Color[] _currentColors;

        // ── V1.1 character-level map ──────────────────────────────────────────
        // _charToWordIndex[i] = which word index character i belongs to.
        // -1 means the character belongs to no word (space, newline, etc.)
        int[] _charToWordIndex;

        // Vertex cache for geometry effects.
        // Cache mesh vertices ONCE after ForceMeshUpdate,
        // restore each frame before applying offsets so they don't accumulate.
        Vector3[] _baseVertices;
        bool _baseVerticesCached = false;

        // End-marker fade state
        bool _endMarkerTriggered = false;

        // ── Unity ─────────────────────────────────────────────────────────────
        void Awake() => _audioSource = GetComponent<AudioSource>();

        void Start()
        {
            if (playOnStart) Play();
        }

        void Update()
        {
            if (data == null || tmpText == null) return;

            // ── End marker check (runs BEFORE end-of-clip so it wins over loop) ─
            if (!_endMarkerTriggered && data.endMarkerTime > 0f
                && CurrentTime >= data.endMarkerTime)
            {
                _endMarkerTriggered = true;
                _currentWordIndex = -2;   // fade all words to doneColor

                _audioSource.Stop();

                _finished = true;
                OnPlaybackComplete?.Invoke();
                if (loop)
                {
                    Play();
                    return;
                }
                if (useBuiltInEffect) ApplyVertexColors();
                return;
            }

            // ── Natural end-of-clip detection ──────────────────────────────────
            if (!_finished && !IsPlaying && _audioSource.clip != null
                && _audioSource.timeSamples == 0 && _currentWordIndex >= 0)
            {
                _finished = true;
                OnPlaybackComplete?.Invoke();
                if (loop)
                {
                    Play();
                    return;
                }
            }

            AdvanceMarkers();

            if (useBuiltInEffect)
                ApplyVertexColors();
        }

        // ── Public API ────────────────────────────────────────────────────────
        public void Play()
        {
            if (data == null)
            { Debug.LogWarning("[UniLyricPlayer] No UniLyricData assigned.", this); return; }
            if (data.audioClip == null)
            { Debug.LogWarning("[UniLyricPlayer] UniLyricData has no AudioClip.", this); return; }
            if (tmpText == null)
            { Debug.LogWarning("[UniLyricPlayer] No TMP_Text assigned.", this); return; }

            tmpText.text = data.lyricsText;
            tmpText.ForceMeshUpdate();

            CacheBaseVertices();
            BuildCharToWordMap();   // V1.1 — must come after ForceMeshUpdate
            InitColorArrays();

            _currentWordIndex = -1;
            _nextMarkerIndex = 0;
            _finished = false;
            _endMarkerTriggered = false;

            _audioSource.clip = data.audioClip;
            _audioSource.loop = false;
            _audioSource.Play();
        }

        public void Stop()
        {
            _audioSource?.Stop();
            _currentWordIndex = -1;
            _nextMarkerIndex = 0;
            _finished = false;
            _endMarkerTriggered = false;
            ResetAllColors();
        }

        public void Pause()
        {
            if (_audioSource == null) return;
            if (_audioSource.isPlaying) _audioSource.Pause();
            else _audioSource.UnPause();
        }

        public void SeekTo(float seconds)
        {
            if (data?.audioClip == null) return;
            seconds = Mathf.Clamp(seconds, 0f, data.audioClip.length);
            _audioSource.timeSamples = Mathf.RoundToInt(seconds * data.audioClip.frequency);

            _nextMarkerIndex = 0;
            _currentWordIndex = data.GetActiveWordIndex(seconds);
            for (int i = 0; i < data.markers.Count; i++)
            {
                if (data.markers[i].time <= seconds) _nextMarkerIndex = i + 1;
                else break;
            }
        }

        // ── Internal ──────────────────────────────────────────────────────────
        void AdvanceMarkers()
        {
            float t = CurrentTime;
            while (_nextMarkerIndex < data.markers.Count
                   && data.markers[_nextMarkerIndex].time <= t)
            {
                var m = data.markers[_nextMarkerIndex];
                _currentWordIndex = m.wordIndex;
                OnWordHighlighted?.Invoke(m.wordIndex, m.word);
                _nextMarkerIndex++;
            }
        }

        void InitColorArrays()
        {
            if (data == null) return;
            int n = data.Words.Length;
            if (_currentColors == null || _currentColors.Length != n)
                _currentColors = new Color[n];
            for (int i = 0; i < n; i++)
                _currentColors[i] = data.defaultColor;
        }

        // ── TODO: Rich text compatibility test ───────────────────────────────
        // BuildCharToWordMap relies on words[w].Length matching TMP's visible
        // character count per word. This breaks when lyrics contain rich text tags
        // e.g. "<b>Hello</b>" — TMP strips the tags before building characterInfo,
        // so words[w].Length = 15 but TMP sees only 5 visible chars → map desync.
        //
        // Test cases to run before enabling rich text support:
        //   1. "<b>word</b>"          — bold tag wrapping a single word
        //   2. "<color=#FF0000>word</color>"  — inline color override
        //   3. Mixed: "normal <b>bold</b> normal"
        //   4. Punctuation inside tag: "<b>Hello,</b>"
        //
        // Fix path (if needed later):
        //   Strip rich text tags from lyricsText before calling Split() in
        //   UniLyricData.Words, so lengths stay in sync with what TMP renders.
        //   System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", "")
        // ─────────────────────────────────────────────────────────────────────

        // ── V1.1 Build char-to-word map ───────────────────────────────────────
        // Strategy (Option B): walk characterInfo in order.
        // Skip non-visible entries (spaces, newlines — isVisible = false).
        // Group consecutive visible chars into words by matching data.Words[w].Length.
        // Result: _charToWordIndex[charIndex] = wordIndex, or -1 if not part of a word.
        void BuildCharToWordMap()
        {
            TMP_TextInfo ti = tmpText.textInfo;
            int totalChars = ti.characterCount;

            _charToWordIndex = new int[totalChars];
            for (int i = 0; i < totalChars; i++)
                _charToWordIndex[i] = -1;   // default: not part of any word

            string[] words = data.Words;
            if (words.Length == 0) return;

            int wordIdx = 0;   // current position in data.Words[]
            int charsInWord = 0;   // how many visible chars we've consumed in current word

            for (int ci = 0; ci < totalChars; ci++)
            {
                TMP_CharacterInfo ch = ti.characterInfo[ci];

                // Non-visible chars (spaces, newlines, zero-width) — skip,
                // but only reset charsInWord if we've finished the current word.
                // This lets punctuation attached to a word stay inside it.
                if (!ch.isVisible)
                {
                    // If we were mid-word and hit a space, the word is done.
                    // Advance to next word if we've consumed at least 1 char.
                    if (charsInWord > 0)
                    {
                        wordIdx++;
                        charsInWord = 0;
                        if (wordIdx >= words.Length) break;
                    }
                    continue;
                }

                // Guard: don't overrun the words array
                if (wordIdx >= words.Length) break;

                // Assign this visible char to the current word
                _charToWordIndex[ci] = wordIdx;
                charsInWord++;

                // If we've consumed all chars in this word, move to the next.
                // words[wordIdx].Length is the source-of-truth character count.
                if (charsInWord >= words[wordIdx].Length)
                {
                    wordIdx++;
                    charsInWord = 0;
                    if (wordIdx >= words.Length) break;
                }
            }

            // Sanity check — mismatch usually means lyrics text ≠ tmpText.text
            if (wordIdx < words.Length)
                Debug.LogWarning(
                    $"[UniLyricPlayer] BuildCharToWordMap: only mapped {wordIdx}/{words.Length} words. " +
                    "Check that tmpText.text matches data.lyricsText exactly.");
        }

        // ── Vertex colour lerp ────────────────────────────────────────────────
        void ApplyVertexColors()
        {
            TMP_TextInfo textInfo = tmpText.textInfo;
            if (textInfo == null || textInfo.characterCount == 0) return;
            if (_currentColors == null || _charToWordIndex == null) return;

            // Framerate-independent lerp factor
            float s = Mathf.Clamp01(data.transitionSmoothness);
            float lerpT = 1f - Mathf.Pow(s, Time.deltaTime * 60f);

            bool colorsDirty = false;
            bool verticesDirty = false;

            // Restore base vertices before applying geometry offsets this frame
            if (data.effect != HighlightEffect.ColorOnly && _baseVerticesCached)
                RestoreBaseVertices(textInfo, ref verticesDirty);

            // ── Advance lerp targets for each word ────────────────────────────
            // We still need one colour per word — drive that from _currentWordIndex
            // exactly as before; only the per-character write loop changes.
            string[] words = data.Words;
            for (int w = 0; w < words.Length; w++)
            {
                Color target;
                if (_currentWordIndex == -2)
                    target = data.doneColor;
                else if (w == _currentWordIndex)
                    target = data.GetHighlightColorForWord(w);
                else if (w < _currentWordIndex)
                    target = data.doneColor;
                else
                    target = data.defaultColor;

                _currentColors[w] = Color.Lerp(_currentColors[w], target, lerpT);
            }

            // ── Write colours + geometry per character ────────────────────────
            int totalChars = textInfo.characterCount;
            for (int ci = 0; ci < totalChars; ci++)
            {
                TMP_CharacterInfo ch = textInfo.characterInfo[ci];
                if (!ch.isVisible) continue;

                int wIdx = (ci < _charToWordIndex.Length) ? _charToWordIndex[ci] : -1;
                if (wIdx < 0 || wIdx >= words.Length) continue;

                // Geometry effect on active word only
                if (data.effect != HighlightEffect.ColorOnly
                    && wIdx == _currentWordIndex
                    && _currentWordIndex >= 0
                    && _baseVerticesCached)
                {
                    ApplyGeometryEffect(textInfo, ci, ref verticesDirty);
                }

                // Write colour
                int mi = ch.materialReferenceIndex;
                int vi = ch.vertexIndex;
                Color32[] cols = textInfo.meshInfo[mi].colors32;
                if (vi + 3 >= cols.Length) continue;

                Color32 c32 = _currentColors[wIdx];
                cols[vi] = c32;
                cols[vi + 1] = c32;
                cols[vi + 2] = c32;
                cols[vi + 3] = c32;
                colorsDirty = true;
            }

            var flags = TMP_VertexDataUpdateFlags.None;
            if (colorsDirty) flags |= TMP_VertexDataUpdateFlags.Colors32;
            if (verticesDirty) flags |= TMP_VertexDataUpdateFlags.Vertices;
            if (flags != TMP_VertexDataUpdateFlags.None)
                tmpText.UpdateVertexData(flags);
        }

        // ── V1.1 geometry effect — now takes charIndex directly ───────────────
        void ApplyGeometryEffect(TMP_TextInfo textInfo, int ci, ref bool dirty)
        {
            TMP_CharacterInfo ch = textInfo.characterInfo[ci];
            if (!ch.isVisible) return;

            int mi = ch.materialReferenceIndex;
            int vi = ch.vertexIndex;
            Vector3[] v = textInfo.meshInfo[mi].vertices;
            if (vi + 3 >= v.Length) return;

            if (data.effect == HighlightEffect.ScaleAndColor)
            {
                Vector3 centre = (v[vi] + v[vi + 1] + v[vi + 2] + v[vi + 3]) * 0.25f;
                float t = (Mathf.Sin(Time.time * data.scaleSpeed) + 1f) * 0.5f;
                float scale = Mathf.Lerp(1f, data.scaleAmount, t);
                for (int k = 0; k < 4; k++)
                    v[vi + k] = centre + (v[vi + k] - centre) * scale;
                dirty = true;
            }
            else if (data.effect == HighlightEffect.WaveAndColor)
            {
                // localIdx = position of this char within its word
                int wIdx = _charToWordIndex[ci];
                int wordStart = FindWordStartCharIndex(textInfo, ci, wIdx);
                int localIdx = ci - wordStart;

                float offset = Mathf.Sin(
                    Time.time * data.waveSpeed + localIdx * data.waveSpread)
                    * data.waveAmplitude;
                for (int k = 0; k < 4; k++)
                    v[vi + k].y += offset;
                dirty = true;
            }
        }

        // Find the first characterInfo index that belongs to wordIndex wIdx,
        // used to compute a character's local position within its word for the wave phase.
        int FindWordStartCharIndex(TMP_TextInfo textInfo, int currentCi, int wIdx)
        {
            for (int ci = 0; ci < currentCi; ci++)
            {
                if (ci < _charToWordIndex.Length && _charToWordIndex[ci] == wIdx)
                    return ci;
            }
            return currentCi; // fallback: treat as first char of word
        }

        // ── Vertex cache ──────────────────────────────────────────────────────
        void CacheBaseVertices()
        {
            tmpText.ForceMeshUpdate();
            TMP_TextInfo ti = tmpText.textInfo;
            if (ti.meshInfo.Length == 0) { _baseVerticesCached = false; return; }

            Vector3[] src = ti.meshInfo[0].vertices;
            if (src == null) { _baseVerticesCached = false; return; }

            _baseVertices = new Vector3[src.Length];
            Array.Copy(src, _baseVertices, src.Length);
            _baseVerticesCached = true;
        }

        void RestoreBaseVertices(TMP_TextInfo ti, ref bool dirty)
        {
            if (!_baseVerticesCached || ti.meshInfo.Length == 0) return;
            Vector3[] dst = ti.meshInfo[0].vertices;
            if (dst == null) return;

            int count = Mathf.Min(dst.Length, _baseVertices.Length);
            Array.Copy(_baseVertices, dst, count);
            dirty = true;
        }

        void ResetAllColors()
        {
            if (tmpText == null || data == null) return;
            tmpText.ForceMeshUpdate();
            TMP_TextInfo ti = tmpText.textInfo;

            Color32 def = data.defaultColor;
            for (int mi = 0; mi < ti.materialCount; mi++)
            {
                Color32[] cols = ti.meshInfo[mi].colors32;
                for (int i = 0; i < cols.Length; i++) cols[i] = def;
            }
            tmpText.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);

            if (_currentColors != null)
                for (int i = 0; i < _currentColors.Length; i++)
                    _currentColors[i] = data.defaultColor;
        }
    }
}