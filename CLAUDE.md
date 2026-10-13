# CLAUDE.md — WhatsACrowdControl

## Plugin Context

WhatsACrowdControl is an ExileApi overlay plugin that monitors the player's active buffs for crowd-control effects (stun, freeze, chill, pin, hinder, etc.) and displays the most severe ones as prioritized icons on-screen with countdown timers, sweep overlays, and fade animations. It helps players react to dangerous CC by making active debuffs immediately visible.

**Main class**: `WhatsACrowdControl` inherits `BaseSettingsPlugin<WhatsACrowdControlSettings>`
**Settings class**: `WhatsACrowdControlSettings` implements `ISettings`
**Key classes**: `CcDefinition` (CC metadata), `ActiveCc` (active CC state), `CcTier` enum (severity tiers), `CrowdControlSettingsUi` (wires `SettingsMenu` + hand-drawn CC Types / Debug tabs)

### Architecture

The plugin follows a strict Tick/Render separation:

1. **Tick()** scans the player's `Buffs` component once per frame, matches buff names against a `Dictionary<string, CcDefinition>` lookup, sorts active CCs by tier then duration, and fills 3 active slots (primary + 2 secondary). It also manages fade-in/fade-out animation state.

2. **Render()** draws the active slots as icons at a configurable screen position — a large center icon (primary) flanked by smaller side icons (secondary). Each icon gets a background box, the CC image, a sweep overlay showing time remaining, a border frame, timer text, and the CC name.

3. **CC Definitions** are statically defined with display name, image key, tier (FullLockout > MovementLock > HeavySlow > CombatDebuff), and one or more buff name strings to match against.

4. **Custom CC** support lets users add comma-separated buff names tracked as Heavy Slow tier.

5. **Object pooling** avoids per-frame allocations — `ActiveCc` instances are pre-allocated and reused.

### Lifecycle Methods Used

| Method | What it does in this plugin |
|---|---|
| `OnLoad()` | Loads CC icon images from `images/` directory |
| `Initialise()` | Initializes object pool, builds buff→definition lookup |
| `Tick()` | Scans player buffs, matches against CC definitions, sorts by priority, fills display slots, manages fade animations |
| `Render()` | Draws CC icons with backgrounds, sweep overlays, timer text, fade animations |
| `DrawSettings()` | Delegates to `CrowdControlSettingsUi`: `SettingsMenu` (SettingsMenu.cs, from the scaffolding new-plugin template) draws the Overlay tab from [Menu] headers/rows; CC Types (tier legend, icon browser, live buff browser) and Debug tabs are hand-drawn |

### Current Settings

| Setting | Type | Default | Description |
|---|---|---|---|
| Enable | ToggleNode | true | Master enable |
| PositionX | RangeNode\<int\> | 1280 (0-3840) | Horizontal screen position |
| PositionY | RangeNode\<int\> | 400 (0-2160) | Vertical screen position |
| PrimaryIconSize | RangeNode\<int\> | 72 (32-128) | Main CC icon size |
| SecondaryIconSize | RangeNode\<int\> | 48 (24-96) | Side CC icon size |
| IconSpacing | RangeNode\<int\> | 8 (0-32) | Gap between icons |
| TimerTextColor | ColorNode | White | Countdown text color |
| ShowTimerText | ToggleNode | true | Display remaining time |
| ShowSweepOverlay | ToggleNode | true | Dark overlay shrinking with timer |
| ShowFadeAnimations | ToggleNode | true | Fade-in/fade-out transitions |
| BackgroundOpacity | RangeNode\<float\> | 0.6 (0-1) | Icon background darkness |
| ShowInTown | ToggleNode | false | Show in town areas |
| ShowInHideout | ToggleNode | false | Show in hideout |
| EnableFullLockout | ToggleNode | true | Track stun/freeze/petrify |
| EnableMovementLock | ToggleNode | true | Track pin/root |
| EnableHeavySlow | ToggleNode | true | Track chill/hinder/maim/etc |
| EnableCombatDebuff | ToggleNode | true | Track shock/blind/silence |
| CustomCcBuffNames | TextNode | "" | Comma-separated custom buff names |
| DebugMode | ToggleNode | false | Log all timed buffs |

Section headers (EmptyNode with `[Menu(index)]`; rows hang under them with `parentIndex`): LayoutHeader 200, VisualHeader 300, AreasHeader 400, TierFiltersHeader 100, CustomHeader 600, DebugHeader 500. The settings class stays flat on purpose: nesting rows into [Submenu] classes would change the JSON shape and reset saved settings.

## Project Setup

