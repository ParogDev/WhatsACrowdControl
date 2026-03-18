using System;
using System.Collections.Generic;
using System.Numerics;
using ExileCore;
using ExileCore.Shared.Nodes;
using ImGuiNET;
using Graphics = ExileCore.Graphics;

namespace WhatsACrowdControl;

/// <summary>
/// Self-contained neon-styled settings panel for WhatsACrowdControl.
/// Red-orange (#FF6040) accent  - danger/CC theme.
/// Custom DrawList widgets, tabbed layout, icon preview carousel, live buff browser.
/// </summary>
public class CrowdControlSettingsUi
{
    private int _activeTab;
    private int _previewIndex;
    private float _previewTimer;
    private readonly Dictionary<string, float> _anims = new();

    // ── WhatsATooltip bridge delegates (lazy init, null-safe) ────────
    private bool _bridgeChecked;
    private Func<uint, uint, uint, uint, uint, float, bool>? _ttBegin;
    private Action? _ttEnd;
    private Action<string, IntPtr, float>? _ttHeader;
    private Action<string, uint>? _ttBadge;
    private Action? _ttSeparator;
    private Action<string>? _ttBody;
    private Action<string, uint>? _ttBullet;
    private Action<string, string, string[], IntPtr, uint, uint, uint, uint, uint, uint>? _ttForCcEntry;
    private Action<string, string, float, int, bool, uint, uint, uint, uint, uint>? _ttForLiveBuff;

    private void EnsureBridge(GameController gc)
    {
        if (_bridgeChecked) return;
        _ttBegin = gc.PluginBridge.GetMethod<Func<uint, uint, uint, uint, uint, float, bool>>("WhatsATooltip.Begin");
        _ttEnd = gc.PluginBridge.GetMethod<Action>("WhatsATooltip.End");
        _ttHeader = gc.PluginBridge.GetMethod<Action<string, IntPtr, float>>("WhatsATooltip.Header");
        _ttBadge = gc.PluginBridge.GetMethod<Action<string, uint>>("WhatsATooltip.Badge");
        _ttSeparator = gc.PluginBridge.GetMethod<Action>("WhatsATooltip.Separator");
        _ttBody = gc.PluginBridge.GetMethod<Action<string>>("WhatsATooltip.Body");
        _ttBullet = gc.PluginBridge.GetMethod<Action<string, uint>>("WhatsATooltip.Bullet");
        _ttForCcEntry = gc.PluginBridge.GetMethod<Action<string, string, string[], IntPtr, uint, uint, uint, uint, uint, uint>>("WhatsATooltip.ForCcEntry");
        _ttForLiveBuff = gc.PluginBridge.GetMethod<Action<string, string, float, int, bool, uint, uint, uint, uint, uint>>("WhatsATooltip.ForLiveBuff");
        _bridgeChecked = true;
    }

    private static readonly string[] Tabs = { "Overlay", "CC Types", "Filtering", "Debug" };

    // CC definitions mirror  - display only, for the icon browser (16 entries, matches CcDefinitions)
    private static readonly (string Name, string ImageKey, string Tier, string[] Buffs)[] CcEntries =
    {
        ("Stun",            "cc_stun",            "Full Lockout",  new[] { "stun_display_buff" }),
        ("Freeze",          "cc_freeze",          "Full Lockout",  new[] { "frozen" }),
        ("Petrify",         "cc_petrify",         "Full Lockout",  new[] { "petrified" }),
        ("Pin",             "cc_pin",             "Movement Lock", new[] { "pinned", "trap_pinned" }),
        ("Root",            "cc_root",            "Movement Lock", new[] { "grasping_vines_buff", "nemesis_vines", "nemesis_vines_large", "elder_siphon_immobilise" }),
        ("Temporal Chains", "cc_temporal_chains", "Heavy Slow",    new[] { "curse_temporal_chains" }),
        ("Hinder",          "cc_hinder",          "Heavy Slow",    new[] { "hinder" }),
        ("Maim",            "cc_maim",            "Heavy Slow",    new[] { "maim" }),
        ("Chill",           "cc_chill",           "Heavy Slow",    new[] { "chilled" }),
        ("Tar",             "cc_slow",            "Heavy Slow",    new[] { "ground_tar_slow" }),
        ("Ash",             "cc_ash",             "Heavy Slow",    new[] { "cover_in_ash" }),
        ("Quicksand",       "cc_slow",            "Heavy Slow",    new[] { "quicksand_buff" }),
        ("Basilisk Slow",   "cc_slow",            "Heavy Slow",    new[] { "basilisk_slow" }),
        ("Shock",           "cc_shock",           "Combat Debuff", new[] { "shocked" }),
        ("Blind",           "cc_blind",           "Combat Debuff", new[] { "blinded" }),
        ("Silence",         "cc_silence",         "Combat Debuff", new[] { "curse_silence", "curse_aura_silence" }),
    };

