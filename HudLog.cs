#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExileCore;

namespace WhatsACrowdControl;

/// <summary>
/// Where this plugin's messages go. The HUD's top-left text (DebugWindow) is the player's screen, not our console:
/// it was far too busy while playing (2026-10-12). Copied from Whats An AI Bridge's Shared/HudLog.cs (a copy, never
/// a reference: plugins don't compile-link each other).
///   - <see cref="Quiet"/>: routine diagnostics. HUD log file only, at Information (agents read it with MCP hud_log).
///   - <see cref="Fault"/>: something broke. Shown on screen through DebugWindow.LogError, which also writes it to the
///     HUD log file at Error (hud_log level=error finds it; DebugWindow writes each distinct text to the file once and
///     afterwards only refreshes the screen line). A key is shown at most once per <see cref="RepeatSec"/>; repeats
///     inside that window (a per-frame exception) are counted and shown as "(N more in the last 5 s)" with the last
///     text when the key comes back after the window, or on <see cref="Flush"/> (call it from OnClose). At most
///     <see cref="MaxKeys"/> keys are tracked; past that, new keys are shown without dedup (texts that vary).
/// Quiet writes through the HUD's own Serilog logger (Core.Logger, the one DebugWindow writes to), found by reflection
/// so the plugin needs no Serilog reference; its Information(string, object[]) is bound once into a delegate (no
/// per-line MethodInfo.Invoke). If it can't be bound as a delegate, that is named once in the log file and lines go
/// through MethodInfo.Invoke instead. While it is not set yet (not now, looked up at most once a second) a line
/// falls back to the screen. A missing member is broken for good; a write that throws falls back for
/// <see cref="RetrySec"/>, then tries again. Each break is named once on screen: nothing is swallowed.
/// </summary>
internal static class HudLog
{
    private const double RepeatSec = 5, RetrySec = 30, UnsetRetrySec = 1;
    private const int MaxKeys = 64;
    private static readonly string Prefix = $"[{typeof(HudLog).Assembly.GetName().Name}] ";

    private static readonly object Lock = new();
    private static readonly Dictionary<string, (DateTime at, int skipped, string last)> Seen = new();
    private static object? _logger;
    private static Action<string, object[]>? _info;   // bound once in Bind(): a direct call per line, no MethodInfo.Invoke
    private static string? _broken;       // why the file can't be written now (null = fine or not set yet)
    private static bool _permanent;       // the break is a missing member: never retried
    private static DateTime _retryAt;
    private static bool _brokenShown;     // the current break was named on screen (reset when a write works again)

    /// <summary>Routine diagnostics: the HUD log file only, never the player's screen.</summary>
    public static void Quiet(string msg)
    {
        if (!Write(Prefix + msg)) DebugWindow.LogMsg(Prefix + msg);
    }

    /// <summary>
    /// Something broke: the screen and the HUD log file at Error, at most once per <see cref="RepeatSec"/> per
    /// <paramref name="key"/> (default: the message; pass a stable key when the text carries changing values).
    /// </summary>
    public static void Fault(string msg, string? key = null)
    {
        key ??= msg;
        var now = DateTime.UtcNow;
        int skipped = 0;
        string? last = null;
        lock (Lock)
        {
            if (Seen.TryGetValue(key, out var seen))
            {
                if ((now - seen.at).TotalSeconds < RepeatSec)
                {
                    Seen[key] = (seen.at, seen.skipped + 1, msg);
                    return;
                }
                skipped = seen.skipped;
                last = seen.last;
                Seen[key] = (now, 0, msg);
            }
            else if (Seen.Count < MaxKeys)
                Seen[key] = (now, 0, msg);
        }
        if (skipped > 0) ShowFault($"({skipped} more in the last {RepeatSec:0} s) {last}");
        ShowFault(msg);
    }

