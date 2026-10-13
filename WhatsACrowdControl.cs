using System;
using System.Collections.Generic;
using System.IO;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.Shared.Enums;
using SharpDX;
using Color = SharpDX.Color;
using RectangleF = SharpDX.RectangleF;
using Vector2N = System.Numerics.Vector2;

namespace WhatsACrowdControl;

public record LiveBuffEntry(string Name, string DisplayName, float Timer, int Charges, bool IsTracked);

public record CcUiState(
    int ActiveCcCount,
    int TrackedBuffCount,
    List<LiveBuffEntry> LiveBuffs
);

public class WhatsACrowdControl : BaseSettingsPlugin<WhatsACrowdControlSettings>
{
    public override void OnClose()
    {
        try { HudLog.Flush(); }
        finally { base.OnClose(); }
    }

    #region CC Data Model

    public enum CcTier { FullLockout = 1, MovementLock = 2, HeavySlow = 3, CombatDebuff = 4 }

    public sealed class CcDefinition
    {
        public readonly string DisplayName;
        public readonly string ImageKey;
        public readonly CcTier Tier;
        public readonly string[] BuffNames;

        public CcDefinition(string displayName, string imageKey, CcTier tier, string[] buffNames)
        {
            DisplayName = displayName;
            ImageKey = imageKey;
            Tier = tier;
            BuffNames = buffNames;
        }
    }

    public sealed class ActiveCc
    {
        public CcDefinition Definition;
        public float TimeRemaining;
        public float MaxTime;
        public float AppearTime; // for fade-in

        public void Reset()
        {
            Definition = null!;
            TimeRemaining = 0;
            MaxTime = 0;
            AppearTime = 0;
        }
    }

    #endregion

    #region CC Definitions

    private static readonly CcDefinition[] CcDefinitions =
    {
        // Tier 1  - Full Lockout
        new("Stun",    "cc_stun",    CcTier.FullLockout, new[] { "stun_display_buff" }),
        new("Freeze",  "cc_freeze",  CcTier.FullLockout, new[] { "frozen" }),
        new("Petrify", "cc_petrify", CcTier.FullLockout, new[] { "petrified" }),

        // Tier 2  - Movement Lock
        new("Pin",  "cc_pin",  CcTier.MovementLock, new[] { "pinned", "trap_pinned" }),
        new("Root", "cc_root", CcTier.MovementLock, new[] { "grasping_vines_buff", "nemesis_vines", "nemesis_vines_large", "elder_siphon_immobilise" }),

        // Tier 3  - Heavy Slow
        new("Temporal Chains", "cc_temporal_chains", CcTier.HeavySlow, new[] { "curse_temporal_chains" }),
        new("Hinder",         "cc_hinder",          CcTier.HeavySlow, new[] { "hinder" }),
        new("Maim",            "cc_maim",            CcTier.HeavySlow, new[] { "maim" }),
        new("Chill",           "cc_chill",           CcTier.HeavySlow, new[] { "chilled" }),
        new("Tar",             "cc_slow",            CcTier.HeavySlow, new[] { "ground_tar_slow" }),
        new("Ash",             "cc_ash",             CcTier.HeavySlow, new[] { "cover_in_ash" }),
        new("Quicksand",       "cc_slow",            CcTier.HeavySlow, new[] { "quicksand_buff" }),
        new("Basilisk Slow",   "cc_slow",            CcTier.HeavySlow, new[] { "basilisk_slow" }),

        // Tier 4  - Combat Debuff
        new("Shock",   "cc_shock",   CcTier.CombatDebuff, new[] { "shocked" }),
        new("Blind",   "cc_blind",   CcTier.CombatDebuff, new[] { "blinded" }),
        new("Silence", "cc_silence", CcTier.CombatDebuff, new[] { "curse_silence", "curse_aura_silence" }),
    };

    #endregion

    #region State

    private const int MaxSlots = 3;
    private const float FadeInDuration = 0.15f;  // 150ms
    private const float FadeOutDuration = 0.3f;   // 300ms
    private const string FallbackImageKey = "cc_unknown";

    private Dictionary<string, CcDefinition> _buffLookup = new();
    private readonly ActiveCc[] _activeSlots = new ActiveCc[MaxSlots];
    private int _activeCount;
    private readonly ActiveCc[] _fadingSlots = new ActiveCc[MaxSlots];
    private float[] _fadingAlpha = new float[MaxSlots];
    private int _fadingCount;
    private bool _canRender;
    private float _currentTime;

