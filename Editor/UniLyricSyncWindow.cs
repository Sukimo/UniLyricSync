// Editor-only. Will NOT be included in player builds.
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UniLyricSync.Editor
{
    /// <summary>
    /// Main Editor Window for UniLyricSync.
    /// Open via: Window → UniLyricSync
    ///
    /// Layout (top → bottom):
    ///   Toolbar          – Play / Stop / Load Audio / Set Lyrics / Clear All
    ///   Ruler            – time ticks
    ///   Audio Track      – waveform texture + playhead
    ///   Trigger Track    – yellow diamond markers; click to add, drag to move
    ///   Scene Preview    – TMP-style text display showing current highlight state
    ///   Inspector Strip  – details of selected marker
    /// </summary>
    public class UniLyricSyncWindow : EditorWindow
    {
        // ── Menu item ──────────────────────────────────────────────────────────
        [MenuItem("Window/UniLyricSync")]
        public static void Open()
        {
            var win = GetWindow<UniLyricSyncWindow>();
            win.titleContent = new GUIContent("UniLyricSync");
            win.minSize = new Vector2(600, 460);
        }

        // ── State ──────────────────────────────────────────────────────────────
        private UniLyricData _data;

        // Playback preview (Editor-side, uses AudioUtil reflection)
        private bool   _isPlaying     = false;
        private double _playStartTime = 0;
        private float  _playheadTime  = 0f;

        // Zoom & scroll
        private float _zoom       = 1f;    // 1 = fit full clip, 2 = 2x zoom, etc.
        private float _scrollX    = 0f;    // normalised 0..1 scroll offset
        private const float MinZoom = 1f;
        private const float MaxZoom = 20f;

        // Interaction
        private int  _selectedMarker          = -1;
        private int  _draggingMarker          = -1;
        private int  _draggingMarkerWordIndex = -1;
        private bool _isDragging              = false;
        private bool _isScrubbing             = false;

        // Layout rects (computed each OnGUI)
        private Rect _rulerRect;
        private Rect _audioTrackRect;
        private Rect _triggerTrackRect;
        private Rect _previewRect;
        private Rect _inspectorRect;

        // Track heights
        private const float RulerHeight = 22f;
        private const float AudioTrackHeight = 68f;
        private const float TriggerTrackHeight = 48f;
        private const float PreviewHeight = 70f;
        private const float InspectorHeight = 36f;
        private const float LabelWidth = 90f;

        // Marker visuals
        private const float MarkerWidth = 2f;
        private const float MarkerHitW = 12f; // click/drag hit area

        // GUIStyles (initialised lazily)
        private GUIStyle _trackLabelStyle;
        private GUIStyle _previewWordStyle;
        private GUIStyle _previewWordActiveStyle;
        private GUIStyle _previewWordDoneStyle;
        private bool _stylesInitialised = false;

        // Lyrics popup
        private bool _showLyricsPopup = false;
        private string _lyricsEditBuffer = "";
        private Rect _lyricsPopupRect;

        // ── Unity messages ─────────────────────────────────────────────────────
        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;

            // Try to restore the last used data asset
            string guid = EditorPrefs.GetString("UniLyricSync_LastDataGuid", "");
            if (!string.IsNullOrEmpty(guid))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    _data = AssetDatabase.LoadAssetAtPath<UniLyricData>(path);
            }
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            StopPreview();
        }

        private void OnEditorUpdate()
        {
            if (!_isPlaying) return;

            // Advance playhead using EditorApplication.timeSinceStartup delta
            double now = EditorApplication.timeSinceStartup;
            _playheadTime = (float)(now - _playStartTime);

            if (_data != null && _data.audioClip != null &&
                _playheadTime >= _data.audioClip.length)
            {
                StopPreview();
            }

            Repaint();
        }

        private void OnGUI()
        {
            InitStyles();

            // ── If no data assigned, show a simple welcome screen ──────────────
            if (_data == null)
            {
                DrawWelcome();
                return;
            }

            float y = 0f;
            float w = position.width;

            // ── Toolbar ────────────────────────────────────────────────────────
            DrawToolbar(new Rect(0, y, w, 34f));
            y += 34f;

            // Horizontal divider
            EditorGUI.DrawRect(new Rect(0, y, w, 0.5f), new Color(0, 0, 0, 0.4f));
            y += 1f;

            // ── Compute track rects ────────────────────────────────────────────
            float trackW = w - LabelWidth;

            _rulerRect = new Rect(LabelWidth, y, trackW, RulerHeight);
            y += RulerHeight;

            _audioTrackRect = new Rect(LabelWidth, y, trackW, AudioTrackHeight);
            y += AudioTrackHeight;

            _triggerTrackRect = new Rect(LabelWidth, y, trackW, TriggerTrackHeight);
            y += TriggerTrackHeight;

            _previewRect = new Rect(0, y, w, PreviewHeight);
            y += PreviewHeight;

            _inspectorRect = new Rect(0, y, w, InspectorHeight);

            // ── Draw sections ──────────────────────────────────────────────────
            DrawRuler();
            DrawAudioTrack(new Rect(0, _audioTrackRect.y, LabelWidth, AudioTrackHeight));
            DrawTriggerTrack(new Rect(0, _triggerTrackRect.y, LabelWidth, TriggerTrackHeight));
            DrawPreview();
            DrawInspectorStrip();

            // ── Lyrics popup (drawn on top) ────────────────────────────────────
            if (_showLyricsPopup)
                DrawLyricsPopup();

            // ── Scroll wheel → zoom, Alt+scroll → pan ─────────────────────────
            if (Event.current.type == EventType.ScrollWheel
                && (new Rect(LabelWidth, 0, position.width - LabelWidth, position.height))
                   .Contains(Event.current.mousePosition))
            {
                if (Event.current.alt)
                {
                    // Pan
                    _scrollX += Event.current.delta.y * 0.01f;
                    ClampScroll();
                }
                else
                {
                    // Zoom toward mouse — keep the time-under-mouse fixed
                    float tAtMouse = XToTime(Event.current.mousePosition.x);
                    _zoom = Mathf.Clamp(
                        _zoom * (Event.current.delta.y > 0 ? 0.85f : 1.18f),
                        MinZoom, MaxZoom);

                    // Recalculate scrollX so tAtMouse stays under the mouse
                    if (_data.audioClip != null && _data.audioClip.length > 0f
                        && _audioTrackRect.width > 0f)
                    {
                        float uvW      = 1f / _zoom;
                        float frac     = (Event.current.mousePosition.x - _audioTrackRect.x)
                                         / _audioTrackRect.width;
                        float normTime = tAtMouse / _data.audioClip.length;
                        // uvX = normTime - frac*uvW  →  scrollX = uvX / (1-uvW)
                        float uvX  = normTime - frac * uvW;
                        float denom = 1f - uvW;
                        _scrollX   = denom > 0.0001f ? uvX / denom : 0f;
                        ClampScroll();
                    }
                }
                Event.current.Use();
                Repaint();
            }

            // ── Process mouse events on trigger track ──────────────────────────
            HandleTriggerTrackEvents();
        }

        // ══════════════════════════════════════════════════════════════════════
        // WELCOME SCREEN
        // ══════════════════════════════════════════════════════════════════════
        private void DrawWelcome()
        {
            GUILayout.FlexibleSpace();

            GUILayout.BeginVertical();
            GUILayout.Space(20);

            GUILayout.Label("UniLyricSync", EditorStyles.boldLabel);
            GUILayout.Space(8);
            GUILayout.Label("No UniLyricData asset loaded.", EditorStyles.wordWrappedLabel);
            GUILayout.Space(12);

            _data = (UniLyricData)EditorGUILayout.ObjectField(
                "Lyric Data", _data, typeof(UniLyricData), false);

            GUILayout.Space(8);
            GUILayout.Label("— or —", EditorStyles.centeredGreyMiniLabel);
            GUILayout.Space(8);

            if (GUILayout.Button("Create New Lyric Data Asset"))
            {
                string path = EditorUtility.SaveFilePanelInProject(
                    "Create Lyric Data", "NewUniLyricData", "asset",
                    "Choose where to save the UniLyricData asset");

                if (!string.IsNullOrEmpty(path))
                {
                    var newData = CreateInstance<UniLyricData>();
                    AssetDatabase.CreateAsset(newData, path);
                    AssetDatabase.SaveAssets();
                    _data = newData;
                    SaveLastDataGuid();
                }
            }

            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
        }

        // ══════════════════════════════════════════════════════════════════════
        // TOOLBAR
        // ══════════════════════════════════════════════════════════════════════
        private void DrawToolbar(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.22f, 0.22f, 0.22f, 1f));

            GUILayout.BeginArea(rect);
            GUILayout.BeginHorizontal();
            GUILayout.Space(8);

            // ── Data field ────────────────────────────────────────────────────
            EditorGUI.BeginChangeCheck();
            _data = (UniLyricData)EditorGUILayout.ObjectField(
                _data, typeof(UniLyricData), false, GUILayout.Width(160));
            if (EditorGUI.EndChangeCheck())
            {
                SaveLastDataGuid();
                StopPreview();
            }

            GUILayout.Space(8);

            // ── Play / Stop ───────────────────────────────────────────────────
            GUI.enabled = _data != null && _data.audioClip != null;
            if (_isPlaying)
            {
                if (GUILayout.Button("■ Stop", GUILayout.Width(60)))
                    StopPreview();
            }
            else
            {
                if (GUILayout.Button("▶ Play", GUILayout.Width(60)))
                    StartPreview();
            }
            GUI.enabled = true;

            GUILayout.Space(4);

            // ── Set Lyrics ────────────────────────────────────────────────────
            if (GUILayout.Button("Set Lyrics", GUILayout.Width(80)))
            {
                _lyricsEditBuffer = _data.lyricsText;
                _showLyricsPopup = !_showLyricsPopup;
            }

            GUILayout.Space(4);

            // ── AudioClip quick assign ────────────────────────────────────────
            EditorGUI.BeginChangeCheck();
            AudioClip newClip = (AudioClip)EditorGUILayout.ObjectField(
                _data.audioClip, typeof(AudioClip), false, GUILayout.Width(160));
            if (EditorGUI.EndChangeCheck() && newClip != _data.audioClip)
            {
                Undo.RecordObject(_data, "Assign AudioClip");
                _data.audioClip = newClip;
                EditorUtility.SetDirty(_data);

                if (newClip != null)
                    UniLyricWaveformBaker.Bake(_data);

                StopPreview();
            }

            GUILayout.FlexibleSpace();

            // ── Zoom controls ─────────────────────────────────────────────────
            GUI.enabled = _data != null && _data.audioClip != null;
            if (GUILayout.Button("−", GUILayout.Width(22)))
            { _zoom = Mathf.Max(MinZoom, _zoom / 1.5f); ClampScroll(); }
            GUILayout.Label($"{_zoom:F1}×", EditorStyles.miniLabel, GUILayout.Width(32));
            if (GUILayout.Button("+", GUILayout.Width(22)))
            { _zoom = Mathf.Min(MaxZoom, _zoom * 1.5f); ClampScroll(); }
            if (GUILayout.Button("Fit", GUILayout.Width(28)))
            { _zoom = 1f; _scrollX = 0f; }
            GUI.enabled = true;

            GUILayout.Space(6);

            // ── End Marker ────────────────────────────────────────────────────
            GUI.enabled = _data != null && _data.audioClip != null;
            GUI.color   = new Color(0.6f, 0.85f, 1f);
            if (GUILayout.Button("⏹ End", GUILayout.Width(52)))
            {
                Undo.RecordObject(_data, "Set End Marker");
                _data.endMarkerTime = _playheadTime;
                EditorUtility.SetDirty(_data);
            }
            GUI.color   = Color.white;
            GUI.enabled = true;

            GUILayout.Space(6);

            // ── Time display ──────────────────────────────────────────────────
            float clipLen = _data.audioClip != null ? _data.audioClip.length : 0f;
            GUILayout.Label(
                $"{FormatTime(_playheadTime)} / {FormatTime(clipLen)}",
                EditorStyles.boldLabel, GUILayout.Width(110));

            GUILayout.Space(4);

            // ── Clear All ─────────────────────────────────────────────────────
            GUI.color = new Color(1f, 0.6f, 0.6f);
            if (GUILayout.Button("Clear All", GUILayout.Width(70)))
            {
                if (EditorUtility.DisplayDialog("Clear All Triggers",
                    "Remove all trigger markers?", "Clear", "Cancel"))
                {
                    Undo.RecordObject(_data, "Clear All Triggers");
                    _data.markers.Clear();
                    _data.endMarkerTime = 0f;
                    EditorUtility.SetDirty(_data);
                    _selectedMarker = -1;
                }
            }
            GUI.color = Color.white;

            GUILayout.Space(8);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ══════════════════════════════════════════════════════════════════════
        // RULER
        // ══════════════════════════════════════════════════════════════════════
        private void DrawRuler()
        {
            EditorGUI.DrawRect(new Rect(0, _rulerRect.y, position.width, RulerHeight),
                               new Color(0.15f, 0.15f, 0.15f, 1f));

            // Label column background
            EditorGUI.DrawRect(new Rect(0, _rulerRect.y, LabelWidth, RulerHeight),
                               new Color(0.18f, 0.18f, 0.18f, 1f));

            if (_data.audioClip == null) return;

            float duration = _data.audioClip.length;
            float tickStep = ChooseTickStep(duration);
            Color tickColor = new Color(0.55f, 0.55f, 0.55f, 1f);

            for (float t = 0; t <= duration + 0.001f; t += tickStep)
            {
                float x = TimeToX(t);
                if (x < _rulerRect.x) continue;

                // Tick line
                EditorGUI.DrawRect(new Rect(x, _rulerRect.y + _rulerRect.height * 0.55f,
                                            0.5f, _rulerRect.height * 0.45f), tickColor);

                // Label
                GUI.color = new Color(0.65f, 0.65f, 0.65f);
                GUI.Label(new Rect(x + 2, _rulerRect.y + 2, 48f, 14f),
                          FormatTime(t), EditorStyles.miniLabel);
                GUI.color = Color.white;
            }

            // Playhead tick on ruler
            DrawPlayheadLine(_rulerRect);
        }

        // ══════════════════════════════════════════════════════════════════════
        // AUDIO TRACK
        // ══════════════════════════════════════════════════════════════════════
        private void DrawAudioTrack(Rect labelRect)
        {
            // Background
            EditorGUI.DrawRect(new Rect(0, _audioTrackRect.y, position.width, AudioTrackHeight),
                               new Color(0.13f, 0.19f, 0.28f, 1f));

            // Label column
            EditorGUI.DrawRect(labelRect, new Color(0.16f, 0.16f, 0.16f, 1f));
            GUI.Label(new Rect(labelRect.x + 8, labelRect.y + 6, labelRect.width - 10, 16f),
                      "Audio", _trackLabelStyle);
            if (_data.audioClip != null)
                GUI.Label(new Rect(labelRect.x + 8, labelRect.y + 22, labelRect.width - 10, 14f),
                          _data.audioClip.name, EditorStyles.miniLabel);

            // Waveform texture — draw only the visible slice based on zoom/scroll
            if (_data.waveformTexture != null)
            {
                // UV rect: which portion of the texture to show
                float uvW     = 1f / _zoom;
                float uvX     = _scrollX * (1f - uvW);
                uvX = Mathf.Clamp(uvX, 0f, 1f - uvW);
                Rect uvRect   = new Rect(uvX, 0f, uvW, 1f);
                GUI.DrawTextureWithTexCoords(_audioTrackRect, _data.waveformTexture, uvRect);
            }
            else if (_data.audioClip != null)
            {
                GUI.color = new Color(1, 1, 1, 0.4f);
                GUI.Label(new Rect(_audioTrackRect.x + 12, _audioTrackRect.y + 24,
                                   300, 20f), "Baking waveform…");
                GUI.color = Color.white;
                UniLyricWaveformBaker.Bake(_data);
            }
            else
            {
                GUI.color = new Color(1, 1, 1, 0.25f);
                GUI.Label(new Rect(_audioTrackRect.x + 12, _audioTrackRect.y + 24,
                                   300, 20f), "← assign an AudioClip");
                GUI.color = Color.white;
            }

            // Clip name tag
            if (_data.audioClip != null)
            {
                GUI.color = new Color(0.35f, 0.65f, 0.95f, 1f);
                GUI.Label(new Rect(_audioTrackRect.x + 8, _audioTrackRect.y + 4,
                                   300, 16f),
                          $"{_data.audioClip.name}  ·  {_data.audioClip.length:F1}s",
                          EditorStyles.miniBoldLabel);
                GUI.color = Color.white;
            }

            DrawPlayheadLine(_audioTrackRect);
        }

        // ══════════════════════════════════════════════════════════════════════
        // TRIGGER TRACK
        // ══════════════════════════════════════════════════════════════════════
        private void DrawTriggerTrack(Rect labelRect)
        {
            // Background
            EditorGUI.DrawRect(new Rect(0, _triggerTrackRect.y, position.width, TriggerTrackHeight),
                               new Color(0.15f, 0.17f, 0.13f, 1f));

            // Label column
            EditorGUI.DrawRect(labelRect, new Color(0.16f, 0.16f, 0.16f, 1f));
            GUI.Label(new Rect(labelRect.x + 8, labelRect.y + 6, labelRect.width - 10, 16f),
                      "Triggers", _trackLabelStyle);
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            GUI.Label(new Rect(labelRect.x + 8, labelRect.y + 22, labelRect.width - 10, 14f),
                      $"{_data.markers.Count} markers", EditorStyles.miniLabel);
            GUI.color = Color.white;

            // Hint text when empty
            if (_data.markers.Count == 0 && _data.audioClip != null)
            {
                GUI.color = new Color(1, 1, 1, 0.3f);
                GUI.Label(new Rect(_triggerTrackRect.x + _triggerTrackRect.width * 0.5f - 80,
                                   _triggerTrackRect.y + 15, 160, 18f),
                          "click to add trigger", EditorStyles.centeredGreyMiniLabel);
                GUI.color = Color.white;
            }

            // Draw each marker
            for (int i = 0; i < _data.markers.Count; i++)
                DrawMarker(i);

            // Draw end marker line
            if (_data.endMarkerTime > 0f)
            {
                float ex = TimeToX(_data.endMarkerTime);
                EditorGUI.DrawRect(new Rect(ex - 1f, _triggerTrackRect.y,
                                            2f, TriggerTrackHeight),
                                   new Color(0.4f, 0.85f, 1f, 0.9f));
                GUI.color = new Color(0.4f, 0.85f, 1f);
                GUI.Label(new Rect(ex + 3f, _triggerTrackRect.y + 2f, 40f, 14f),
                          "END", EditorStyles.miniLabel);
                GUI.color = Color.white;
            }

            DrawPlayheadLine(_triggerTrackRect);

            // Border
            EditorGUI.DrawRect(new Rect(0, _triggerTrackRect.y + TriggerTrackHeight - 0.5f,
                                        position.width, 0.5f),
                               new Color(0, 0, 0, 0.5f));
        }

        private void DrawMarker(int index)
        {
            UniLyricMarker m = _data.markers[index];
            float x = TimeToX(m.time);

            bool selected = (index == _selectedMarker);

            // Vertical bar
            Color barCol = selected ? new Color(1f, 0.85f, 0.2f) : new Color(0.9f, 0.75f, 0.1f);
            EditorGUI.DrawRect(
                new Rect(x - MarkerWidth * 0.5f, _triggerTrackRect.y,
                         MarkerWidth, TriggerTrackHeight),
                barCol);

            // Diamond head at top
            float dSize = selected ? 7f : 5f;
            DrawDiamond(new Vector2(x, _triggerTrackRect.y + 4), dSize, barCol);

            // Word label tag
            if (!string.IsNullOrEmpty(m.word))
            {
                float tagW = m.word.Length * 7f + 10f;
                Rect tagRect = new Rect(x + 4, _triggerTrackRect.y + 10, tagW, 16f);

                EditorGUI.DrawRect(tagRect, selected
                    ? new Color(0.95f, 0.8f, 0.15f)
                    : new Color(0.7f, 0.6f, 0.1f));

                GUI.color = new Color(0.1f, 0.08f, 0f);
                GUI.Label(tagRect, m.word, EditorStyles.miniLabel);
                GUI.color = Color.white;
            }
        }

        private void HandleTriggerTrackEvents()
        {
            if (_data == null || _data.audioClip == null) return;

            Event e = Event.current;

            // ══ FIX 1 — Playhead scrub on ruler + audio track ═════════════════
            // Must be BEFORE triggerTrackRect guard. Supports click AND drag.
            bool inScrubZone = _rulerRect.Contains(e.mousePosition) ||
                               _audioTrackRect.Contains(e.mousePosition);

            if (e.type == EventType.MouseDown && e.button == 0 && inScrubZone)
            {
                _isScrubbing = true;
                _playheadTime = Mathf.Clamp(XToTime(e.mousePosition.x), 0f, _data.audioClip.length);
                if (_isPlaying)
                {
                    StopAllClips();
                    _playStartTime = EditorApplication.timeSinceStartup - _playheadTime;
                    PlayClipAtTime(_data.audioClip, _playheadTime);
                }
                Repaint();
                e.Use();
                return;
            }

            if (_isScrubbing)
            {
                if (e.type == EventType.MouseDrag)
                {
                    _playheadTime = Mathf.Clamp(XToTime(e.mousePosition.x), 0f, _data.audioClip.length);
                    if (_isPlaying)
                    {
                        StopAllClips();
                        _playStartTime = EditorApplication.timeSinceStartup - _playheadTime;
                        PlayClipAtTime(_data.audioClip, _playheadTime);
                    }
                    Repaint();
                    e.Use();
                    return;
                }
                if (e.type == EventType.MouseUp)
                {
                    _isScrubbing = false;
                    e.Use();
                    return;
                }
            }

            // ══ FIX 3 — Marker drag uses wordIndex, not list index ═════════════
            // After SortMarkers() the list index shifts — wordIndex never changes.
            // Also: sort only on MouseUp, NOT during drag, so other markers stay put.
            if (_isDragging && _draggingMarker >= 0)
            {
                if (e.type == EventType.MouseDrag)
                {
                    float newTime = Mathf.Clamp(XToTime(e.mousePosition.x), 0f, _data.audioClip.length);

                    // Find the marker by its stable wordIndex
                    int idx = _data.markers.FindIndex(m => m.wordIndex == _draggingMarkerWordIndex);
                    if (idx >= 0)
                    {
                        Undo.RecordObject(_data, "Move Trigger");
                        UniLyricMarker marker = _data.markers[idx];
                        marker.time = newTime;
                        _data.markers[idx] = marker;
                        // intentionally NOT calling SortMarkers() here
                        EditorUtility.SetDirty(_data);
                        Repaint();
                    }
                    e.Use();
                }

                if (e.type == EventType.MouseUp)
                {
                    _data.SortMarkers(); // sort once on release
                    EditorUtility.SetDirty(_data);
                    _isDragging              = false;
                    _draggingMarker          = -1;
                    _draggingMarkerWordIndex = -1;
                    Repaint();
                    e.Use();
                }
                return;
            }

            if (!_triggerTrackRect.Contains(e.mousePosition)) return;

            // ── Left click: select+drag or add new ────────────────────────────
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                int hit = HitTestMarker(e.mousePosition.x);
                if (hit >= 0)
                {
                    _selectedMarker          = hit;
                    _draggingMarker          = hit;
                    _draggingMarkerWordIndex = _data.markers[hit].wordIndex;
                    _isDragging              = true;
                }
                else
                {
                    AddMarkerAt(XToTime(e.mousePosition.x));
                }
                e.Use();
            }

            // ── Right click → delete ──────────────────────────────────────────
            if (e.type == EventType.MouseDown && e.button == 1)
            {
                int hit = HitTestMarker(e.mousePosition.x);
                if (hit >= 0)
                {
                    GenericMenu menu = new GenericMenu();
                    int capturedHit = hit;
                    menu.AddItem(new GUIContent("Delete Trigger"), false, () =>
                    {
                        Undo.RecordObject(_data, "Delete Trigger");
                        _data.markers.RemoveAt(capturedHit);
                        EditorUtility.SetDirty(_data);
                        if (_selectedMarker == capturedHit) _selectedMarker = -1;
                        Repaint();
                    });
                    menu.ShowAsContext();
                    e.Use();
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // SCENE PREVIEW
        // ══════════════════════════════════════════════════════════════════════
        private void DrawPreview()
        {
            EditorGUI.DrawRect(_previewRect, new Color(0.07f, 0.07f, 0.07f, 1f));
            EditorGUI.DrawRect(new Rect(0, _previewRect.y, position.width, 0.5f),
                               new Color(0, 0, 0, 0.6f));

            // Label
            GUI.color = new Color(0.5f, 0.5f, 0.5f);
            GUI.Label(new Rect(8, _previewRect.y + 4, 160, 14f),
                      "scene preview — TextMeshPro", EditorStyles.miniLabel);
            GUI.color = Color.white;

            if (string.IsNullOrWhiteSpace(_data.lyricsText)) return;

            string[] words = _data.Words;
            int activeIndex = GetActiveWordIndex();

            // Lay words out horizontally, word-wrap at window edge
            float startX = LabelWidth + 12f;
            float cx = startX;
            float cy = _previewRect.y + 20f;
            float wordH = 26f;
            float padding = 6f;

            for (int i = 0; i < words.Length; i++)
            {
                GUIStyle style;
                if (i == activeIndex) style = _previewWordActiveStyle;
                else if (i < activeIndex) style = _previewWordDoneStyle;
                else style = _previewWordStyle;

                float wordW = style.CalcSize(new GUIContent(words[i])).x + padding;

                // Wrap to next line if needed
                if (cx + wordW > position.width - 16f)
                {
                    cx = startX;
                    cy += wordH;
                    if (cy > _previewRect.yMax - wordH) break; // no more room
                }

                GUI.Label(new Rect(cx, cy, wordW, wordH), words[i], style);
                cx += wordW + 4f;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // INSPECTOR STRIP
        // ══════════════════════════════════════════════════════════════════════
        private void DrawInspectorStrip()
        {
            EditorGUI.DrawRect(_inspectorRect, new Color(0.17f, 0.17f, 0.17f, 1f));
            EditorGUI.DrawRect(new Rect(0, _inspectorRect.y, position.width, 0.5f),
                               new Color(0, 0, 0, 0.5f));

            float x = 8f;
            float y = _inspectorRect.y + 8f;

            if (_selectedMarker >= 0 && _selectedMarker < _data.markers.Count)
            {
                UniLyricMarker m = _data.markers[_selectedMarker];

                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                GUI.Label(new Rect(x, y, 60, 18f), "Selected:", EditorStyles.miniLabel);
                GUI.color = Color.white;
                x += 62f;

                GUI.Label(new Rect(x, y, 80, 18f),
                    $"\"{m.word}\" #{_selectedMarker}", EditorStyles.miniBoldLabel);
                x += 100f;

                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                GUI.Label(new Rect(x, y, 40, 18f), "Time:", EditorStyles.miniLabel);
                GUI.color = Color.white;
                x += 42f;

                EditorGUI.BeginChangeCheck();
                float newTime = EditorGUI.FloatField(
                    new Rect(x, y - 1, 55, 16f), m.time);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_data, "Edit Trigger Time");
                    m.time = Mathf.Clamp(newTime, 0f,
                        _data.audioClip != null ? _data.audioClip.length : 999f);
                    _data.markers[_selectedMarker] = m;
                    _data.SortMarkers();
                    EditorUtility.SetDirty(_data);
                }
                x += 65f;
            }
            else
            {
                GUI.color = new Color(0.45f, 0.45f, 0.45f);
                GUI.Label(new Rect(x, y, 300, 18f),
                    "Click a trigger to select · Click track to add · Right-click to delete",
                    EditorStyles.miniLabel);
                GUI.color = Color.white;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // LYRICS POPUP
        // ══════════════════════════════════════════════════════════════════════
        private void DrawLyricsPopup()
        {
            float pw = Mathf.Min(460f, position.width - 40f);
            float ph = 220f;
            float px = (position.width - pw) * 0.5f;
            float py = 40f;
            _lyricsPopupRect = new Rect(px, py, pw, ph);

            // Shadow
            EditorGUI.DrawRect(new Rect(px + 4, py + 4, pw, ph),
                               new Color(0, 0, 0, 0.4f));
            // Background
            EditorGUI.DrawRect(_lyricsPopupRect, new Color(0.2f, 0.2f, 0.2f, 1f));
            // Border
            DrawRectOutline(_lyricsPopupRect, new Color(0.4f, 0.4f, 0.4f), 1f);

            GUILayout.BeginArea(_lyricsPopupRect);

            GUILayout.Label("Edit Lyrics", EditorStyles.boldLabel);

            _lyricsEditBuffer = EditorGUILayout.TextArea(
                _lyricsEditBuffer, GUILayout.Height(120));

            string[] previewWords = string.IsNullOrWhiteSpace(_lyricsEditBuffer)
                ? System.Array.Empty<string>()
                : _lyricsEditBuffer.Split(
                    new char[] { ' ', '\t', '\n', '\r' },
                    System.StringSplitOptions.RemoveEmptyEntries);

            EditorGUILayout.LabelField($"{previewWords.Length} words detected",
                EditorStyles.centeredGreyMiniLabel);

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Apply"))
            {
                Undo.RecordObject(_data, "Set Lyrics");
                _data.lyricsText = _lyricsEditBuffer;
                EditorUtility.SetDirty(_data);
                _showLyricsPopup = false;
                Repaint();
            }

            if (GUILayout.Button("Cancel"))
                _showLyricsPopup = false;

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ══════════════════════════════════════════════════════════════════════
        // PLAYBACK PREVIEW
        // ══════════════════════════════════════════════════════════════════════
        private void StartPreview()
        {
            if (_data == null || _data.audioClip == null) return;

            _isPlaying     = true;
            _playStartTime = EditorApplication.timeSinceStartup - _playheadTime;

            // PlayPreviewClip does not support startSample in all Unity versions,
            // so we invoke it and immediately set the preview position via
            // SetPreviewClipSamplePosition if available, otherwise fall back.
            PlayClipAtTime(_data.audioClip, _playheadTime);
        }

        private void StopPreview()
        {
            _isPlaying    = false;
            _playheadTime = 0f;
            StopAllClips();
        }

        // Unity internal audio preview — works in Unity 6
        private static System.Reflection.MethodInfo _playClipMethod;
        private static System.Reflection.MethodInfo _stopClipsMethod;
        private static System.Reflection.MethodInfo _setSampleMethod;

        private static void PlayClipAtTime(AudioClip clip, float startTime)
        {
            if (_playClipMethod == null)
            {
                var asm = typeof(AudioImporter).Assembly;
                var t   = asm.GetType("UnityEditor.AudioUtil");
                // Signature: PlayPreviewClip(AudioClip, int startSample, bool loop)
                _playClipMethod = t?.GetMethod("PlayPreviewClip",
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Public,
                    null,
                    new System.Type[] { typeof(AudioClip), typeof(int), typeof(bool) },
                    null);
            }

            // Convert startTime to sample index
            int startSample = Mathf.RoundToInt(startTime * clip.frequency);
            startSample = Mathf.Clamp(startSample, 0, clip.samples - 1);

            _playClipMethod?.Invoke(null, new object[] { clip, startSample, false });
        }

        private static void StopAllClips()
        {
            if (_stopClipsMethod == null)
            {
                var asm = typeof(AudioImporter).Assembly;
                var t   = asm.GetType("UnityEditor.AudioUtil");
                _stopClipsMethod = t?.GetMethod("StopAllPreviewClips",
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Public);
            }
            _stopClipsMethod?.Invoke(null, null);
        }

        // ══════════════════════════════════════════════════════════════════════
        // HELPERS
        // ══════════════════════════════════════════════════════════════════════
        private void AddMarkerAt(float time)
        {
            string[] words = _data.Words;
            int nextWordIdx = _data.markers.Count; // auto-link to next word in sequence

            if (nextWordIdx >= words.Length)
            {
                Debug.LogWarning("[UniLyricSync] More triggers than words. " +
                    "Add more words in 'Set Lyrics' first.");
                return;
            }

            Undo.RecordObject(_data, "Add Trigger");

            _data.markers.Add(new UniLyricMarker
            {
                time = time,
                wordIndex = nextWordIdx,
                word = words[nextWordIdx]
            });

            _data.SortMarkers();
            _selectedMarker = _data.markers.FindIndex(m => m.wordIndex == nextWordIdx);
            EditorUtility.SetDirty(_data);
        }

        private int HitTestMarker(float mouseX)
        {
            for (int i = 0; i < _data.markers.Count; i++)
            {
                float mx = TimeToX(_data.markers[i].time);
                if (Mathf.Abs(mouseX - mx) <= MarkerHitW * 0.5f)
                    return i;
            }
            return -1;
        }

        private int GetActiveWordIndex()
        {
            if (_data.markers == null || _data.markers.Count == 0) return -1;

            int active = -1;
            for (int i = 0; i < _data.markers.Count; i++)
            {
                if (_data.markers[i].time <= _playheadTime)
                    active = _data.markers[i].wordIndex;
                else break;
            }
            return active;
        }

        // ── Coordinate helpers (zoom-aware) ───────────────────────────────────
        // Must match the UV rect used in DrawTextureWithTexCoords exactly:
        //   uvW  = 1 / zoom
        //   uvX  = scrollX * (1 - uvW)   <- visible start in normalised time
        // So: visibleStart = uvX, visibleEnd = uvX + uvW
        // pixel x maps to:  normTime = uvX + (x - trackLeft)/trackWidth * uvW

        private float TimeToX(float t)
        {
            if (_data?.audioClip == null) return _audioTrackRect.x;
            float uvW  = 1f / _zoom;
            float uvX  = Mathf.Clamp(_scrollX * (1f - uvW), 0f, 1f - uvW);
            float norm = t / _data.audioClip.length;         // 0..1
            float frac = (norm - uvX) / uvW;                 // 0..1 within visible window
            return _audioTrackRect.x + frac * _audioTrackRect.width;
        }

        private float XToTime(float x)
        {
            if (_data?.audioClip == null) return 0f;
            float uvW  = 1f / _zoom;
            float uvX  = Mathf.Clamp(_scrollX * (1f - uvW), 0f, 1f - uvW);
            float frac = (x - _audioTrackRect.x) / _audioTrackRect.width;  // 0..1 on screen
            float norm = uvX + frac * uvW;                   // 0..1 in clip
            return Mathf.Clamp(norm * _data.audioClip.length, 0f, _data.audioClip.length);
        }

        private void ClampScroll()
        {
            _scrollX = Mathf.Clamp01(_scrollX);
        }

        private void DrawPlayheadLine(Rect trackRect)
        {
            if (_data.audioClip == null) return;
            float x = TimeToX(_playheadTime);
            EditorGUI.DrawRect(new Rect(x - 0.75f, trackRect.y, 1.5f, trackRect.height),
                               new Color(1f, 0.4f, 0.15f, 0.9f));
        }

        private void DrawDiamond(Vector2 centre, float halfSize, Color col)
        {
            // Draw a diamond using two overlapping rects rotated 45° (approximated with 4 rects)
            EditorGUI.DrawRect(new Rect(centre.x - halfSize, centre.y - 1,
                                        halfSize * 2, 2), col);
            EditorGUI.DrawRect(new Rect(centre.x - 1, centre.y - halfSize,
                                        2, halfSize * 2), col);
        }

        private static float ChooseTickStep(float duration)
        {
            if (duration <= 5f) return 0.5f;
            if (duration <= 20f) return 1f;
            if (duration <= 60f) return 5f;
            return 10f;
        }

        private static string FormatTime(float t)
        {
            int m = Mathf.FloorToInt(t / 60f);
            float s = t - m * 60f;
            return $"{m}:{s:00.00}";
        }

        private void DrawRectOutline(Rect r, Color col, float thickness)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, thickness), col);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - thickness, r.width, thickness), col);
            EditorGUI.DrawRect(new Rect(r.x, r.y, thickness, r.height), col);
            EditorGUI.DrawRect(new Rect(r.xMax - thickness, r.y, thickness, r.height), col);
        }

        private void SaveLastDataGuid()
        {
            if (_data == null) return;
            string path = AssetDatabase.GetAssetPath(_data);
            string guid = AssetDatabase.AssetPathToGUID(path);
            EditorPrefs.SetString("UniLyricSync_LastDataGuid", guid);
        }

        // ── Style initialisation ───────────────────────────────────────────────
        private void InitStyles()
        {
            if (_stylesInitialised) return;
            _stylesInitialised = true;

            _trackLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.75f, 0.75f, 0.75f) },
                fontStyle = FontStyle.Bold,
                fontSize = 10
            };

            _previewWordStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                normal = { textColor = new Color(0.55f, 0.55f, 0.55f) },
                alignment = TextAnchor.MiddleLeft
            };

            _previewWordActiveStyle = new GUIStyle(_previewWordStyle)
            {
                normal = { textColor = Color.white },
                fontSize = 20
            };

            _previewWordDoneStyle = new GUIStyle(_previewWordStyle)
            {
                normal = { textColor = new Color(0.35f, 0.35f, 0.35f) }
            };
        }
    }
}