    // Known buff names from built-in CC definitions (for marking tracked buffs in live browser)
    private static readonly HashSet<string> KnownBuffNames;
    static CrowdControlSettingsUi()
    {
        KnownBuffNames = new();
        foreach (var entry in CcEntries)
            foreach (var buff in entry.Buffs)
                KnownBuffNames.Add(buff);
    }

    // ── Palette ─────────────────────────────────────────────────────
    // Red-orange accent  - evokes danger/CC
    private static uint Accent => Col(1f, 0.38f, 0.25f);
    private static uint AccentDim => Col(0.65f, 0.25f, 0.16f);
    private static uint AccentSoft => Col(1f, 0.38f, 0.25f, 0.15f);
    private static uint Label => Col(0.88f, 0.88f, 0.88f);
    private static uint Desc => Col(0.38f, 0.40f, 0.44f);
    private static uint TabOff => Col(0.48f, 0.48f, 0.52f);
    private static uint CardBg => Col(0.05f, 0.05f, 0.07f, 1f);
    private static uint TierFull => Col(1f, 0.25f, 0.25f);       // red
    private static uint TierMove => Col(1f, 0.55f, 0.15f);       // orange
    private static uint TierSlow => Col(0.3f, 0.7f, 1f);         // blue
    private static uint TierDebuff => Col(0.8f, 0.6f, 1f);       // purple
    private static uint StatusGreen => Col(0.2f, 0.85f, 0.3f);
    private static uint StatusDim => Col(0.3f, 0.3f, 0.35f);
    private const float Row = 34f;
    private const float RowText = 40f;

    // ── Entry point ─────────────────────────────────────────────────

    public void Draw(WhatsACrowdControlSettings s, Graphics gfx, GameController gc, CcUiState state)
    {
        EnsureBridge(gc);
        var contentMin = ImGui.GetCursorScreenPos();
        float contentW = ImGui.GetContentRegionAvail().X;
        var dl = ImGui.GetWindowDrawList();

        // Tab bar
        float tabH = 26f;
        float tabW = contentW / Tabs.Length;

        for (int i = 0; i < Tabs.Length; i++)
        {
            var tMin = new Vector2(contentMin.X + i * tabW, contentMin.Y);
            var tMax = new Vector2(contentMin.X + (i + 1) * tabW, contentMin.Y + tabH);
            bool active = i == _activeTab;

            if (active)
                dl.AddRectFilled(tMin, tMax, WithAlpha(Accent, 0.12f));

            ImGui.SetCursorScreenPos(tMin);
            ImGui.InvisibleButton($"##cc_tab_{i}", tMax - tMin);
            bool hov = ImGui.IsItemHovered();
            if (ImGui.IsItemClicked()) _activeTab = i;

            if (hov && !active)
                dl.AddRectFilled(tMin, tMax, WithAlpha(Accent, 0.06f));
            if (active)
                dl.AddLine(tMin with { Y = tMax.Y - 1 } + new Vector2(3, 0),
                    tMax - new Vector2(3, 1), Accent, 2f);

            uint tc = active ? Accent : (hov ? AccentDim : TabOff);
            CenterText(dl, Tabs[i], (tMin + tMax) * 0.5f, tc);
        }

        float sepY = contentMin.Y + tabH + 1;
        dl.AddLine(new Vector2(contentMin.X, sepY),
            new Vector2(contentMin.X + contentW, sepY), WithAlpha(Accent, 0.10f), 1f);

        // Status bar below tab bar
        float statusY = sepY + 3;
        DrawStatusBar(dl, contentMin.X, contentW, statusY, state);
        float statusH = 20f;

        // Content area
        ImGui.SetCursorScreenPos(new Vector2(contentMin.X, sepY + statusH + 6));
        var avail = new Vector2(contentW, ImGui.GetContentRegionAvail().Y);
        ImGui.BeginChild("##cc_content", avail, ImGuiChildFlags.None, ImGuiWindowFlags.None);

        var cdl = ImGui.GetWindowDrawList();
        var cMin = ImGui.GetWindowPos();
        var cSz = ImGui.GetWindowSize();

        cdl.AddRectFilled(cMin, cMin + cSz, CardBg, 3f);
        float pulse = (float)(0.4 + 0.3 * Math.Sin(ImGui.GetTime() * 1.8));
        cdl.AddRect(cMin, cMin + cSz, WithAlpha(Accent, pulse * 0.18f), 3f, ImDrawFlags.None, 1f);

        float scrollY = ImGui.GetScrollY();
        float y = cMin.Y + 10 - scrollY;
        float x = cMin.X + 12;
        float cx = cMin.X + cSz.X * 0.50f;
        float sw = cSz.X * 0.40f;

        switch (_activeTab)
        {
            case 0: TabOverlay(cdl, s, x, cx, ref y, sw); break;
            case 1: TabCcTypes(cdl, s, gfx, x, cx, ref y, sw, cSz.X, state); break;
            case 2: TabFiltering(cdl, s, x, cx, ref y, sw); break;
            case 3: TabDebug(cdl, s, gfx, x, cx, ref y, sw, state); break;
        }

        ImGui.SetCursorScreenPos(new Vector2(x, y));
        ImGui.Dummy(new Vector2(1, 4));
        ImGui.EndChild();
    }