    // Temp list for sorting  - avoids allocation per frame
    private readonly List<ActiveCc> _tempCcList = new(16);
    // Pool of ActiveCc instances to avoid allocation
    private readonly ActiveCc[] _ccPool = new ActiveCc[32];
    private int _poolIndex;

    // Track which definitions are currently active (for fade-out detection)
    private readonly HashSet<string> _prevActiveImageKeys = new();
    private readonly HashSet<string> _currActiveImageKeys = new();

    // DebugMode: timed buffs seen last tick / this tick, so each appearance is logged once
    private HashSet<string> _debugPrevBuffs = new(), _debugCurrBuffs = new();

    // Track when each CC image key first appeared (for fade-in)
    private readonly Dictionary<string, float> _appearTimes = new();

    // Image keys we successfully loaded
    private readonly HashSet<string> _loadedImages = new();

    // Custom CC definition for user-added buffs
    private CcDefinition? _customCcDef;
    private string _lastCustomBuffNames = "";

    // Custom settings UI
    private CrowdControlSettingsUi _settingsUi;

    // Live buff snapshot for settings UI
    private readonly List<LiveBuffEntry> _liveBuffSnapshot = new(64);

    #endregion

    #region Lifecycle

    private static readonly string[] ImageKeys =
    {
        "cc_stun", "cc_freeze", "cc_chill", "cc_shock",
        "cc_petrify", "cc_pin", "cc_hinder", "cc_maim",
        "cc_temporal_chains", "cc_silence", "cc_blind",
        "cc_root", "cc_slow", "cc_ash", "cc_unknown"
    };

    public override void OnLoad()
    {
        LoadImages();
    }

    private void LoadImages()
    {
        // Try multiple candidate directories:
        // 1. DirectoryFullName/images (Temp or Compiled plugin dir)
        // 2. Source directory (Plugins/Source/<name>/images)
        var candidates = new List<string>
        {
            Path.Combine(DirectoryFullName, "images")
        };

        // Derive Source path: DirectoryFullName might be Plugins/Temp/<name> or Plugins/Compiled/<name>
        // The source is always Plugins/Source/<name>
        var pluginsRoot = Path.GetDirectoryName(Path.GetDirectoryName(DirectoryFullName));
        if (pluginsRoot != null)
        {
            var sourcePath = Path.Combine(pluginsRoot, "Source", DirectoryName, "images");
            candidates.Add(sourcePath);
        }

        foreach (var imagesDir in candidates)
        {
            if (!Directory.Exists(imagesDir))
                continue;

            foreach (var key in ImageKeys)
            {
                if (_loadedImages.Contains(key))
                    continue;

                var filename = key.Replace("cc_", "") + ".png";
                var fullPath = Path.Combine(imagesDir, filename);
                if (File.Exists(fullPath))
                {
                    if (Graphics.InitImage(key, fullPath))
                        _loadedImages.Add(key);
                }
            }
        }
    }

    /// <summary>
    /// Ensures images are loaded. Called lazily from settings UI if OnLoad ran before images were available.
    /// </summary>
    public void EnsureImagesLoaded()
    {
        if (_loadedImages.Count >= ImageKeys.Length)
            return;
        LoadImages();
    }

    public override bool Initialise()
    {
        Name = "Whats A Crowd Control";
        // Init pool
        for (int i = 0; i < _ccPool.Length; i++)
            _ccPool[i] = new ActiveCc();
        for (int i = 0; i < _activeSlots.Length; i++)
            _activeSlots[i] = new ActiveCc();
        for (int i = 0; i < _fadingSlots.Length; i++)
            _fadingSlots[i] = new ActiveCc();

        BuildLookup();
        return true;
    }

    private void BuildLookup()
    {
        _buffLookup.Clear();
        foreach (var def in CcDefinitions)
        {
            foreach (var buffName in def.BuffNames)
            {
                _buffLookup[buffName] = def;
            }
        }

        // Add custom buffs
        RebuildCustomBuffs();
    }

    private void RebuildCustomBuffs()
    {
        // Remove old custom entries
        if (_customCcDef != null)
        {
            foreach (var buffName in _customCcDef.BuffNames)
                _buffLookup.Remove(buffName);
        }

        var customStr = Settings.CustomCcBuffNames?.Value ?? "";
        if (string.IsNullOrWhiteSpace(customStr))
        {
            _customCcDef = null;
            _lastCustomBuffNames = customStr;
            return;
        }

        var names = customStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length == 0)
        {
            _customCcDef = null;
            _lastCustomBuffNames = customStr;
            return;
        }

