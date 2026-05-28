using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UniLyricSync;

namespace UniLyricSync.Demo
{
    [RequireComponent(typeof(UIDocument))]
    public class UniLyricUIController : MonoBehaviour
    {
        [Header("Player")]
        public UniLyricPlayer player;

        [Header("Catalog")]
        public List<UniLyricData> catalogEntries = new List<UniLyricData>();

        // ── UI elements ───────────────────────────────────────────────────────
        Button        _btnPlay, _btnPause, _btnStop;
        Toggle        _toggleLoop;
        DropdownField _dropEffect;
        Slider        _sliderTransition;
        Label         _labelTransition;
        VisualElement _groupScale;
        Slider        _sliderScaleAmount, _sliderScaleSpeed;
        Label         _labelScaleAmount,  _labelScaleSpeed;
        VisualElement _groupWave;
        Slider        _sliderWaveAmp, _sliderWaveSpeed, _sliderWaveSpread;
        Label         _labelWaveAmp,  _labelWaveSpeed,  _labelWaveSpread;
        ListView      _catalogList;

        int  _selectedIndex   = -1;
        bool _ignoreCallbacks = false;

        // Clone cache — one clone per asset, created on first selection
        readonly Dictionary<UniLyricData, UniLyricData> _cloneCache
            = new Dictionary<UniLyricData, UniLyricData>();

        // ══════════════════════════════════════════════════════════════════════
        void OnEnable()
        {
            // Use cached clone — original asset never touched
            if (player != null && player.data != null)
                player.data = GetOrClone(player.data);

            var root = GetComponent<UIDocument>().rootVisualElement;
            Bind(root);
            RegisterCallbacks();
            BuildCatalog();
            SyncUIToData();
        }

        void OnDisable()
        {
            // Destroy all clones when scene/object disabled — clean up memory
            foreach (var clone in _cloneCache.Values)
                if (clone != null) Destroy(clone);
            _cloneCache.Clear();
        }

        // ══════════════════════════════════════════════════════════════════════
        // BIND
        // ══════════════════════════════════════════════════════════════════════
        void Bind(VisualElement root)
        {
            _btnPlay    = root.Q<Button>("btn-play");
            _btnPause   = root.Q<Button>("btn-pause");
            _btnStop    = root.Q<Button>("btn-stop");
            _toggleLoop = root.Q<Toggle>("toggle-loop");

            _dropEffect        = root.Q<DropdownField>("drop-effect");
            _sliderTransition  = root.Q<Slider>("slider-transition");
            _labelTransition   = root.Q<Label>("label-transition");

            _groupScale        = root.Q<VisualElement>("group-scale");
            _sliderScaleAmount = root.Q<Slider>("slider-scale-amount");
            _labelScaleAmount  = root.Q<Label>("label-scale-amount");
            _sliderScaleSpeed  = root.Q<Slider>("slider-scale-speed");
            _labelScaleSpeed   = root.Q<Label>("label-scale-speed");

            _groupWave         = root.Q<VisualElement>("group-wave");
            _sliderWaveAmp     = root.Q<Slider>("slider-wave-amp");
            _labelWaveAmp      = root.Q<Label>("label-wave-amp");
            _sliderWaveSpeed   = root.Q<Slider>("slider-wave-speed");
            _labelWaveSpeed    = root.Q<Label>("label-wave-speed");
            _sliderWaveSpread  = root.Q<Slider>("slider-wave-spread");
            _labelWaveSpread   = root.Q<Label>("label-wave-spread");

            _catalogList = root.Q<ListView>("catalog-list");
        }