    // ── Status Bar ──────────────────────────────────────────────────

    private static void DrawStatusBar(ImDrawListPtr dl, float startX, float width, float y, CcUiState state)
    {
        var items = new (string label, int count)[]
        {
            ("Active CCs", state.ActiveCcCount),
            ("Tracked Buffs", state.TrackedBuffCount),
            ("Live Buffs", state.LiveBuffs.Count),
        };

        float x = startX + 12;
        foreach (var (label, count) in items)
        {
            uint dotCol = count > 0 ? StatusGreen : StatusDim;
            dl.AddCircleFilled(new Vector2(x + 4, y + 8), 4f, dotCol);
            string text = $"{count} {label}";
            uint textCol = count > 0 ? Label : StatusDim;
            dl.AddText(new Vector2(x + 12, y + 1), textCol, text);
            x += ImGui.CalcTextSize(text).X + 26;
        }
    }

    // ── Info Block ──────────────────────────────────────────────────

    private static void DrawInfoBlock(ImDrawListPtr dl, float x, ref float y, string title, string body)
    {
        float boxX = x + 2;
        float textX = boxX + 10;
        float startY = y;

        // Title
        dl.AddText(new Vector2(textX, y + 2), Accent, title);
        y += 18f;

        // Body lines
        foreach (var line in body.Split('\n'))
        {
            dl.AddText(new Vector2(textX, y), Desc, line);
            y += 15f;
        }
        y += 4f;

        // Left accent bar
        dl.AddRectFilled(new Vector2(boxX, startY), new Vector2(boxX + 3, y), WithAlpha(Accent, 0.4f), 1f);
        y += 4f;
    }

    // ── Tabs ────────────────────────────────────────────────────────

    private void TabOverlay(ImDrawListPtr dl, WhatsACrowdControlSettings s,
        float x, float cx, ref float y, float sw)
    {
        SectionHeader(dl, x, ref y, "Position");
        IntSlider("cc_px", s.PositionX, dl, x, cx, ref y, sw,
            "Position X", "Horizontal screen position in pixels");
        IntSlider("cc_py", s.PositionY, dl, x, cx, ref y, sw,
            "Position Y", "Vertical screen position in pixels");

        SectionHeader(dl, x, ref y, "Icon Sizes");
        IntSlider("cc_ps", s.PrimaryIconSize, dl, x, cx, ref y, sw,
            "Primary Size", "Main CC icon size (center)");
        IntSlider("cc_ss", s.SecondaryIconSize, dl, x, cx, ref y, sw,
            "Secondary Size", "Side CC icon size (flanking)");
        IntSlider("cc_sp", s.IconSpacing, dl, x, cx, ref y, sw,
            "Icon Spacing", "Gap between icons in pixels");

        SectionHeader(dl, x, ref y, "Visual Style");
        Toggle("cc_timer", s.ShowTimerText, dl, x, cx, ref y,
            "Show Timer", "Display remaining seconds on each icon");
        Toggle("cc_sweep", s.ShowSweepOverlay, dl, x, cx, ref y,
            "Sweep Overlay", "Dark clock-wipe effect as CC depletes");
        Toggle("cc_fade", s.ShowFadeAnimations, dl, x, cx, ref y,
            "Fade Animations", "Smooth fade-in on appear, fade-out on expire");
        FloatSlider("cc_bgop", s.BackgroundOpacity, dl, x, cx, ref y, sw,
            "Background Opacity", "Darkness of icon background and sweep");

        SectionHeader(dl, x, ref y, "Timer Color");
        ColorPicker("cc_tcol", s.TimerTextColor, dl, x, cx, ref y,
            "Timer Text Color", "Color of the countdown text");
    }

