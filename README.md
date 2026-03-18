# WhatsACrowdControl

An OmniCC-style overlay plugin for ExileApi that displays active crowd control effects on the player character. When you're stunned, frozen, slowed, or otherwise CC'd during combat, you immediately see **what** is affecting you and **how long** it lasts.

## How It Works

The most impactful CC dominates the display as a large primary icon. Up to 2 secondary CCs are shown as smaller icons alongside it.

```
     [48x48]    [72x72]    [48x48]
   Secondary   PRIMARY   Secondary
     dim         bright      dim
```

Each icon shows:
- The CC type icon image
- A **sweep overlay** — semi-transparent dark overlay that shrinks from top as the timer depletes
- A **timer** — centered remaining duration in seconds (e.g. `3.2`)
- The primary CC's **display name** beneath it

## CC Priority System

4 tiers, highest priority first. Within the same tier, the longest remaining duration wins the primary slot.

| Tier | Label | CC Types | Buff Names |
|------|-------|----------|-----------|
| 1 | Full Lockout | Stun, Freeze, Petrify | `stun_display_buff`, `frozen`, `petrified` |
| 2 | Movement Lock | Pin, Root | `pinned`, `trap_pinned`, `grasping_vines_buff`, `nemesis_vines`, `nemesis_vines_large`, `elder_siphon_immobilise` |
| 3 | Heavy Slow | Temporal Chains, Hinder, Maim, Chill, Tar, Ash, Quicksand, Basilisk Slow | `curse_temporal_chains`, `hinder`, `maim`, `chilled`, `ground_tar_slow`, `cover_in_ash`, `quicksand_buff`, `basilisk_slow` |
| 4 | Combat Debuff | Shock, Blind, Silence | `shocked`, `blinded`, `curse_silence`, `curse_aura_silence` |

## Settings Reference

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| **Enable** | Toggle | `true` | Master enable/disable |
| **Position X** | Range (0–3840) | `1280` | Horizontal screen position |
| **Position Y** | Range (0–2160) | `400` | Vertical screen position |
| **Primary Icon Size** | Range (32–128) | `72` | Main CC icon size in pixels |
| **Secondary Icon Size** | Range (24–96) | `48` | Side CC icon sizes in pixels |
| **Icon Spacing** | Range (0–32) | `8` | Gap between icons in pixels |
| **Timer Text Color** | Color | White | Color of the countdown timer |
| **Show Timer Text** | Toggle | `true` | Show remaining duration on icons |
| **Show Sweep Overlay** | Toggle | `true` | Show the depleting dark overlay |
| **Show Fade Animations** | Toggle | `true` | Fade-in on appear, fade-out on expire |
| **Background Opacity** | Range (0–1) | `0.6` | Opacity of the dark background/sweep |
| **Show In Town** | Toggle | `false` | Display overlay while in town |
| **Show In Hideout** | Toggle | `false` | Display overlay while in hideout |
| **Full Lockout** | Toggle | `true` | Track Tier 1 CCs (Stun/Freeze/Petrify) |
| **Movement Lock** | Toggle | `true` | Track Tier 2 CCs (Pin/Root) |
| **Heavy Slow** | Toggle | `true` | Track Tier 3 CCs (Chill/Hinder/Maim/etc) |
| **Combat Debuff** | Toggle | `true` | Track Tier 4 CCs (Shock/Blind/Silence) |
| **Custom CC Buff Names** | Text | `""` | Comma-separated buff names, added as Tier 3 |
| **Debug Mode** | Toggle | `false` | Log all detected timed buffs to debug window |

## Icon Management

Icons are stored in `images/` as PNG files. Each CC type maps to an image key:

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

If an icon file is missing, the plugin falls back to `unknown.png`.

## Architecture

### Tick/Render Split
- **Tick()** — data only, no graphics. Scans player buffs via dictionary lookup (O(1) per buff), sorts by priority, fills pre-allocated active slots.
- **Render()** — drawing only, no GetComponent calls. Draws icons, sweep overlays, timer text from cached data.

### Performance
- Pre-allocated arrays: `ActiveCc[3]` for active + `ActiveCc[3]` for fading — no per-frame heap allocation
- Dictionary lookup: O(1) per buff name, single pass over BuffsList
- No LINQ in hot path: manual iteration only
- BuffsList is FrameCache-backed: safe to call in Tick
- Max 9 draw calls per frame (3 icons x 3 layers)

### Animations
- **Fade-in**: 150ms alpha ramp when CC appears
- **Fade-out**: 300ms alpha decay when CC expires

## Adding New CCs

1. Add a new `CcDefinition` entry in `WhatsACrowdControl.cs` → `CcDefinitions` array
2. Provide the buff name(s) — find them via DevTree's buff inspector or Debug Mode
3. Add a corresponding icon PNG to `images/`
4. The image key follows the pattern `cc_<name>`, filename is `<name>.png`

## Custom Buffs

Users can add arbitrary buff names via the **Custom CC Buff Names** setting. Enter comma-separated internal buff names (e.g. `my_custom_debuff, another_debuff`). These are tracked as Tier 3 (Heavy Slow) and use the fallback icon.
