#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using ImGuiNET;
using ExileCore;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;

namespace WhatsACrowdControl;

/// <summary>Draws the setting as a value, not a control: for a node the plugin overwrites itself (an auto-detected
/// buff name). The note, when given, is the badge's tooltip ("set from your passive tree").</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class MenuReadOnlyAttribute : Attribute
{
    public MenuReadOnlyAttribute(string? note = null) => Note = note;
    public string? Note { get; }
}

/// <summary>Unit shown after a RangeNode's value: <c>[MenuUnit("px")]</c>.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class MenuUnitAttribute : Attribute
{
    public MenuUnitAttribute(string unit) => Unit = unit;
    public string Unit { get; }
}

/// <summary>Puts a root-level [Submenu] property on a named tab instead of its own: several submenus with the same
/// tab name share it, each as a card, in declaration order. The root's own leaf nodes live on the
/// <see cref="SettingsMenu.RootTabName"/> tab; a submenu tagged with that name joins them. On a root-level leaf node
/// (a flat settings class with no submenus) it moves that row, with the rows hanging under it by parentIndex, to
/// the named tab, into one headerless card above that tab's submenu cards. Tabs only such rows make are listed in
/// the footer's notes, so a misspelt name shows.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class MenuTabAttribute : Attribute
{
    public MenuTabAttribute(string tab) => Tab = tab;
    public string Tab { get; }
    /// <summary>Root-level leaf rows only: their card goes below the tab's submenu cards instead of above them (a long
    /// list under its options).</summary>
    public bool Last { get; set; }
}

/// <summary>Options for a List&lt;T&gt;, T[] or Dictionary&lt;K,V&gt; property (drawn without it too). Every name is
/// optional: <c>[MenuList(ItemName = "status", Summary = nameof(StatusRule.Label), Detail = nameof(StatusRule.Id))]</c>.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class MenuListAttribute : Attribute
{
    /// <summary>What one element is called: "Add status", "3 statuses", "No statuses yet". Default "item".</summary>
    public string? ItemName { get; set; }
    /// <summary>Most elements the list takes; 0 (default) is no limit. The add row disables at the limit.</summary>
    public int MaxCount { get; set; }
    /// <summary>Element property shown as a folded element's title (default: its first string property).</summary>
    public string? Summary { get; set; }
    /// <summary>Element property shown muted after the title (default: its second string property).</summary>
    public string? Detail { get; set; }
    /// <summary>Element bool property drawn as a pill at the start of its header (default: one named Enabled, Enable or On).</summary>
    public string? Enabled { get; set; }
    /// <summary>String lists: elements as chips (click removes) with an add box, instead of one text field per row.</summary>
    public bool Chips { get; set; }
    /// <summary>The list body starts folded; the row shows the count.</summary>
    public bool Collapsed { get; set; }
    /// <summary>No up/down arrows (order carries no meaning). Dictionaries never reorder.</summary>
    public bool NoReorder { get; set; }
    /// <summary>Lists of strings and numbers refuse a value already present unless this is set.</summary>
    public bool AllowDuplicates { get; set; }
    /// <summary>Dictionaries: hint in the add row's key box ("buff id").</summary>
    public string? KeyName { get; set; }
}

/// <summary>A string property (or a List&lt;string&gt;'s elements) that takes one of these values: drawn as segmented
/// buttons when they are few and short, a combo otherwise.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class MenuChoicesAttribute : Attribute
{
    public MenuChoicesAttribute(params string[] values) => Values = values;
    public string[] Values { get; }
}

/// <summary>A numeric property (or a numeric list's elements) drawn as a slider between these bounds instead of an
/// input box. Plain numbers, not nodes: <c>[MenuRange(0.5, 3)] public float Scale { get; set; }</c>.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class MenuRangeAttribute : Attribute
{
    public MenuRangeAttribute(double min, double max) { Min = min; Max = max; }
    public double Min { get; }
    public double Max { get; }
}

/// <summary>A string property holding a colour as hex, edited with a colour picker: "#AARRGGBB" (also read as
/// "#RRGGBB"), or "#RRGGBBAA" with <c>Argb = false</c>.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class MenuHexColorAttribute : Attribute
{
    public bool Argb { get; set; } = true;
}

/// <summary>
/// Settings panel drawn from the plugin's [Menu] attributes and node types, so a plugin keeps labels, tooltips and
/// grouping in its settings class and gets a polished panel for free. Copied per plugin like HudLog.cs (never a
/// reference: plugins don't compile-link each other); the template lives in the scaffolding's new-plugin skill.
/// <para>Use: a field <c>private readonly SettingsMenu _menu = new(m => HudLog.Fault(m));</c> (or <c>new()</c> without
/// a HudLog: faults then go to DebugWindow.LogError) and
/// <c>public override void DrawSettings() => _menu.Draw(Settings);</c>. Options, all set once (constructor or
/// Initialise): <see cref="Accent"/> (the plugin's colour), <see cref="RootTabName"/>, <see cref="Tab"/> (a custom tab,
/// or extra content on a generated one), <see cref="Custom"/> (replace one property's row with your own drawing, also
/// for [IgnoreMenu] or unsupported types; inside it <see cref="Label"/> and <see cref="ControlColumn"/> line the row up
/// with the generated ones), <see cref="Banner"/> (a strip above the tabs). A Tab() name that no generated tab has
/// becomes a tab of its own and is named in the footer's notes (hover), so a typo shows.</para>
/// <para>Layout: every root-level [Submenu] property is a tab (merge them with [MenuTab]); each submenu is a filled
/// card with the [Menu] label as title and the tooltip as description; nested submenus are cards inside cards; every
/// other property is a row: label, control, tooltip under it (full text on hover). EmptyNode with [Menu] is a section
/// header; [Menu(parentIndex: N)] rows sit under the row with [Menu(index: N)] (the HUD's own grouping convention).
/// [IgnoreMenu] hides; [MenuReadOnly] shows the value only; [MenuUnit] suffixes a slider; [ConditionalDisplay] and
/// [Submenu(RenderMethod)] work as in the HUD. "Enable" at the root is the HUD's own checkbox in the plugin list and
/// is not drawn again. Right-click a label: reset to the value a fresh settings object has.</para>
/// <para>Node types drawn: ToggleNode (with its attached hotkey), RangeNode of int/float/long/double/Vector2, ColorNode,
/// TextNode, ListNode, HotkeyNodeV2, HotkeyNode, ButtonNode, EmptyNode, CustomNode. Anything else is named in the
/// footer (hover it) rather than silently skipped, as is any row without a default. The footer also shows the draw
/// cost.</para>
/// <para>Collections: a List&lt;T&gt;, T[] or Dictionary&lt;K,V&gt; property (K string, int or enum) is a row with a
/// foldable body: one line per element with up/down/remove, an add row, an empty-state hint, and a right-click
/// reset to the fresh object's contents. Elements are edited in place: string (text, or [MenuChoices] buttons, or a
/// [MenuHexColor] picker), bool (pill), int/long/float/double (input, or a slider with [MenuRange]), enum (combo),
/// the HUD's colour struct (picker), a settings node (drawn as its row would be), and a plain class with public
/// get/set properties: folded to a title line (its first string property, or [MenuList(Summary)]) with a detail and an
/// Enabled pill, opened to its fields, which may nest one more class. A Nullable&lt;T&gt; or nullable-annotated
/// property gets a "set" checkbox, so unset stays a state of its own. [Menu] on an element property gives its label
/// and tooltip; [ConditionalDisplay] hides it; [MenuList] on the collection names the item and tunes the layout.
/// Hooks for the rest: <see cref="CustomField"/> (draw one element field yourself, e.g. an icon picker),
/// <see cref="ItemLead"/> (an icon or validity dot at the start of each element's header), <see cref="ListExtras"/>
/// (content above the rows and below the add row), and <see cref="ListEdits"/> counts every edit made through a
/// collection so the plugin knows when to recompile. On any row, <see cref="RowLead"/> draws something before the
/// label and <see cref="RowTip"/> replaces the label's tooltip with your own drawing.</para>
/// <para>The filter box searches row labels, tooltips and paths (a Custom row by its own label). It can't see inside
/// drawing it doesn't own: Tab bodies, Banner and [Submenu(RenderMethod)] content are not searched.</para>
/// <para>Techniques after syrairc's ExileMaps ExileImGui2 kit (card surfaces raised off the window colour, trailing
/// groups, search combos), ported rather than linked.</para>
/// </summary>
internal sealed class SettingsMenu
{
    // ---- options ----

    /// <summary>The plugin's colour: tab underline, slider fill, toggles, headers.</summary>
    public Vector4 Accent { get; set; } = new(0.35f, 0.72f, 1f, 1f);

    /// <summary>Name of the tab holding the root's own leaf nodes (and [MenuTab] submenus of that name).</summary>
    public string RootTabName { get; set; } = "General";

    /// <summary>Hint in the filter box.</summary>
    public string FilterHint { get; set; } = "Filter settings...";

    // Where the menu's own faults go (a reset that threw, a ColorNode the change check can't read). Owned by the
    // instance, so it lives and dies with the plugin load that built the menu (a static sink would keep a reloaded
    // plugin's old HudLog alive).
    private readonly Action<string> _fault;

    /// <param name="fault">Where faults go: pass the plugin's log (<c>m => HudLog.Fault(m)</c>) when it has one, so
    /// they follow its dedup and file routing. Null (the default) writes them with DebugWindow.LogError.</param>
    public SettingsMenu(Action<string>? fault = null)
    {
        _fault = fault ?? (m => DebugWindow.LogError(m));
        if (PackColorError != null) Fault(PackColorError);   // the static build ran before any menu (and sink) existed
    }

    private void Fault(string message)
    {
        try { _fault(message); }
        catch { /* a broken sink must not take the settings panel down; nowhere left to report it */ }
    }

    private readonly List<(string name, Action body, bool first)> _customTabs = new();
    private readonly Dictionary<string, Action> _custom = new(StringComparer.Ordinal);
    private Action? _banner;

