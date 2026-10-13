using System.Collections.Generic;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using SharpDX;
using Color = SharpDX.Color;

namespace WhatsACrowdControl;

// Drawn by SettingsMenu (SettingsMenu.cs) on the Overlay tab: every EmptyNode is a section header and
// [Menu(index, parentIndex)] hangs rows under it. [Menu] gives the label and the description, [MenuUnit] the
// slider suffix. The flat layout is kept so saved settings files still load.
public class WhatsACrowdControlSettings : ISettings
{
    // Required
    public ToggleNode Enable { get; set; } = new(true);

    // Position & Size
    [Menu("Position & Size", "Where the CC icons sit and how big they are", 200)]
    public EmptyNode LayoutHeader { get; set; } = new();

    [Menu("Position X", "Horizontal screen position of the primary icon (pixels)", 201, 200)]
    [MenuUnit("px")]
    public RangeNode<int> PositionX { get; set; } = new(1280, 0, 3840);

    [Menu("Position Y", "Vertical screen position of the primary icon (pixels)", 202, 200)]
    [MenuUnit("px")]
    public RangeNode<int> PositionY { get; set; } = new(400, 0, 2160);

    [Menu("Primary Icon Size", "Size of the main (most severe) CC icon in the centre (pixels)", 203, 200)]
    [MenuUnit("px")]
    public RangeNode<int> PrimaryIconSize { get; set; } = new(72, 32, 128);

    [Menu("Secondary Icon Size", "Size of the two flanking CC icons (pixels)", 204, 200)]
    [MenuUnit("px")]
    public RangeNode<int> SecondaryIconSize { get; set; } = new(48, 24, 96);

    [Menu("Icon Spacing", "Gap between icons (pixels)", 205, 200)]
    [MenuUnit("px")]
    public RangeNode<int> IconSpacing { get; set; } = new(8, 0, 32);

    // Visual
    [Menu("Visual Style", "How each icon is drawn", 300)]
    public EmptyNode VisualHeader { get; set; } = new();

    [Menu("Timer Text Color", "Colour of the countdown text on each icon", 301, 300)]
    public ColorNode TimerTextColor { get; set; } = Color.White;

    [Menu("Show Timer Text", "Display remaining seconds on each icon", 302, 300)]
    public ToggleNode ShowTimerText { get; set; } = new(true);

    [Menu("Show Sweep Overlay", "Dark clock-wipe overlay that shrinks as the CC runs out", 303, 300)]
    public ToggleNode ShowSweepOverlay { get; set; } = new(true);

    [Menu("Show Fade Animations", "Fade in when a CC appears, fade out when it expires", 304, 300)]
    public ToggleNode ShowFadeAnimations { get; set; } = new(true);

    [Menu("Background Opacity", "Darkness of the icon background and the sweep overlay", 305, 300)]
    public RangeNode<float> BackgroundOpacity { get; set; } = new(0.6f, 0f, 1f);

    // Filtering
    [Menu("Areas", "CC detection is off in towns and hideouts unless enabled here", 400)]
    public EmptyNode AreasHeader { get; set; } = new();

    [Menu("Show In Town", "Display the CC overlay while in town areas", 401, 400)]
    public ToggleNode ShowInTown { get; set; } = new(false);

    [Menu("Show In Hideout", "Display the CC overlay while in your hideout", 402, 400)]
    public ToggleNode ShowInHideout { get; set; } = new(false);

    // Per-tier enables
    [Menu("CC Tier Filters", "Which severity tiers are tracked (Full Lockout > Movement Lock > Heavy Slow > Combat Debuff)", 100)]
    public EmptyNode TierFiltersHeader { get; set; } = new();

    [Menu("Full Lockout (Stun/Freeze/Petrify)", "Tier 1  - complete loss of control", 101, 100)]
    public ToggleNode EnableFullLockout { get; set; } = new(true);

    [Menu("Movement Lock (Pin/Root)", "Tier 2  - cannot move", 102, 100)]
    public ToggleNode EnableMovementLock { get; set; } = new(true);

    [Menu("Heavy Slow (Chill/Hinder/Maim/etc)", "Tier 3  - significant movement penalty", 103, 100)]
    public ToggleNode EnableHeavySlow { get; set; } = new(true);

    [Menu("Combat Debuff (Shock/Blind/Silence)", "Tier 4  - combat effectiveness reduced", 104, 100)]
    public ToggleNode EnableCombatDebuff { get; set; } = new(true);

    // Custom buffs
    [Menu("Custom CC", "Extra buffs to treat as crowd control", 600)]
    public EmptyNode CustomHeader { get; set; } = new();

    [Menu("Custom CC Buff Names", "Internal buff names to track as Tier 3 (Heavy Slow): type one and press Enter, click a chip to drop it, or click a buff in the CC Types tab's Live Buff Browser", 601, 600)]
    [MenuList(ItemName = "buff name", Chips = true)]
    public List<string> CustomCcBuffs { get; set; } = new();

    /// <summary>Before the list: the same names as one comma-separated string. Read once at load into
    /// <see cref="CustomCcBuffs"/> and cleared, so an old settings file keeps its custom CCs.</summary>
    [IgnoreMenu]
    public TextNode CustomCcBuffNames { get; set; } = new("");

    // Debug
    [Menu("Debug", "Diagnostics (live values are on the Debug tab)", 500)]
    public EmptyNode DebugHeader { get; set; } = new();

    [Menu("Debug Mode", "Log every timed buff to the debug window each frame (use DevTree to cross-reference names)", 501, 500)]
    public ToggleNode DebugMode { get; set; } = new(false);
}
