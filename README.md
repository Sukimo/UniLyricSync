# UniLyricSync
**Unity 6.0+ · TextMeshPro · Editor Tool**

Word-by-word lyric highlighting synced to an AudioClip.
Author timing markers on a Timeline-inspired Editor Window, play back smooth
per-character colour transitions at runtime using TMP vertex colour animation.

> Demo project → [UniLyricSync-demo](https://github.com/Sukimo/UniLyricSync-demo)

---

## Install

**Option A — .unitypackage**
1. Download `UniLyricSync_v1.0.0.unitypackage` from [Releases](https://github.com/Sukimo/UniLyricSync/releases)
2. Double-click → Import All
3. If prompted: `Window → TextMeshPro → Import TMP Essential Resources`

**Option B — UPM git URL**
```
Window → Package Manager → + → Add package from git URL
https://github.com/yourname/UniLyricSync.git
```

**Option C — Drop folder**
Copy `Assets/UniLyricSync/` into your project's `Assets/` folder directly

---

## Quick Start

1. Right-click in Project → **Create → UniLyricSync → Lyric Data**
2. Open **Window → UniLyricSync**
3. Drag your AudioClip into the Audio field — waveform bakes automatically
4. Click **Set Lyrics** → paste your lyrics → Apply
5. Click on the **Trigger Track** to place markers — auto-links words in order
6. Drag markers left/right to adjust timing
7. Click **Ruler** or **Waveform** to scrub the playhead
8. Add **UniLyricPlayer** component to any GameObject in your scene
9. Assign `UniLyricData` asset and `TextMeshProUGUI` reference in the Inspector
10. Press Play

---

## Editor Window

| Control | Description |
|---------|-------------|
| ▶ Play / ⏹ Stop | Preview audio in Editor from playhead position |
| Set Lyrics | Open lyrics text editor |
| ⏹ End | Place end marker at current playhead — fades last word to done color |
| − / + / Fit | Zoom waveform in/out |
| Scroll wheel | Zoom toward mouse · Alt + scroll to pan |
| Click Trigger Track | Add new marker — auto-links to next word |
| Drag marker | Move timing — sort on release |
| Right-click marker | Delete |
| Clear All | Remove all markers |

---

## Audio Formats

| Format | Works | Note |
|--------|-------|------|
| .wav / .aif | ✅ Always | Recommended |
| .mp3 / .ogg / .flac | ⚠️ Setting needed | Set Load Type → Decompress On Load before baking waveform. Can switch back after. |
| Streaming | ❌ Never | GetData() returns zeros |

---

## UniLyricData — Inspector Fields

**Colours**
| Field | Description |
|-------|-------------|
| Highlight Color | Active word colour |
| Default Color | Words not yet reached |
| Done Color | Words already passed |

**Transition**
| Field | Range | Description |
|-------|-------|-------------|
| Transition Smoothness | 0 – 1 | 0 = instant snap · higher = slower lerp |

**Effect**
| Field | Description |
|-------|-------------|
| Effect | ColorOnly / ScaleAndColor / WaveAndColor |
| Scale Amount | 1.0 – 2.0 — how large the active word grows |
| Scale Speed | 1 – 30 — pulse cycles per second |
| Wave Amplitude | 0.5 – 20px — vertical bounce height |
| Wave Speed | 1 – 30 — wave cycles per second |
| Wave Spread | 0 – 3 — phase offset between characters |

**End Marker**
| Field | Description |
|-------|-------------|
| End Marker Time | When the last word fades to Done Color and audio stops. Set via ⏹ End button in the Editor Window. 0 = disabled |

---

## Per-word Override Color

Select a marker in the Trigger Track → Inspector strip → enable **Use Override Color** → pick a color

That word will use the override color instead of the global Highlight Color

---

## Runtime API

```csharp
UniLyricPlayer player = GetComponent<UniLyricPlayer>();

// Playback
player.Play();
player.Stop();
player.Pause();
player.SeekTo(2.5f);

// State
bool  playing  = player.IsPlaying;
int   index    = player.CurrentWordIndex;
float time     = player.CurrentTime;

// Events
player.OnWordHighlighted  += (index, word) => Debug.Log($"{index}: {word}");
player.OnPlaybackComplete += () => Debug.Log("Done");

// Disable built-in effect to drive TMP yourself
player.useBuiltInEffect = false;
player.OnWordHighlighted += (index, word) =>
{
    // your custom TMP vertex colour code here
};
```

---

## UniLyricPlayer — Inspector Fields

| Field | Description |
|-------|-------------|
| Data | UniLyricData asset |
| Tmp Text | TextMeshPro or TextMeshProUGUI component |
| Play On Start | Auto-play when scene starts |
| Loop | Loop playback — respects End Marker if set |
| Use Built In Effect | Disable to drive TMP vertex colours yourself |

---

## TMP Setup

```
TextMeshProUGUI Inspector:
  Override Color Tags  →  must be OFF (false)
  Overflow             →  Overflow or Truncate (not Linked)
```

---

## File Structure

```
Assets/UniLyricSync/
├── Editor/
│   ├── UniLyricSyncWindow.cs       ← EditorWindow — IMGUI timeline
│   ├── UniLyricWaveformBaker.cs    ← Bakes AudioClip → Texture2D
│   └── UniLyricSync.Editor.asmdef  ← Editor only, excluded from builds
├── Runtime/
│   ├── UniLyricData.cs             ← ScriptableObject save file
│   ├── UniLyricMarker.cs           ← Serializable timing marker struct
│   ├── UniLyricPlayer.cs           ← MonoBehaviour runtime driver
│   └── UniLyricSync.Runtime.asmdef
├── package.json
└── README.md
```

---

## Compatibility

| | |
|---|---|
| Unity | 6.0.0+ |
| TextMeshPro | 3.0.6+ (via com.unity.ugui) |
| Render Pipeline | Any — URP, HDRP, Built-in |
| Platform | All platforms |
| Scripting Backend | Mono and IL2CPP |

---

## Troubleshooting

| Problem | Cause | Fix |
|---------|-------|-----|
| Waveform shows red X | Load Type = Streaming | Change to Decompress On Load |
| Words highlight wrong order | Marker count ≠ word count | Check Set Lyrics word count matches marker count |
| Colour not changing in game | Override Color Tags is ON | Disable on TMP component |
| No sound in Editor preview | — | Check system audio is not muted |
| Play after clip ends does nothing | — | Press Stop first then Play |

---

MIT License · Unity 6.0+
