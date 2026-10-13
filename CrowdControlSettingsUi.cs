using System;
using System.Collections.Generic;
using System.Numerics;
using ExileCore;
using ImGuiNET;
using Graphics = ExileCore.Graphics;

namespace WhatsACrowdControl;

/// <summary>
/// Settings panel: the generated SettingsMenu (the Overlay tab from the [Menu] headers and rows) plus what it can't
/// know about: a live status strip above the tabs, the CC Types tab (tier legend, icon browser, live buff browser that
/// adds a buff to Custom CC Buff Names with a click) and a live Debug tab (counts, icon load status).
/// Red-orange (#FF6040) accent: danger/CC theme. Rich tooltips come from WhatsATooltip over the PluginBridge when it
/// is loaded, and are simply absent when it isn't.
/// </summary>
public class CrowdControlSettingsUi
{
    private readonly SettingsMenu _menu;
    private WhatsACrowdControlSettings? _settings;
    private Graphics? _gfx;
    private CcUiState? _state;
    private int _previewIndex;

    // ── WhatsATooltip bridge delegates (lazy init, null-safe) ────────
    private bool _bridgeChecked;
    private Action<string, string, string[], IntPtr, uint, uint, uint, uint, uint, uint>? _ttForCcEntry;
    private Action<string, string, float, int, bool, uint, uint, uint, uint, uint>? _ttForLiveBuff;

    private void EnsureBridge(GameController gc)
    {
        if (_bridgeChecked) return;
        _ttForCcEntry = gc.PluginBridge.GetMethod<Action<string, string, string[], IntPtr, uint, uint, uint, uint, uint, uint>>("WhatsATooltip.ForCcEntry");
        _ttForLiveBuff = gc.PluginBridge.GetMethod<Action<string, string, float, int, bool, uint, uint, uint, uint, uint>>("WhatsATooltip.ForLiveBuff");
        _bridgeChecked = true;
    }

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

    private static readonly (string Tier, string Members)[] TierLegend =
    {
        ("Full Lockout", "Stun / Freeze / Petrify"),
        ("Movement Lock", "Pin / Root"),
        ("Heavy Slow", "Chill / Hinder / Maim / Tar / Ash / Quicksand / Basilisk"),
        ("Combat Debuff", "Shock / Blind / Silence"),
    };

    // ── Palette ─────────────────────────────────────────────────────
    private static readonly Vector4 Accent = new(1f, 0.38f, 0.25f, 1f);
    private static readonly Vector4 AccentDim = new(0.65f, 0.25f, 0.16f, 1f);
    private static readonly Vector4 LabelCol = new(0.88f, 0.88f, 0.88f, 1f);
    private static readonly Vector4 Desc = new(0.52f, 0.55f, 0.60f, 1f);
    private static readonly Vector4 CardBg = new(0.05f, 0.05f, 0.07f, 1f);
    private static readonly Vector4 TierFull = new(1f, 0.25f, 0.25f, 1f);       // red
    private static readonly Vector4 TierMove = new(1f, 0.55f, 0.15f, 1f);       // orange
    private static readonly Vector4 TierSlow = new(0.3f, 0.7f, 1f, 1f);         // blue
    private static readonly Vector4 TierDebuff = new(0.8f, 0.6f, 1f, 1f);       // purple
    private static readonly Vector4 Good = new(0.2f, 0.85f, 0.3f, 1f);
    private static readonly Vector4 Bad = new(0.9f, 0.25f, 0.25f, 1f);

    public CrowdControlSettingsUi()
    {
        _menu = new SettingsMenu { Accent = Accent, RootTabName = "Overlay" }
            .Banner(DrawStatusStrip)
            .Tab("CC Types", DrawCcTypesTab)
            .Tab("Debug", DrawDebugTab);
    }

    public void Draw(WhatsACrowdControlSettings s, Graphics gfx, GameController gc, CcUiState state)
    {
        EnsureBridge(gc);
        _settings = s;
        _gfx = gfx;
        _state = state;
        _menu.Draw(s);
    }

    private static uint U32(Vector4 c) => ImGui.GetColorU32(c);

    private static Vector4 TierColor(string tier) => tier switch
    {
        "Full Lockout" => TierFull,
        "Movement Lock" => TierMove,
        "Heavy Slow" => TierSlow,
        "Combat Debuff" => TierDebuff,
        _ => LabelCol
    };

    // ── Status strip above the tabs ─────────────────────────────────

    private void DrawStatusStrip()
    {
        var st = _state;
        if (st == null) { SettingsMenu.Dim("No live state yet"); return; }
        _menu.Badge($"{st.ActiveCcCount} Active CCs", st.ActiveCcCount > 0 ? Good : Desc, "Crowd-control effects on you right now");
        ImGui.SameLine();
        _menu.Badge($"{st.TrackedBuffCount} Tracked Buffs", st.TrackedBuffCount > 0 ? Good : Desc, "Buff names the plugin matches (built-in + custom)");
        ImGui.SameLine();
        _menu.Badge($"{st.LiveBuffs.Count} Live Buffs", st.LiveBuffs.Count > 0 ? Good : Desc, "Buffs with a timer on you right now");
        ImGui.Spacing();
    }