- This is an ExileApi plugin (game HUD overlay framework for Path of Exile)
- Do not edit anything outside this directory
- Target framework: net10.0-windows, OutputType: Library

## Build & Run

- NO manual build command — Loader.exe auto-compiles from Plugins/Source/
- HUD installation path: resolved from .csproj HintPath (parent dir of ExileCore.dll)
- For IDE support set env var: `exapiPackage` = `<HUD installation path>`

## API Reference

- **Default**: HUD installation (from .csproj HintPath) — compiled DLLs with intellisense
- **Enhanced**: If `.claude/override-path` exists, read it for a path to expanded
  API reference with full type definitions and source. Use that path for deep lookups
  when the compiled DLLs don't provide enough detail about a type, method, or pattern.

## Plugin Anatomy

Every plugin is a C# class library. The main class inherits `BaseSettingsPlugin<TSettings>` and the settings class implements `ISettings`.

Minimal `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <OutputType>Library</OutputType>
    <UseWindowsForms>true</UseWindowsForms>
    <PlatformTarget>x64</PlatformTarget>
    <LangVersion>latest</LangVersion>
    <DebugType>embedded</DebugType>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="ExileCore">
      <HintPath>$(exapiPackage)\ExileCore.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="GameOffsets">
      <HintPath>$(exapiPackage)\GameOffsets.dll</HintPath>
      <Private>False</Private>
    </Reference>
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="ImGui.NET" Version="1.90.0.1" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="SharpDX.Mathematics" Version="4.2.0" />
  </ItemGroup>
</Project>
```

### Plugin Lifecycle

| Method | When called | Notes |
|---|---|---|
| `Initialise()` | Once on load | Register hotkeys, wire up `OnPressed`/`OnValueChanged`, return `true` on success |
| `OnLoad()` | After Initialise | Load textures: `Graphics.InitImage("file.png")` |
| `AreaChange(AreaInstance area)` | Zone change | Clear cached entity lists here |
| `Tick()` | Every frame | Return `null` (no async job needed) or a `Job` for background work |
| `Render()` | Every frame | Draw overlays; check `Settings.Enable` and `GameController.InGame` |
| `EntityAdded(Entity entity)` | Entity enters range | Filter and cache relevant entities here |
| `EntityRemoved(Entity entity)` | Entity leaves range | Remove from caches |
| `DrawSettings()` | Settings panel open | Call `base.DrawSettings()` unless fully custom |

## GameController API

`GameController` is the main access point available in all plugin methods:

```csharp
// State checks
GameController.InGame
GameController.Game.IsInGameState

// Player
GameController.Player                          // local player Entity
GameController.Player.GetComponent<Positioned>().GridPosNum

// Area
GameController.Area.CurrentArea.IsPeaceful
GameController.Area.CurrentArea.Area.RawName
GameController.Area.CurrentArea.IsTown
GameController.Area.CurrentArea.IsHideout

// Entities — prefer ValidEntitiesByType over Entities for filtered access
GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Monster]
GameController.Entities   // all entities, more expensive

// Ingame UI / state
GameController.Game.IngameState.IngameUi.Map.LargeMap.IsVisible
GameController.Game.IngameState.IngameUi.FullscreenPanels
GameController.Game.IngameState.IngameUi.OpenRightPanel
GameController.Game.IngameState.Camera        // Camera, WorldToScreen
GameController.Game.IngameState.Data          // terrain, server data, area dimensions

// Game files (static data)
GameController.Files.BaseItemTypes.Translate(entity.Path)
GameController.Files.GemEffects.GetById(id)

// Window
GameController.Window.GetWindowRectangleTimeCache   // cached, use this not GetWindowRectangle()

// Inter-plugin
GameController.PluginBridge.SaveMethod("MyPlugin.Method", delegate)
GameController.PluginBridge.GetMethod<TDelegate>("OtherPlugin.Method")

// Timing
GameController.DeltaTime  // double, seconds since last frame
```

## Entity API

```csharp
entity.Path         // "Metadata/Monsters/..."
entity.Metadata     // same as Path for most entities
entity.Id           // uint, unique per session
entity.IsValid      // always check before extensive use
entity.IsAlive
entity.GridPosNum   // Vector2, 2D grid position
entity.PosNum       // Vector3, 3D world position
entity.Distance(otherEntity)
entity.DistancePlayer  // float, distance to local player
entity.Type         // EntityType enum
entity.GetComponent<Positioned>()     // null if not present
entity.GetComponent<Render>()
entity.GetComponent<Stats>()
entity.GetComponent<ObjectMagicProperties>()?.Mods
```

## Component Quick Reference