    private void TabCcTypes(ImDrawListPtr dl, WhatsACrowdControlSettings s, Graphics gfx,
        float x, float cx, ref float y, float sw, float cardW, CcUiState state)
    {
        // ── Info block ──
        DrawInfoBlock(dl, x, ref y,
            "CC Tier System",
            "CCs are ranked by severity: Full Lockout > Movement Lock > Heavy Slow > Combat Debuff.\n" +
            "The overlay shows up to 3 active CCs, sorted by tier then duration.\n" +
            "Toggle tiers below or add custom buff names to extend detection.");

        // ── Icon preview carousel ──
        SectionHeader(dl, x, ref y, "CC Icon Browser");

        _previewTimer += ImGui.GetIO().DeltaTime;

        float carouselW = cardW - 24f;
        float iconPreviewSize = 48f;
        float carouselCenterX = x + carouselW * 0.5f;

        // Navigation arrows + current CC
        var entry = CcEntries[_previewIndex];
        var tierColor = GetTierColor(entry.Tier);

        // Left arrow
        var arrowLeftPos = new Vector2(x + 10, y + iconPreviewSize * 0.5f - 6f);
        ImGui.SetCursorScreenPos(arrowLeftPos - new Vector2(4, 4));
        if (ImGui.InvisibleButton("##cc_prev", new Vector2(20, 20)))
            _previewIndex = (_previewIndex - 1 + CcEntries.Length) % CcEntries.Length;
        bool hovL = ImGui.IsItemHovered();
        dl.AddText(arrowLeftPos, hovL ? Accent : Label, "<");

        // Right arrow
        var arrowRightPos = new Vector2(x + carouselW - 10, y + iconPreviewSize * 0.5f - 6f);
        ImGui.SetCursorScreenPos(arrowRightPos - new Vector2(4, 4));
        if (ImGui.InvisibleButton("##cc_next", new Vector2(20, 20)))
            _previewIndex = (_previewIndex + 1) % CcEntries.Length;
        bool hovR = ImGui.IsItemHovered();
        dl.AddText(arrowRightPos, hovR ? Accent : Label, ">");

        // Center icon
        var iconPos = new Vector2(carouselCenterX - iconPreviewSize * 0.5f, y);
        var iconEnd = iconPos + new Vector2(iconPreviewSize, iconPreviewSize);

        // Icon background
        dl.AddRectFilled(iconPos - new Vector2(2, 2), iconEnd + new Vector2(2, 2),
            WithAlpha(tierColor, 0.15f), 4f);
        dl.AddRect(iconPos - new Vector2(2, 2), iconEnd + new Vector2(2, 2),
            WithAlpha(tierColor, 0.5f), 4f, ImDrawFlags.None, 1f);

        // Draw the actual icon image via DrawList (no ImGui widget)
        IntPtr carouselTexId = IntPtr.Zero;
        if (gfx.HasImage(entry.ImageKey))
        {
            carouselTexId = gfx.GetTextureId(entry.ImageKey);
            if (carouselTexId != IntPtr.Zero)
                dl.AddImage(carouselTexId, iconPos, iconEnd);
            else
                CenterText(dl, "?", (iconPos + iconEnd) * 0.5f, Accent);
        }
        else
        {
            CenterText(dl, "?", (iconPos + iconEnd) * 0.5f, Accent);
        }

        // Hover detection for rich tooltip on icon
        ImGui.SetCursorScreenPos(iconPos - new Vector2(2, 2));
        ImGui.InvisibleButton("##cc_icon_tip", new Vector2(iconPreviewSize + 4, iconPreviewSize + 4));
        if (ImGui.IsItemHovered() && _ttForCcEntry != null)
        {
            _ttForCcEntry(entry.Name, entry.Tier, entry.Buffs,
                carouselTexId, tierColor, Accent, AccentDim, Label, Desc, CardBg);
        }

        y += iconPreviewSize + 6f;

        // CC name centered
        CenterText(dl, entry.Name, new Vector2(carouselCenterX, y + 2), Label);
        y += 16f;

        // Tier badge
        var tierText = entry.Tier;
        var tierTextSize = ImGui.CalcTextSize(tierText);
        var badgeMin = new Vector2(carouselCenterX - tierTextSize.X * 0.5f - 6f, y);
        var badgeMax = new Vector2(carouselCenterX + tierTextSize.X * 0.5f + 6f, y + tierTextSize.Y + 4f);
        dl.AddRectFilled(badgeMin, badgeMax, WithAlpha(tierColor, 0.2f), 8f);
        dl.AddRect(badgeMin, badgeMax, WithAlpha(tierColor, 0.5f), 8f, ImDrawFlags.None, 1f);
        CenterText(dl, tierText, (badgeMin + badgeMax) * 0.5f, tierColor);
        y += tierTextSize.Y + 10f;

        // Buff names list
        dl.AddText(new Vector2(x + 20, y), Desc, "Buff names:");
        y += 16f;
        for (int b = 0; b < entry.Buffs.Length; b++)
        {
            dl.AddText(new Vector2(x + 30, y), WithAlpha(Label, 0.7f), entry.Buffs[b]);
            y += 14f;
        }

        // Page indicator dots
        y += 6f;
        float dotsW = CcEntries.Length * 10f;
        float dotsX = carouselCenterX - dotsW * 0.5f;
        for (int i = 0; i < CcEntries.Length; i++)
        {
            var dotCenter = new Vector2(dotsX + i * 10f + 4f, y + 4f);
            if (i == _previewIndex)
                dl.AddCircleFilled(dotCenter, 3f, Accent);
            else
                dl.AddCircleFilled(dotCenter, 2f, WithAlpha(Label, 0.3f));
        }
        y += 16f;

        // ── Per-tier toggles ──
        SectionHeader(dl, x, ref y, "Tier Filters");
        TierToggle("cc_t1", s.EnableFullLockout, dl, x, cx, ref y,
            "Full Lockout", "Stun / Freeze / Petrify", TierFull);
        TierToggle("cc_t2", s.EnableMovementLock, dl, x, cx, ref y,
            "Movement Lock", "Pin / Root", TierMove);
        TierToggle("cc_t3", s.EnableHeavySlow, dl, x, cx, ref y,
            "Heavy Slow", "Chill / Hinder / Maim / Tar / Ash / Quicksand / Basilisk", TierSlow);
        TierToggle("cc_t4", s.EnableCombatDebuff, dl, x, cx, ref y,
            "Combat Debuff", "Shock / Blind / Silence", TierDebuff);

        // ── Custom buffs ──
        SectionHeader(dl, x, ref y, "Custom Buffs");
        dl.AddText(new Vector2(x, y), Desc, "Add comma-separated internal buff names (tracked as Heavy Slow)");
        y += 16f;
        TextInput("cc_custom", s.CustomCcBuffNames, dl, x, x + 10, ref y, cardW - 44f);

        // ── Live Buff Browser ──
        SectionHeader(dl, x, ref y, "Live Buff Browser");

        var liveBuffs = state.LiveBuffs;
        if (liveBuffs.Count == 0)
        {
            dl.AddText(new Vector2(x + 6, y), Desc, "Enter a zone to see live buffs");
            y += 20f;
        }
        else
        {
            // Header
            string headerText = $"{liveBuffs.Count} active buffs  - click to add as custom CC";
            dl.AddText(new Vector2(x + 6, y), WithAlpha(Label, 0.7f), headerText);
            y += 18f;

            // Column headers
            float col1 = x + 6;
            float col2 = x + 240;
            float col3 = x + 400;
            float col4 = x + 460;
            dl.AddText(new Vector2(col1, y), Accent, "Internal Name");
            dl.AddText(new Vector2(col2, y), Accent, "Display Name");
            dl.AddText(new Vector2(col3, y), Accent, "Timer");
            dl.AddText(new Vector2(col4, y), Accent, "Charges");
            y += 16f;

            // Separator
            dl.AddLine(new Vector2(x + 4, y), new Vector2(x + cardW - 24, y),
                WithAlpha(Accent, 0.15f), 1f);
            y += 4f;

            // Scrollable buff list
            float listStartY = y;
            float maxListH = 300f;
            ImGui.SetCursorScreenPos(new Vector2(x, y));
            ImGui.BeginChild("##cc_live_buffs", new Vector2(cardW - 24f, maxListH),
                ImGuiChildFlags.None, ImGuiWindowFlags.None);

            var bDl = ImGui.GetWindowDrawList();
            var bMin = ImGui.GetWindowPos();
            float by = bMin.Y + 2;

            for (int i = 0; i < liveBuffs.Count; i++)
            {
                var buff = liveBuffs[i];
                float rowH = 18f;
                var rowMin = new Vector2(bMin.X, by);
                var rowMax = new Vector2(bMin.X + cardW - 28f, by + rowH);

                // Invisible button for click/hover
                ImGui.SetCursorScreenPos(rowMin);
                if (ImGui.InvisibleButton($"##cc_buff_{i}", rowMax - rowMin))
                {
                    // Click → append to CustomCcBuffNames
                    var current = s.CustomCcBuffNames?.Value ?? "";
                    if (!current.Contains(buff.Name))
                    {
                        if (!string.IsNullOrWhiteSpace(current) && !current.EndsWith(","))
                            current += ",";
                        current += buff.Name;
                        s.CustomCcBuffNames.Value = current;
                    }
                }

                bool rowHov = ImGui.IsItemHovered();

                // Hover highlight + rich tooltip
                if (rowHov)
                {
                    bDl.AddRectFilled(rowMin, rowMax, WithAlpha(Accent, 0.08f), 2f);
                    _ttForLiveBuff?.Invoke(buff.Name, buff.DisplayName,
                        buff.Timer, buff.Charges, buff.IsTracked,
                        Accent, AccentDim, Label, Desc, CardBg);
                }

                // Tracked dot (green if already tracked)
                float bCol1 = bMin.X + 6;
                if (buff.IsTracked)
                    bDl.AddCircleFilled(new Vector2(bCol1 - 2, by + rowH * 0.5f), 3f, StatusGreen);

                // Internal name
                bDl.AddText(new Vector2(bCol1 + 6, by + 1), Label, buff.Name);

                // Display name
                float bCol2 = bMin.X + 240;
                if (!string.IsNullOrEmpty(buff.DisplayName))
                    bDl.AddText(new Vector2(bCol2, by + 1), WithAlpha(Label, 0.6f), buff.DisplayName);

                // Timer
                float bCol3 = bMin.X + 400;
                string timerText = buff.Timer > 0 ? $"{buff.Timer:F1}s" : "-";
                bDl.AddText(new Vector2(bCol3, by + 1), WithAlpha(Label, 0.5f), timerText);

                // Charges
                float bCol4 = bMin.X + 460;
                if (buff.Charges > 0)
                    bDl.AddText(new Vector2(bCol4, by + 1), WithAlpha(Label, 0.5f), buff.Charges.ToString());

                by += rowH;
            }

            ImGui.SetCursorScreenPos(new Vector2(bMin.X, by));
            ImGui.Dummy(new Vector2(1, 4));
            ImGui.EndChild();

            y += maxListH + 4;
        }
    }