        _customCcDef = new CcDefinition("Custom", FallbackImageKey, CcTier.HeavySlow, names);
        foreach (var name in names)
        {
            if (!_buffLookup.ContainsKey(name))
                _buffLookup[name] = _customCcDef;
        }
        _lastCustomBuffNames = customStr;
    }

    private bool IsTierEnabled(CcTier tier) => tier switch
    {
        CcTier.FullLockout => Settings.EnableFullLockout,
        CcTier.MovementLock => Settings.EnableMovementLock,
        CcTier.HeavySlow => Settings.EnableHeavySlow,
        CcTier.CombatDebuff => Settings.EnableCombatDebuff,
        _ => true
    };

    #endregion

    #region Tick  - Data Only

    public override Job Tick()
    {
        _currentTime += (float)GameController.DeltaTime;
        _canRender = false;

        // Check custom buffs changed
        var currentCustom = Settings.CustomCcBuffNames?.Value ?? "";
        if (currentCustom != _lastCustomBuffNames)
            RebuildCustomBuffs();

        // Guard: not in game or dead
        if (!GameController.InGame)
            return null;

        var player = GameController.Player;
        if (player == null || !player.IsAlive)
            return null;

        // Guard: town/hideout
        var area = GameController.Area?.CurrentArea;
        if (area != null)
        {
            if (area.IsTown && !Settings.ShowInTown)
                return null;
            if (area.IsHideout && !Settings.ShowInHideout)
                return null;
        }

        // Get buffs
        var buffsComponent = player.GetComponent<Buffs>();
        var buffsList = buffsComponent?.BuffsList;
        if (buffsList == null)
            return null;

        // Save previous active keys for fade-out detection
        _prevActiveImageKeys.Clear();
        foreach (var key in _currActiveImageKeys)
            _prevActiveImageKeys.Add(key);
        _currActiveImageKeys.Clear();

        (_debugPrevBuffs, _debugCurrBuffs) = (_debugCurrBuffs, _debugPrevBuffs);
        _debugCurrBuffs.Clear();

        // Reset pool
        _poolIndex = 0;
        _tempCcList.Clear();

        // Single pass over buffs
        for (int i = 0; i < buffsList.Count; i++)
        {
            var buff = buffsList[i];
            if (buff == null) continue;

            var buffName = buff.Name;
            if (string.IsNullOrEmpty(buffName)) continue;

            // Debug: one log-file line when a timed buff appears (not one per frame, never on screen).
            if (Settings.DebugMode && buff.Timer > 0 && _debugCurrBuffs.Add(buffName) && !_debugPrevBuffs.Contains(buffName))
                HudLog.Quiet($"[CC Debug] Buff: {buffName} Timer: {buff.Timer:F2} MaxTime: {buff.MaxTime:F2}");

            if (!_buffLookup.TryGetValue(buffName, out var def))
                continue;

            // Skip expired/stale buffs (timer <= 0 means the effect has ended)
            if (buff.Timer <= 0)
                continue;

            if (!IsTierEnabled(def.Tier))
                continue;

            // Get or create ActiveCc from pool
            if (_poolIndex >= _ccPool.Length)
                continue;

            var cc = _ccPool[_poolIndex++];
            cc.Definition = def;
            cc.TimeRemaining = buff.Timer;
            cc.MaxTime = buff.MaxTime;

            // Track first-appear time for fade-in (only set when newly appearing)
            if (!_appearTimes.TryGetValue(def.ImageKey, out var appearTime))
            {
                appearTime = _currentTime;
                _appearTimes[def.ImageKey] = appearTime;
            }
            cc.AppearTime = appearTime;

            _tempCcList.Add(cc);
            _currActiveImageKeys.Add(def.ImageKey);
        }

        // Capture live buff snapshot for settings UI
        _liveBuffSnapshot.Clear();
        for (int i = 0; i < buffsList.Count; i++)
        {
            var buff = buffsList[i];
            if (buff == null || string.IsNullOrEmpty(buff.Name)) continue;
            _liveBuffSnapshot.Add(new LiveBuffEntry(
                buff.Name, buff.DisplayName ?? "", buff.Timer, buff.BuffCharges,
                _buffLookup.ContainsKey(buff.Name)));
        }

        // Sort: tier ascending, then time remaining descending (longest first within same tier)
        _tempCcList.Sort(CompareCc);

        // Fill active slots (top 3)
        _activeCount = Math.Min(_tempCcList.Count, MaxSlots);
        for (int i = 0; i < _activeCount; i++)
        {
            var src = _tempCcList[i];
            _activeSlots[i].Definition = src.Definition;
            _activeSlots[i].TimeRemaining = src.TimeRemaining;
            _activeSlots[i].MaxTime = src.MaxTime;
            _activeSlots[i].AppearTime = src.AppearTime;
        }

        // Clean up appear times for CCs no longer active
        foreach (var key in _prevActiveImageKeys)
        {
            if (!_currActiveImageKeys.Contains(key))
                _appearTimes.Remove(key);
        }

        // Detect fade-outs: was active last frame, not active now
        if (Settings.ShowFadeAnimations)
        {
            foreach (var key in _prevActiveImageKeys)
            {
                if (!_currActiveImageKeys.Contains(key) && _fadingCount < MaxSlots)
                {
                    // Find the definition for this key
                    CcDefinition? fadeDef = null;
                    foreach (var def in CcDefinitions)
                    {
                        if (def.ImageKey == key)
                        {
                            fadeDef = def;
                            break;
                        }
                    }
                    if (fadeDef != null)
                    {
                        _fadingSlots[_fadingCount].Definition = fadeDef;
                        _fadingSlots[_fadingCount].TimeRemaining = 0;
                        _fadingSlots[_fadingCount].MaxTime = 0;
                        _fadingSlots[_fadingCount].AppearTime = _currentTime;
                        _fadingAlpha[_fadingCount] = 1.0f;
                        _fadingCount++;
                    }
                }
            }
        }

        // Update fading slots
        for (int i = _fadingCount - 1; i >= 0; i--)
        {
            _fadingAlpha[i] -= (float)GameController.DeltaTime / FadeOutDuration;
            if (_fadingAlpha[i] <= 0)
            {
                // Remove by swapping with last
                _fadingCount--;
                if (i < _fadingCount)
                {
                    _fadingSlots[i].Definition = _fadingSlots[_fadingCount].Definition;
                    _fadingSlots[i].AppearTime = _fadingSlots[_fadingCount].AppearTime;
                    _fadingAlpha[i] = _fadingAlpha[_fadingCount];
                }
            }
        }

        _canRender = _activeCount > 0 || _fadingCount > 0;
        return null;
    }

    private static int CompareCc(ActiveCc a, ActiveCc b)
    {
        int tierCmp = ((int)a.Definition.Tier).CompareTo((int)b.Definition.Tier);
        if (tierCmp != 0) return tierCmp;
        // Descending time remaining (longest first)
        return b.TimeRemaining.CompareTo(a.TimeRemaining);
    }

    #endregion

    #region Render  - Drawing Only

    public override void Render()
    {
        if (!_canRender) return;

        var centerX = (float)Settings.PositionX.Value;
        var centerY = (float)Settings.PositionY.Value;
        var primarySize = Settings.PrimaryIconSize.Value;
        var secondarySize = Settings.SecondaryIconSize.Value;
        var spacing = Settings.IconSpacing.Value;

        // Layout: [secondary] [gap] [PRIMARY] [gap] [secondary]
        // Primary is slot 0, secondaries are slots 1 and 2
        // All vertically centered on centerY

        for (int i = 0; i < _activeCount; i++)
        {
            var cc = _activeSlots[i];
            bool isPrimary = (i == 0);

            var rect = GetSlotRect(i, centerX, centerY, primarySize, secondarySize, spacing);

            // Calculate alpha (fade-in)
            float alpha = 1.0f;
            if (Settings.ShowFadeAnimations)
            {
                float elapsed = _currentTime - cc.AppearTime;
                if (elapsed < FadeInDuration)
                    alpha = elapsed / FadeInDuration;
            }

            DrawCcIcon(cc, rect, alpha, isPrimary);
        }

        // Draw fading slots (these are CCs that just expired)
        for (int i = 0; i < _fadingCount; i++)
        {
            var cc = _fadingSlots[i];
            float alpha = _fadingAlpha[i];

            // Find which slot this CC was in  - just draw in-place near center
            // Fading CCs display at the primary position (simplified)
            var rect = GetSlotRect(0, centerX, centerY, primarySize, secondarySize, spacing);
            DrawCcIcon(cc, rect, alpha, true);
        }
    }

    private RectangleF GetSlotRect(int slotIndex, float centerX, float centerY, int primarySize, int secondarySize, int spacing)
    {
        // Slot 0 = primary (center)
        // Slot 1 = left secondary
        // Slot 2 = right secondary
        switch (slotIndex)
        {
            case 0: // Primary  - centered
                return new RectangleF(
                    centerX - primarySize / 2f,
                    centerY - primarySize / 2f,
                    primarySize,
                    primarySize);

            case 1: // Left secondary
            {
                float x = centerX - primarySize / 2f - spacing - secondarySize;
                float y = centerY - secondarySize / 2f;
                return new RectangleF(x, y, secondarySize, secondarySize);
            }

            case 2: // Right secondary
            {
                float x = centerX + primarySize / 2f + spacing;
                float y = centerY - secondarySize / 2f;
                return new RectangleF(x, y, secondarySize, secondarySize);
            }

            default:
                return default;
        }
    }

    private void DrawCcIcon(ActiveCc cc, RectangleF rect, float alpha, bool isPrimary)
    {
        var imageKey = cc.Definition.ImageKey;
        bool hasImage = _loadedImages.Contains(imageKey);
        if (!hasImage)
        {
            imageKey = FallbackImageKey;
            hasImage = _loadedImages.Contains(imageKey);
        }

        int a = (int)(Math.Clamp(alpha, 0f, 1f) * 255);
        var iconColor = new Color(255, 255, 255, a);

        // Dim secondaries
        if (!isPrimary)
            iconColor = new Color(180, 180, 180, a);

        // 1. Draw background box (dark rounded rect behind icon)
        int bgAlpha = (int)(Settings.BackgroundOpacity * a);
        Graphics.DrawBox(
            new Vector2N(rect.X, rect.Y),
            new Vector2N(rect.X + rect.Width, rect.Y + rect.Height),
            new Color(0, 0, 0, bgAlpha),
            4f);

        // 2. Draw icon image
        if (hasImage)
        {
            Graphics.DrawImage(imageKey, rect, iconColor);
        }

        // 3. Draw sweep overlay (dark overlay shrinking from top as timer depletes)
        if (Settings.ShowSweepOverlay && cc.MaxTime > 0 && cc.TimeRemaining > 0)
        {
            float fraction = cc.TimeRemaining / cc.MaxTime;
            float darkHeight = rect.Height * (1f - fraction);
            if (darkHeight > 0.5f)
            {
                int sweepAlpha = (int)(Settings.BackgroundOpacity * a * 0.7f);
                Graphics.DrawBox(
                    new Vector2N(rect.X, rect.Y),
                    new Vector2N(rect.X + rect.Width, rect.Y + darkHeight),
                    new Color(0, 0, 0, sweepAlpha),
                    0f);
            }
        }

        // 4. Draw border frame
        Graphics.DrawFrame(
            new Vector2N(rect.X, rect.Y),
            new Vector2N(rect.X + rect.Width, rect.Y + rect.Height),
            new Color(40, 40, 40, a),
            4, 1, 0);

        // 5. Draw timer text
        if (Settings.ShowTimerText && cc.TimeRemaining > 0)
        {
            var timerStr = cc.TimeRemaining >= 10f
                ? $"{cc.TimeRemaining:F0}"
                : $"{cc.TimeRemaining:F1}";

            var textColor = new Color(
                (int)Settings.TimerTextColor.Value.R,
                (int)Settings.TimerTextColor.Value.G,
                (int)Settings.TimerTextColor.Value.B,
                a);

            var textPos = new Vector2N(
                rect.X + rect.Width / 2f,
                rect.Y + rect.Height / 2f - 7f);

            // Draw text with background for readability
            Graphics.DrawTextWithBackground(timerStr, textPos, textColor, FontAlign.Center, new Color(0, 0, 0, (int)(a * 0.7f)));
        }

        // 6. Draw CC name below icon (small)
        if (isPrimary)
        {
            var namePos = new Vector2N(
                rect.X + rect.Width / 2f,
                rect.Y + rect.Height + 2f);
            var nameColor = new Color(220, 220, 220, a);
            Graphics.DrawTextWithBackground(cc.Definition.DisplayName, namePos, nameColor, FontAlign.Center, new Color(0, 0, 0, (int)(a * 0.5f)));
        }
    }

    #endregion

    #region Settings UI

    public override void DrawSettings()
    {
        EnsureImagesLoaded();
        _settingsUi ??= new CrowdControlSettingsUi();
        _settingsUi.Draw(Settings, Graphics, GameController, new CcUiState(
            _activeCount,
            _buffLookup.Count,
            _liveBuffSnapshot
        ));
    }

    #endregion
}
