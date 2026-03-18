# What's a Crowd Control?

> See exactly what's stunning, freezing, or slowing you -- and how long it lasts -- like OmniCC for Path of Exile.

Part of the **WhatsA** plugin family for ExileApi.

## What It Does

- Displays active crowd control effects on your character as prioritized icons with countdown timers
- The most dangerous CC shows as a large center icon, with up to 2 secondary effects alongside it
- Covers 16 CC types across 4 severity tiers: full lockout (stun/freeze), movement lock (pin/root), heavy slows (chill/hinder/maim), and combat debuffs (shock/blind/silence)
- Inspired by the WoW addon [OmniCC](https://www.curseforge.com/wow/addons/omni-cc) -- bringing the same "always know what's affecting you" clarity to PoE

## Getting Started

1. Download and place in `Plugins/Source/What's a Crowd Control/`
2. HUD auto-compiles on next launch
3. Enable in plugin list
4. Get CC'd in combat and the overlay appears automatically

## Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Position X / Y | 1280 / 400 | Screen position of the CC display |
| Primary Icon Size | 72 | Main CC icon size in pixels |
| Secondary Icon Size | 48 | Side CC icon sizes |
| Show Timer Text | On | Display remaining duration on icons |
| Show Sweep Overlay | On | Dark overlay that shrinks as the timer depletes |
| Show Fade Animations | On | Fade-in on appear, fade-out on expire |
| Background Opacity | 0.6 | Darkness of the icon background |
| Show In Town | Off | Display overlay while in town |
| Show In Hideout | Off | Display overlay while in hideout |
| Full Lockout | On | Track Tier 1: Stun, Freeze, Petrify |
| Movement Lock | On | Track Tier 2: Pin, Root |
| Heavy Slow | On | Track Tier 3: Chill, Hinder, Maim, Temporal Chains, etc. |
| Combat Debuff | On | Track Tier 4: Shock, Blind, Silence |
| Custom CC Buff Names | *(empty)* | Add your own buff names (comma-separated), tracked as Tier 3 |

<details>
<summary>Technical Details</summary>

### CC Priority System

4 tiers, highest priority first. Within the same tier, the longest remaining duration wins the primary slot.

| Tier | Label | CC Types | Buff Names |
|------|-------|----------|-----------|
| 1 | Full Lockout | Stun, Freeze, Petrify | `stun_display_buff`, `frozen`, `petrified` |
| 2 | Movement Lock | Pin, Root | `pinned`, `trap_pinned`, `grasping_vines_buff`, `nemesis_vines`, `nemesis_vines_large`, `elder_siphon_immobilise` |
| 3 | Heavy Slow | Temporal Chains, Hinder, Maim, Chill, Tar, Ash, Quicksand, Basilisk Slow | `curse_temporal_chains`, `hinder`, `maim`, `chilled`, `ground_tar_slow`, `cover_in_ash`, `quicksand_buff`, `basilisk_slow` |
| 4 | Combat Debuff | Shock, Blind, Silence | `shocked`, `blinded`, `curse_silence`, `curse_aura_silence` |

### Display Layout

```
     [48x48]    [72x72]    [48x48]
   Secondary   PRIMARY   Secondary
     dim         bright      dim
```

Each icon shows:
- The CC type icon image
- A sweep overlay -- semi-transparent dark overlay that shrinks from top as the timer depletes
- A timer -- centered remaining duration in seconds (e.g. `3.2`)
- The primary CC's display name beneath it

### Icon Management

Icons are stored in `images/` as PNG files. Each CC type maps to an image key following the pattern `cc_<name>` (e.g., `cc_stun` -> `stun.png`). Missing icons fall back to `unknown.png`.

| Image Key | Filename | Used By |
|-----------|----------|---------|
| `cc_stun` | `stun.png` | Stun |
| `cc_freeze` | `freeze.png` | Freeze |
| `cc_petrify` | `petrify.png` | Petrify |
| `cc_pin` | `pin.png` | Pin |
| `cc_root` | `root.png` | Root |
| `cc_temporal_chains` | `temporal_chains.png` | Temporal Chains |
| `cc_hinder` | `hinder.png` | Hinder |
| `cc_maim` | `maim.png` | Maim |
| `cc_chill` | `chill.png` | Chill |
| `cc_slow` | `slow.png` | Tar, Quicksand, Basilisk Slow |
| `cc_ash` | `ash.png` | Ash |
| `cc_shock` | `shock.png` | Shock |
| `cc_blind` | `blind.png` | Blind |
| `cc_silence` | `silence.png` | Silence |
| `cc_unknown` | `unknown.png` | Fallback for missing icons |

### Architecture

#### Tick/Render Split
- **Tick()** -- data only, no graphics. Scans player buffs via dictionary lookup (O(1) per buff), sorts by priority, fills pre-allocated active slots.
- **Render()** -- drawing only, no GetComponent calls. Draws icons, sweep overlays, timer text from cached data.

#### Performance
- Pre-allocated arrays: `ActiveCc[3]` for active + `ActiveCc[3]` for fading -- no per-frame heap allocation
- Dictionary lookup: O(1) per buff name, single pass over BuffsList
- No LINQ in hot path: manual iteration only
- BuffsList is FrameCache-backed: safe to call in Tick
- Max 9 draw calls per frame (3 icons x 3 layers)

#### Animations
- **Fade-in**: 150ms alpha ramp when CC appears
- **Fade-out**: 300ms alpha decay when CC expires

### Adding New CCs

1. Add a new `CcDefinition` entry in `WhatsACrowdControl.cs` -> `CcDefinitions` array
2. Provide the buff name(s) -- find them via DevTree's buff inspector or Debug Mode
3. Add a corresponding icon PNG to `images/`
4. The image key follows the pattern `cc_<name>`, filename is `<name>.png`

### Custom Buffs

Users can add arbitrary buff names via the **Custom CC Buff Names** setting. Enter comma-separated internal buff names (e.g. `my_custom_debuff, another_debuff`). These are tracked as Tier 3 (Heavy Slow) and use the fallback icon.

</details>

## About This Project

These plugins are built with AI-assisted development using Claude Code and the
ExileApiScaffolding (private development workspace) workspace.

The developer works professionally in cybersecurity and high-risk software --
AI compensates for a C# knowledge gap specifically, not engineering judgment.
Plugin data comes from the PoE Wiki and PoEDB data mining.

The focus is on UX: friction points and missing expected features that the
existing plugin ecosystem doesn't address. Every hour spent developing is an
hour not spent on league progression, so feedback is the best way to support
the project.

## WhatsA Plugin Family

| Plugin | Description |
|--------|-------------|
| [What's a Breakpoint?](https://github.com/ParogDev/WhatsABreakpoint) | Kinetic Fusillade attack speed breakpoint visualizer |
| **What's a Crowd Control?** | OmniCC-style CC effect overlay with timers |
| [What's a Mirage?](https://github.com/ParogDev/WhatsAMirage) | League mechanic overlay for spawners, chests, and wishes |
| [What's a Tincture?](https://github.com/ParogDev/WhatsATincture) | Automated tincture management with burn stack tracking |
| [What's a Tooltip?](https://github.com/ParogDev/WhatsATooltip) | Shared rich tooltip service for WhatsA plugins |
| [What's an AI Bridge?](https://github.com/ParogDev/WhatsAnAiBridge) | File-based IPC for AI-assisted plugin development |
| [What's an Unbound Avatar?](https://github.com/ParogDev/WhatsAnUnboundAvatar) | Auto-activation for Avatar of the Wilds at 100 fury |

Built with ExileApiScaffolding (private development workspace)