    private void TabFiltering(ImDrawListPtr dl, WhatsACrowdControlSettings s,
        float x, float cx, ref float y, float sw)
    {
        DrawInfoBlock(dl, x, ref y,
            "Area Filtering",
            "By default, CC detection is disabled in towns and hideouts.\n" +
            "Enable below if you want the overlay active everywhere.");

        SectionHeader(dl, x, ref y, "Area Filtering");
        Toggle("cc_town", s.ShowInTown, dl, x, cx, ref y,
            "Show In Town", "Display CC overlay while in town areas");
        Toggle("cc_hide", s.ShowInHideout, dl, x, cx, ref y,
            "Show In Hideout", "Display CC overlay while in your hideout");
    }

    private void TabDebug(ImDrawListPtr dl, WhatsACrowdControlSettings s, Graphics gfx,
        float x, float cx, ref float y, float sw, CcUiState state)
    {
        SectionHeader(dl, x, ref y, "Diagnostics");
        Toggle("cc_dbg", s.DebugMode, dl, x, cx, ref y,
            "Debug Mode", "Log all timed buffs to the debug window each frame");

        y += 8f;
        dl.AddText(new Vector2(x, y), Desc,
            "When enabled, every buff with a timer will be logged.");
        y += 14f;
        dl.AddText(new Vector2(x, y), Desc,
            "Use DevTree to cross-reference buff names.");
        y += 20f;

        // Live stats
        SectionHeader(dl, x, ref y, "Live Data");
        dl.AddText(new Vector2(x + 6, y), Label, "Active CCs:");
        dl.AddText(new Vector2(cx, y), Accent, state.ActiveCcCount.ToString());
        y += 16f;
        dl.AddText(new Vector2(x + 6, y), Label, "Tracked Buff Names:");
        dl.AddText(new Vector2(cx, y), Accent, state.TrackedBuffCount.ToString());
        y += 16f;
        dl.AddText(new Vector2(x + 6, y), Label, "Live Buff Count:");
        dl.AddText(new Vector2(cx, y), Accent, state.LiveBuffs.Count.ToString());
        y += 20f;

        SectionHeader(dl, x, ref y, "Icon Status");
        for (int i = 0; i < CcEntries.Length; i++)
        {
            var e = CcEntries[i];
            dl.AddText(new Vector2(x + 6, y), Label, e.Name);

            bool loaded = gfx.HasImage(e.ImageKey);
            uint dotCol = loaded ? StatusGreen : Col(0.9f, 0.25f, 0.25f);
            dl.AddCircleFilled(new Vector2(cx, y + 6), 4f, dotCol);

            string status = loaded ? "loaded" : "missing";
            uint statusCol = loaded ? StatusGreen : Col(0.9f, 0.25f, 0.25f);
            dl.AddText(new Vector2(cx + 12, y), statusCol, status);
            y += 16f;
        }
    }

