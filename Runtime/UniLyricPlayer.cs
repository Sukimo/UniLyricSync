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

        // Vertex cache for geometry effects
        // Key insight: we cache the mesh vertices ONCE after ForceMeshUpdate,
        // then restore them each frame before applying offsets.
        // Without restore, offsets accumulate and blow up the mesh.
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

                // Stop audio at end marker position
                _audioSource.Stop();

                // Fire complete event + handle loop
                _finished = true;
                OnPlaybackComplete?.Invoke();
                if (loop)
                {
                    Play();
                    return;
                }
                // Still apply colours this frame so the fade begins
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

            // Cache base vertices for geometry effects
            CacheBaseVertices();

            InitColorArrays();
            _currentWordIndex = -1;
            _nextMarkerIndex = 0;
            _finished = false;
            _endMarkerTriggered = false;

            _audioSource.clip = data.audioClip;
            _audioSource.loop = false;   // loop handled in Update
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

        // ── Vertex colour lerp ────────────────────────────────────────────────
        void ApplyVertexColors()
        {
            TMP_TextInfo textInfo = tmpText.textInfo;
            if (textInfo == null || textInfo.wordCount == 0) return;
            if (_currentColors == null) return;

            // Framerate-independent lerp:
            //   lerpT = 1 - smoothness^deltaTime
            //   smoothness = 0   → instant (lerpT = 1)
            //   smoothness = 0.9 → slow    (lerpT ≈ 0.06 @ 60fps)
            float s = Mathf.Clamp01(data.transitionSmoothness);
            float lerpT = 1f - Mathf.Pow(s, Time.deltaTime * 60f); //*

            bool colorsDirty = false;
            bool verticesDirty = false;

            // Restore base vertices before applying any effect offsets this frame
            if (data.effect != HighlightEffect.ColorOnly && _baseVerticesCached)
                RestoreBaseVertices(textInfo, ref verticesDirty);

            for (int w = 0; w < textInfo.wordCount && w < data.Words.Length; w++)
            {
                Color target;

                if (_currentWordIndex == -2)
                    // end marker triggered — everything fades to done
                    target = data.doneColor;
                else if (w == _currentWordIndex)
                    target = data.GetHighlightColorForWord(w);
                else if (w < _currentWordIndex)
                    target = data.doneColor;
                else
                    target = data.defaultColor;

                _currentColors[w] = Color.Lerp(_currentColors[w], target, lerpT);

                // Geometry effect on active word only
                if (data.effect != HighlightEffect.ColorOnly
                    && w == _currentWordIndex
                    && _currentWordIndex >= 0
                    && _baseVerticesCached)
                {
                    ApplyGeometryEffect(textInfo, w, ref verticesDirty);
                }

                // Write colour
                TMP_WordInfo wordInfo = textInfo.wordInfo[w];
                Color32 c32 = _currentColors[w];

                for (int ci = wordInfo.firstCharacterIndex;
                     ci < wordInfo.firstCharacterIndex + wordInfo.characterCount; ci++)
                {
                    var ch = textInfo.characterInfo[ci];
                    if (!ch.isVisible) continue;

                    int mi = ch.materialReferenceIndex;
                    int vi = ch.vertexIndex;
                    Color32[] cols = textInfo.meshInfo[mi].colors32;
                    if (vi + 3 >= cols.Length) continue;

                    cols[vi] = c32;
                    cols[vi + 1] = c32;
                    cols[vi + 2] = c32;
                    cols[vi + 3] = c32;
                    colorsDirty = true;
                }
            }

            var flags = TMP_VertexDataUpdateFlags.None;
            if (colorsDirty) flags |= TMP_VertexDataUpdateFlags.Colors32;
            if (verticesDirty) flags |= TMP_VertexDataUpdateFlags.Vertices;
            if (flags != TMP_VertexDataUpdateFlags.None)
                tmpText.UpdateVertexData(flags);
        }

        void ApplyGeometryEffect(TMP_TextInfo textInfo, int wordIndex, ref bool dirty)
        {
            TMP_WordInfo wordInfo = textInfo.wordInfo[wordIndex];

            for (int ci = wordInfo.firstCharacterIndex;
                 ci < wordInfo.firstCharacterIndex + wordInfo.characterCount; ci++)
            {
                var ch = textInfo.characterInfo[ci];
                if (!ch.isVisible) continue;

                int mi = ch.materialReferenceIndex;
                int vi = ch.vertexIndex;
                Vector3[] v = textInfo.meshInfo[mi].vertices;
                if (vi + 3 >= v.Length) continue;

                if (data.effect == HighlightEffect.ScaleAndColor)
                {
                    // scale from character centre
                    Vector3 centre = (v[vi] + v[vi + 1] + v[vi + 2] + v[vi + 3]) * 0.25f;
                    // sin oscillation: stays above 1.0, range = [1.0 .. scaleAmount]
                    float t = (Mathf.Sin(Time.time * data.scaleSpeed) + 1f) * 0.5f;
                    float scale = Mathf.Lerp(1f, data.scaleAmount, t);
                    for (int k = 0; k < 4; k++)
                        v[vi + k] = centre + (v[vi + k] - centre) * scale;
                    dirty = true;
                }
                else if (data.effect == HighlightEffect.WaveAndColor)
                {
                    int localIdx = ci - wordInfo.firstCharacterIndex;
                    float offset = Mathf.Sin(
                        Time.time * data.waveSpeed + localIdx * data.waveSpread)
                        * data.waveAmplitude;
                    for (int k = 0; k < 4; k++)
                        v[vi + k].y += offset;
                    dirty = true;
                }
            }
        }

        // ── Vertex cache ──────────────────────────────────────────────────────
        void CacheBaseVertices()
        {
            tmpText.ForceMeshUpdate();
            TMP_TextInfo ti = tmpText.textInfo;
            if (ti.meshInfo.Length == 0) { _baseVerticesCached = false; return; }

            // Only need mesh[0] — most text uses one material
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