### Life
```csharp
var life = entity.GetComponent<Life>();
life.CurHP / life.MaxHP      // health
life.CurMana / life.MaxMana  // mana
life.CurES / life.MaxES      // energy shield
life.HPPercentage             // 0-100
```

### Buffs
```csharp
var buffs = entity.GetComponent<Buffs>();
buffs.BuffsList               // List<Buff>
buff.Name                     // internal string ID (e.g., "frozen", "shocked")
buff.DisplayName              // human-readable
buff.Timer                    // seconds remaining
buff.MaxTime                  // original duration
buff.Charges                  // stack count
```

### Positioned & Render
```csharp
var pos = entity.GetComponent<Positioned>();
pos.GridPosNum                // Vector2 grid coords

var render = entity.GetComponent<Render>();
render.Name                   // display name
render.Bounds                 // bounding box
```

### ObjectMagicProperties
```csharp
var omp = entity.GetComponent<ObjectMagicProperties>();
omp.Rarity                    // MonsterRarity enum
omp.Mods                      // List<string> mod names
```

### Stats
```csharp
var stats = entity.GetComponent<Stats>();
stats.StatDictionary          // Dictionary<GameStat, int>
```

### Other Common Components
- `Monster` — monster-specific data
- `Player` — player-specific data
- `Chest` — chest state (IsOpened, IsStrongbox)
- `Portal` — portal destination
- `WorldItem` — ground item info
- `Targetable` — whether entity can be targeted (isTargetable)
- `Base` — item base type info (Name, ItemBaseName)
- `Mods` — item mods (ItemMods, ImplicitMods, ItemRarity)
- `Sockets` — socket links, colors, count

## Settings Node Types

```csharp
public class MySettings : ISettings
{
    public ToggleNode Enable { get; set; } = new ToggleNode(false);
    public RangeNode<int> SomeRange { get; set; } = new RangeNode<int>(10, 1, 100);
    public RangeNode<float> SomeFloat { get; set; } = new RangeNode<float>(1.5f, 0f, 10f);
    public ColorNode SomeColor { get; set; } = new ColorNode(Color.White);
    public HotkeyNodeV2 SomeKey { get; set; } = Keys.F5;
    public ButtonNode SomeButton { get; set; }   // wire OnPressed in constructor
    public TextNode SomeText { get; set; } = new TextNode("default");
    public EmptyNode SomeHeader { get; set; }     // visual separator in settings

    [Menu("Display Name")]
    public ToggleNode WithLabel { get; set; } = new ToggleNode(true);

    [Menu("Category Header", parentIndex: 100)]
    public EmptyNode CategoryNode { get; set; }

    [Menu("Sub Option", parentIndex: 101, index: 100)]
    public ToggleNode SubOption { get; set; } = new ToggleNode(false);

    [IgnoreMenu]
    public SomeType NotShownInSettings { get; set; }
}

[Submenu]
public class NestedSection
{
    public ToggleNode SubOption { get; set; } = new ToggleNode(false);
}
```

Register hotkeys in `Initialise()`:
```csharp
Input.RegisterKey(Settings.SomeKey.Value);
Settings.SomeKey.OnValueChanged += () => Input.RegisterKey(Settings.SomeKey.Value);
```

## Graphics API

```csharp
// Rectangles — use System.Numerics.Vector2 for positions
Graphics.DrawBox(topLeft, bottomRight, color, rounding);
Graphics.DrawFrame(topLeft, bottomRight, color, borderWidth, segments, rounding);

// Text
Graphics.DrawText("text", position, color);
Graphics.DrawTextWithBackground("text", position, textColor, alignment, bgColor);
var size = Graphics.MeasureText("text");
using (Graphics.SetTextScale(1.5f)) { /* draw at scale */ }

// World / map lines
Graphics.DrawLineInWorld(gridPos1, gridPos2, width, color);
Graphics.DrawLineOnLargeMap(gridPos1, gridPos2, width, color);

// Circles
Graphics.DrawFilledCircleInWorld(worldPos, radius, color);

// Textures (load in OnLoad, draw in Render)
Graphics.InitImage("myimage.png");            // from plugin directory
Graphics.InitImage("key", fullPath);          // with explicit path
Graphics.DrawImage("key", rectF, color);      // RectangleF from SharpDX
var texId = Graphics.GetTextureId("key");     // IntPtr for ImGui
Graphics.HasImage("key");                     // check if loaded

// Camera projection
var screenPos = GameController.Game.IngameState.Camera.WorldToScreen(entity.PosNum);
```

## Performance Rules

ExileApi plugins run every frame (60+ fps). The game process is memory-mapped, so every property access that isn't cached may read from game memory.