    // ── Widget primitives ───────────────────────────────────────────

    private static void HelpMarker(string desc)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
            ImGui.TextUnformatted(desc);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    private void SectionHeader(ImDrawListPtr dl, float x, ref float y, string title)
    {
        y += 4f;
        dl.AddText(new Vector2(x, y), Accent, title);
        y += 18f;
        dl.AddLine(new Vector2(x, y - 4), new Vector2(x + ImGui.CalcTextSize(title).X + 40, y - 4),
            WithAlpha(Accent, 0.25f), 1f);
    }

    private void Toggle(string key, ToggleNode node, ImDrawListPtr dl,
        float x, float cx, ref float y, string label, string desc)
    {
        dl.AddText(new Vector2(x + 6, y + 1), Label, label);
        dl.AddText(new Vector2(x + 6, y + 16), Desc, desc);
        ImGui.SetCursorScreenPos(new Vector2(cx, y + 5));
        var v = node.Value;
        _anims.TryGetValue(key, out float a);
        if (PillToggle($"##{key}", ref v, ref a))
            node.Value = v;
        _anims[key] = a;
        y += Row;
    }

    private void TierToggle(string key, ToggleNode node, ImDrawListPtr dl,
        float x, float cx, ref float y, string label, string desc, uint tierCol)
    {
        // Tier color dot
        dl.AddCircleFilled(new Vector2(x + 10, y + 8), 4f, tierCol);

        dl.AddText(new Vector2(x + 20, y + 1), Label, label);
        dl.AddText(new Vector2(x + 20, y + 16), Desc, desc);

        // Hover on label area shows rich tooltip with tier info
        ImGui.SetCursorScreenPos(new Vector2(x + 20, y));
        ImGui.InvisibleButton($"{key}_tip", new Vector2(cx - x - 24, Row));
        if (ImGui.IsItemHovered() && _ttBegin != null)
        {
            _ttBegin(Accent, AccentDim, Label, Desc, CardBg, 320f);
            _ttHeader?.Invoke(label, IntPtr.Zero, 0f);
            _ttBadge?.Invoke(label, tierCol);
            _ttSeparator?.Invoke();
            _ttBody?.Invoke(desc);
            _ttEnd?.Invoke();
        }

        ImGui.SetCursorScreenPos(new Vector2(cx, y + 5));
        var v = node.Value;
        _anims.TryGetValue(key, out float a);
        if (PillToggle($"##{key}", ref v, ref a))
            node.Value = v;
        _anims[key] = a;
        y += Row;
    }