    /// <summary>A tab of your own (a Debug tree, a guide). If a generated tab has that name, the body draws on it:
    /// after its cards, or before them with <paramref name="first"/>.
    /// One entry per tab name: calling it again with the same name replaces the body (no duplicate, no rebuild).</summary>
    public SettingsMenu Tab(string name, Action body, bool first = false)
    {
        int i = _customTabs.FindIndex(t => string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase));
        if (i < 0)
        {
            _customTabs.Add((name, body, first));
            _model = null;   // a new tab (or new content on a generated one): the tab list changes
            return this;
        }
        var old = _customTabs[i];
        if (old.first == first && old.body == body) return this;   // nothing changed
        _customTabs[i] = (old.name, body, first);
        if (_model == null) return this;
        var tab = _model.Tabs.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (tab == null || old.first != first) { _model = null; return this; }
        // same place, new body: swap it in the built model
        var list = first ? tab.Before : tab.After;
        int j = list.IndexOf(old.body);
        if (j >= 0) list[j] = body; else _model = null;
        return this;
    }

    /// <summary>Draw this property yourself, in its place: <paramref name="path"/> is the property name, dotted when
    /// nested ("Hud.Bars"). Works for [IgnoreMenu] properties and types the renderer can't draw (a Dictionary).
    /// Calling it again for the same path replaces the drawing without rebuilding the panel.</summary>
    public SettingsMenu Custom(string path, Action draw)
    {
        if (_custom.TryGetValue(path, out var old))
        {
            if (old == draw) return this;   // nothing changed
            _custom[path] = draw;
            // the row is already a Custom row: swap the delegate in place
            if (_model != null && !ReplaceCustom(_model, path, draw)) _model = null;
            return this;
        }
        _custom[path] = draw;
        _model = null;   // a new custom path changes how the model is built
        return this;
    }

    private static bool ReplaceCustom(Model m, string path, Action draw)
    {
        bool found = false;
        void Walk(Section s)
        {
            foreach (var r in s.Rows.SelectMany(Flatten))
                if (r.Kind == Kind.Custom && r.Path == path) { r.Custom = draw; found = true; }
            foreach (var c in s.Children) Walk(c);
        }
        foreach (var t in m.Tabs) foreach (var s in t.Sections) Walk(s);
        return found;
    }

    /// <summary>A strip above the tab bar (a status line, a rescan button).</summary>
    public SettingsMenu Banner(Action draw)
    {
        _banner = draw;
        return this;
    }

    private readonly Dictionary<string, Action<object>> _customFields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Action<object>> _itemLeads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Action? above, Action? below)> _listExtras = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Action> _rowLeads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Action> _rowTips = new(StringComparer.Ordinal);

    /// <summary>Inside a collection row, draw one element field yourself: <paramref name="path"/> is the collection's
    /// path, then the element property ("Statuses.Icon"; nested "Statuses.Patch.LifeColor"). The label column is drawn
    /// for you and the cursor is on the control column; the delegate gets the element being edited. Count your own edits
    /// with <see cref="ListEdited"/> when the plugin recompiles on <see cref="ListEdits"/>.</summary>
    public SettingsMenu CustomField(string path, Action<object> draw) => Hook(_customFields, path, draw);

    /// <summary>Drawn at the start of each element's header line in that collection (an icon as the bar draws it, a
    /// validity dot with its tooltip); gets the element. Keep it one frame height tall and narrow.</summary>
    public SettingsMenu ItemLead(string path, Action<object> draw) => Hook(_itemLeads, path, draw);

    /// <summary>Content inside a collection's body: <paramref name="above"/> its element lines (a search box that adds
    /// entries), <paramref name="below"/> its add row (a restore-defaults button).</summary>
    public SettingsMenu ListExtras(string path, Action? above = null, Action? below = null)
    {
        if (_listExtras.TryGetValue(path, out var old) && old.above == above && old.below == below) return this;
        _listExtras[path] = (above, below);
        _model = null;
        return this;
    }

    /// <summary>Drawn before a row's label (a colour dot, a 16 px icon). Small: the label column makes no room for it.</summary>
    public SettingsMenu RowLead(string path, Action draw) => Hook(_rowLeads, path, draw);

    /// <summary>Replaces the hover tooltip of a row's label with your own drawing (a rich tooltip through another
    /// plugin); called each frame the label is hovered, inside no tooltip window of its own.</summary>
    public SettingsMenu RowTip(string path, Action draw) => Hook(_rowTips, path, draw);

    private SettingsMenu Hook<T>(Dictionary<string, T> into, string path, T draw) where T : Delegate
    {
        if (into.TryGetValue(path, out var old) && old == draw) return this;
        into[path] = draw;
        _model = null;   // the hooks are read when the model is built; a change rebuilds it (one reflection pass)
        return this;
    }

    /// <summary>Edits made through collection rows (add, remove, reorder, a field changed), counting up: compare with
    /// the last value you saw to recompile what the lists feed. Node rows don't count; their nodes have OnValueChanged.</summary>
    public int ListEdits { get; private set; }

    /// <summary>A <see cref="CustomField"/> or <see cref="ListExtras"/> drawing changed something: counts it.</summary>
    public void ListEdited() => ListEdits++;

    /// <summary>Opens (unfolds) the element at <paramref name="index"/> of the collection at <paramref name="path"/>,
    /// and the collection itself: for a <see cref="ListExtras"/> drawing that just added one. -1 folds every element.</summary>
    public void OpenItem(string path, int index)
    {
        _listOpen[path] = index;
        _open[path] = true;
    }

    /// <summary>Milliseconds the last <see cref="Draw"/> took (also shown in the footer).</summary>
    public double LastDrawMs { get; private set; }

    /// <summary>Inside a <see cref="Custom"/> drawing: the x of the card's control column, as ImGui.SameLine(x) takes
    /// it. Draw the label, then <c>ImGui.SameLine(menu.ControlColumn)</c> and the control, and the row lines up with
    /// the generated rows around it. <see cref="Label"/> draws the label in the generated rows' style. Set per card
    /// (and per search group) before its rows draw, so a Custom row reads its own card's column; outside a card (a
    /// Tab() body, a Banner) it is <see cref="DefaultControlColumn"/>.</summary>
    public float ControlColumn { get; private set; } = DefaultControlColumn;

    /// <summary><see cref="ControlColumn"/> outside any card.</summary>
    public const float DefaultControlColumn = 160f;

    /// <summary>A row label as the generated rows draw it (aligned to the frame, tooltip on hover), for a
    /// <see cref="Custom"/> row; follow it with <c>ImGui.SameLine(menu.ControlColumn)</c>. Plain on purpose: a Custom
    /// row has no default or read-only state, so no changed-from-default dot and no reset menu.</summary>
    public static void Label(string label, string? tip = null)
    {
        ImGui.AlignTextToFramePadding();
        Text(ImGui.GetStyle().Colors[(int)ImGuiCol.Text], label);
        if (!string.IsNullOrEmpty(tip) && ImGui.IsItemHovered()) Tooltip(tip);
    }

    // ---- model ----

    private enum Kind { Toggle, RangeInt, RangeFloat, RangeLong, RangeDouble, RangeVec2, Color, Text, List, HotkeyV2, Hotkey, Button, Empty, CustomNode, Custom, Collection, Unsupported }

    private sealed class Row
    {
        public string Path = "", Label = "", Name = "";
        public string? Tip, Unit, ReadOnlyNote;
        public bool ReadOnly, Hidden;
        public int Index = -1, Parent = -1;
        public Kind Kind;
        public Type Type = typeof(object);
        public Func<object, object?> Get = _ => null;
        public Action<object, object?>? Set;   // the property's public setter, when it has one (collection reset, array resize)
        public Func<object, bool>? Visible;
        public Action? Custom;
        public Action? Lead, TipDraw;          // RowLead / RowTip hooks
        public Coll? Coll;                     // Kind.Collection: how the collection and its elements are edited
        public Section? Section;               // the section it is drawn in (a collection reset walks it on a fresh object)
        public object? Default;
        public uint? DefaultColor;   // ColorNode: packed RGBA of the default, compared without boxing
        public string DefaultText = "";   // Describe(Default), built once for the tooltip and the reset item
        public bool HasDefault;
        public readonly List<Row> Children = new();
        public bool Attached;      // placed under a parent row via parentIndex
        public Row? ParentRow;     // the row it hangs under: search honours that row's [ConditionalDisplay] too
        public int Depth;          // rows above it (set by FreezeAll): its label is indented 14 px per level
        public string ButtonId = "";   // ButtonNode: Label + "##b", built once
        // hotkey button labels, rebuilt only when the key changes (slot 0: the row's own, slot 1: a toggle's hotkey)
        public readonly int[] HkHash = { int.MinValue, int.MinValue };
        public readonly string[] HkLabel = { "", "" };
    }

    private sealed class Section
    {
        public string Path = "", Title = "", HeaderId = "";   // HeaderId: Title + "###h", built once
        public string? Tip;
        public bool Collapsed, Headerless;
        public Section? Parent;                 // the section whose object this one's Get reads; null at the root
        public Func<object, object?> Get = o => o;
        public Func<object, bool>? Visible;
        public Action<object>? RenderMethod;
        public readonly List<Row> Rows = new();
        public readonly List<Section> Children = new();
        // fixed at build time, so a frame allocates nothing walking them
        public List<Row> VisibleRows = new();   // top-level rows that draw (children hang off them)
        public List<Row> FlatRows = new();      // every drawable row including attached children: label width, search

        /// <summary>Called once per section, from FreezeAll at the end of Build.</summary>
        public void Freeze()
        {
            VisibleRows = Rows.Where(r => !r.Hidden).ToList();
            FlatRows = VisibleRows.SelectMany(Flatten).Where(r => !r.Hidden).ToList();
            HeaderId = Title + "###h";
            foreach (var r in FlatRows)
            {
                r.Depth = 0;
                for (var up = r.ParentRow; up != null; up = up.ParentRow) r.Depth++;
                r.ButtonId = r.Label + "##b";
            }
        }
    }

    private sealed class TabModel
    {
        public string Name = "";
        public readonly List<Section> Sections = new();
        public readonly List<Action> Before = new(), After = new();
        private float _fitW = -1f;
        private string _fit = "";

        /// <summary>The name shortened to fit, recomputed only when the width changes.</summary>
        public string Fitted(float maxW)
        {
            if (maxW != _fitW) { _fitW = maxW; _fit = Fit(Name, maxW); }
            return _fit;
        }
    }

    private sealed class Model
    {
        public readonly List<TabModel> Tabs = new();
        public readonly List<string> Problems = new();   // named once in the footer: what could not be drawn and why
        public readonly List<string> Notes = new();      // not wrong, worth knowing (a custom tab of its own): footer hover
        public int RowCount;
        public string? DefaultsUnavailable;
    }

    private Model? _model;
    private object? _modelFor;
    private int _activeTab;
    private string _filter = "";
    private readonly Dictionary<string, float> _anim = new();
    private readonly Dictionary<string, string> _listFilter = new();
    private readonly Dictionary<string, bool> _open = new();
    private readonly System.Diagnostics.Stopwatch _clock = new();
    private int _depth;   // card depth of the rows being drawn (a collection body raises its surface one step further)

    // ---- draw ----

    /// <summary>Call from DrawSettings every frame the panel is open.</summary>
    public void Draw(ISettings settings)
    {
        _clock.Restart();
        if (settings == null) { Text(Bad, "[SettingsMenu] Draw(null): the plugin passed no settings object"); return; }
        if (_model == null || !ReferenceEquals(_modelFor, settings))
        {
            try { _model = Build(settings); }
            catch (Exception ex)
            {
                // cached like a good model, so a broken settings class costs one reflection pass, not one per frame;
                // the per-property catches name the path, this one only catches what escaped them
                _model = new Model();
                _model.Problems.Add($"building the panel for {settings.GetType().Name} failed: {ex.GetType().Name}: {ex.Message}");
            }
            _modelFor = settings;
            _footerAt = double.NaN;   // the footer names this model's counts, not the last one's
            _activeTab = Math.Clamp(_activeTab, 0, Math.Max(0, _model.Tabs.Count - 1));   // fewer tabs after a rebuild
        }
        var m = _model;
        ControlColumn = DefaultControlColumn;   // no card yet: a Banner or Tab() body sees the default, not last frame's
        ImGui.PushID("settings-menu");
        try
        {
            if (_banner != null) Guarded(_banner, "Banner");
            DrawTabBar(m);
            var footerH = ImGui.GetTextLineHeightWithSpacing() + 4f;
            ImGui.BeginChild("##content", new Vector2(0, -footerH), ImGuiChildFlags.None, ImGuiWindowFlags.None);
            try
            {
                if (_filter.Length > 0) DrawSearch(m, settings);
                else if (m.Tabs.Count > 0) DrawTab(m.Tabs[_activeTab], settings);
                else if (m.Problems.Count > 0) foreach (var p in m.Problems) Text(Bad, p);
                else Dim("No settings to draw: no [Submenu] properties and no supported nodes on " + settings.GetType().Name);
            }
            finally { ImGui.EndChild(); }
            DrawFooter(m);
        }
        finally
        {
            ImGui.PopID();
            _clock.Stop();
            LastDrawMs = _clock.Elapsed.TotalMilliseconds;
        }
    }

    private void DrawTabBar(Model m)
    {
        const float tabH = 28f, filterW = 190f;
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        float w = ImGui.GetContentRegionAvail().X;
        float tabsW = Math.Max(60f, w - filterW - 8f);
        int n = m.Tabs.Count;
        if (n > 0)
        {
            float tabW = tabsW / n;
            for (int i = 0; i < n; i++)
            {
                var tMin = new Vector2(origin.X + i * tabW, origin.Y);
                var tMax = new Vector2(origin.X + (i + 1) * tabW, origin.Y + tabH);
                ImGui.SetCursorScreenPos(tMin);
                ImGui.PushID(i);
                ImGui.InvisibleButton("##tab", tMax - tMin);
                ImGui.PopID();
                bool hov = ImGui.IsItemHovered();
                if (ImGui.IsItemClicked()) { _activeTab = i; _filter = ""; }
                bool active = i == _activeTab && _filter.Length == 0;
                if (active) dl.AddRectFilled(tMin, tMax, U32(Accent, 0.10f), 3f);
                else if (hov) dl.AddRectFilled(tMin, tMax, U32(Accent, 0.05f), 3f);
                if (active) dl.AddLine(new Vector2(tMin.X + 3, tMax.Y - 1), new Vector2(tMax.X - 3, tMax.Y - 1), U32(Accent), 2f);
                var text = m.Tabs[i].Fitted(tabW - 10f);
                var sz = ImGui.CalcTextSize(text);
                dl.AddText((tMin + tMax) * 0.5f - sz * 0.5f, active ? U32(Accent) : hov ? U32(Accent, 0.8f) : Muted, text);
                if (hov && text != m.Tabs[i].Name) Tooltip(m.Tabs[i].Name);
            }
        }
        ImGui.SetCursorScreenPos(new Vector2(origin.X + w - filterW, origin.Y + (tabH - ImGui.GetFrameHeight()) * 0.5f));
        ImGui.SetNextItemWidth(filterW);
        ImGui.InputTextWithHint("##filter", FilterHint, ref _filter, 64);
        if (ImGui.IsItemActive() && ImGui.IsKeyPressed(ImGuiKey.Escape)) _filter = "";
        dl.AddLine(new Vector2(origin.X, origin.Y + tabH + 1), new Vector2(origin.X + w, origin.Y + tabH + 1), U32(Accent, 0.12f));
        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + tabH + 6));
    }

    private void DrawTab(TabModel tab, object root)
    {
        ImGui.PushID(tab.Name);
        try
        {
            foreach (var b in tab.Before) Guarded(b, tab.Name);
            foreach (var s in tab.Sections) DrawSection(s, root, 0);
            foreach (var a in tab.After) Guarded(a, tab.Name);
        }
        finally { ImGui.PopID(); }
    }

    private readonly List<Row> _searchHits = new();

    private void DrawSearch(Model m, object root)
    {
        int hits = 0;
        foreach (var tab in m.Tabs)
        foreach (var s in tab.Sections)
            hits += DrawSearchSection(tab.Name, s, root);
        if (hits == 0) Dim($"Nothing matches \"{_filter}\"");
    }

    private int DrawSearchSection(string crumbs, Section s, object parentOwner)
    {
        if (s.Visible != null && !s.Visible(parentOwner)) return 0;
        var owner = s.Get(parentOwner);
        if (owner == null) return 0;
        var here = string.IsNullOrEmpty(s.Title) || s.Headerless ? crumbs : crumbs + "  >  " + s.Title;
        _searchHits.Clear();
        // a hit is a row the tab would draw: hidden by [ConditionalDisplay] on itself or on any row it hangs under
        // means no hit, so the count is what is on screen and "Nothing matches" shows when nothing is
        foreach (var r in s.FlatRows)
            if (r.Kind != Kind.Empty && Matches(r) && ShownUnder(r, owner)) _searchHits.Add(r);
        int hits = _searchHits.Count;
        if (hits > 0)
        {
            ImGui.Spacing();
            Text(Accent, here);
            ImGui.Separator();
            float labelW = LabelWidth(_searchHits, indented: false);
            var outerColumn = ControlColumn;
            ControlColumn = labelW;   // this group's column, for its Custom hits
            ImGui.PushID(s.Path);
            try
            {
                // FlatRows already holds every attached child, so a hit draws itself only (no recursion)
                foreach (var r in _searchHits) DrawRow(r, owner, labelW, 0, recurse: false);
            }
            finally { ImGui.PopID(); ControlColumn = outerColumn; }
        }
        foreach (var c in s.Children) hits += DrawSearchSection(here, c, owner);
        return hits;
    }

    private static bool ShownUnder(Row r, object owner)
    {
        // the chain is acyclic (AttachByIndex never attaches into a loop) and at most Rows.Count long
        for (var x = r; x != null; x = x.ParentRow)
            if (x.Hidden || (x.Visible != null && !x.Visible(owner))) return false;
        return true;
    }

    private static IEnumerable<Row> Flatten(Row r)
    {
        yield return r;
        foreach (var c in r.Children) foreach (var x in Flatten(c)) yield return x;
    }

    private bool Matches(Row r) =>
        r.Label.Contains(_filter, StringComparison.OrdinalIgnoreCase)
        || (r.Tip?.Contains(_filter, StringComparison.OrdinalIgnoreCase) ?? false)
        || r.Path.Contains(_filter, StringComparison.OrdinalIgnoreCase);

    private void DrawSection(Section s, object parentOwner, int depth)
    {
        if (s.Visible != null && !s.Visible(parentOwner)) return;
        var owner = s.Get(parentOwner);
        if (owner == null)
        {
            Text(Bad, $"{s.Title}: the settings property {s.Path} is null (the plugin should create it in Initialise)");
            return;
        }
        ImGui.PushID(s.Path);
        var style = ImGui.GetStyle();
        var window = style.Colors[(int)ImGuiCol.WindowBg];
        var frame = style.Colors[(int)ImGuiCol.FrameBg];
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 8f));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, SurfaceFill(window, frame, depth));
        ImGui.PushStyleColor(ImGuiCol.Border, U32(Accent, 0.18f));
        ImGui.BeginChild("##card", new Vector2(0, 0), ImGuiChildFlags.Border | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysAutoResize);
        try
        {
            bool open = true;
            if (!s.Headerless)
            {
                if (!_open.TryGetValue(s.Path, out open)) open = !s.Collapsed;
                ImGui.PushStyleColor(ImGuiCol.Header, U32(Accent, 0.10f));
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, U32(Accent, 0.18f));
                ImGui.PushStyleColor(ImGuiCol.HeaderActive, U32(Accent, 0.24f));
                ImGui.PushStyleColor(ImGuiCol.Text, Accent);
                ImGui.SetNextItemOpen(open);
                open = ImGui.TreeNodeEx(s.HeaderId,ImGuiTreeNodeFlags.Framed | ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.AllowOverlap);
                ImGui.PopStyleColor(4);
                _open[s.Path] = open;
                if (!string.IsNullOrEmpty(s.Tip) && ImGui.IsItemHovered()) Tooltip(s.Tip);
                if (ImGui.BeginPopupContextItem("##cardctx"))
                {
                    if (ImGui.MenuItem("Reset this section to defaults")) ResetAll(s, owner);
                    ImGui.EndPopup();
                }
                if (open && !string.IsNullOrEmpty(s.Tip))
                {
                    ImGui.PushTextWrapPos(0f);
                    Dim(s.Tip);
                    ImGui.PopTextWrapPos();
                    ImGui.Spacing();
                }
            }
            if (open)
            {
                if (s.RenderMethod != null) Guarded(s.RenderMethod, owner, s.Path);
                float labelW = LabelWidth(s.FlatRows, indented: true);
                var outerColumn = ControlColumn;
                int outerDepth = _depth;
                ControlColumn = labelW;   // this card's column, for its Custom rows; a child card sets its own
                _depth = depth;
                try
                {
                    foreach (var r in s.VisibleRows) DrawRow(r, owner, labelW, 0);
                    foreach (var c in s.Children) { ImGui.Spacing(); DrawSection(c, owner, depth + 1); }
                }
                finally { ControlColumn = outerColumn; _depth = outerDepth; }
            }
        }
        finally
        {
            ImGui.EndChild();
            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar(2);
            ImGui.PopID();
        }
        ImGui.Spacing();
    }

    /// <summary>X of the control column, measured from the window's left edge like ImGui.SameLine(x), which ignores
    /// Indent: so an indented row's label width includes its indent and every row's control lines up.</summary>
    private static float LabelWidth(List<Row> rows, bool indented)
    {
        float w = 0f;
        foreach (var r in rows)
            if (r.Kind != Kind.Empty && r.Kind != Kind.Custom && r.Kind != Kind.CustomNode)
                w = Math.Max(w, ImGui.CalcTextSize(r.Label).X + (indented ? r.Depth * 14f : 0f));
        float avail = ImGui.GetContentRegionAvail().X;
        return Math.Clamp(w + 26f, 120f, Math.Max(120f, avail * 0.46f));
    }

    private void DrawRow(Row r, object owner, float labelW, int depth, bool recurse = true)
    {
        if (r.Visible != null && !r.Visible(owner)) return;
        ImGui.PushID(r.Path);
        if (depth > 0) ImGui.Indent(14f);
        try
        {
            switch (r.Kind)
            {
                case Kind.Empty:
                    SectionHeader(r.Label, r.Tip);
                    break;
                case Kind.Custom:
                    Guarded(r.Custom!, r.Path);   // ControlColumn already holds this card's column (DrawSection)
                    break;
                case Kind.Collection:
                    DrawCollectionRow(r, owner, labelW);
                    break;
                default:
                    DrawValueRow(r, owner, labelW);   // SameLine(x) ignores Indent: the column is the same at any depth
                    break;
            }
            if (recurse) foreach (var c in r.Children) DrawRow(c, owner, labelW, depth + 1);
        }
        finally
        {
            if (depth > 0) ImGui.Unindent(14f);
            ImGui.PopID();
        }
    }

    /// <summary>Typed for the common kinds so the per-frame check boxes nothing; the rest compare boxed.</summary>
    private static bool ChangedFromDefault(Row r, object node)
    {
        if (!r.HasDefault || r.Default == null) return false;
        switch (r.Kind)
        {
            case Kind.Toggle: return ((ToggleNode)node).Value != (bool)r.Default;
            case Kind.RangeInt: return ((RangeNode<int>)node).Value != (int)r.Default;
            case Kind.RangeFloat: return ((RangeNode<float>)node).Value != (float)r.Default;
            case Kind.RangeLong: return ((RangeNode<long>)node).Value != (long)r.Default;
            case Kind.RangeDouble: return ((RangeNode<double>)node).Value != (double)r.Default;
            case Kind.RangeVec2: return ((RangeNode<Vector2>)node).Value != (Vector2)r.Default;
            case Kind.Text: return !string.Equals(((TextNode)node).Value, (string)r.Default, StringComparison.Ordinal);
            case Kind.List: return !string.Equals(((ListNode)node).Value, (string)r.Default, StringComparison.Ordinal);
            // packed RGBA, alpha included (ColorNode.Hex drops alpha and is null until the value is first set)
            case Kind.Color: return r.DefaultColor is { } dc && PackColor != null && PackColor(node) != dc;
            case Kind.Collection: return false;   // comparing contents each frame would cost more than the dot is worth
            default: return !Equals(ValueOf(node), r.Default);
        }
    }

    private void DrawValueRow(Row r, object owner, float labelW)
    {
        var node = r.Get(owner);
        if (node == null && r.Kind != Kind.Unsupported)
        {
            Text(Bad, $"{r.Label}: {r.Path} is null (the plugin should initialise it)");
            return;
        }
        // a read-only value is the plugin's to set (an auto-detected buff name): "changed from default" means nothing
        bool changedFromDefault = !r.ReadOnly && node != null && ChangedFromDefault(r, node);
        DrawLabel(r, owner, node, changedFromDefault);
        // control column
        ImGui.SameLine(labelW);
        ImGui.SetNextItemWidth(-1);
        if (node != null) DrawControl(r, node);
        else Dim("not drawn (" + TypeName(r.Type) + ")");
        TipLine(r);
    }

    /// <summary>The label column of a row: the RowLead hook, the label, the changed-from-default dot, the tooltip (or
    /// the RowTip hook) and the right-click reset menu.</summary>
    private void DrawLabel(Row r, object owner, object? node, bool changedFromDefault)
    {
        var style = ImGui.GetStyle();
        if (r.Lead != null)
        {
            Guarded(r.Lead, r.Path);
            ImGui.SameLine(0f, 4f);
        }
        ImGui.AlignTextToFramePadding();
        Text(r.ReadOnly ? style.Colors[(int)ImGuiCol.TextDisabled] : style.Colors[(int)ImGuiCol.Text], r.Label);
        bool labelHovered = ImGui.IsItemHovered();
        if (changedFromDefault)
        {
            var lr = ImGui.GetItemRectMax();
            ImGui.GetWindowDrawList().AddCircleFilled(new Vector2(lr.X + 6f, (ImGui.GetItemRectMin().Y + lr.Y) * 0.5f), 2.5f, U32(Accent, 0.9f));
        }
        if (labelHovered && r.TipDraw != null) Guarded(r.TipDraw, r.Path);
        else if (labelHovered && (!string.IsNullOrEmpty(r.Tip) || changedFromDefault))
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(360f);
            if (!string.IsNullOrEmpty(r.Tip)) ImGui.TextUnformatted(r.Tip);
            if (changedFromDefault) Dim("default: " + r.DefaultText + "  (right-click to reset)");
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
        if (r.HasDefault && !r.ReadOnly && ImGui.BeginPopupContextItem("##ctx"))
        {
            if (ImGui.MenuItem("Reset to default: " + r.DefaultText))
            {
                if (r.Kind == Kind.Collection) ResetCollection(r, owner);
                else if (node != null) SetValue(node, r.Default);
            }
            ImGui.EndPopup();
        }
    }

    /// <summary>The description line under a row's control: the tooltip text, one line, clipped (full text on hover
    /// of the label).</summary>
    private static void TipLine(Row r)
    {
        if (!string.IsNullOrEmpty(r.Tip))
        {
            var dl = ImGui.GetWindowDrawList();
            var p = ImGui.GetCursorScreenPos();
            float maxX = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;
            dl.PushClipRect(new Vector2(p.X, p.Y), new Vector2(maxX, p.Y + ImGui.GetTextLineHeight() + 2), true);
            dl.AddText(p, Muted, r.Tip);
            dl.PopClipRect();
            ImGui.Dummy(new Vector2(1, ImGui.GetTextLineHeight()));
        }
        ImGui.Dummy(new Vector2(1, 2f));
    }

    private void DrawControl(Row r, object node)
    {
        switch (r.Kind)
        {
            case Kind.Toggle:
            {
                var n = (ToggleNode)node;
                bool v = n.Value;
                if (r.ReadOnly) { PillToggle("##t", ref v, r.Path, disabled: true); ReadOnlyBadge(r); break; }
                if (PillToggle("##t", ref v, r.Path)) n.Value = v;
                if (ImGui.BeginPopupContextItem("##hkctx"))
                {
                    Dim("Hotkey that flips this toggle");
                    n.AttachedHotkey.DrawPickerButton($"{n.AttachedHotkey.Value}##hk");   // only while the popup is open
                    ImGui.EndPopup();
                }
                else if (ImGui.IsItemHovered()) Tooltip(v ? "On (right-click: hotkey)" : "Off (right-click: hotkey)");
                if (n.AttachedHotkey.Value.Mode != HotkeyNodeV2.HotkeyNodeMode.None)
                {
                    ImGui.SameLine();
                    PushAccentButton();
                    try { n.AttachedHotkey.DrawPickerButton(HotkeyLabel(r, 1, n.AttachedHotkey.Value, n.AttachedHotkey.Value.GetHashCode(), "##hk2")); }
                    finally { ImGui.PopStyleColor(4); }
                    if (ImGui.IsItemHovered()) Tooltip("Hotkey that flips this toggle (click to change)");
                }
                break;
            }
            case Kind.RangeInt:
            {
                var n = (RangeNode<int>)node;
                if (r.ReadOnly) { ValueText(n.Value + Suffix(r)); ReadOnlyBadge(r); break; }
                int v = n.Value;
                if (Slider("##v", ref v, n.Min, n.Max, r.Unit)) n.Value = v;
                break;
            }
            case Kind.RangeFloat:
            {
                var n = (RangeNode<float>)node;
                if (r.ReadOnly) { ValueText(n.Value.ToString("0.##") + Suffix(r)); ReadOnlyBadge(r); break; }
                float v = n.Value;
                if (Slider("##v", ref v, n.Min, n.Max, r.Unit)) n.Value = v;
                break;
            }
            case Kind.RangeLong:
            {
                var n = (RangeNode<long>)node;
                if (r.ReadOnly) { ValueText(n.Value + Suffix(r)); ReadOnlyBadge(r); break; }
                // 64-bit slider: no round trip through int, no clamp at int.MaxValue
                _s64[0] = n.Value; _s64[1] = n.Min; _s64[2] = n.Max;
                PushSliderStyle();
                bool c = ImGui.SliderScalar("##v", ImGuiDataType.S64, Slot(_s64H, 0), Slot(_s64H, 1), Slot(_s64H, 2), Format("%lld", r.Unit), ImGuiSliderFlags.AlwaysClamp);
                PopSliderStyle();
                if (c) n.Value = _s64[0];
                break;
            }
            case Kind.RangeDouble:
            {
                var n = (RangeNode<double>)node;
                if (r.ReadOnly) { ValueText(n.Value.ToString("0.###") + Suffix(r)); ReadOnlyBadge(r); break; }
                // double slider: the value is never squeezed through float
                _f64[0] = n.Value; _f64[1] = n.Min; _f64[2] = n.Max;
                PushSliderStyle();
                bool c = ImGui.SliderScalar("##v", ImGuiDataType.Double, Slot(_f64H, 0), Slot(_f64H, 1), Slot(_f64H, 2), Format("%.3f", r.Unit), ImGuiSliderFlags.AlwaysClamp);
                PopSliderStyle();
                if (c) n.Value = _f64[0];
                break;
            }
            case Kind.RangeVec2:
            {
                var n = (RangeNode<Vector2>)node;
                if (r.ReadOnly) { ValueText($"{n.Value.X:0.##}, {n.Value.Y:0.##}" + Suffix(r)); ReadOnlyBadge(r); break; }
                var v = n.Value;
                PushSliderStyle();
                if (ImGui.SliderFloat2("##v", ref v, Math.Min(n.Min.X, n.Min.Y), Math.Max(n.Max.X, n.Max.Y), Format("%.1f", r.Unit), ImGuiSliderFlags.AlwaysClamp)) n.Value = v;
                PopSliderStyle();
                break;
            }
            case Kind.Color:
            {
                var n = (ColorNode)node;
                if (r.ReadOnly)
                {
                    ImGui.BeginDisabled();
                    n.DrawPicker("##c");
                    ImGui.EndDisabled();
                }
                else n.DrawPicker("##c");
                ImGui.SameLine();
                Dim(n.Hex ?? "");
                if (r.ReadOnly) { ImGui.SameLine(); ReadOnlyBadge(r); }
                break;
            }
            case Kind.Text:
            {
                var n = (TextNode)node;
                if (r.ReadOnly) { ValueText(n.Value ?? ""); ReadOnlyBadge(r); break; }
                string v = n.Value ?? "";
                if (ImGui.InputText("##x", ref v, 512)) n.Value = v;
                break;
            }
            case Kind.List:
            {
                var n = (ListNode)node;
                if (r.ReadOnly) { ValueText(n.Value ?? ""); ReadOnlyBadge(r); break; }
                ListCombo(r.Path, n);
                break;
            }
            case Kind.HotkeyV2:
            {
                var n = (HotkeyNodeV2)node;
                var label = HotkeyLabel(r, 0, n.Value, n.Value?.GetHashCode() ?? 0, r.ReadOnly ? "" : "##hk");
                if (r.ReadOnly) { ValueText(label); ReadOnlyBadge(r); break; }
                PushAccentButton();
                try { n.DrawPickerButton(label); }
                finally { ImGui.PopStyleColor(4); }
                ImGui.SameLine();
                Dim("click, then press the new key");
                break;
            }
            case Kind.Hotkey:
            {
#pragma warning disable CS0618
                var n = (HotkeyNode)node;
                var label = HotkeyLabel(r, 0, n.Value, n.Value.GetHashCode(), r.ReadOnly ? "" : "##hk");
                if (r.ReadOnly) { ValueText(label); ReadOnlyBadge(r); break; }
                PushAccentButton();
                try { n.DrawPickerButton(label); }
                finally { ImGui.PopStyleColor(4); }
#pragma warning restore CS0618
                ImGui.SameLine();
                Dim("click, then press the new key");
                break;
            }
            case Kind.Button:
            {
                var n = (ButtonNode)node;
                if (AccentButton(r.ButtonId)) Guarded(() => n.OnPressed(), r.Path);   // the closure only on a click
                break;
            }
            case Kind.CustomNode:
            {
                var n = (CustomNode)node;
                if (n.DrawDelegate != null) Guarded(n.DrawDelegate, r.Path);
                else Dim("CustomNode with no DrawDelegate");
                break;
            }
            default:
                Dim("not drawn (" + TypeName(r.Type) + ")");
                break;
        }
    }

    private void ListCombo(string path, ListNode n)
    {
        _listFilter.TryGetValue(path, out var filter);
        filter ??= "";
        ImGui.PushStyleColor(ImGuiCol.FrameBg, U32(Accent, 0.08f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, U32(Accent, 0.14f));
        bool open = ImGui.BeginCombo("##l", string.IsNullOrEmpty(n.Value) ? "(none)" : n.Value);
        ImGui.PopStyleColor(2);
        if (!open) { if (filter.Length > 0) _listFilter[path] = ""; return; }
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        ImGui.SetNextItemWidth(-1);
        bool enter = ImGui.InputTextWithHint("##f", n.AllowCustomValues ? "filter or type a value" : "filter", ref filter, 128, ImGuiInputTextFlags.EnterReturnsTrue);
        _listFilter[path] = filter;
        var values = n.Values ?? new List<string>();
        int shown = 0;
        foreach (var v in values)
        {
            if (filter.Length > 0 && !(v?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)) continue;
            shown++;
            if (ImGui.Selectable((v ?? "") + "##" + shown, v == n.Value)) { n.Value = v; ImGui.CloseCurrentPopup(); }
        }
        if (shown == 0 && !n.AllowCustomValues) Dim(values.Count == 0 ? "no values (the plugin fills this list)" : "no match");
        if (n.AllowCustomValues && filter.Length > 0 && !values.Contains(filter))
        {
            if (ImGui.Selectable($"Use \"{filter}\"##custom") || enter) { n.Value = filter; ImGui.CloseCurrentPopup(); }
        }
        ImGui.EndCombo();
    }

    private string _footer = "";
    private double _footerAt = double.NaN;   // NaN: rebuild on the next frame (a new model)

    private void DrawFooter(Model m)
    {
        ImGui.Separator();
        // the string is rebuilt four times a second, not per frame: the panel must not allocate while it idles.
        // Abs: ImGui's clock restarts with a new context, so "now" can be behind the last rebuild
        double now = ImGui.GetTime();
        if (double.IsNaN(_footerAt) || Math.Abs(now - _footerAt) > 0.25)
        {
            _footerAt = now;
            var text = $"{m.RowCount} settings  -  drawn in {LastDrawMs:0.00} ms";
            if (m.DefaultsUnavailable != null) text += "  -  reset unavailable: " + m.DefaultsUnavailable;
            if (m.Problems.Count > 0) text += $"  -  {m.Problems.Count} problem" + (m.Problems.Count == 1 ? "" : "s") + " (hover)";
            if (m.Notes.Count > 0) text += $"  -  {m.Notes.Count} note" + (m.Notes.Count == 1 ? "" : "s") + (m.Problems.Count > 0 ? "" : " (hover)");
            _footer = text;
        }
        Dim(_footer);
        if ((m.Problems.Count > 0 || m.Notes.Count > 0) && ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(520f);
            foreach (var p in m.Problems) ImGui.TextUnformatted(p);
            foreach (var n in m.Notes) Dim(n);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    // ---- widgets (also for custom blocks, so they match) ----

    /// <summary>Coloured text taken as-is: ImGui's Text/TextColored/TextDisabled are printf-style and eat a % in a
    /// label such as "Min Resource %".</summary>
    public static void Text(Vector4 color, string? text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text ?? "");
        ImGui.PopStyleColor();
    }

    /// <summary>Dim text taken as-is.</summary>
    public static void Dim(string? text) => Text(ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], text);

    /// <summary>Tooltip with the text taken as-is, wrapped. Call it when the item is hovered.</summary>
    public static void Tooltip(string? text)
    {
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(360f);
        ImGui.TextUnformatted(text ?? "");
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    /// <summary>Accent title with a rule under it.</summary>
    public void SectionHeader(string title, string? tip = null)
    {
        ImGui.Spacing();
        Text(Accent, title);
        if (!string.IsNullOrEmpty(tip) && ImGui.IsItemHovered()) Tooltip(tip);
        var p = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(p, new Vector2(p.X + ImGui.GetContentRegionAvail().X, p.Y), U32(Accent, 0.2f));
        ImGui.Dummy(new Vector2(1, 4));
    }

    /// <summary>Animated pill toggle. True the frame it flips. The animation state lives in the menu under
    /// <paramref name="animKey"/> (a row path); see the <c>ref float</c> overload to keep it yourself.</summary>
    public bool PillToggle(string id, ref bool value, string animKey, bool disabled = false)
    {
        // seeded at the state before this frame's click, so a toggle that is already on doesn't slide in from off
        // the first time, and a first-frame click still animates
        if (!_anim.TryGetValue(animKey, out float a)) a = value ? 1f : 0f;
        bool clicked = PillToggle(id, ref value, ref a, disabled);
        _anim[animKey] = a;
        return clicked;
    }

    /// <summary>Animated pill toggle with the animation state (0..1) in your own variable; seed it with 1 for an on
    /// value and 0 for off the first time, or it slides in.</summary>
    public bool PillToggle(string id, ref bool value, ref float a, bool disabled = false)
    {
        var dl = ImGui.GetWindowDrawList();
        var cur = ImGui.GetCursorScreenPos();
        const float w = 38f, h = 20f, r = h * 0.5f;
        float frameH = ImGui.GetFrameHeight();
        cur.Y += Math.Max(0f, (frameH - h) * 0.5f);
        ImGui.SetCursorScreenPos(cur);
        ImGui.InvisibleButton(id, new Vector2(w, h));
        bool clicked = !disabled && ImGui.IsItemClicked();
        if (clicked) value = !value;
        float dt = ImGui.GetIO().DeltaTime;
        a = Math.Clamp(a + ((value ? 1f : 0f) - a) * Math.Min(dt * 12f, 1f), 0f, 1f);
        var off = ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg];
        var track = Vector4.Lerp(new Vector4(off.X, off.Y, off.Z, 1f), Accent, a);
        if (disabled) track.W = 0.5f;
        dl.AddRectFilled(cur, cur + new Vector2(w, h), ImGui.GetColorU32(track), r);
        if (ImGui.IsItemHovered() && !disabled) dl.AddRect(cur, cur + new Vector2(w, h), U32(Accent, 0.6f), r);
        float knobX = cur.X + r + a * (w - h);
        float k = 0.55f + 0.45f * a;
        dl.AddCircleFilled(new Vector2(knobX, cur.Y + r), r - 3f, ImGui.GetColorU32(new Vector4(k, k, k, disabled ? 0.6f : 1f)));
        return clicked;
    }

    /// <summary>Int slider in the accent colour with the value and unit in the readout. Ctrl+click types.</summary>
    public bool Slider(string id, ref int value, int min, int max, string? unit = null)
    {
        PushSliderStyle();
        // AlwaysClamp: a Ctrl+click typed value stays inside [min, max] like a dragged one
        bool c = ImGui.SliderInt(id, ref value, min, max, Format("%d", unit), ImGuiSliderFlags.AlwaysClamp);
        PopSliderStyle();
        return c;
    }

    /// <summary>Float slider, same look.</summary>
    public bool Slider(string id, ref float value, float min, float max, string? unit = null)
    {
        PushSliderStyle();
        bool c = ImGui.SliderFloat(id, ref value, min, max, Format("%.2f", unit), ImGuiSliderFlags.AlwaysClamp);
        PopSliderStyle();
        return c;
    }

    /// <summary>Accent-outlined button.</summary>
    public bool AccentButton(string label)
    {
        bool hit;
        ImGui.PushStyleColor(ImGuiCol.Button, U32(Accent, 0.12f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, U32(Accent, 0.25f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, U32(Accent, 0.4f));
        ImGui.PushStyleColor(ImGuiCol.Text, Accent);
        hit = ImGui.Button(label);
        ImGui.PopStyleColor(4);
        ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), U32(Accent, 0.5f), ImGui.GetStyle().FrameRounding);
        return hit;
    }

    /// <summary>A small coloured pill of text (a status, a tag). Draw-list only: it never eats a click.</summary>
    public void Badge(string text, Vector4? color = null, string? tooltip = null)
    {
        var c = color ?? Accent;
        var ts = ImGui.CalcTextSize(text);
        var pad = new Vector2(6f, 1f);
        var size = ts + pad * 2;
        var org = ImGui.GetCursorScreenPos();
        org.Y += Math.Max(0f, (ImGui.GetFrameHeight() - size.Y) * 0.5f);
        ImGui.SetCursorScreenPos(org);
        ImGui.Dummy(size);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(org, org + size, U32(c, 0.18f), size.Y * 0.5f);
        dl.AddRect(org, org + size, U32(c, 0.6f), size.Y * 0.5f);
        dl.AddText(org + pad, U32(c), text);
        if (tooltip != null && ImGui.IsItemHovered()) Tooltip(tooltip);
    }

    /// <summary>A row of buttons, one per value, the chosen one lit in the accent: for 2-5 short choices. True the
    /// frame <paramref name="selected"/> changes (it is then the new index).</summary>
    public bool Segmented(string id, IReadOnlyList<string> values, ref int selected)
    {
        bool changed = false;
        ImGui.PushID(id);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2f, ImGui.GetStyle().ItemSpacing.Y));
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0) ImGui.SameLine();
            bool on = i == selected;
            ImGui.PushID(i);
            ImGui.PushStyleColor(ImGuiCol.Button, U32(Accent, on ? 0.45f : 0.10f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, U32(Accent, on ? 0.55f : 0.22f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, U32(Accent, 0.6f));
            ImGui.PushStyleColor(ImGuiCol.Text, on ? ImGui.GetStyle().Colors[(int)ImGuiCol.Text] : Accent);
            if (ImGui.Button(values[i]) && !on) { selected = i; changed = true; }
            ImGui.PopStyleColor(4);
            ImGui.PopID();
        }
        ImGui.PopStyleVar();
        ImGui.PopID();
        return changed;
    }

    /// <summary>A small clickable chip of text (a path, a token); wraps to the next line when the row is full. True
    /// when clicked.</summary>
    public bool Chip(string text, string? tip = null, bool sameLineIfRoom = true)
    {
        if (sameLineIfRoom)
        {
            var style = ImGui.GetStyle();
            float next = ImGui.GetItemRectMax().X + style.ItemSpacing.X + ImGui.CalcTextSize(text).X + 2 * style.FramePadding.X + 4f;
            if (next < ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X) ImGui.SameLine();
        }
        ImGui.PushStyleColor(ImGuiCol.Button, U32(Accent, 0.12f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, U32(Accent, 0.28f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, U32(Accent, 0.4f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 9f);
        bool hit = ImGui.SmallButton(text);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
        if (tip != null && ImGui.IsItemHovered()) Tooltip(tip);
        return hit;
    }

    private void ReadOnlyBadge(Row r)
    {
        ImGui.SameLine();
        Badge("auto", null, r.ReadOnlyNote ?? "Set by the plugin, not editable here");
    }

    private void ValueText(string text)
    {
        ImGui.AlignTextToFramePadding();
        Text(Accent, text);
    }

    /// <summary>Accent button colours for a button drawn elsewhere (a hotkey picker): pop with PopStyleColor(4).</summary>
    private void PushAccentButton()
    {
        ImGui.PushStyleColor(ImGuiCol.Button, U32(Accent, 0.12f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, U32(Accent, 0.25f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, U32(Accent, 0.4f));
        ImGui.PushStyleColor(ImGuiCol.Text, Accent);
    }

    /// <summary>"[key]" + suffix, rebuilt only when the key's hash changes, so an idle panel builds no strings.
    /// Generic so an enum key (the legacy HotkeyNode's Keys) is not boxed.</summary>
    private static string HotkeyLabel<T>(Row r, int slot, T value, int hash, string suffix)
    {
        if (r.HkHash[slot] != hash || r.HkLabel[slot].Length == 0)
        {
            r.HkHash[slot] = hash;
            r.HkLabel[slot] = "[" + value?.ToString() + "]" + suffix;
        }
        return r.HkLabel[slot];
    }

    // SliderScalar reads min/max/value through pointers: arrays on the pinned object heap never move, so their
    // element addresses are taken once and nothing is pinned or allocated per frame
    private static readonly long[] _s64 = GC.AllocateArray<long>(3, pinned: true);
    private static readonly double[] _f64 = GC.AllocateArray<double>(3, pinned: true);
    private static readonly IntPtr[] _s64H = { Addr(_s64, 0), Addr(_s64, 1), Addr(_s64, 2) };
    private static readonly IntPtr[] _f64H = { Addr(_f64, 0), Addr(_f64, 1), Addr(_f64, 2) };
    private static IntPtr Addr<T>(T[] a, int i) => System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement(a, i);
    private static IntPtr Slot(IntPtr[] h, int i) => h[i];

    // printf formats per (base, unit), built once: ImGui needs a % in a unit doubled
    private static readonly Dictionary<(string, string), string> Formats = new();
    private static string Format(string baseFmt, string? unit)
    {
        if (string.IsNullOrEmpty(unit)) return baseFmt;
        lock (Formats)
        {
            if (!Formats.TryGetValue((baseFmt, unit), out var f)) Formats[(baseFmt, unit)] = f = baseFmt + " " + unit.Replace("%", "%%");
            return f;
        }
    }

    private void PushSliderStyle()
    {
        var style = ImGui.GetStyle();
        var fb = style.Colors[(int)ImGuiCol.FrameBg];
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(fb.X * 0.8f, fb.Y * 0.8f, fb.Z * 0.8f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, U32(Accent, 0.12f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, U32(Accent, 0.18f));
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, U32(Accent, 0.9f));
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Accent);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
    }

    private static void PopSliderStyle()
    {
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(5);
    }

    private static string Suffix(Row r) => string.IsNullOrEmpty(r.Unit) ? "" : " " + r.Unit;

    private uint U32(Vector4 c, float alpha = 1f) => ImGui.GetColorU32(new Vector4(c.X, c.Y, c.Z, c.W * alpha));
    private static uint Muted => ImGui.GetColorU32(ImGuiCol.TextDisabled);
    private static readonly Vector4 Bad = new(0.9f, 0.35f, 0.35f, 1f);

    /// <summary>Card fill: the window colour nudged away from itself (lighter on dark, darker on light), pushed past
    /// the control colour so inputs still read as recessed. The SurfaceFill rule from ExileImGui2.</summary>
    private static Vector4 SurfaceFill(Vector4 window, Vector4 frame, int depth)
    {
        static float Luma(Vector4 c) => c.X * 0.299f + c.Y * 0.587f + c.Z * 0.114f;
        static Vector4 Shift(Vector4 c, float d) => new(Math.Clamp(c.X + d, 0f, 1f), Math.Clamp(c.Y + d, 0f, 1f), Math.Clamp(c.Z + d, 0f, 1f), 1f);
        float dir = Luma(window) > 0.5f ? -1f : 1f;
        var c = Shift(window, dir * (0.045f + 0.03f * depth));
        float gap = (Luma(c) - Luma(frame)) * dir;
        return gap >= 0.04f ? c : Shift(c, dir * (0.04f - gap));
    }

    private static string Fit(string text, float maxW)
    {
        if (ImGui.CalcTextSize(text).X <= maxW) return text;
        for (int n = text.Length - 1; n > 1; n--)
        {
            var t = text[..n] + "..";
            if (ImGui.CalcTextSize(t).X <= maxW) return t;
        }
        return text.Length > 0 ? text[..1] : text;
    }

    /// <summary>Runs a plugin callback; a throw is shown in place of it. The callback must keep its own
    /// Push*/Pop* balanced (try/finally): ImGui's style and ID stacks can't be unwound from here.</summary>
    private static void Guarded(Action a, string where)
    {
        try { a(); }
        catch (Exception ex)
        {
            Text(Bad, $"{where}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Same, for a callback that takes its owner: no closure per frame.</summary>
    private static void Guarded(Action<object> a, object arg, string where)
    {
        try { a(arg); }
        catch (Exception ex)
        {
            Text(Bad, $"{where}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- collections: List<T>, T[] and Dictionary<K,V> rows ----

    private enum Shape { List, Array, Dict }
    private enum Leaf { Bool, Int, Long, Float, Double, String, Enum, Color, Node, Object, Unsupported }

    /// <summary>How a value of one type is read, written and edited in one slot: a property of an element, an index of
    /// a list or array, or a key of a dictionary. Built once per slot from compiled expressions; the typed accessors
    /// take (owner, key, index) and box nothing, so an idle open list allocates nothing per frame.</summary>
    private sealed class Elem
    {
        public Type Type = typeof(object);
        public Leaf Leaf = Leaf.Unsupported;
        public string TypeName = "";
        public bool Optional;              // Nullable<T>, or a reference annotated nullable: unset is a state of its own
        public bool Settable;
        public Func<object, object?, int, bool>? Has;
        public Action<object, object?, int>? Clear;
        public Func<object, object?, int, bool>? GetBool;
        public Action<object, object?, int, bool>? SetBool;
        public Func<object, object?, int, long>? GetLong;
        public Action<object, object?, int, long>? SetLong;
        public Func<object, object?, int, double>? GetDouble;
        public Action<object, object?, int, double>? SetDouble;
        public Func<object, object?, int, string?>? GetString;
        public Action<object, object?, int, string?>? SetString;
        public Func<object, object?, int, Vector4>? GetColor;
        public Action<object, object?, int, Vector4>? SetColor;
        public Func<object, object?, int, object?>? GetObject;
        public Action<object, object?, int, object?>? SetObject;   // boxed: user actions only (add, set, tick)
        public Func<object?>? New;         // a fresh value: "" / 0 / false / the first enum member / new T()
        public string[]? Choices;          // [MenuChoices]
        public bool HasRange; public double Min, Max; public string? Unit;   // [MenuRange], [MenuUnit]
        public bool IsHex, HexArgb = true; // [MenuHexColor]
        public string[]? EnumNames; public long[]? EnumValues;
        public Row? NodeRow;               // Leaf.Node: a synthetic row, so DrawControl draws it like a settings row
        public readonly List<Field> Fields = new();   // Leaf.Object
        public Field? Summary, Detail, Enabled;       // Leaf.Object: the header line's parts
    }

    private sealed class Field
    {
        public string Name = "", Label = "", Path = "";
        public string? Tip;
        public int Index;
        public bool InHeader;              // the Enabled pill: drawn on the header line, not again in the body
        public Elem Elem = new();
        public Func<object, bool>? Visible;
        public Action<object>? Custom;
    }

    private sealed class Coll
    {
        public Shape Shape;
        public Type Type = typeof(object), ElemType = typeof(object), KeyType = typeof(object);
        public Elem Elem = new();
        public string ItemName = "item", Items = "items", AddLabel = "", AddHint = "", EmptyHint = "", KeyHint = "";
        public int MaxCount;
        public bool Chips, Collapsed, Reorder = true, AllowDuplicates;
        public string[]? KeyEnumNames; public long[]? KeyEnumValues;
        public Func<object?>? New;         // a fresh collection, for a null property with a setter
        public Action<object>? Lead;
        public Action? Above, Below;
    }

    private sealed class KeySnap
    {
        public readonly List<object?> Keys = new();
        public readonly List<string> Text = new();
        public int Count = -1;
        public double At = double.NegativeInfinity;
        public float KeyW;
    }

    private readonly record struct PillKey(object Owner, object? Key, int Index, Elem Elem);

    private readonly Dictionary<string, int> _listOpen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _listInput = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _listKeyPick = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int count, string text)> _countText = new(StringComparer.Ordinal);
    private readonly Dictionary<string, KeySnap> _dictKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<PillKey, float> _pillAnim = new();
    private static readonly List<string> ItemLabels = new();

    private enum ItemAction { None, Toggle, Up, Down, Remove }

    private void DrawCollectionRow(Row r, object owner, float labelW)
    {
        var c = r.Coll!;
        var coll = r.Get(owner);
        DrawLabel(r, owner, coll, false);
        ImGui.SameLine(labelW);
        if (coll == null)
        {
            if (r.Set != null && c.New != null)
            {
                if (AccentButton("Create##mk")) { r.Set(owner, c.New()); ListEdits++; }
                ImGui.SameLine();
            }
            Dim("null (the plugin should create it in the property initialiser)");
            TipLine(r);
            return;
        }
        int count = ((System.Collections.ICollection)coll).Count;
        if (!_open.TryGetValue(r.Path, out bool open)) open = !c.Collapsed;
        if (FoldPill(open, CountText(r.Path, c, count))) { open = !open; _open[r.Path] = open; }
        TipLine(r);
        if (!open) return;
        var style = ImGui.GetStyle();
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 6f));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, SurfaceFill(style.Colors[(int)ImGuiCol.WindowBg], style.Colors[(int)ImGuiCol.FrameBg], _depth + 1));
        ImGui.PushStyleColor(ImGuiCol.Border, U32(Accent, 0.12f));
        ImGui.BeginChild("##list", new Vector2(0, 0), ImGuiChildFlags.Border | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysAutoResize);
        try
        {
            if (c.Above != null) Guarded(c.Above, r.Path);
            bool structural = c.Shape == Shape.Dict ? DrawDict(r, c, coll, count) : DrawList(r, c, owner, coll, count);
            if (c.Below != null) Guarded(c.Below, r.Path);
            if (structural)
            {
                ListEdits++;
                _pillAnim.Clear();   // elements moved: the pills re-seed at their current value next frame (no slide)
                if (_dictKeys.TryGetValue(r.Path, out var snap)) snap.Count = -1;
            }
        }
        finally
        {
            ImGui.EndChild();
            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar(2);
        }
        ImGui.Dummy(new Vector2(1, 2f));
    }

    private string CountText(string path, Coll c, int count)
    {
        if (_countText.TryGetValue(path, out var ct) && ct.count == count) return ct.text;
        var text = count == 1 ? "1 " + c.ItemName : count + " " + c.Items;
        if (c.MaxCount > 0) text += " of " + c.MaxCount;
        _countText[path] = (count, text);
        return text;
    }

    private static string ItemLabel(int i)
    {
        while (ItemLabels.Count <= i) ItemLabels.Add("Item " + (ItemLabels.Count + 1));
        return ItemLabels[i];
    }

    /// <summary>True when the list's structure changed (the caller stops iterating: next frame draws the new order).</summary>
    private bool DrawList(Row r, Coll c, object owner, object coll, int count)
    {
        var list = (System.Collections.IList)coll;
        bool canResize = c.Shape == Shape.List || r.Set != null;
        if (!_listOpen.TryGetValue(r.Path, out int openIdx)) openIdx = -1;
        if (count == 0) Dim(c.EmptyHint);
        float trailing = TrailingWidth(c.Reorder);
        var action = ItemAction.None;
        int at = -1;
        for (int i = 0; i < count && action == ItemAction.None; i++)
        {
            ImGui.PushID(i);
            try
            {
                if (c.Chips)
                {
                    var text = c.Elem.GetString!(coll, null, i) ?? "";
                    if (Chip(text.Length == 0 ? "(empty)" : text, canResize ? "Click to remove" : null, i > 0) && canResize) { action = ItemAction.Remove; at = i; }
                    continue;
                }
                if (c.Elem.Leaf == Leaf.Object)
                {
                    var elem = c.Elem.GetObject!(coll, null, i);
                    if (elem == null)
                    {
                        ImGui.AlignTextToFramePadding();
                        Dim("(null entry)");
                        action = TrailingButtons(c, i, count, canResize, trailing);
                    }
                    else
                    {
                        action = ObjectHeader(c, elem, i, count, openIdx == i, canResize, trailing, null);
                        if (openIdx == i && action == ItemAction.None) DrawFields(c.Elem, elem);
                    }
                }
                else
                {
                    float w = ImGui.GetContentRegionAvail().X - trailing;
                    DrawLeaf(c.Elem, coll, null, i, null, null, 0f, Math.Max(60f, w));
                    action = TrailingButtons(c, i, count, canResize, trailing);
                }
                if (action != ItemAction.None) at = i;
            }
            finally { ImGui.PopID(); }
        }
        bool structural = false;
        switch (action)
        {
            case ItemAction.Toggle:
                _listOpen[r.Path] = openIdx == at ? -1 : at;
                break;
            case ItemAction.Up when at > 0:
                Swap(list, at, at - 1);
                if (openIdx == at) _listOpen[r.Path] = at - 1; else if (openIdx == at - 1) _listOpen[r.Path] = at;
                structural = true;
                break;
            case ItemAction.Down when at < count - 1:
                Swap(list, at, at + 1);
                if (openIdx == at) _listOpen[r.Path] = at + 1; else if (openIdx == at + 1) _listOpen[r.Path] = at;
                structural = true;
                break;
            case ItemAction.Remove:
                if (RemoveAt(r, c, owner, coll, at))
                {
                    _listOpen[r.Path] = openIdx == at ? -1 : openIdx > at ? openIdx - 1 : openIdx;
                    structural = true;
                }
                break;
        }
        // add row
        ImGui.Spacing();
        bool full = c.MaxCount > 0 && count >= c.MaxCount;
        bool canAdd = canResize && !full && c.Elem.New != null;
        if (c.Elem.Leaf == Leaf.String && c.Elem.Choices == null)
        {
            if (!_listInput.TryGetValue(r.Path, out var input)) input = "";
            float addW = ImGui.CalcTextSize("Add").X + 2 * ImGui.GetStyle().FramePadding.X + 8f;
            ImGui.SetNextItemWidth(Math.Max(80f, ImGui.GetContentRegionAvail().X - addW - ImGui.GetStyle().ItemSpacing.X));
            bool enter = ImGui.InputTextWithHint("##add", c.AddHint, ref input, 256, ImGuiInputTextFlags.EnterReturnsTrue);
            _listInput[r.Path] = input;
            var trimmed = input.AsSpan().Trim();
            ImGui.SameLine();
            ImGui.BeginDisabled(!canAdd || trimmed.Length == 0);
            bool click = AccentButton("Add##a");
            ImGui.EndDisabled();
            if ((enter || click) && canAdd && trimmed.Length > 0)
            {
                var value = trimmed.ToString();
                if (c.AllowDuplicates || !ContainsString(c, coll, count, value))
                {
                    if (Add(r, c, owner, coll, value)) structural = true;
                }
                _listInput[r.Path] = "";
                if (enter) ImGui.SetKeyboardFocusHere(-1);   // keep typing: the next path, the next name
            }
            if (full && ImGui.IsItemHovered()) Tooltip("Full: " + c.MaxCount + " " + c.Items + " at most");
        }
        else
        {
            ImGui.BeginDisabled(!canAdd);
            bool click = AccentButton(c.AddLabel);
            ImGui.EndDisabled();
            if (!canAdd && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tooltip(full ? "Full: " + c.MaxCount + " " + c.Items + " at most"
                    : !canResize ? "The array property has no setter, so nothing can be added"
                    : TypeName(c.ElemType) + " has no public parameterless constructor, so nothing can be added");
            if (click && canAdd && Add(r, c, owner, coll, c.Elem.New!()))
            {
                if (c.Elem.Leaf == Leaf.Object) _listOpen[r.Path] = count;   // open the new one
                structural = true;
            }
        }
        return structural;
    }

    private static bool ContainsString(Coll c, object coll, int count, string value)
    {
        for (int i = 0; i < count; i++)
            if (string.Equals(c.Elem.GetString!(coll, null, i), value, StringComparison.Ordinal)) return true;
        return false;
    }

    private static void Swap(System.Collections.IList list, int i, int j)
    {
        var a = list[i];
        list[i] = list[j];
        list[j] = a;
    }

    private bool Add(Row r, Coll c, object owner, object coll, object? value)
    {
        try
        {
            if (c.Shape == Shape.List) { ((System.Collections.IList)coll).Add(value); return true; }
            var old = (Array)coll;
            var arr = Array.CreateInstance(c.ElemType, old.Length + 1);
            Array.Copy(old, arr, old.Length);
            arr.SetValue(value, old.Length);
            r.Set!(owner, arr);
            return true;
        }
        catch (Exception ex) { Fault($"[SettingsMenu] {r.Path}: add failed: {ex.GetType().Name}: {ex.Message}"); return false; }
    }

    private bool RemoveAt(Row r, Coll c, object owner, object coll, int i)
    {
        try
        {
            if (c.Shape == Shape.List) { ((System.Collections.IList)coll).RemoveAt(i); return true; }
            var old = (Array)coll;
            var arr = Array.CreateInstance(c.ElemType, old.Length - 1);
            Array.Copy(old, 0, arr, 0, i);
            Array.Copy(old, i + 1, arr, i, old.Length - i - 1);
            r.Set!(owner, arr);
            return true;
        }
        catch (Exception ex) { Fault($"[SettingsMenu] {r.Path}: remove failed: {ex.GetType().Name}: {ex.Message}"); return false; }
    }

    private bool DrawDict(Row r, Coll c, object coll, int count)
    {
        var dict = (System.Collections.IDictionary)coll;
        if (!_dictKeys.TryGetValue(r.Path, out var snap)) _dictKeys[r.Path] = snap = new KeySnap();
        double now = ImGui.GetTime();
        if (snap.Count != count || Math.Abs(now - snap.At) > 0.5)
        {
            // the keys, boxed once here and not per frame; their text too (a dictionary edited elsewhere shows within 0.5 s)
            snap.Keys.Clear();
            snap.Text.Clear();
            snap.KeyW = 0f;
            foreach (var k in dict.Keys)
            {
                snap.Keys.Add(k);
                var text = k as string ?? k?.ToString() ?? "null";
                snap.Text.Add(text);
                snap.KeyW = Math.Max(snap.KeyW, ImGui.CalcTextSize(text).X);
            }
            snap.Count = count;
            snap.At = now;
        }
        if (!_listOpen.TryGetValue(r.Path, out int openIdx)) openIdx = -1;
        if (count == 0) Dim(c.EmptyHint);
        float trailing = TrailingWidth(false);
        float keyW = Math.Min(snap.KeyW + 16f, ImGui.GetContentRegionAvail().X * 0.4f);
        var action = ItemAction.None;
        int at = -1;
        for (int i = 0; i < snap.Keys.Count && action == ItemAction.None; i++)
        {
            var key = snap.Keys[i];
            if (key == null || !dict.Contains(key)) continue;   // removed since the snapshot
            ImGui.PushID(i);
            try
            {
                if (c.Elem.Leaf == Leaf.Object)
                {
                    var elem = c.Elem.GetObject!(coll, key, 0);
                    if (elem == null) { ImGui.AlignTextToFramePadding(); Text(Accent, snap.Text[i]); ImGui.SameLine(); Dim("(null)"); action = TrailingButtons(c, i, count, true, trailing); }
                    else
                    {
                        action = ObjectHeader(c, elem, i, count, openIdx == i, true, trailing, snap.Text[i]);
                        if (openIdx == i && action == ItemAction.None) DrawFields(c.Elem, elem);
                    }
                }
                else
                {
                    ImGui.AlignTextToFramePadding();
                    Text(Accent, snap.Text[i]);
                    if (ImGui.IsItemHovered()) Tooltip(snap.Text[i]);
                    ImGui.SameLine(keyW);
                    float w = ImGui.GetContentRegionAvail().X - trailing;
                    DrawLeaf(c.Elem, coll, key, 0, null, null, 0f, Math.Max(60f, w));
                    action = TrailingButtons(c, i, count, true, trailing);
                }
                if (action != ItemAction.None) at = i;
            }
            finally { ImGui.PopID(); }
        }
        bool structural = false;
        if (action == ItemAction.Toggle) _listOpen[r.Path] = openIdx == at ? -1 : at;
        else if (action == ItemAction.Remove)
        {
            try { dict.Remove(snap.Keys[at]!); structural = true; _listOpen[r.Path] = openIdx == at ? -1 : openIdx; }
            catch (Exception ex) { Fault($"[SettingsMenu] {r.Path}: remove failed: {ex.GetType().Name}: {ex.Message}"); }
        }
        // add row: a key, then Add
        ImGui.Spacing();
        bool full = c.MaxCount > 0 && count >= c.MaxCount;
        bool canAdd = !full && c.Elem.New != null;
        float addW = ImGui.CalcTextSize("Add").X + 2 * ImGui.GetStyle().FramePadding.X + 8f;
        object? newKey = null;
        bool enter = false, keyOk = false;
        if (c.KeyEnumNames != null)
        {
            if (!_listKeyPick.TryGetValue(r.Path, out int pick)) pick = 0;
            pick = Math.Clamp(pick, 0, c.KeyEnumNames.Length - 1);
            ImGui.SetNextItemWidth(Math.Max(80f, ImGui.GetContentRegionAvail().X - addW - ImGui.GetStyle().ItemSpacing.X));
            if (ImGui.BeginCombo("##key", c.KeyEnumNames[pick]))
            {
                for (int j = 0; j < c.KeyEnumNames.Length; j++)
                {
                    var kv = Enum.ToObject(c.KeyType, c.KeyEnumValues![j]);
                    bool present = dict.Contains(kv);
                    if (present) ImGui.BeginDisabled();
                    if (ImGui.Selectable(c.KeyEnumNames[j], j == pick)) pick = j;
                    if (present) ImGui.EndDisabled();
                }
                ImGui.EndCombo();
            }
            _listKeyPick[r.Path] = pick;
            newKey = Enum.ToObject(c.KeyType, c.KeyEnumValues![pick]);   // a small box on the frames the add row draws
            keyOk = !dict.Contains(newKey);
        }
        else
        {
            if (!_listInput.TryGetValue(r.Path, out var input)) input = "";
            ImGui.SetNextItemWidth(Math.Max(80f, ImGui.GetContentRegionAvail().X - addW - ImGui.GetStyle().ItemSpacing.X));
            enter = ImGui.InputTextWithHint("##key", c.AddHint, ref input, 256, ImGuiInputTextFlags.EnterReturnsTrue);
            _listInput[r.Path] = input;
            var trimmed = input.AsSpan().Trim();
            if (trimmed.Length > 0)
            {
                if (c.KeyType == typeof(string)) { newKey = trimmed.ToString(); keyOk = !dict.Contains(newKey); }
                else if (int.TryParse(trimmed, out int ik)) { newKey = ik; keyOk = !dict.Contains(newKey); }
            }
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(!canAdd || !keyOk);
        bool click = AccentButton("Add##a");
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (full) Tooltip("Full: " + c.MaxCount + " " + c.Items + " at most");
            else if (newKey != null && !keyOk) Tooltip("That key is already in the list");
        }
        if ((enter || click) && canAdd && keyOk && newKey != null)
        {
            try
            {
                dict[newKey] = c.Elem.New!();
                structural = true;
                _listInput[r.Path] = "";
                if (c.Elem.Leaf == Leaf.Object) _listOpen[r.Path] = count;   // the new key lands at the end of the snapshot
            }
            catch (Exception ex) { Fault($"[SettingsMenu] {r.Path}: add failed: {ex.GetType().Name}: {ex.Message}"); }
        }
        return structural;
    }

    private static float TrailingWidth(bool reorder)
    {
        var style = ImGui.GetStyle();
        float x = ImGui.CalcTextSize("x").X + 2 * style.FramePadding.X;
        return (reorder ? 2 * ImGui.GetFrameHeight() + 2 * style.ItemSpacing.X : 0f) + x + style.ItemSpacing.X + 2f;
    }

    /// <summary>The header line of a class element: the Enabled pill, the ItemLead hook, the title (click folds or
    /// opens it), the muted detail, the fold chevron, then up / down / remove. Nothing here builds a string.</summary>
    private ItemAction ObjectHeader(Coll c, object elem, int i, int count, bool open, bool canResize, float trailing, string? titleOverride)
    {
        var e = c.Elem;
        bool enabled = true;
        if (e.Enabled != null)
        {
            enabled = e.Enabled.Elem.GetBool!(elem, null, 0);
            var pk = new PillKey(elem, null, 0, e.Enabled.Elem);
            if (!_pillAnim.TryGetValue(pk, out float a)) a = enabled ? 1f : 0f;
            bool v = enabled;
            if (PillToggle("##en", ref v, ref a)) { e.Enabled.Elem.SetBool!(elem, null, 0, v); enabled = v; ListEdits++; }
            _pillAnim[pk] = a;
            if (ImGui.IsItemHovered()) Tooltip(e.Enabled.Tip ?? e.Enabled.Label);
            ImGui.SameLine();
        }
        if (c.Lead != null)
        {
            Guarded(c.Lead, elem, "ItemLead");
            ImGui.SameLine();
        }
        var style = ImGui.GetStyle();
        var title = titleOverride ?? Title(e, elem, i);
        string? detail = e.Detail != null && e.Detail != e.Summary ? e.Detail.Elem.GetString!(elem, null, 0) : null;
        if (string.IsNullOrEmpty(detail) || string.Equals(detail, title, StringComparison.Ordinal)) detail = null;
        var at = ImGui.GetCursorScreenPos();
        float w = Math.Max(40f, ImGui.GetContentRegionAvail().X - trailing);
        float h = ImGui.GetFrameHeight();
        ImGui.PushStyleColor(ImGuiCol.Header, U32(Accent, 0.10f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, U32(Accent, 0.16f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, U32(Accent, 0.22f));
        bool clicked = ImGui.Selectable("##row", open, ImGuiSelectableFlags.None, new Vector2(w, h));
        ImGui.PopStyleColor(3);
        bool hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        float textY = at.Y + (h - ImGui.GetTextLineHeight()) * 0.5f;
        var textCol = enabled ? ImGui.GetColorU32(ImGuiCol.Text) : Muted;
        dl.PushClipRect(at, new Vector2(at.X + w - 16f, at.Y + h), true);
        dl.AddText(new Vector2(at.X + style.FramePadding.X, textY), textCol, title);
        if (detail != null)
        {
            float tx = at.X + style.FramePadding.X + ImGui.CalcTextSize(title).X + 10f;
            if (tx < at.X + w - 30f) dl.AddText(new Vector2(tx, textY), Muted, detail);
        }
        dl.PopClipRect();
        Chevron(dl, new Vector2(at.X + w - 9f, at.Y + h * 0.5f), open, U32(Accent, hovered ? 1f : 0.6f));
        if (hovered && detail != null) Tooltip(detail);
        var action = clicked ? ItemAction.Toggle : ItemAction.None;
        var trailingAction = TrailingButtons(c, i, count, canResize, trailing);
        return trailingAction != ItemAction.None ? trailingAction : action;
    }

    private static string Title(Elem e, object elem, int i)
    {
        var t = e.Summary?.Elem.GetString!(elem, null, 0);
        if (string.IsNullOrEmpty(t)) t = e.Detail?.Elem.GetString!(elem, null, 0);
        return string.IsNullOrEmpty(t) ? ItemLabel(i) : t;
    }

    /// <summary>Up, down and remove at the right edge of an element line.</summary>
    private ItemAction TrailingButtons(Coll c, int i, int count, bool canResize, float trailing)
    {
        var action = ItemAction.None;
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - trailing + ImGui.GetStyle().ItemSpacing.X);
        ImGui.PushStyleColor(ImGuiCol.Button, U32(Accent, 0.08f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, U32(Accent, 0.22f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, U32(Accent, 0.35f));
        ImGui.PushStyleColor(ImGuiCol.Text, Accent);
        try
        {
            if (c.Reorder)
            {
                ImGui.BeginDisabled(i == 0);
                if (ImGui.ArrowButton("##up", ImGuiDir.Up)) action = ItemAction.Up;
                ImGui.EndDisabled();
                if (i > 0 && ImGui.IsItemHovered()) Tooltip("Move up (earlier)");
                ImGui.SameLine();
                ImGui.BeginDisabled(i >= count - 1);
                if (ImGui.ArrowButton("##dn", ImGuiDir.Down)) action = ItemAction.Down;
                ImGui.EndDisabled();
                if (i < count - 1 && ImGui.IsItemHovered()) Tooltip("Move down (later)");
                ImGui.SameLine();
            }
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(Bad.X, Bad.Y, Bad.Z, 0.45f));
            ImGui.BeginDisabled(!canResize);
            if (ImGui.Button("x")) action = ItemAction.Remove;
            ImGui.EndDisabled();
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) Tooltip(canResize ? "Remove" : "The array property has no setter, so nothing can be removed");
        }
        finally { ImGui.PopStyleColor(4); }
        return action;
    }

    /// <summary>An open element's fields: label column, control, nested classes as tree nodes; an accent rule down
    /// the left marks the open element.</summary>
    private void DrawFields(Elem e, object elem)
    {
        const float indent = 12f;
        ImGui.Indent(indent);
        var top = ImGui.GetCursorScreenPos();
        float labelW = 0f;
        foreach (var f in e.Fields)
            if (!f.InHeader) labelW = Math.Max(labelW, ImGui.CalcTextSize(f.Label).X);
        float avail = ImGui.GetContentRegionAvail().X;
        labelW = Math.Clamp(labelW + indent + 20f, 100f, Math.Max(100f, (avail + indent) * 0.42f));
        try
        {
            foreach (var f in e.Fields)
            {
                if (f.InHeader) continue;
                if (f.Visible != null && !f.Visible(elem)) continue;
                ImGui.PushID(f.Index);
                try
                {
                    if (f.Custom != null)
                    {
                        Label(f.Label, f.Tip);
                        ImGui.SameLine(labelW);
                        ImGui.SetNextItemWidth(-1);
                        Guarded(f.Custom, elem, f.Path);
                    }
                    else DrawLeaf(f.Elem, elem, null, 0, f.Label, f.Tip, labelW, -1f);
                }
                finally { ImGui.PopID(); }
            }
        }
        finally
        {
            var bottom = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddLine(new Vector2(top.X - 7f, top.Y + 2f), new Vector2(top.X - 7f, bottom.Y - 4f), U32(Accent, 0.4f), 2f);
            ImGui.Unindent(indent);
            ImGui.Dummy(new Vector2(1, 3f));
        }
    }

    /// <summary>One value in one slot: an optional label column, the "set" box of an optional value, then the control
    /// sized to <paramref name="width"/> (-1: the rest of the line).</summary>
    private void DrawLeaf(Elem e, object owner, object? key, int index, string? label, string? tip, float labelW, float width)
    {
        if (e.Leaf == Leaf.Object) { DrawObjectField(e, owner, key, index, label, tip); return; }
        if (label != null)
        {
            Label(label, tip);
            ImGui.SameLine(labelW);
        }
        if (e.Optional)
        {
            bool has = e.Has!(owner, key, index);
            if (ImGui.Checkbox("##has", ref has) && e.Settable)
            {
                if (has) SetFresh(e, owner, key, index);
                else e.Clear?.Invoke(owner, key, index);
                ListEdits++;
            }
            if (ImGui.IsItemHovered()) Tooltip(has ? "Set: untick to leave it unchanged" : "Not set: tick to set a value");
            if (!has) { ImGui.SameLine(); Dim("not set"); return; }
            ImGui.SameLine();
            if (width > 0) width = Math.Max(40f, width - ImGui.GetFrameHeight() - ImGui.GetStyle().ItemSpacing.X);
        }
        ImGui.SetNextItemWidth(width);
        if (!e.Settable) ImGui.BeginDisabled();
        try
        {
            switch (e.Leaf)
            {
                case Leaf.Bool:
                {
                    bool v = e.GetBool!(owner, key, index);
                    var pk = new PillKey(owner, key, index, e);
                    if (!_pillAnim.TryGetValue(pk, out float a)) a = v ? 1f : 0f;
                    if (PillToggle("##v", ref v, ref a, !e.Settable)) { e.SetBool!(owner, key, index, v); ListEdits++; }
                    _pillAnim[pk] = a;
                    break;
                }
                case Leaf.Int:
                {
                    int v = (int)e.GetLong!(owner, key, index);
                    bool c = e.HasRange ? Slider("##v", ref v, (int)e.Min, (int)e.Max, e.Unit) : ImGui.InputInt("##v", ref v);
                    if (c && e.Settable) { e.SetLong!(owner, key, index, v); ListEdits++; }
                    break;
                }
                case Leaf.Long:
                {
                    _s64[0] = e.GetLong!(owner, key, index);
                    bool c;
                    if (e.HasRange)
                    {
                        _s64[1] = (long)e.Min; _s64[2] = (long)e.Max;
                        PushSliderStyle();
                        c = ImGui.SliderScalar("##v", ImGuiDataType.S64, Slot(_s64H, 0), Slot(_s64H, 1), Slot(_s64H, 2), Format("%lld", e.Unit), ImGuiSliderFlags.AlwaysClamp);
                        PopSliderStyle();
                    }
                    else c = ImGui.InputScalar("##v", ImGuiDataType.S64, Slot(_s64H, 0));
                    if (c && e.Settable) { e.SetLong!(owner, key, index, _s64[0]); ListEdits++; }
                    break;
                }
                case Leaf.Float:
                {
                    float v = (float)e.GetDouble!(owner, key, index);
                    bool c = e.HasRange ? Slider("##v", ref v, (float)e.Min, (float)e.Max, e.Unit) : ImGui.InputFloat("##v", ref v, 0f, 0f, "%.3f");
                    if (c && e.Settable) { e.SetDouble!(owner, key, index, v); ListEdits++; }
                    break;
                }
                case Leaf.Double:
                {
                    _f64[0] = e.GetDouble!(owner, key, index);
                    bool c;
                    if (e.HasRange)
                    {
                        _f64[1] = e.Min; _f64[2] = e.Max;
                        PushSliderStyle();
                        c = ImGui.SliderScalar("##v", ImGuiDataType.Double, Slot(_f64H, 0), Slot(_f64H, 1), Slot(_f64H, 2), Format("%.3f", e.Unit), ImGuiSliderFlags.AlwaysClamp);
                        PopSliderStyle();
                    }
                    else c = ImGui.InputScalar("##v", ImGuiDataType.Double, Slot(_f64H, 0));
                    if (c && e.Settable) { e.SetDouble!(owner, key, index, _f64[0]); ListEdits++; }
                    break;
                }
                case Leaf.String:
                {
                    var s = e.GetString!(owner, key, index) ?? "";
                    if (e.Choices != null)
                    {
                        int sel = Array.IndexOf(e.Choices, s);
                        bool c;
                        if (e.Choices.Length <= 5 && ChoicesFit(e.Choices)) c = Segmented("##v", e.Choices, ref sel);
                        else
                        {
                            c = false;
                            if (ImGui.BeginCombo("##v", sel >= 0 ? e.Choices[sel] : s.Length == 0 ? "(none)" : s))
                            {
                                for (int j = 0; j < e.Choices.Length; j++)
                                    if (ImGui.Selectable(e.Choices[j], j == sel)) { sel = j; c = true; }
                                ImGui.EndCombo();
                            }
                        }
                        if (c && e.Settable && sel >= 0) { e.SetString!(owner, key, index, e.Choices[sel]); ListEdits++; }
                    }
                    else if (e.IsHex)
                    {
                        var col = ParseHex(s, e.HexArgb, out bool ok);
                        if (!ok) col = new Vector4(1f, 1f, 1f, 1f);
                        ImGui.SetNextItemWidth(-1);
                        bool c = ImGui.ColorEdit4("##v", ref col, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf);
                        ImGui.SameLine();
                        if (ok) Dim(s); else Text(Bad, s.Length == 0 ? "(empty: white)" : "not a hex colour");
                        if (c && e.Settable) { e.SetString!(owner, key, index, FormatHex(col, e.HexArgb)); ListEdits++; }
                    }
                    else if (ImGui.InputText("##v", ref s, 512) && e.Settable) { e.SetString!(owner, key, index, s); ListEdits++; }
                    break;
                }
                case Leaf.Enum:
                {
                    long v = e.GetLong!(owner, key, index);
                    int sel = Array.IndexOf(e.EnumValues!, v);
                    ImGui.PushStyleColor(ImGuiCol.FrameBg, U32(Accent, 0.08f));
                    ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, U32(Accent, 0.14f));
                    bool open = ImGui.BeginCombo("##v", sel >= 0 ? e.EnumNames![sel] : "(not a named value)");
                    ImGui.PopStyleColor(2);
                    if (open)
                    {
                        for (int j = 0; j < e.EnumNames!.Length; j++)
                            if (ImGui.Selectable(e.EnumNames[j], j == sel) && e.Settable) { e.SetLong!(owner, key, index, e.EnumValues![j]); ListEdits++; }
                        ImGui.EndCombo();
                    }
                    break;
                }
                case Leaf.Color:
                {
                    var v = e.GetColor!(owner, key, index);
                    if (ImGui.ColorEdit4("##v", ref v, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf) && e.Settable && e.SetColor != null)
                    { e.SetColor(owner, key, index, v); ListEdits++; }
                    break;
                }
                case Leaf.Node:
                {
                    var node = e.GetObject!(owner, key, index);
                    if (node == null) Dim("null node (the plugin should create it)");
                    else DrawControl(e.NodeRow!, node);
                    break;
                }
                default:
                    Dim("not editable (" + e.TypeName + ")");
                    break;
            }
        }
        finally { if (!e.Settable) ImGui.EndDisabled(); }
    }

    private static bool ChoicesFit(string[] choices)
    {
        int n = 0;
        foreach (var c in choices) n += c.Length;
        return n <= 40;
    }

    /// <summary>A nested class in an element: a tree node with its fields, and for an optional one a set / clear control.</summary>
    private void DrawObjectField(Elem e, object owner, object? key, int index, string? label, string? tip)
    {
        var obj = e.GetObject!(owner, key, index);
        ImGui.PushStyleColor(ImGuiCol.Text, Accent);
        bool open = ImGui.TreeNodeEx(label ?? "Item", ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.AllowOverlap);
        ImGui.PopStyleColor();
        if (!string.IsNullOrEmpty(tip) && ImGui.IsItemHovered()) Tooltip(tip);
        if (e.Optional && e.Settable)
        {
            ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - 70f);
            if (obj == null)
            {
                if (AccentButton("Set up##mk") && e.New != null) { e.SetObject!(owner, key, index, e.New()); ListEdits++; }
                if (ImGui.IsItemHovered()) Tooltip("Not set: create it to fill its fields");
            }
            else
            {
                if (ImGui.SmallButton("clear##rm")) { e.Clear?.Invoke(owner, key, index); ListEdits++; }
                if (ImGui.IsItemHovered()) Tooltip("Unset it: back to no value at all");
            }
        }
        if (open)
        {
            if (obj == null) Dim(e.Optional ? "not set" : "null (the plugin should create it)");
            else DrawFields(e, obj);
            ImGui.TreePop();
        }
    }

    private void SetFresh(Elem e, object owner, object? key, int index)
    {
        if (e.New == null || e.SetObject == null) return;
        try { e.SetObject(owner, key, index, e.New()); }
        catch (Exception ex) { Fault($"[SettingsMenu] setting a fresh {e.TypeName} failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    private bool FoldPill(bool open, string text)
    {
        var ts = ImGui.CalcTextSize(text);
        var pad = new Vector2(8f, 2f);
        var size = new Vector2(ts.X + pad.X * 2 + 12f, ts.Y + pad.Y * 2);
        var org = ImGui.GetCursorScreenPos();
        org.Y += Math.Max(0f, (ImGui.GetFrameHeight() - size.Y) * 0.5f);
        ImGui.SetCursorScreenPos(org);
        bool clicked = ImGui.InvisibleButton("##fold", size);
        bool hov = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(org, org + size, U32(Accent, hov ? 0.2f : 0.1f), size.Y * 0.5f);
        Chevron(dl, new Vector2(org.X + pad.X + 4f, org.Y + size.Y * 0.5f), open, U32(Accent));
        dl.AddText(new Vector2(org.X + pad.X + 12f, org.Y + pad.Y), U32(Accent), text);
        return clicked;
    }

    private static void Chevron(ImDrawListPtr dl, Vector2 c, bool open, uint col)
    {
        const float s = 4f;
        if (open) dl.AddTriangleFilled(c + new Vector2(-s, -s * 0.5f), c + new Vector2(s, -s * 0.5f), c + new Vector2(0, s * 0.7f), col);
        else dl.AddTriangleFilled(c + new Vector2(-s * 0.5f, -s), c + new Vector2(s * 0.7f, 0), c + new Vector2(-s * 0.5f, s), col);
    }

    /// <summary>"#AARRGGBB" / "#RRGGBB" (argb) or "#RRGGBBAA" to a colour; <paramref name="ok"/> false when it isn't one.</summary>
    private static Vector4 ParseHex(string s, bool argb, out bool ok)
    {
        var t = s.AsSpan().Trim();
        if (t.Length > 0 && t[0] == '#') t = t[1..];
        uint v = 0;
        ok = (t.Length == 8 || t.Length == 6) && uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out v);
        if (!ok) return default;
        uint a, r, g, b;
        if (t.Length == 6) { a = 255; r = (v >> 16) & 0xFF; g = (v >> 8) & 0xFF; b = v & 0xFF; }
        else if (argb) { a = (v >> 24) & 0xFF; r = (v >> 16) & 0xFF; g = (v >> 8) & 0xFF; b = v & 0xFF; }
        else { r = (v >> 24) & 0xFF; g = (v >> 16) & 0xFF; b = (v >> 8) & 0xFF; a = v & 0xFF; }
        return new Vector4(r / 255f, g / 255f, b / 255f, a / 255f);
    }

    private static string FormatHex(Vector4 c, bool argb)
    {
        static uint B(float f) => (uint)Math.Clamp((int)MathF.Round(f * 255f), 0, 255);
        uint v = argb ? B(c.W) << 24 | B(c.X) << 16 | B(c.Y) << 8 | B(c.Z) : B(c.X) << 24 | B(c.Y) << 16 | B(c.Z) << 8 | B(c.W);
        return "#" + v.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Right-click reset of a collection: the property set to what a fresh settings object holds there (the
    /// fresh object is built per reset, so each one starts from untouched contents).</summary>
    private void ResetCollection(Row r, object owner)
    {
        if (r.Set == null || _modelFor == null) return;
        try
        {
            var root = Activator.CreateInstance(_modelFor.GetType()) ?? throw new InvalidOperationException("CreateInstance returned null");
            var freshOwner = r.Section == null ? root : FreshOwner(r.Section, root);
            if (freshOwner == null) { Fault($"[SettingsMenu] {r.Path}: reset found no fresh owner object"); return; }
            r.Set(owner, r.Get(freshOwner));
            _listOpen[r.Path] = -1;
            ListEdits++;
        }
        catch (Exception ex) { Fault($"[SettingsMenu] reset of {r.Path} failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static object? FreshOwner(Section s, object root)
    {
        var parent = s.Parent == null ? root : FreshOwner(s.Parent, root);
        return parent == null ? null : s.Get(parent);
    }

    // ---- building collection descriptors ----

    private static readonly Type? ColorValueType = typeof(ColorNode).GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)?.PropertyType;
    private static readonly NullabilityInfoContext Nullability = new();

    private delegate Expression SlotOf(ParameterExpression owner, ParameterExpression key, ParameterExpression index);

    private Coll? BuildColl(Model m, PropertyInfo p, string path)
    {
        var t = p.PropertyType;
        var attr = p.GetCustomAttribute<MenuListAttribute>() ?? new MenuListAttribute();
        var c = new Coll { Type = t, MaxCount = Math.Max(0, attr.MaxCount), Collapsed = attr.Collapsed, AllowDuplicates = attr.AllowDuplicates };
        SlotOf slot;
        if (t.IsArray && t.GetArrayRank() == 1)
        {
            c.Shape = Shape.Array;
            c.ElemType = t.GetElementType()!;
            slot = (o, _, i) => Expression.ArrayAccess(Expression.Convert(o, t), i);
            c.New = () => Array.CreateInstance(c.ElemType, 0);
        }
        else if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
        {
            c.Shape = Shape.List;
            c.ElemType = t.GetGenericArguments()[0];
            slot = (o, _, i) => Expression.Property(Expression.Convert(o, t), "Item", i);
            c.New = () => Activator.CreateInstance(t);
        }
        else if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            c.Shape = Shape.Dict;
            c.KeyType = t.GetGenericArguments()[0];
            c.ElemType = t.GetGenericArguments()[1];
            if (c.KeyType.IsEnum)
            {
                c.KeyEnumNames = Enum.GetNames(c.KeyType);
                c.KeyEnumValues = Enum.GetValues(c.KeyType).Cast<object>().Select(Convert.ToInt64).ToArray();
            }
            else if (c.KeyType != typeof(string) && c.KeyType != typeof(int))
            {
                m.Problems.Add($"{path}: Dictionary keys of {TypeName(c.KeyType)} can't be typed into the add row (string, int or an enum can): register SettingsMenu.Custom(\"{path}\", ...) or add [IgnoreMenu]");
                return null;
            }
            var keyType = c.KeyType;
            slot = (o, k, _) => Expression.Property(Expression.Convert(o, t), "Item", Expression.Convert(k, keyType));
            c.New = () => Activator.CreateInstance(t);
        }
        else return null;
        c.Elem = BuildElem(m, c.ElemType, path, slot, settable: true, attrs: p, optional: false, new HashSet<Type>(), depth: 0);
        if (c.Elem.Leaf == Leaf.Unsupported) return null;   // BuildElem named the reason
        c.ItemName = string.IsNullOrWhiteSpace(attr.ItemName) ? "item" : attr.ItemName.Trim();
        c.Items = Plural(c.ItemName);
        c.AddLabel = "+ Add " + c.ItemName;
        c.Chips = attr.Chips && c.Elem.Leaf == Leaf.String && c.Shape != Shape.Dict;
        c.Reorder = !attr.NoReorder && c.Shape != Shape.Dict;
        c.KeyHint = string.IsNullOrWhiteSpace(attr.KeyName) ? "key" : attr.KeyName.Trim();
        bool typed = c.Shape == Shape.Dict || (c.Elem.Leaf == Leaf.String && c.Elem.Choices == null);
        c.AddHint = c.Shape == Shape.Dict ? "new " + c.KeyHint + ", then Add (or Enter)" : "new " + c.ItemName + ", then Add (or Enter)";
        c.EmptyHint = "No " + c.Items + " yet" + (typed ? ": type one below" : ": add one below");
        if (c.Elem.Leaf == Leaf.Object)
        {
            var fields = c.Elem.Fields;
            Field? Named(string? name, string what)
            {
                if (string.IsNullOrEmpty(name)) return null;
                var f = fields.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal));
                if (f == null) m.Problems.Add($"{path}: [MenuList({what} = \"{name}\")] names no public get/set property on {TypeName(c.ElemType)}");
                return f;
            }
            c.Elem.Summary = Named(attr.Summary, nameof(MenuListAttribute.Summary)) ?? fields.FirstOrDefault(f => f.Elem.Leaf == Leaf.String && !f.Elem.Optional && f.Custom == null);
            c.Elem.Detail = Named(attr.Detail, nameof(MenuListAttribute.Detail)) ?? fields.FirstOrDefault(f => f.Elem.Leaf == Leaf.String && !f.Elem.Optional && f.Custom == null && f != c.Elem.Summary);
            c.Elem.Enabled = Named(attr.Enabled, nameof(MenuListAttribute.Enabled)) ?? fields.FirstOrDefault(f => f.Elem.Leaf == Leaf.Bool && !f.Elem.Optional && f.Custom == null
                && (f.Name is "Enabled" or "Enable" or "On" or "IsEnabled" or "Active"));
            if (c.Elem.Summary != null && c.Elem.Summary.Elem.Leaf != Leaf.String) { m.Problems.Add($"{path}: [MenuList(Summary)] must name a string property"); c.Elem.Summary = null; }
            if (c.Elem.Detail != null && c.Elem.Detail.Elem.Leaf != Leaf.String) { m.Problems.Add($"{path}: [MenuList(Detail)] must name a string property"); c.Elem.Detail = null; }
            if (c.Elem.Enabled != null && (c.Elem.Enabled.Elem.Leaf != Leaf.Bool || c.Elem.Enabled.Elem.Optional)) { m.Problems.Add($"{path}: [MenuList(Enabled)] must name a plain bool property"); c.Elem.Enabled = null; }
            if (c.Elem.Enabled != null) c.Elem.Enabled.InHeader = true;
        }
        if (_itemLeads.TryGetValue(path, out var lead)) c.Lead = lead;
        if (_listExtras.TryGetValue(path, out var extras)) { c.Above = extras.above; c.Below = extras.below; }
        return c;
    }

    private static string Plural(string word)
    {
        if (word.EndsWith("s", StringComparison.OrdinalIgnoreCase) || word.EndsWith("x", StringComparison.OrdinalIgnoreCase)
            || word.EndsWith("ch", StringComparison.OrdinalIgnoreCase) || word.EndsWith("sh", StringComparison.OrdinalIgnoreCase)) return word + "es";
        if (word.Length > 1 && word.EndsWith("y", StringComparison.OrdinalIgnoreCase) && !"aeiou".Contains(char.ToLowerInvariant(word[^2]))) return word[..^1] + "ies";
        return word + "s";
    }

    /// <summary>Compiles the accessors for one slot. <paramref name="attrs"/> carries the [Menu*] attributes that
    /// shape the editor (the element property, or the collection property for its elements).</summary>
    private Elem BuildElem(Model m, Type t, string path, SlotOf slot, bool settable, PropertyInfo? attrs, bool optional, HashSet<Type> onPath, int depth)
    {
        var e = new Elem { Type = t, Settable = settable, TypeName = TypeName(t) };
        try
        {
            var o = Expression.Parameter(typeof(object), "o");
            var k = Expression.Parameter(typeof(object), "k");
            var i = Expression.Parameter(typeof(int), "i");
            var access = slot(o, k, i);
            var nul = Nullable.GetUnderlyingType(t);
            var inner = nul ?? t;
            Expression value = nul != null ? Expression.Coalesce(access, Expression.Default(nul)) : access;
            if (nul != null) e.Optional = true;
            else if (optional && !t.IsValueType) e.Optional = true;
            if (e.Optional)
            {
                Expression has = nul != null ? Expression.Property(access, "HasValue") : Expression.NotEqual(access, Expression.Constant(null, t));
                e.Has = Expression.Lambda<Func<object, object?, int, bool>>(has, o, k, i).Compile();
                if (settable) e.Clear = Expression.Lambda<Action<object, object?, int>>(Expression.Assign(access, Expression.Default(t)), o, k, i).Compile();
            }
            if (attrs != null)
            {
                e.Choices = attrs.GetCustomAttribute<MenuChoicesAttribute>()?.Values;
                var range = attrs.GetCustomAttribute<MenuRangeAttribute>();
                if (range != null) { e.HasRange = true; e.Min = range.Min; e.Max = range.Max; }
                e.Unit = attrs.GetCustomAttribute<MenuUnitAttribute>()?.Unit;
                var hex = attrs.GetCustomAttribute<MenuHexColorAttribute>();
                if (hex != null) { e.IsHex = true; e.HexArgb = hex.Argb; }
            }
            // the boxed pair: user actions (a tick, an add) and the Node / Object leaves
            e.GetObject = Expression.Lambda<Func<object, object?, int, object?>>(Expression.Convert(access, typeof(object)), o, k, i).Compile();
            if (settable)
            {
                var vo = Expression.Parameter(typeof(object), "v");
                e.SetObject = Expression.Lambda<Action<object, object?, int, object?>>(Expression.Assign(access, Expression.Convert(vo, t)), o, k, i, vo).Compile();
            }
            if (inner == typeof(bool))
            {
                e.Leaf = Leaf.Bool;
                e.GetBool = Expression.Lambda<Func<object, object?, int, bool>>(value, o, k, i).Compile();
                if (settable) { var v = Expression.Parameter(typeof(bool), "v"); e.SetBool = Expression.Lambda<Action<object, object?, int, bool>>(Expression.Assign(access, Expression.Convert(v, t)), o, k, i, v).Compile(); }
                e.New = () => false;
            }
            else if (inner == typeof(int) || inner == typeof(short) || inner == typeof(byte) || inner == typeof(sbyte) || inner == typeof(ushort)
                     || inner == typeof(long) || inner == typeof(uint))
            {
                e.Leaf = inner == typeof(long) || inner == typeof(uint) ? Leaf.Long : Leaf.Int;
                e.GetLong = Expression.Lambda<Func<object, object?, int, long>>(Expression.Convert(value, typeof(long)), o, k, i).Compile();
                if (settable) { var v = Expression.Parameter(typeof(long), "v"); e.SetLong = Expression.Lambda<Action<object, object?, int, long>>(Expression.Assign(access, Expression.Convert(Expression.Convert(v, inner), t)), o, k, i, v).Compile(); }
                e.New = () => Activator.CreateInstance(inner);
            }
            else if (inner == typeof(float) || inner == typeof(double))
            {
                e.Leaf = inner == typeof(float) ? Leaf.Float : Leaf.Double;
                e.GetDouble = Expression.Lambda<Func<object, object?, int, double>>(Expression.Convert(value, typeof(double)), o, k, i).Compile();
                if (settable) { var v = Expression.Parameter(typeof(double), "v"); e.SetDouble = Expression.Lambda<Action<object, object?, int, double>>(Expression.Assign(access, Expression.Convert(Expression.Convert(v, inner), t)), o, k, i, v).Compile(); }
                e.New = () => Activator.CreateInstance(inner);
            }
            else if (inner == typeof(string))
            {
                e.Leaf = Leaf.String;
                e.GetString = Expression.Lambda<Func<object, object?, int, string?>>(access, o, k, i).Compile();
                if (settable) { var v = Expression.Parameter(typeof(string), "v"); e.SetString = Expression.Lambda<Action<object, object?, int, string?>>(Expression.Assign(access, v), o, k, i, v).Compile(); }
                var first = e.Choices is { Length: > 0 } ch ? ch[0] : e.IsHex ? "#FFFFFFFF" : "";
                e.New = () => first;
            }
            else if (inner.IsEnum)
            {
                e.Leaf = Leaf.Enum;
                var under = Enum.GetUnderlyingType(inner);
                e.EnumNames = Enum.GetNames(inner);
                e.EnumValues = Enum.GetValues(inner).Cast<object>().Select(Convert.ToInt64).ToArray();
                e.GetLong = Expression.Lambda<Func<object, object?, int, long>>(Expression.Convert(Expression.Convert(value, under), typeof(long)), o, k, i).Compile();
                if (settable) { var v = Expression.Parameter(typeof(long), "v"); e.SetLong = Expression.Lambda<Action<object, object?, int, long>>(Expression.Assign(access, Expression.Convert(Expression.Convert(Expression.Convert(v, under), inner), t)), o, k, i, v).Compile(); }
                var firstValue = e.EnumValues.Length > 0 ? Enum.ToObject(inner, e.EnumValues[0]) : Activator.CreateInstance(inner);
                e.New = () => firstValue;
            }
            else if (ColorValueType != null && inner == ColorValueType)
            {
                e.Leaf = Leaf.Color;
                e.GetColor = Expression.Lambda<Func<object, object?, int, Vector4>>(ColorToVec(value), o, k, i).Compile();
                if (settable && VecToColor(inner) is { } make)
                {
                    var v = Expression.Parameter(typeof(Vector4), "v");
                    e.SetColor = Expression.Lambda<Action<object, object?, int, Vector4>>(Expression.Assign(access, Expression.Convert(make(v), t)), o, k, i, v).Compile();
                }
                e.New = () => Activator.CreateInstance(inner);
            }
            else if (KindOf(inner) is not (Kind.Unsupported or Kind.Collection or Kind.Empty or Kind.Custom))
            {
                e.Leaf = Leaf.Node;
                e.NodeRow = new Row { Path = path, Label = "", Kind = KindOf(inner), Type = inner, Unit = e.Unit, ButtonId = "Press##b" };
                var ctor = inner.GetConstructor(Type.EmptyTypes);
                if (ctor != null) e.New = () => Activator.CreateInstance(inner);
            }
            else if (inner.IsClass && !inner.IsAbstract && KindOf(inner) == Kind.Unsupported && !typeof(Delegate).IsAssignableFrom(inner))
            {
                if (depth >= 3) { m.Problems.Add($"{path}: {TypeName(t)} nests deeper than the list editor follows (3 levels)"); return e; }
                e.Leaf = Leaf.Object;
                if (inner.GetConstructor(Type.EmptyTypes) != null) e.New = () => Activator.CreateInstance(inner);
                BuildFields(m, inner, path, e.Fields, onPath, depth + 1);
                if (e.Fields.Count == 0) m.Problems.Add($"{path}: {TypeName(t)} has no public get/set property the list editor can draw");
            }
            else
            {
                m.Problems.Add($"{path}: {TypeName(t)} is not a value the list editor edits (string, bool, numbers, enums, {TypeName(ColorValueType ?? typeof(object))}, settings nodes, or a class of those): register SettingsMenu.Custom(\"{path}\", ...) or add [IgnoreMenu]");
            }
        }
        catch (Exception ex)
        {
            e.Leaf = Leaf.Unsupported;
            m.Problems.Add($"{path}: building the editor for {TypeName(t)} threw {ex.GetType().Name}: {ex.Message}");
        }
        return e;
    }

    private void BuildFields(Model m, Type t, string path, List<Field> into, HashSet<Type> onPath, int depth)
    {
        if (!onPath.Add(t))
        {
            m.Problems.Add($"{path}: {TypeName(t)} contains itself, so its fields are drawn once, not again here");
            return;
        }
        try
        {
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(x => x.MetadataToken))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                if (p.GetMethod is not { IsPublic: true }) continue;
                if (p.GetCustomAttribute<IgnoreMenuAttribute>() != null) continue;
                if (p.GetCustomAttributes().Any(a => a.GetType().Name == "JsonIgnoreAttribute")) continue;   // not saved: not a setting
                bool settable = p.SetMethod is { IsPublic: true };
                if (!settable && (p.PropertyType.IsValueType || p.PropertyType == typeof(string))) continue;   // computed: nothing to edit
                var fPath = path + "." + p.Name;
                var menu = p.GetCustomAttribute<MenuAttribute>();
                var f = new Field { Name = p.Name, Label = menu?.MenuName ?? DefaultName(p.Name), Tip = menu?.Tooltip, Path = fPath, Index = into.Count, Visible = Condition(m, p, fPath) };
                if (_customFields.TryGetValue(fPath, out var custom))
                {
                    f.Custom = custom;
                    f.Elem = new Elem { Type = p.PropertyType, TypeName = TypeName(p.PropertyType) };
                    into.Add(f);
                    continue;
                }
                bool optional = false;
                if (!p.PropertyType.IsValueType)
                {
                    try { optional = Nullability.Create(p).WriteState == NullabilityState.Nullable; } catch { /* no annotations: plain */ }
                }
                var owner = t;
                f.Elem = BuildElem(m, p.PropertyType, fPath, (o, _, _) => Expression.Property(Expression.Convert(o, owner), p), settable, p, optional, onPath, depth);
                into.Add(f);
            }
        }
        finally { onPath.Remove(t); }
    }

    /// <summary>The HUD's colour struct (ColorNode.Value) read as a Vector4 through its R/G/B/A members (bytes or floats).</summary>
    private static Expression ColorToVec(Expression c)
    {
        Expression Ch(string name)
        {
            var ch = Expression.PropertyOrField(c, name);
            if (ch.Type == typeof(float)) return ch;
            if (ch.Type == typeof(double)) return Expression.Convert(ch, typeof(float));
            return Expression.Divide(Expression.Convert(ch, typeof(float)), Expression.Constant(255f));
        }
        var ctor = typeof(Vector4).GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(float) })!;
        return Expression.New(ctor, Ch("R"), Ch("G"), Ch("B"), Ch("A"));
    }

    /// <summary>A Vector4 back to the colour struct: FromArgb(a, r, g, b), a (byte r, g, b, a) constructor or a float
    /// one, whichever the type has; null when none does (the picker is then read-only).</summary>
    private static Func<ParameterExpression, Expression>? VecToColor(Type ct)
    {
        Expression B(Expression f)
        {
            var clamp = typeof(Math).GetMethod(nameof(Math.Clamp), new[] { typeof(float), typeof(float), typeof(float) })!;
            var round = typeof(MathF).GetMethod(nameof(MathF.Round), new[] { typeof(float) })!;
            return Expression.Convert(Expression.Call(clamp, Expression.Call(round, Expression.Multiply(f, Expression.Constant(255f))), Expression.Constant(0f), Expression.Constant(255f)), typeof(int));
        }
        var fromArgb = ct.GetMethod("FromArgb", BindingFlags.Public | BindingFlags.Static, new[] { typeof(int), typeof(int), typeof(int), typeof(int) });
        if (fromArgb != null && fromArgb.ReturnType == ct)
            return v => Expression.Call(fromArgb, B(Expression.Field(v, "W")), B(Expression.Field(v, "X")), B(Expression.Field(v, "Y")), B(Expression.Field(v, "Z")));
        var bytes = ct.GetConstructor(new[] { typeof(byte), typeof(byte), typeof(byte), typeof(byte) });
        if (bytes != null)
            return v => Expression.New(bytes, Expression.Convert(B(Expression.Field(v, "X")), typeof(byte)), Expression.Convert(B(Expression.Field(v, "Y")), typeof(byte)),
                Expression.Convert(B(Expression.Field(v, "Z")), typeof(byte)), Expression.Convert(B(Expression.Field(v, "W")), typeof(byte)));
        var floats = ct.GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(float) });
        if (floats != null)
            return v => Expression.New(floats, Expression.Field(v, "X"), Expression.Field(v, "Y"), Expression.Field(v, "Z"), Expression.Field(v, "W"));
        return null;
    }

    // ---- model building (reflection once per settings object, compiled getters afterwards) ----

    private Model Build(ISettings settings)
    {
        var m = new Model();
        var rootType = settings.GetType();
        TabModel TabNamed(string name)
        {
            var t = m.Tabs.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (t == null) { t = new TabModel { Name = name }; m.Tabs.Add(t); }
            return t;
        }

        var rootSection = new Section { Path = "", Title = RootTabName, Headerless = true };
        var onPath = new HashSet<Type> { rootType };   // settings types being built: a type that contains itself stops here
        var leafTabs = new Dictionary<Row, MenuTabAttribute>();   // root-level rows with [MenuTab], moved after parentIndex linking
        var leafSections = new Dictionary<string, (Section section, bool last)>(StringComparer.OrdinalIgnoreCase);   // their cards, per tab
        foreach (var p in rootType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        try
        {
            if (p.GetIndexParameters().Length > 0) continue;
            if (Unreadable(m, p, p.Name)) continue;
            var menu = p.GetCustomAttribute<MenuAttribute>();
            var submenu = p.GetCustomAttribute<SubmenuAttribute>() ?? p.PropertyType.GetCustomAttribute<SubmenuAttribute>();
            bool isSub = submenu != null || typeof(ISettings).IsAssignableFrom(p.PropertyType);
            if (isSub && !_custom.ContainsKey(p.Name))
            {
                if (p.GetCustomAttribute<IgnoreMenuAttribute>() != null) continue;
                var tabName = p.GetCustomAttribute<MenuTabAttribute>()?.Tab ?? menu?.MenuName ?? DefaultName(p.Name);
                var section = BuildSection(m, p, p.Name, menu, submenu, onPath);
                if (section == null) continue;
                if (string.Equals(tabName, RootTabName, StringComparison.OrdinalIgnoreCase) && !m.Tabs.Any(t => t.Sections.Contains(rootSection)))
                {
                    // the root's own rows first, then this submenu's card, on the root tab
                    var t = TabNamed(RootTabName);
                    if (rootSection.Rows.Count > 0 || RootHasRows(rootType)) t.Sections.Insert(0, rootSection);
                    t.Sections.Add(section);
                }
                else TabNamed(tabName).Sections.Add(section);
                continue;
            }
            if (p.Name == "Enable" && p.PropertyType == typeof(ToggleNode)) continue;   // the HUD's own checkbox in the plugin list
            var row = BuildRow(m, p, p.Name, p.Name);
            if (row == null) continue;
            row.Section = rootSection;
            rootSection.Rows.Add(row);
            var leafTabAttr = p.GetCustomAttribute<MenuTabAttribute>();
            var leafTab = leafTabAttr?.Tab?.Trim();
            if (leafTabAttr != null && !string.IsNullOrEmpty(leafTab) && !string.Equals(leafTab, RootTabName, StringComparison.OrdinalIgnoreCase))
                leafTabs[row] = leafTabAttr;
        }
        catch (Exception ex) { m.Problems.Add($"{p.Name}: not drawn, building it threw {ex.GetType().Name}: {ex.Message}"); }
        // parentIndex links first, across every root-level row, then [MenuTab] moves top-level rows (their attached
        // children with them) to that tab's own headerless card at the top of the tab. A tagged row attached under an
        // untagged one stays with its parent: named in the notes.
        AttachByIndex(m, rootSection);
        foreach (var (row, tabAttr) in leafTabs)
        {
            var tab = tabAttr.Tab.Trim();
            if (row.Attached)
            {
                m.Problems.Add($"{row.Path}: [MenuTab(\"{tab}\")] ignored, the row hangs under {row.ParentRow?.Path} by parentIndex and stays with it (tag the parent, or drop the parentIndex)");
                continue;
            }
            // the first tagged row decides whether the tab's leaf card goes above or below its submenu cards
            if (!leafSections.TryGetValue(tab, out var entry))
                leafSections[tab] = entry = (new Section { Path = "#tab:" + tab, Title = tab, Headerless = true }, tabAttr.Last);
            row.Section = entry.section;
            entry.section.Rows.Add(row);
            rootSection.Rows.Remove(row);
        }
        var submenuTabs = m.Tabs.Select(x => x.Name).ToList();   // before leaf and custom ones
        foreach (var (name, (ls, last)) in leafSections)
        {
            // a new tab is right for a flat settings class; one that nearly matches a submenu tab is likely a typo
            if (!m.Tabs.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
                && NearTab(name, submenuTabs) is { } meant)
                m.Notes.Add($"[MenuTab(\"{name}\")]: no submenu tab has that name, so it is a tab of its own (meant \"{meant}\"?)");
            if (last) TabNamed(name).Sections.Add(ls); else TabNamed(name).Sections.Insert(0, ls);
        }
        if (rootSection.Rows.Count == 0)   // every root row moved away: no empty card on the root tab, nor an empty tab
        {
            foreach (var t in m.Tabs) t.Sections.Remove(rootSection);
            m.Tabs.RemoveAll(t => t.Sections.Count == 0 && t.Before.Count == 0 && t.After.Count == 0);
        }
        if (rootSection.Rows.Count > 0 && !m.Tabs.Any(t => t.Sections.Contains(rootSection)))
        {
            var t = TabNamed(RootTabName);
            t.Sections.Insert(0, rootSection);
            if (m.Tabs[0] != t) { m.Tabs.Remove(t); m.Tabs.Insert(0, t); }
        }
        var generatedTabs = m.Tabs.Select(x => x.Name).ToList();   // every generated tab, before the custom ones
        foreach (var (name, body, first) in _customTabs)
        {
            // a Tab() name no generated tab has makes a tab of its own: right for a Debug tab, a likely typo when it
            // nearly matches a generated tab, so only that case is named
            if (!m.Tabs.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
                && NearTab(name, generatedTabs) is { } meant)
                m.Notes.Add($"Tab(\"{name}\"): no generated tab has that name, so it is a tab of its own (meant \"{meant}\"?)");
            var t = TabNamed(name);
            if (first) t.Before.Add(body); else t.After.Add(body);
        }
        foreach (var t in m.Tabs) foreach (var s in t.Sections) FreezeAll(s);
        m.RowCount = m.Tabs.Sum(t => t.Sections.Sum(CountRows));
        BuildDefaults(m, settings);
        return m;
    }

    /// <summary>The tab in <paramref name="tabs"/> that <paramref name="name"/> nearly matches (the same letters and
    /// digits ignoring case, spaces and punctuation, or 1 edit away, 2 when both are over 6 characters), or null.</summary>
    private static string? NearTab(string name, List<string> tabs)
    {
        static string Norm(string s) => new string(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var a = Norm(name);
        foreach (var tab in tabs)
        {
            var b = Norm(tab);
            if (a == b) return tab;
            int max = Math.Min(a.Length, b.Length) > 6 ? 2 : 1;
            if (a.Length >= 3 && b.Length >= 3 && Math.Abs(a.Length - b.Length) <= max && EditDistance(a, b) <= max) return tab;
        }
        return null;
    }

    private static int EditDistance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    private static bool RootHasRows(Type rootType) =>
        rootType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(p =>
            p.Name != "Enable" && p.GetCustomAttribute<IgnoreMenuAttribute>() == null && IsLeafNode(p.PropertyType));

    private static bool IsLeafNode(Type t) => KindOf(t) != Kind.Unsupported;

    private static int CountRows(Section s) => s.Rows.Sum(r => Flatten(r).Count(x => !x.Hidden && x.Kind != Kind.Empty)) + s.Children.Sum(CountRows);

    /// <summary>A property without a public getter can't be drawn: named in the footer, never thrown.</summary>
    private static bool Unreadable(Model m, PropertyInfo p, string path)
    {
        if (p.GetMethod is { IsPublic: true }) return false;
        m.Problems.Add($"{path}: has no public getter, so it can't be drawn (add [IgnoreMenu] or a getter)");
        return true;
    }

    private Section? BuildSection(Model m, PropertyInfo p, string path, MenuAttribute? menu, SubmenuAttribute? submenu, HashSet<Type> onPath)
    {
        if (!onPath.Add(p.PropertyType))
        {
            m.Problems.Add($"{path}: {TypeName(p.PropertyType)} contains itself through its submenus, so it is drawn once, not again here (add [IgnoreMenu])");
            return null;
        }
        try { return BuildSectionBody(m, p, path, menu, submenu, onPath); }
        finally { onPath.Remove(p.PropertyType); }
    }

    private Section BuildSectionBody(Model m, PropertyInfo p, string path, MenuAttribute? menu, SubmenuAttribute? submenu, HashSet<Type> onPath)
    {
        var s = new Section
        {
            Path = path,
            Title = menu?.MenuName ?? DefaultName(p.Name),
            Tip = menu?.Tooltip,
            Collapsed = submenu?.CollapsedByDefault ?? menu?.CollapsedByDefault ?? false,
            Get = Getter(p),
            Visible = Condition(m, p, path),
        };
        if (submenu?.RenderMethod is { } rm)
        {
            var method = p.PropertyType.GetMethod(rm, BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
            if (method == null) m.Problems.Add($"{path}: [Submenu(RenderMethod = \"{rm}\")] names no public parameterless instance method on {p.PropertyType.Name}");
            else
            {
                var o = Expression.Parameter(typeof(object), "o");
                s.RenderMethod = Expression.Lambda<Action<object>>(Expression.Call(Expression.Convert(o, p.PropertyType), method), o).Compile();
            }
        }
        foreach (var c in p.PropertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        try
        {
            if (c.GetIndexParameters().Length > 0) continue;
            var cPath = path + "." + c.Name;
            if (Unreadable(m, c, cPath)) continue;
            var cMenu = c.GetCustomAttribute<MenuAttribute>();
            var cSub = c.GetCustomAttribute<SubmenuAttribute>() ?? c.PropertyType.GetCustomAttribute<SubmenuAttribute>();
            if ((cSub != null || typeof(ISettings).IsAssignableFrom(c.PropertyType)) && !_custom.ContainsKey(cPath))
            {
                if (c.GetCustomAttribute<IgnoreMenuAttribute>() != null) continue;
                if (BuildSection(m, c, cPath, cMenu, cSub, onPath) is { } child) { child.Parent = s; s.Children.Add(child); }
                continue;
            }
            var row = BuildRow(m, c, cPath, c.Name);
            if (row != null) { row.Section = s; s.Rows.Add(row); }
        }
        catch (Exception ex) { m.Problems.Add($"{path}.{c.Name}: not drawn, building it threw {ex.GetType().Name}: {ex.Message}"); }
        AttachByIndex(m, s);
        return s;
    }

    private Row? BuildRow(Model m, PropertyInfo p, string path, string name)
    {
        var menu = p.GetCustomAttribute<MenuAttribute>();
        var row = new Row
        {
            Path = path,
            Name = name,
            Label = menu?.MenuName ?? DefaultName(name),
            Tip = menu?.Tooltip,
            Index = menu?.index ?? -1,
            Parent = menu?.parentIndex ?? -1,
            Type = p.PropertyType,
            Unit = p.GetCustomAttribute<MenuUnitAttribute>()?.Unit,
        };
        var ro = p.GetCustomAttribute<MenuReadOnlyAttribute>();
        if (ro != null) { row.ReadOnly = true; row.ReadOnlyNote = ro.Note; }
        if (_rowLeads.TryGetValue(path, out var lead)) row.Lead = lead;
        if (_rowTips.TryGetValue(path, out var tipDraw)) row.TipDraw = tipDraw;
        if (_custom.TryGetValue(path, out var custom))
        {
            row.Kind = Kind.Custom;
            row.Custom = custom;
            row.Visible = Condition(m, p, path);   // [ConditionalDisplay] hides a custom row like any other
            return row;
        }
        if (p.GetCustomAttribute<IgnoreMenuAttribute>() != null) return null;
        row.Kind = KindOf(p.PropertyType);
        if (row.Kind == Kind.Unsupported)
        {
            m.Problems.Add($"{path}: {TypeName(p.PropertyType)} is not a settings node this renderer draws (register SettingsMenu.Custom(\"{path}\", ...) or add [IgnoreMenu])");
            row.Hidden = true;
            return row;
        }
        if (row.Kind == Kind.Collection)
        {
            row.Coll = BuildColl(m, p, path);   // names what it can't edit in the footer
            if (row.Coll == null) { row.Kind = Kind.Unsupported; row.Hidden = true; return row; }
            row.Set = Setter(p);
        }
        row.Get = Getter(p);
        row.Visible = Condition(m, p, path);
        return row;
    }

    private static Action<object, object?>? Setter(PropertyInfo p)
    {
        if (p.SetMethod is not { IsPublic: true }) return null;
        var o = Expression.Parameter(typeof(object), "o");
        var v = Expression.Parameter(typeof(object), "v");
        var body = Expression.Assign(Expression.Property(Expression.Convert(o, p.DeclaringType!), p), Expression.Convert(v, p.PropertyType));
        return Expression.Lambda<Action<object, object?>>(body, o, v).Compile();
    }

    private static Kind KindOf(Type t)
    {
        if (t == typeof(ToggleNode)) return Kind.Toggle;
        if (t == typeof(RangeNode<int>)) return Kind.RangeInt;
        if (t == typeof(RangeNode<float>)) return Kind.RangeFloat;
        if (t == typeof(RangeNode<long>)) return Kind.RangeLong;
        if (t == typeof(RangeNode<double>)) return Kind.RangeDouble;
        if (t == typeof(RangeNode<Vector2>)) return Kind.RangeVec2;
        if (t == typeof(ColorNode)) return Kind.Color;
        if (t == typeof(TextNode)) return Kind.Text;
        if (t == typeof(ListNode)) return Kind.List;
        if (t == typeof(HotkeyNodeV2)) return Kind.HotkeyV2;
#pragma warning disable CS0618
        if (t == typeof(HotkeyNode)) return Kind.Hotkey;
#pragma warning restore CS0618
        if (t == typeof(ButtonNode)) return Kind.Button;
        if (t == typeof(EmptyNode)) return Kind.Empty;
        if (t == typeof(CustomNode)) return Kind.CustomNode;
        if (t.IsArray && t.GetArrayRank() == 1) return Kind.Collection;
        if (t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(List<>) || t.GetGenericTypeDefinition() == typeof(Dictionary<,>))) return Kind.Collection;
        return Kind.Unsupported;
    }

    /// <summary>[Menu(parentIndex: N)] rows move under the row whose [Menu(index: N)] matches, inside this section.
    /// A parent chain that loops back (A under B, B under A) is named and left flat: nothing disappears.</summary>
    private static void AttachByIndex(Model m, Section s)
    {
        var byIndex = new Dictionary<int, Row>();
        foreach (var r in s.Rows) if (r.Index != -1 && !byIndex.ContainsKey(r.Index)) byIndex[r.Index] = r;
        var where = s.Path == "" ? "the root" : s.Path;
        foreach (var r in s.Rows.ToList())
        {
            if (r.Parent == -1) continue;
            if (!byIndex.TryGetValue(r.Parent, out var parent) || parent == r)
            {
                m.Problems.Add($"{r.Path}: [Menu(parentIndex: {r.Parent})] names no row with index {r.Parent} in {where}: drawn unattached");
                continue;
            }
            // walk the parent's own chain: if it reaches r, attaching would make a cycle. The visited set ends the
            // walk on a loop further up that r is not part of (A under B, B under C, C under B): A still attaches,
            // since B and C are left flat when their own turn comes. At most Rows.Count steps either way.
            bool cycle = false;
            var seen = new HashSet<Row> { r };
            for (var up = parent; up != null; )
            {
                if (!seen.Add(up)) { cycle = up == r; break; }
                if (up.Parent == -1 || !byIndex.TryGetValue(up.Parent, out var next)) break;
                up = next;
            }
            if (cycle)
            {
                m.Problems.Add($"{r.Path}: [Menu(parentIndex: {r.Parent})] closes a parentIndex loop through {parent.Path} in {where}: drawn unattached");
                continue;
            }
            parent.Children.Add(r);
            r.Attached = true;
            r.ParentRow = parent;
        }
        s.Rows.RemoveAll(r => r.Attached);
    }

    /// <summary>The one place sections freeze their draw lists: after every row is built and attached.</summary>
    private static void FreezeAll(Section s)
    {
        s.Freeze();
        foreach (var c in s.Children) FreezeAll(c);
    }

    private static Func<object, object?> Getter(PropertyInfo p)
    {
        var o = Expression.Parameter(typeof(object), "o");
        var body = Expression.Convert(Expression.Property(Expression.Convert(o, p.DeclaringType!), p), typeof(object));
        return Expression.Lambda<Func<object, object?>>(body, o).Compile();
    }

    /// <summary>[ConditionalDisplay] on the property or its type: a bool method or property on the owner.</summary>
    private static Func<object, bool>? Condition(Model m, PropertyInfo p, string path)
    {
        var cd = p.GetCustomAttribute<ConditionalDisplayAttribute>() ?? p.PropertyType.GetCustomAttribute<ConditionalDisplayAttribute>();
        if (cd == null) return null;
        var owner = p.DeclaringType!;
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var o = Expression.Parameter(typeof(object), "o");
        Expression? call = null;
        var method = owner.GetMethod(cd.ConditionMethodName, any, Type.EmptyTypes);
        if (method != null && method.ReturnType == typeof(bool)) call = Expression.Call(Expression.Convert(o, owner), method);
        var prop = owner.GetProperty(cd.ConditionMethodName, any);
        if (call == null && prop != null && prop.PropertyType == typeof(bool)) call = Expression.Property(Expression.Convert(o, owner), prop);
        if (call == null)
        {
            m.Problems.Add($"{path}: [ConditionalDisplay(\"{cd.ConditionMethodName}\")] names no bool method or property on {owner.Name}: always shown");
            return null;
        }
        var cmp = Expression.Equal(call, Expression.Constant(cd.ComparisonValue));
        return Expression.Lambda<Func<object, bool>>(cmp, o).Compile();
    }

    // ---- defaults: a fresh settings object, read through the same getters ----

    private static readonly Dictionary<Type, (Func<object, object?> get, Action<object, object?>? set)> ValueAccessors = new();

    private static (Func<object, object?> get, Action<object, object?>? set)? Accessor(Type nodeType)
    {
        lock (ValueAccessors)
        {
            if (ValueAccessors.TryGetValue(nodeType, out var a)) return a;
            var vp = nodeType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            if (vp == null || !vp.CanRead) return null;
            var o = Expression.Parameter(typeof(object), "o");
            var get = Expression.Lambda<Func<object, object?>>(Expression.Convert(Expression.Property(Expression.Convert(o, nodeType), vp), typeof(object)), o).Compile();
            Action<object, object?>? set = null;
            if (vp.CanWrite && vp.SetMethod!.IsPublic)
            {
                var v = Expression.Parameter(typeof(object), "v");
                set = Expression.Lambda<Action<object, object?>>(Expression.Assign(Expression.Property(Expression.Convert(o, nodeType), vp), Expression.Convert(v, vp.PropertyType)), o, v).Compile();
            }
            ValueAccessors[nodeType] = (get, set);
            return (get, set);
        }
    }

    private static object? ValueOf(object node) => Accessor(node.GetType())?.get(node);

    private void SetValue(object node, object? value)
    {
        var a = Accessor(node.GetType());
        if (a?.set == null || value == null) return;
        try { a.Value.set(node, value); }
        catch (Exception ex) { Fault($"[SettingsMenu] reset of {node.GetType().Name} failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    private void BuildDefaults(Model m, ISettings settings)
    {
        object fresh;
        try
        {
            fresh = Activator.CreateInstance(settings.GetType())
                    ?? throw new InvalidOperationException("CreateInstance returned null");
        }
        catch (Exception ex)
        {
            m.DefaultsUnavailable = $"new {settings.GetType().Name}() failed ({(ex is TargetInvocationException { InnerException: { } ie } ? ie : ex).Message})";
            return;
        }
        foreach (var t in m.Tabs) foreach (var s in t.Sections) SnapshotDefaults(m, s, fresh);
    }

    /// <summary>Reads each row's value on the fresh object. A row it can't read has no reset and no changed-dot,
    /// and the footer says which and why.</summary>
    private static void SnapshotDefaults(Model m, Section s, object parentFresh)
    {
        object? fresh;
        try { fresh = s.Get(parentFresh); }
        catch (Exception ex)
        {
            m.Problems.Add($"{(s.Path == "" ? "root" : s.Path)}: no defaults (reading it on a fresh settings object threw {ex.GetType().Name}: {ex.Message})");
            return;
        }
        if (fresh == null)
        {
            m.Problems.Add($"{s.Path}: no defaults (it is null on a fresh settings object; create it in the property initialiser)");
            return;
        }
        foreach (var r in s.Rows.SelectMany(Flatten))
        {
            if (r.Kind is Kind.Empty or Kind.Custom or Kind.CustomNode or Kind.Button or Kind.Unsupported) continue;
            object? node;
            try { node = r.Get(fresh); }
            catch (Exception ex)
            {
                m.Problems.Add($"{r.Path}: no default (reading it on a fresh settings object threw {ex.GetType().Name}: {ex.Message})");
                continue;
            }
            if (node == null) { m.Problems.Add($"{r.Path}: no default (null on a fresh settings object)"); continue; }
            if (r.Kind == Kind.Collection)
            {
                // reset re-reads a fresh object, so only the count is kept, for the menu item's text
                int n = ((System.Collections.ICollection)node).Count;
                r.DefaultText = n == 1 ? "1 " + r.Coll!.ItemName : n + " " + r.Coll!.Items;
                r.HasDefault = r.Set != null;
                continue;
            }
            var v = ValueOf(node);
            if (v == null) continue;
            r.Default = v;
            if (r.Kind == Kind.Color && PackColor != null)
            {
                uint packed = PackColor(node);
                r.DefaultColor = packed;
                r.DefaultText = $"#{packed:X8}";   // RRGGBBAA, alpha included
            }
            else r.DefaultText = Describe(v);
            r.HasDefault = Accessor(node.GetType())?.set != null;
        }
        foreach (var c in s.Children) SnapshotDefaults(m, c, fresh);
    }

    private void ResetAll(Section s, object owner)
    {
        foreach (var r in s.Rows.SelectMany(Flatten))
        {
            if (!r.HasDefault || r.ReadOnly) continue;
            if (r.Kind == Kind.Collection) { ResetCollection(r, owner); continue; }
            var node = r.Get(owner);
            if (node != null) SetValue(node, r.Default);
        }
        foreach (var c in s.Children)
        {
            var child = c.Get(owner);
            if (child != null) ResetAll(c, child);
        }
    }

    private static string Describe(object? v) => v switch
    {
        null => "none",
        float f => f.ToString("0.##"),
        double d => d.ToString("0.###"),
        bool b => b ? "on" : "off",
        Vector2 vec => $"{vec.X:0.##}, {vec.Y:0.##}",
        _ => v.ToString() ?? "",
    };

    /// <summary>ColorNode -> RRGGBBAA, compiled once. ColorNode.Value is SharpDX.Color (byte fields) in one HUD and a
    /// different Color in the other, so R/G/B/A are read as property or field by name. Null when the type has no
    /// such members (then colours never show "changed").</summary>
    private static readonly Func<object, uint>? PackColor = BuildPackColor();
    private static string? PackColorError;   // no initializer: BuildPackColor (PackColor's initializer) sets it; each menu reports it once

    private static Func<object, uint>? BuildPackColor()
    {
        try
        {
            var vp = typeof(ColorNode).GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            if (vp == null) return null;
            var o = Expression.Parameter(typeof(object), "o");
            var value = Expression.Variable(vp.PropertyType, "v");
            Expression Channel(string name, int shift)
            {
                var ch = Expression.PropertyOrField(value, name);
                Expression b;
                if (ch.Type == typeof(float) || ch.Type == typeof(double))
                {
                    // 0..1 channels: scale to a byte, rounded and clamped, so a float colour packs like a byte one
                    var scaled = Expression.Add(Expression.Multiply(Expression.Convert(ch, typeof(double)), Expression.Constant(255.0)), Expression.Constant(0.5));
                    var clamp = typeof(Math).GetMethod(nameof(Math.Clamp), new[] { typeof(double), typeof(double), typeof(double) })!;
                    b = Expression.Convert(Expression.Call(clamp, scaled, Expression.Constant(0.0), Expression.Constant(255.0)), typeof(uint));
                }
                else if (ch.Type == typeof(byte) || ch.Type == typeof(int) || ch.Type == typeof(uint) || ch.Type == typeof(short) || ch.Type == typeof(ushort))
                    b = Expression.And(Expression.Convert(ch, typeof(uint)), Expression.Constant(0xFFu));
                else throw new NotSupportedException($"{vp.PropertyType.Name}.{name} is {ch.Type.Name}");
                return Expression.LeftShift(b, Expression.Constant(shift));
            }
            var packed = Expression.Or(Expression.Or(Channel("R", 24), Channel("G", 16)), Expression.Or(Channel("B", 8), Channel("A", 0)));
            var body = Expression.Block(new[] { value },
                Expression.Assign(value, Expression.Property(Expression.Convert(o, typeof(ColorNode)), vp)),
                packed);
            return Expression.Lambda<Func<object, uint>>(body, o).Compile();
        }
        catch (Exception ex)
        {
            PackColorError = $"[SettingsMenu] ColorNode.Value has no R/G/B/A to compare ({ex.GetType().Name}: {ex.Message}): colour rows never show changed-from-default";
            return null;
        }
    }

    /// <summary>The HUD's own label rule for a property without [Menu]: "DrawDistance" -> "Draw Distance".</summary>
    private static string DefaultName(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, @"(((?<![A-Z])\B[A-Z])|(\B[A-Z](?![A-Z])))", " $1");

    private static string TypeName(Type t) => !t.IsGenericType ? t.Name
        : $"{t.Name[..t.Name.IndexOf('`')]}<{string.Join(", ", t.GetGenericArguments().Select(TypeName))}>";
}