- **NEVER** call `GetComponent<T>()` in `Render()` — do it in `Tick()`, store in fields
- **NEVER** iterate `GameController.Entities` — use `EntityListWrapper.ValidEntitiesByType`
- **NEVER** call `entity.Path` per-frame — cache in `EntityAdded()` via `SetHudComponent`
- Use `EntityAdded`/`EntityRemoved` to maintain filtered entity lists
- Use `TimeCache<T>` for expensive operations that don't need per-frame freshness
- Clear state in `AreaChange()`, not per-tick
- Use cached accessors (`GetWindowRectangleTimeCache`, `GetClientRectCache`)
- Check `entity.IsValid` and `entity.IsAlive` before processing
- Check `screenPos == Vector2.Zero` after `WorldToScreen()`
- Load textures in `OnLoad()`, never in `Render()`
- Separate data reads (`Tick`) from drawing (`Render`) — never GetComponent in Render
- Use `_canRender` flags to skip `Render()` entirely when nothing to draw
- Guard early in `Tick()`: InGame, IsAlive, area checks (town/hideout) before work

## C# Best Practices for Plugin Development

- Use file-scoped namespaces
- Seal leaf classes for virtual dispatch optimization
- Object-pool reusable instances to avoid per-frame GC pressure
- Use Dictionary/HashSet lookups instead of per-frame string comparisons
- Use switch expressions for tier/type mappings
- Use readonly fields for immutable configuration
- Settings UI: `SettingsMenu.cs` draws every [Menu] row; add a setting with a [Menu] label/tooltip (and `index`/`parentIndex` under a header) instead of hand-drawing it. Hand-draw only live content (CC Types / Debug tabs in `CrowdControlSettingsUi`).

## Coordinate Systems

- **Grid** (`GridPosNum`, `Vector2`): tile-based map coordinates. Used for minimap drawing.
- **World** (`PosNum`, `Vector3`): 3D world coordinates. Convert to screen with `Camera.WorldToScreen`.
- **Screen** (`Vector2`): pixel coordinates. Used for ImGui and `Graphics.DrawText/DrawFrame`.
- GridToWorld multiplier: `250f / 23f`

## Common Patterns

### Entity Tracking
```csharp
// In EntityAdded — filter and cache
public override void EntityAdded(Entity entity)
{
    if (entity.Type == EntityType.Monster && entity.IsAlive)
        _trackedMonsters[entity.Id] = entity;
}

// In EntityRemoved — clean up
public override void EntityRemoved(Entity entity)
{
    _trackedMonsters.Remove(entity.Id);
}

// In AreaChange — clear all
public override void AreaChange(AreaInstance area)
{
    _trackedMonsters.Clear();
}
```

### Buff Monitoring
```csharp
var buffs = player.GetComponent<Buffs>()?.BuffsList;
if (buffs != null)
{
    for (int i = 0; i < buffs.Count; i++)
    {
        var buff = buffs[i];
        if (buff?.Name == "target_buff" && buff.Timer > 0)
        {
            // buff is active
        }
    }
}
```

### Rate-Limited Updates
```csharp
// Option 1: TimeCache — auto-invalidates after ms
private readonly TimeCache<List<MyData>> _cache;
_cache = new TimeCache<List<MyData>>(BuildData, 200); // refresh every 200ms

// Option 2: Stopwatch throttle
private readonly Stopwatch _timer = Stopwatch.StartNew();
if (_timer.ElapsedMilliseconds < 200) return null;
_timer.Restart();
```

### Inter-Plugin Communication
```csharp
// Expose a method
GameController.PluginBridge.SaveMethod("MyPlugin.GetData", (Func<MyData>)GetData);

// Consume another plugin's method
var getData = GameController.PluginBridge.GetMethod<Func<OtherData>>("OtherPlugin.GetData");
if (getData != null) { var data = getData(); }

// Events
GameController.PluginBridge.PublishEvent("MyPlugin.SomethingHappened", eventData);
GameController.PluginBridge.ReceiveEvent<EventType>("OtherPlugin.Event", handler);
```

### Custom ImGui Settings UI
```csharp
public override void DrawSettings()
{
    // Call base for default rendering, or skip for fully custom
    // base.DrawSettings();

    // Custom ImGui widgets
    var dl = ImGui.GetWindowDrawList();
    // ... custom drawing with ImDrawList
}
```

## Reference Plugins

Study other plugins in `Plugins/Source/` for pattern reference. Look at:
- Simple buff-tracking plugins for entity/buff patterns
- Minimap overlay plugins for coordinate system usage
- Settings-heavy plugins for ImGui custom UI patterns