    // ── CC Types tab ────────────────────────────────────────────────

    private void DrawCcTypesTab()
    {
        Intro("CC Tier System",
            "CCs are ranked by severity: Full Lockout > Movement Lock > Heavy Slow > Combat Debuff. The overlay shows up to "
            + "3 active CCs, sorted by tier then duration. Tiers are switched on the Overlay tab (CC Tier Filters).");

        _menu.SectionHeader("Tiers");
        foreach (var (tier, members) in TierLegend)
        {
            _menu.Badge(tier, TierColor(tier));
            ImGui.SameLine();
            SettingsMenu.Text(Desc, members);
        }

        _menu.SectionHeader("CC Icon Browser", "Every built-in CC, its icon and the buff names that trigger it");
        DrawCarousel();

        _menu.SectionHeader("Live Buff Browser", "Buffs on you right now; click one to add it to Custom CC Buff Names (tracked as Heavy Slow)");
        DrawLiveBuffs();
    }

    private void DrawCarousel()
    {
        var entry = CcEntries[_previewIndex];
        var tierColor = TierColor(entry.Tier);
        const float icon = 48f;
        float w = ImGui.GetContentRegionAvail().X;
        var org = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        // arrows at the sides, icon in the middle
        ImGui.SetCursorScreenPos(new Vector2(org.X, org.Y + (icon - ImGui.GetFrameHeight()) * 0.5f));
        if (ImGui.ArrowButton("##cc_prev", ImGuiDir.Left))
            _previewIndex = (_previewIndex - 1 + CcEntries.Length) % CcEntries.Length;
        ImGui.SetCursorScreenPos(new Vector2(org.X + w - ImGui.GetFrameHeight(), org.Y + (icon - ImGui.GetFrameHeight()) * 0.5f));
        if (ImGui.ArrowButton("##cc_next", ImGuiDir.Right))
            _previewIndex = (_previewIndex + 1) % CcEntries.Length;

        float cx = org.X + w * 0.5f;
        var iconMin = new Vector2(cx - icon * 0.5f, org.Y);
        var iconMax = iconMin + new Vector2(icon, icon);
        dl.AddRectFilled(iconMin - new Vector2(2, 2), iconMax + new Vector2(2, 2), U32(tierColor with { W = 0.15f }), 4f);
        dl.AddRect(iconMin - new Vector2(2, 2), iconMax + new Vector2(2, 2), U32(tierColor with { W = 0.5f }), 4f);
        IntPtr tex = IntPtr.Zero;
        if (_gfx != null && _gfx.HasImage(entry.ImageKey)) tex = _gfx.GetTextureId(entry.ImageKey);
        if (tex != IntPtr.Zero) dl.AddImage(tex, iconMin, iconMax);
        else
        {
            var q = ImGui.CalcTextSize("?");
            dl.AddText((iconMin + iconMax) * 0.5f - q * 0.5f, U32(Accent), "?");
        }
        ImGui.SetCursorScreenPos(iconMin - new Vector2(2, 2));
        ImGui.InvisibleButton("##cc_icon_tip", new Vector2(icon + 4, icon + 4));
        if (ImGui.IsItemHovered())
        {
            if (_ttForCcEntry != null)
                _ttForCcEntry(entry.Name, entry.Tier, entry.Buffs, tex, U32(tierColor), U32(Accent), U32(AccentDim), U32(LabelCol), U32(Desc), U32(CardBg));
            else
                SettingsMenu.Tooltip($"{entry.Name} ({entry.Tier})\n{string.Join(", ", entry.Buffs)}");
        }

        float y = org.Y + icon + 6f;
        var nameSz = ImGui.CalcTextSize(entry.Name);
        dl.AddText(new Vector2(cx - nameSz.X * 0.5f, y), U32(LabelCol), entry.Name);
        y += nameSz.Y + 4f;
        var tierSz = ImGui.CalcTextSize(entry.Tier);
        ImGui.SetCursorScreenPos(new Vector2(cx - tierSz.X * 0.5f - 6f, y));
        _menu.Badge(entry.Tier, tierColor);
        y = ImGui.GetCursorScreenPos().Y + 2f;

        ImGui.SetCursorScreenPos(new Vector2(org.X, y));
        SettingsMenu.Text(Desc, "Buff names:");
        foreach (var b in entry.Buffs)
        {
            ImGui.Indent(12f);
            SettingsMenu.Text(LabelCol with { W = 0.7f }, b);
            ImGui.Unindent(12f);
        }

        // page dots
        var dots = ImGui.GetCursorScreenPos();
        float dotsX = cx - CcEntries.Length * 10f * 0.5f;
        for (int i = 0; i < CcEntries.Length; i++)
        {
            var c = new Vector2(dotsX + i * 10f + 4f, dots.Y + 6f);
            if (i == _previewIndex) dl.AddCircleFilled(c, 3f, U32(Accent));
            else dl.AddCircleFilled(c, 2f, U32(LabelCol with { W = 0.3f }));
        }
        ImGui.Dummy(new Vector2(w, 14f));
    }