    /// <summary>OnClose: show the counts of repeats not yet reported.</summary>
    public static void Flush()
    {
        List<(int n, string last)> due;
        lock (Lock)
        {
            due = Seen.Values.Where(e => e.skipped > 0).Select(e => (e.skipped, e.last)).ToList();
            Seen.Clear();
        }
        foreach (var (n, last) in due) ShowFault($"({n} more in the last {RepeatSec:0} s) {last}");
    }

    private static void ShowFault(string msg) => DebugWindow.LogError(Prefix + msg);

    /// <summary>Write one Information line to the HUD's log file. False: no logger now, or the link is broken (named once).</summary>
    private static bool Write(string text)
    {
        object? logger;
        Action<string, object[]>? write;
        lock (Lock)
        {
            if (_logger == null && !_permanent && DateTime.UtcNow >= _retryAt)
            {
                _broken = null;
                Bind();
                // Not set yet (not now): look again in a second, not on every message.
                if (_logger == null && _broken == null) _retryAt = DateTime.UtcNow.AddSeconds(UnsetRetrySec);
            }
            logger = _logger;
            write = _info;
        }
        if (logger != null && write != null)
        {
            try
            {
                // Template "{M:l}" with the text as its value: braces in the text are never parsed as a template.
                write("{M:l}", new object[] { text });
                _brokenShown = false;
                return true;
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException { InnerException: { } ie } ? ie : ex;
                lock (Lock)
                {
                    _broken = $"writing to Core.Logger: {inner.GetType().Name}: {inner.Message}";
                    _logger = null;
                    _retryAt = DateTime.UtcNow.AddSeconds(RetrySec);
                }
            }
        }
        string? broken;
        lock (Lock)
        {
            broken = _broken;
            if (broken == null || _brokenShown) return false;   // not set yet (not now), or already named
            _brokenShown = true;
        }
        DebugWindow.LogError($"{Prefix}[Log] the HUD log file can't be written ({broken}): messages go to the screen instead"
            + (_permanent ? "" : $", retried every {RetrySec:0} s"));
        return false;
    }

    /// <summary>Find Core.Logger and its Information(string, object[]). Sets _broken (permanent) naming the missing
    /// link; leaves everything null while the logger is just not set yet.</summary>
    private static void Bind()
    {
        var prop = typeof(Core).GetProperties(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(p => p.Name == "Logger");
        if (prop == null) { _broken = "Core.Logger: no such public static property in this HUD build"; _permanent = true; return; }
        var logger = prop.GetValue(null);
        if (logger == null) return;
        var args = new[] { typeof(string), typeof(object[]) };
        MethodInfo? info = null;
        foreach (var t in new[] { prop.PropertyType }.Concat(prop.PropertyType.GetInterfaces()).Append(logger.GetType()))
            info ??= t.GetMethod("Information", args);
        if (info == null)
        {
            _broken = $"{prop.PropertyType.FullName}: no Information(string, object[])";
            _permanent = true;
            return;
        }
        // Bind once: a delegate closed over the logger, so each line is a direct call (an interface method still
        // dispatches virtually through it). A bind that fails (signature or visibility mismatch) is named once in the
        // log file and lines fall back to MethodInfo.Invoke.
        string? unbound = null;
        Action<string, object[]> write;
        try
        {
            write = (Action<string, object[]>)info.CreateDelegate(typeof(Action<string, object[]>), logger);
        }
        catch (Exception ex)
        {
            unbound = $"{info.DeclaringType?.FullName}.Information(string, object[]) can't be bound as Action<string, object[]> ({ex.GetType().Name}: {ex.Message})";
            write = (template, values) => info.Invoke(logger, new object[] { template, values });
        }
        _info = write;
        _logger = logger;
        if (unbound == null) return;
        try { write("{M:l}", new object[] { $"{Prefix}[Log] {unbound}: falling back to MethodInfo.Invoke per line" }); }
        catch { /* a failing logger is named by the next Write, which hits the same throw */ }
    }
}