    private void IntSlider(string key, RangeNode<int> node, ImDrawListPtr dl,
        float x, float cx, ref float y, float sw, string label, string desc)
    {
        dl.AddText(new Vector2(x + 6, y + 1), Label, label);
        dl.AddText(new Vector2(x + 6, y + 16), Desc, desc);
        ImGui.SetCursorScreenPos(new Vector2(cx, y + 7));
        var v = node.Value;
        if (SliderInt($"##{key}", ref v, node.Min, node.Max, sw))
            node.Value = v;
        dl.AddText(new Vector2(cx + sw + 6, y + 7), Accent, v.ToString());
        y += Row;
    }

    private void FloatSlider(string key, RangeNode<float> node, ImDrawListPtr dl,
        float x, float cx, ref float y, float sw, string label, string desc)
    {
        dl.AddText(new Vector2(x + 6, y + 1), Label, label);
        dl.AddText(new Vector2(x + 6, y + 16), Desc, desc);
        ImGui.SetCursorScreenPos(new Vector2(cx, y + 7));
        var v = node.Value;
        if (SliderF($"##{key}", ref v, node.Min, node.Max, sw))
            node.Value = v;
        dl.AddText(new Vector2(cx + sw + 6, y + 7), Accent, v.ToString("F2"));
        y += Row;
    }

    private void ColorPicker(string key, ColorNode node, ImDrawListPtr dl,
        float x, float cx, ref float y, string label, string desc)
    {
        dl.AddText(new Vector2(x + 6, y + 1), Label, label);
        dl.AddText(new Vector2(x + 6, y + 16), Desc, desc);

        var c = node.Value;
        ImGui.SetCursorScreenPos(new Vector2(cx, y + 3));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.07f, 0.07f, 0.09f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.65f, 0.25f, 0.16f, 0.6f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        var colorVec = new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
        ImGui.SetNextItemWidth(120);
        if (ImGui.ColorEdit4($"##{key}", ref colorVec,
            ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
        {
            node.Value = new SharpDX.Color(
                (int)(colorVec.X * 255), (int)(colorVec.Y * 255),
                (int)(colorVec.Z * 255), (int)(colorVec.W * 255));
        }
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);

        y += Row;
    }

    private void TextInput(string key, TextNode node, ImDrawListPtr dl,
        float x, float cx, ref float y, float sw)
    {
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.07f, 0.07f, 0.09f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.88f, 0.88f, 0.88f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.65f, 0.25f, 0.16f, 0.6f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);

        string val = node.Value ?? "";
        ImGui.SetNextItemWidth(sw);
        if (ImGui.InputText($"##{key}", ref val, 512))
            node.Value = val;

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(3);
        y += RowText;
    }

    // ── Self-contained drawing helpers ──────────────────────────────