        // ══════════════════════════════════════════════════════════════════════
        // CALLBACKS
        // ══════════════════════════════════════════════════════════════════════
        void RegisterCallbacks()
        {
            _btnPlay ?.RegisterCallback<ClickEvent>(_ => player?.Play());
            _btnPause?.RegisterCallback<ClickEvent>(_ => player?.Pause());
            _btnStop ?.RegisterCallback<ClickEvent>(_ => player?.Stop());

            _toggleLoop?.RegisterValueChangedCallback(e =>
            {
                if (player != null) player.loop = e.newValue;
            });

            if (_dropEffect != null)
            {
                _dropEffect.choices = new List<string>
                    { "Color Only", "Scale + Color", "Wave + Color" };

                _dropEffect.RegisterValueChangedCallback(e =>
                {
                    if (_ignoreCallbacks || player?.data == null) return;
                    int idx = _dropEffect.choices.IndexOf(e.newValue);
                    if (idx < 0) return;
                    player.data.effect = (HighlightEffect)idx;
                    SetGroupVisibility(player.data.effect);
                });
            }

            Bind(_sliderTransition,  _labelTransition,
                v => { if (player?.data != null) player.data.transitionSmoothness = v; },
                v => v.ToString("F2"));

            Bind(_sliderScaleAmount, _labelScaleAmount,
                v => { if (player?.data != null) player.data.scaleAmount = v; },
                v => $"{v:F2}x");

            Bind(_sliderScaleSpeed, _labelScaleSpeed,
                v => { if (player?.data != null) player.data.scaleSpeed = v; },
                v => $"{v:F1}");

            Bind(_sliderWaveAmp, _labelWaveAmp,
                v => { if (player?.data != null) player.data.waveAmplitude = v; },
                v => $"{v:F1}px");

            Bind(_sliderWaveSpeed, _labelWaveSpeed,
                v => { if (player?.data != null) player.data.waveSpeed = v; },
                v => $"{v:F1}");

            Bind(_sliderWaveSpread, _labelWaveSpread,
                v => { if (player?.data != null) player.data.waveSpread = v; },
                v => $"{v:F2}");
        }

        void Bind(Slider slider, Label label,
                  System.Action<float> onChange,
                  System.Func<float, string> fmt)
        {
            if (slider == null) return;
            slider.RegisterValueChangedCallback(e =>
            {
                if (_ignoreCallbacks) return;
                onChange(e.newValue);
                if (label != null) label.text = fmt(e.newValue);
            });
        }

        // ══════════════════════════════════════════════════════════════════════
        // CATALOG
        // ══════════════════════════════════════════════════════════════════════
        void BuildCatalog()
        {
            if (_catalogList == null) return;

            _catalogList.makeItem = () =>
            {
                var row = new VisualElement();
                row.style.flexDirection     = FlexDirection.Row;
                row.style.alignItems        = Align.Center;
                row.style.paddingLeft       = 6;
                row.style.paddingRight      = 6;
                row.style.height            = 56;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = new Color(0.18f, 0.18f, 0.18f);

                var nameLabel = new Label { name = "row-name" };
                nameLabel.style.flexGrow                = 1;
                nameLabel.style.fontSize                = 15;
                nameLabel.style.color                   = new Color(0.85f, 0.85f, 0.85f);
                nameLabel.style.overflow                = Overflow.Hidden;

                var metaCol = new VisualElement();
                metaCol.style.flexDirection = FlexDirection.Column;
                metaCol.style.alignItems    = Align.FlexEnd;
                metaCol.style.width         = 52;

                var durLabel = new Label { name = "row-dur" };
                durLabel.style.fontSize = 13;
                durLabel.style.color    = new Color(0.6f, 0.6f, 0.6f);

                var wordsLabel = new Label { name = "row-words" };
                wordsLabel.style.fontSize = 12;
                wordsLabel.style.color    = new Color(0.4f, 0.4f, 0.4f);

                metaCol.Add(durLabel);
                metaCol.Add(wordsLabel);
                row.Add(nameLabel);
                row.Add(metaCol);
                return row;
            };

            _catalogList.bindItem = (element, index) =>
            {
                if (index >= catalogEntries.Count) return;
                var entry = catalogEntries[index];

                element.Q<Label>("row-name").text  = entry != null ? entry.name : "—";
                element.Q<Label>("row-dur").text   = entry?.audioClip != null
                    ? FormatDuration(entry.audioClip.length) : "--:--";
                element.Q<Label>("row-words").text = entry != null
                    ? $"{entry.Words.Length} words" : "";

                element.style.backgroundColor = index == _selectedIndex
                    ? new Color(0.22f, 0.40f, 0.70f)
                    : new Color(0.13f, 0.13f, 0.13f);
            };

            _catalogList.itemsSource    = catalogEntries;
            _catalogList.fixedItemHeight = 56;
            _catalogList.selectionType  = SelectionType.Single;

            // Unity 6: use selectedIndicesChanged instead of selectionChanged
            _catalogList.selectedIndicesChanged += OnSelectionChanged;
        }