    private void DrawLiveBuffs()
    {
        var s = _settings;
        var live = _state?.LiveBuffs;
        if (s == null || live == null || live.Count == 0)
        {
            SettingsMenu.Dim("Enter a zone to see live buffs");
            return;
        }
        SettingsMenu.Text(LabelCol with { W = 0.7f }, $"{live.Count} active buffs  - click to add as custom CC");

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerH
                                      | ImGuiTableFlags.SizingStretchProp;
        float h = Math.Min(300f, (live.Count + 1) * (ImGui.GetTextLineHeightWithSpacing() + 2f) + 6f);
        if (!ImGui.BeginTable("##cc_live_buffs", 4, flags, new Vector2(0, h))) return;
        try
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Internal Name", ImGuiTableColumnFlags.WidthStretch, 3f);
            ImGui.TableSetupColumn("Display Name", ImGuiTableColumnFlags.WidthStretch, 2f);
            ImGui.TableSetupColumn("Timer", ImGuiTableColumnFlags.WidthStretch, 0.8f);
            ImGui.TableSetupColumn("Charges", ImGuiTableColumnFlags.WidthStretch, 0.8f);
            ImGui.PushStyleColor(ImGuiCol.Text, Accent);
            ImGui.TableHeadersRow();
            ImGui.PopStyleColor();

            for (int i = 0; i < live.Count; i++)
            {
                var buff = live[i];
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.PushID(i);
                bool tracked = buff.IsTracked;
                ImGui.PushStyleColor(ImGuiCol.Text, tracked ? Good : LabelCol);
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Accent with { W = 0.12f });
                bool clicked = ImGui.Selectable((tracked ? "* " : "") + buff.Name, false, ImGuiSelectableFlags.SpanAllColumns);
                ImGui.PopStyleColor(2);
                if (ImGui.IsItemHovered())
                {
                    if (_ttForLiveBuff != null)
                        _ttForLiveBuff(buff.Name, buff.DisplayName, buff.Timer, buff.Charges, buff.IsTracked,
                            U32(Accent), U32(AccentDim), U32(LabelCol), U32(Desc), U32(CardBg));
                    else
                        SettingsMenu.Tooltip(tracked ? "Already tracked" : "Click to track as Heavy Slow");
                }
                if (clicked && !tracked) AddCustom(s, buff.Name);   // a tracked buff is already matched: nothing to add
                ImGui.PopID();
                ImGui.TableNextColumn();
                SettingsMenu.Text(LabelCol with { W = 0.6f }, buff.DisplayName ?? "");
                ImGui.TableNextColumn();
                SettingsMenu.Text(LabelCol with { W = 0.5f }, buff.Timer > 0 ? $"{buff.Timer:F1}s" : "-");
                ImGui.TableNextColumn();
                SettingsMenu.Text(LabelCol with { W = 0.5f }, buff.Charges > 0 ? buff.Charges.ToString() : "");
            }
        }
        finally { ImGui.EndTable(); }
        SettingsMenu.Dim("* = already tracked");
    }

    private static void AddCustom(WhatsACrowdControlSettings s, string name)
    {
        var node = s.CustomCcBuffNames;
        if (node == null) return;
        var current = node.Value ?? "";
        foreach (var part in current.Split(','))
            if (string.Equals(part.Trim(), name, StringComparison.Ordinal)) return;   // already listed
        if (!string.IsNullOrWhiteSpace(current) && !current.TrimEnd().EndsWith(",")) current += ",";
        node.Value = current + name;
    }

    // ── Debug tab ───────────────────────────────────────────────────

    private void DrawDebugTab()
    {
        var st = _state;
        if (st == null) { SettingsMenu.Dim("No live state yet"); return; }
        _menu.SectionHeader("Live Data");
        Pair("Active CCs", st.ActiveCcCount.ToString(), Accent);
        Pair("Tracked Buff Names", st.TrackedBuffCount.ToString(), Accent);
        Pair("Live Buff Count", st.LiveBuffs.Count.ToString(), Accent);

        _menu.SectionHeader("Icon Status", "Whether each CC icon image loaded from the plugin's images folder");
        foreach (var e in CcEntries)
        {
            bool loaded = _gfx != null && _gfx.HasImage(e.ImageKey);
            Pair(e.Name, loaded ? "loaded" : "missing (" + e.ImageKey + ".png)", loaded ? Good : Bad);
        }
    }

    // ── small helpers ───────────────────────────────────────────────

    private static void Intro(string title, string body)
    {
        SettingsMenu.Text(Accent, title);
        ImGui.PushTextWrapPos(0f);
        SettingsMenu.Text(Desc, body);
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
    }

    private static void Pair(string label, string value, Vector4 valueCol)
    {
        SettingsMenu.Text(LabelCol, label + ":");
        ImGui.SameLine(Math.Max(160f, ImGui.GetContentRegionAvail().X * 0.4f));
        SettingsMenu.Text(valueCol, value);
    }
}