    private static bool PillToggle(string id, ref bool value, ref float animState)
    {
        var dl = ImGui.GetWindowDrawList();
        var cur = ImGui.GetCursorScreenPos();
        const float w = 40f, h = 20f, r = h * 0.5f;

        ImGui.InvisibleButton(id, new Vector2(w, h));
        bool changed = ImGui.IsItemClicked();
        if (changed) value = !value;

        float dt = ImGui.GetIO().DeltaTime;
        float target = value ? 1f : 0f;
        animState = Math.Clamp(animState + (target - animState) * Math.Min(dt * 10f, 1f), 0f, 1f);

        uint trackOff = ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 1f));
        uint trackColor = LerpCol(trackOff, Accent, animState);
        dl.AddRectFilled(cur, cur + new Vector2(w, h), trackColor, r);

        float knobX = cur.X + r + animState * (w - h);
        uint knobCol = ImGui.GetColorU32(new Vector4(
            0.5f + 0.5f * animState, 0.5f + 0.5f * animState,
            0.5f + 0.5f * animState, 1f));
        dl.AddCircleFilled(new Vector2(knobX, cur.Y + r), r - 2f, knobCol);

        return changed;
    }

    private static bool SliderInt(string id, ref int value, int min, int max, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var cur = ImGui.GetCursorScreenPos();
        const float h = 16f, th = 4f, tr = 7f;

        ImGui.InvisibleButton(id, new Vector2(width, h));
        bool changed = false;
        if (ImGui.IsItemActive())
        {
            float frac = Math.Clamp((ImGui.GetMousePos().X - cur.X) / width, 0f, 1f);
            int nv = min + (int)(frac * (max - min));
            if (nv != value) { value = nv; changed = true; }
        }

        float trackY = cur.Y + (h - th) * 0.5f;
        dl.AddRectFilled(cur with { Y = trackY }, new Vector2(cur.X + width, trackY + th),
            ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.15f, 1f)), 2f);

        float f = (max > min) ? (float)(value - min) / (max - min) : 0f;
        float fw = f * width;
        dl.AddRectFilled(cur with { Y = trackY }, new Vector2(cur.X + fw, trackY + th), Accent, 2f);

        float tx = cur.X + fw, ty = cur.Y + h * 0.5f;
        dl.AddCircleFilled(new Vector2(tx, ty), tr, Accent);
        dl.AddCircleFilled(new Vector2(tx, ty), tr - 2f,
            ImGui.GetColorU32(new Vector4(0.95f, 0.95f, 0.95f, 1f)));

        return changed;
    }

    private static bool SliderF(string id, ref float value, float min, float max, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var cur = ImGui.GetCursorScreenPos();
        const float h = 16f, th = 4f, tr = 7f;

        ImGui.InvisibleButton(id, new Vector2(width, h));
        bool changed = false;
        if (ImGui.IsItemActive())
        {
            float frac = Math.Clamp((ImGui.GetMousePos().X - cur.X) / width, 0f, 1f);
            float nv = MathF.Round(min + frac * (max - min), 2);
            if (MathF.Abs(nv - value) > 0.001f) { value = nv; changed = true; }
        }

        float trackY = cur.Y + (h - th) * 0.5f;
        dl.AddRectFilled(cur with { Y = trackY }, new Vector2(cur.X + width, trackY + th),
            ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.15f, 1f)), 2f);

        float f = (max > min) ? (value - min) / (max - min) : 0f;
        float fw = f * width;
        dl.AddRectFilled(cur with { Y = trackY }, new Vector2(cur.X + fw, trackY + th), Accent, 2f);

        float tx = cur.X + fw, ty = cur.Y + h * 0.5f;
        dl.AddCircleFilled(new Vector2(tx, ty), tr, Accent);
        dl.AddCircleFilled(new Vector2(tx, ty), tr - 2f,
            ImGui.GetColorU32(new Vector4(0.95f, 0.95f, 0.95f, 1f)));

        return changed;
    }

    // ── Tier color helper ───────────────────────────────────────────

    private static uint GetTierColor(string tier) => tier switch
    {
        "Full Lockout" => TierFull,
        "Movement Lock" => TierMove,
        "Heavy Slow" => TierSlow,
        "Combat Debuff" => TierDebuff,
        _ => Label
    };

    // ── Color utilities ─────────────────────────────────────────────

    private static uint Col(float r, float g, float b, float a = 1f)
        => ImGui.GetColorU32(new Vector4(r, g, b, a));

    private static uint WithAlpha(uint color, float alpha)
    {
        var v = ImGui.ColorConvertU32ToFloat4(color);
        v.W = alpha;
        return ImGui.GetColorU32(v);
    }

    private static uint LerpCol(uint a, uint b, float t)
    {
        var va = ImGui.ColorConvertU32ToFloat4(a);
        var vb = ImGui.ColorConvertU32ToFloat4(b);
        return ImGui.GetColorU32(new Vector4(
            va.X + (vb.X - va.X) * t, va.Y + (vb.Y - va.Y) * t,
            va.Z + (vb.Z - va.Z) * t, va.W + (vb.W - va.W) * t));
    }

    private static void CenterText(ImDrawListPtr dl, string text, Vector2 center, uint color)
    {
        var sz = ImGui.CalcTextSize(text);
        dl.AddText(center - sz * 0.5f, color, text);
    }
}