        void OnSelectionChanged(IEnumerable<int> indices)
        {
            _selectedIndex = _catalogList.selectedIndex;
            if (_selectedIndex < 0 || _selectedIndex >= catalogEntries.Count) return;

            var entry = catalogEntries[_selectedIndex];
            if (entry == null || player == null) return;

            player.Stop();
            player.data = GetOrClone(entry);  // reuse existing clone if available
            SyncUIToData();
            _catalogList.RefreshItems();
        }

        // ── Clone cache helper ────────────────────────────────────────────────
        UniLyricData GetOrClone(UniLyricData original)
        {
            if (_cloneCache.TryGetValue(original, out var existing))
                return existing;

            var clone = Instantiate(original);
            clone.name = original.name + " (Runtime)";
            _cloneCache[original] = clone;
            return clone;
        }

        // ══════════════════════════════════════════════════════════════════════
        // SYNC UI ← DATA
        // ══════════════════════════════════════════════════════════════════════
        void SyncUIToData()
        {
            if (player?.data == null) return;
            var d = player.data;

            _ignoreCallbacks = true;

            if (_dropEffect != null)
            {
                int idx = (int)d.effect;
                if (idx >= 0 && idx < _dropEffect.choices.Count)
                    _dropEffect.value = _dropEffect.choices[idx];
            }

            Set(_sliderTransition,  _labelTransition,  d.transitionSmoothness, v => v.ToString("F2"));
            Set(_sliderScaleAmount, _labelScaleAmount, d.scaleAmount,           v => $"{v:F2}x");
            Set(_sliderScaleSpeed,  _labelScaleSpeed,  d.scaleSpeed,            v => $"{v:F1}");
            Set(_sliderWaveAmp,     _labelWaveAmp,     d.waveAmplitude,         v => $"{v:F1}px");
            Set(_sliderWaveSpeed,   _labelWaveSpeed,   d.waveSpeed,             v => $"{v:F1}");
            Set(_sliderWaveSpread,  _labelWaveSpread,  d.waveSpread,            v => $"{v:F2}");

            if (_toggleLoop != null) _toggleLoop.value = player.loop;

            SetGroupVisibility(d.effect);

            _ignoreCallbacks = false;
        }

        void Set(Slider s, Label l, float value, System.Func<float, string> fmt)
        {
            if (s != null) s.value = value;
            if (l != null) l.text  = fmt(value);
        }

        void SetGroupVisibility(HighlightEffect effect)
        {
            if (_groupScale != null)
                _groupScale.style.display = effect == HighlightEffect.ScaleAndColor
                    ? DisplayStyle.Flex : DisplayStyle.None;

            if (_groupWave != null)
                _groupWave.style.display = effect == HighlightEffect.WaveAndColor
                    ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ── Public API ────────────────────────────────────────────────────────
        public void LoadEntry(int index)
        {
            if (_catalogList == null) return;
            _catalogList.selectedIndex = index;
        }

        public void RefreshCatalog() => _catalogList?.RefreshItems();

        // ── Helper ────────────────────────────────────────────────────────────
        static string FormatDuration(float s)
        {
            int m   = Mathf.FloorToInt(s / 60f);
            int sec = Mathf.FloorToInt(s % 60f);
            return $"{m}:{sec:00}";
        }
    }
}
