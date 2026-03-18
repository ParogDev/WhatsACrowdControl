using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using SharpDX;
using Color = SharpDX.Color;

namespace WhatsACrowdControl;

public class WhatsACrowdControlSettings : ISettings
{
    // Required
    public ToggleNode Enable { get; set; } = new(true);

    // Position & Size
    [Menu("Position X", "Horizontal screen position (pixels)")]
    public RangeNode<int> PositionX { get; set; } = new(1280, 0, 3840);

    [Menu("Position Y", "Vertical screen position (pixels)")]
    public RangeNode<int> PositionY { get; set; } = new(400, 0, 2160);

    [Menu("Primary Icon Size", "Size of the main CC icon (pixels)")]
    public RangeNode<int> PrimaryIconSize { get; set; } = new(72, 32, 128);

    [Menu("Secondary Icon Size", "Size of side CC icons (pixels)")]
    public RangeNode<int> SecondaryIconSize { get; set; } = new(48, 24, 96);

    [Menu("Icon Spacing", "Gap between icons (pixels)")]
    public RangeNode<int> IconSpacing { get; set; } = new(8, 0, 32);

    // Visual
    [Menu("Timer Text Color")]
    public ColorNode TimerTextColor { get; set; } = Color.White;

    [Menu("Show Timer Text", "Display remaining time on icons")]
    public ToggleNode ShowTimerText { get; set; } = new(true);

    [Menu("Show Sweep Overlay", "Dark overlay that shrinks as timer depletes")]
    public ToggleNode ShowSweepOverlay { get; set; } = new(true);

    [Menu("Show Fade Animations", "Fade-in/fade-out on CC appear/expire")]
    public ToggleNode ShowFadeAnimations { get; set; } = new(true);

    [Menu("Background Opacity", "Opacity of the sweep overlay")]
    public RangeNode<float> BackgroundOpacity { get; set; } = new(0.6f, 0f, 1f);

    // Filtering
    [Menu("Show In Town")]
    public ToggleNode ShowInTown { get; set; } = new(false);

    [Menu("Show In Hideout")]
    public ToggleNode ShowInHideout { get; set; } = new(false);

    // Per-tier enables
    [Menu("CC Tier Filters", 100)]
    public EmptyNode TierFiltersHeader { get; set; }

    [Menu("Full Lockout (Stun/Freeze/Petrify)", "Tier 1  - complete loss of control", 101, 100)]
    public ToggleNode EnableFullLockout { get; set; } = new(true);

    [Menu("Movement Lock (Pin/Root)", "Tier 2  - cannot move", 102, 100)]
    public ToggleNode EnableMovementLock { get; set; } = new(true);

    [Menu("Heavy Slow (Chill/Hinder/Maim/etc)", "Tier 3  - significant movement penalty", 103, 100)]
    public ToggleNode EnableHeavySlow { get; set; } = new(true);

    [Menu("Combat Debuff (Shock/Blind/Silence)", "Tier 4  - combat effectiveness reduced", 104, 100)]
    public ToggleNode EnableCombatDebuff { get; set; } = new(true);

    // Extensibility
    [Menu("Custom CC Buff Names", "Comma-separated buff names to track as Tier 3")]
    public TextNode CustomCcBuffNames { get; set; } = new("");

    // Debug
    [Menu("Debug Mode", "Log all detected buffs to debug window")]
    public ToggleNode DebugMode { get; set; } = new(false);
}
