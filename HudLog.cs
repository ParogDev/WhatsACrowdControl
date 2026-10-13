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
///   - <see cref="Fault"/>: something broke. HUD log file at Error, full text, so hud_log level=error finds it, plus
///     the screen. A key is written and shown at most once per <see cref="RepeatSec"/>; repeats inside that window
///     (a per-frame exception) are counted and written as "(N more in the last 5 s)" with the last text when the key
///     comes back after the window, or on <see cref="Flush"/> (call it from OnClose).
/// The file is written through the HUD's own Serilog logger (Core.Logger, the one DebugWindow writes to), found by
/// reflection so the plugin needs no Serilog reference. While it is not set yet (not now) a message falls back to the
/// screen. A missing member is broken for good; a write that throws falls back for <see cref="RetrySec"/>, then tries
/// again. Either break is named once on screen: nothing is swallowed.
/// </summary>
internal static class HudLog
{
    private const double RepeatSec = 5, RetrySec = 30, UnsetRetrySec = 1;
    private const int MaxKeys = 64;   // past this, closed windows are written and forgotten (keys built from varying text)
    private static readonly string Prefix = $"[{typeof(HudLog).Assembly.GetName().Name}] ";

    private static readonly object Lock = new();
    private static readonly Dictionary<string, (DateTime at, int skipped, string last)> Seen = new();
    private static object? _logger;
    private static MethodInfo? _info, _error;
    private static string? _broken;       // why the file can't be written now (null = fine or not set yet)
    private static bool _permanent;       // the break is a missing member: never retried
    private static DateTime _retryAt;
    private static bool _brokenShown;

    /// <summary>Routine diagnostics: the HUD log file only, never the player's screen.</summary>
    public static void Quiet(string msg)
    {
        if (!Write(false, Prefix + msg)) DebugWindow.LogMsg(Prefix + msg);
    }

    /// <summary>
    /// Something broke: the HUD log file at Error and the screen, at most once per <see cref="RepeatSec"/> per
    /// <paramref name="key"/> (default: the message; pass a stable key when the text carries changing values).
    /// </summary>
    public static void Fault(string msg, string? key = null)
    {
        key ??= msg;
        var now = DateTime.UtcNow;
        int skipped;
        string? last;
        List<(int n, string last)>? evicted = null;
        lock (Lock)
        {
            if (Seen.Count >= MaxKeys)
                foreach (var k in Seen.Keys.ToList())
                {
                    var e = Seen[k];
                    if ((now - e.at).TotalSeconds < RepeatSec) continue;
                    if (e.skipped > 0) (evicted ??= new()).Add((e.skipped, e.last));
                    Seen.Remove(k);
                }
            if (Seen.TryGetValue(key, out var seen) && (now - seen.at).TotalSeconds < RepeatSec)
            {
                Seen[key] = (seen.at, seen.skipped + 1, msg);
                return;
            }
            skipped = seen.skipped;
            last = seen.last;
            Seen[key] = (now, 0, msg);
        }
        if (evicted != null)
            foreach (var (n, l) in evicted) WriteFault($"({n} more in the last {RepeatSec:0} s) {l}");
        if (skipped > 0) WriteFault($"({skipped} more in the last {RepeatSec:0} s) {last}");
        WriteFault(msg);
    }

    /// <summary>OnClose: write the counts of repeats not yet reported.</summary>
    public static void Flush()
    {
        List<(int n, string last)> due;
        lock (Lock)
        {
            due = Seen.Values.Where(e => e.skipped > 0).Select(e => (e.skipped, e.last)).ToList();
            Seen.Clear();
        }
        foreach (var (n, last) in due)
            if (!Write(true, $"{Prefix}({n} more in the last {RepeatSec:0} s) {last}")) DebugWindow.LogError(Prefix + last);
    }

    private static void WriteFault(string msg)
    {
        Write(true, Prefix + msg);
        DebugWindow.LogError(Prefix + msg);
    }

    /// <summary>Write one line to the HUD's log file. False: no logger now, or the link is broken (named once).</summary>
    private static bool Write(bool error, string text)
    {
        object? logger;
        MethodInfo? write;
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
            write = error ? _error : _info;
        }
        if (logger != null && write != null)
        {
            try
            {
                // Template "{M:l}" with the text as its value: braces in the text are never parsed as a template.
                write.Invoke(logger, new object[] { "{M:l}", new object[] { text } });
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

    /// <summary>Find Core.Logger and its Information / Error(string, object[]). Sets _broken (permanent) naming the
    /// missing link; leaves everything null while the logger is just not set yet.</summary>
    private static void Bind()
    {
        var prop = typeof(Core).GetProperties(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(p => p.Name == "Logger");
        if (prop == null) { _broken = "Core.Logger: no such public static property in this HUD build"; _permanent = true; return; }
        var logger = prop.GetValue(null);
        if (logger == null) return;
        var args = new[] { typeof(string), typeof(object[]) };
        MethodInfo? info = null, err = null;
        foreach (var t in new[] { prop.PropertyType }.Concat(prop.PropertyType.GetInterfaces()).Append(logger.GetType()))
        {
            info ??= t.GetMethod("Information", args);
            err ??= t.GetMethod("Error", args);
        }
        if (info == null || err == null)
        {
            _broken = $"{prop.PropertyType.FullName}: no Information/Error(string, object[])";
            _permanent = true;
            return;
        }
        _info = info;
        _error = err;
        _logger = logger;
    }
}
