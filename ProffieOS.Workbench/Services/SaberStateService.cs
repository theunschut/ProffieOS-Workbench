using System.Globalization;
using ProffieOS.Workbench.Models;

namespace ProffieOS.Workbench.Services;

/// <summary>
/// Manages all saber state: presets, tracks, fonts, styles, volume, battery, run loop.
/// UI components subscribe to StateChanged and read from properties.
/// </summary>
public class SaberStateService(SaberCommandService commands)
{
    // ── State ─────────────────────────────────────────────────────────────────
    public List<Preset> Presets { get; } = [];
    public List<string> TrackList { get; } = [];
    public List<string> FontList { get; } = [];
    public Dictionary<string, NamedStyle> NamedStyles { get; } = new();

    public int CurrentPresetIndex { get; private set; } = -1;
    public string CurrentTrack { get; private set; } = "";
    public int Volume { get; private set; }
    public string BatteryVoltage { get; private set; } = "---";
    public bool IsOn { get; private set; }

    public bool HasEditMode { get; private set; }
    public bool HasSettings { get; private set; }
    public bool SettingsLoaded { get; private set; }
    public int MaxBladeLength { get; private set; }

    // ── Settings state (loaded lazily via LoadSettingsAsync) ──────────────────
    public bool HasSdToggle { get; private set; }
    public bool SdEnabled { get; private set; }
    public bool HasBrightness { get; private set; }
    public int Brightness { get; private set; } = 100;
    public bool HasClashThreshold { get; private set; }
    public float ClashThreshold { get; private set; } = 1.0f;
    public List<int> BladeLengths { get; } = [];
    public List<BoolSettingItem> GestureBoolSettings { get; } = [];
    public List<IntSettingItem> GestureIntSettings { get; } = [];

    public event Action? StateChanged;

    // ── Run loop ──────────────────────────────────────────────────────────────
    private CancellationTokenSource? _loopCts;
    private bool _initialised;

    public async Task StartAsync()
    {
        _initialised = false;
        Presets.Clear();
        TrackList.Clear();
        FontList.Clear();
        NamedStyles.Clear();
        BladeLengths.Clear();
        GestureBoolSettings.Clear();
        GestureIntSettings.Clear();
        HasSdToggle = false;
        HasBrightness = false;
        HasClashThreshold = false;
        SettingsLoaded = false;

        await Sync();
        await LoadPresets();
        Notify();

        _loopCts = new CancellationTokenSource();
        _ = RunLoop(_loopCts.Token);
    }

    public void Stop()
    {
        _loopCts?.Cancel();
        _loopCts = null;
    }

    private async Task Sync()
    {
        // Flush until we get a known response, matching the original SYNC()
        var x = 42;
        while (true)
        {
            var cmd = $"fnord{x}";
            var str = await commands.Send(cmd);
            if (str.StartsWith($"Whut? :{cmd}")) break;
            x++;
        }
    }

    private async Task LoadPresets()
    {
        var raw = await commands.Send("list_presets", retry: true);
        var lines = raw.Split('\n');
        Dictionary<string, string>? current = null;

        foreach (var line in lines)
        {
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq];
            var val = line[(eq + 1)..];

            if (key == "FONT")
            {
                if (current is not null) Presets.Add(Preset.FromDictionary(current));
                current = new Dictionary<string, string>();
            }
            if (current is not null) current[key] = val;
        }
        if (current is { Count: > 0 }) Presets.Add(Preset.FromDictionary(current));
    }

    private async Task RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Check tagging support once
                if (!commands.UseTagging)
                {
                    var v = await commands.Send("check| version");
                    commands.UseTagging = !v.StartsWith("Whut?");
                }

                CurrentPresetIndex = int.TryParse(await commands.Send("get_preset", true), out var p) ? p : CurrentPresetIndex;

                var track = await commands.Send("get_track", true);
                CurrentTrack = track.Split('\n')[0];

                if (int.TryParse(await commands.Send("get_volume", true), out var vol))
                    Volume = vol;

                BatteryVoltage = (await commands.Send("battery_voltage", true)).Trim();

                var onStr = await commands.Send("get_on", true);
                IsOn = int.TryParse(onStr, out var on) && on != 0;

                if (!_initialised)
                {
                    _initialised = true;
                    await LoadInitialData();
                }

                Notify();
                await Task.Delay(5000, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { /* keep running */ }
        }
    }

    private async Task LoadInitialData()
    {
        TrackList.AddRange(await GetList("list_tracks"));
        FontList.AddRange(await GetList("list_fonts"));

        var hasCommon = await HasDir("common");
        if (hasCommon)
            for (var i = 0; i < FontList.Count; i++)
                FontList[i] += ";common";

        await LoadNamedStyles();

        HasEditMode = NamedStyles.Count > 0;

        var maxBladeStr = await commands.Send("get_max_blade_length 1", true);
        MaxBladeLength = int.TryParse(maxBladeStr, out var ml) ? ml : 0;

        // Quick capability probes — keeps dashboard refresh fast
        var hasDimming   = await HasCmd("get_blade_dimming");
        var hasThreshold = await HasCmd("get_clash_threshold");
        var hasGesture   = await HasCmd("get_gesture test");
        var hasSd        = await HasCmd("sd");

        HasSettings = MaxBladeLength > 0 || hasDimming || hasThreshold || hasGesture || hasSd;

        Notify(); // dashboard refreshes here

        // Load all settings values in the background so the settings page is instant
        _ = LoadSettingsBackgroundAsync();
    }

    private async Task LoadSettingsBackgroundAsync()
    {
        try
        {
            await LoadSettingsValuesAsync();
            SettingsLoaded = true;
            Notify();
        }
        catch { /* best-effort */ }
    }

    private async Task LoadNamedStyles()
    {
        var styleNames = await GetList("list_named_styles");
        var templateIds = new List<int>();

        foreach (var styleName in styleNames)
        {
            var desc = await GetList($"describe_named_style {styleName}");
            if (desc.Count == 0) continue;

            var argString = desc[0];
            var argParts = argString.Split(',');
            var arguments = new List<StyleArgument>();

            for (var j = 1; j < desc.Count; j++)
            {
                var defaults = desc[j].Split(' ');
                var argLabel = j < argParts.Length ? argParts[j].Trim() : "";
                var argType = defaults.Length > 0 ? defaults[0] : "INT";
                var argDefault = defaults.Length > 1 ? defaults[1] : "";
                arguments.Add(new StyleArgument(argType, argLabel, argDefault));
            }

            if (styleName.Split(' ')[0] == "builtin" && arguments.Count >= 2)
                arguments = arguments.Skip(2).ToList();

            // Find a compatible template id (same arg string and arg types)
            var templateId = templateIds.Count;
            var bestMatches = -1;
            for (var j = 0; j < templateIds.Count; j++)
            {
                var tid = templateIds[j];
                var compatible = true;
                foreach (var existing in NamedStyles.Values)
                {
                    if (existing.TemplateId != tid) continue;
                    if (argString != existing.ArgString) { compatible = false; break; }
                    for (var k = 0; k < Math.Min(arguments.Count, existing.Args.Count); k++)
                    {
                        if (arguments[k].Type == "VOID" || existing.Args[k].Type == "VOID") continue;
                        if (arguments[k].Type != existing.Args[k].Type) { compatible = false; break; }
                    }
                    if (!compatible) break;
                }
                if (compatible && bestMatches < 0)
                {
                    bestMatches = 0;
                    templateId = tid;
                }
            }
            templateIds.Add(templateId);

            NamedStyles[styleName] = new NamedStyle
            {
                Name = styleName,
                Description = argParts.Length > 0 ? argParts[0] : "",
                ArgString = argString,
                Args = arguments,
                TemplateId = templateId
            };
        }
    }

    // ── Preset actions ────────────────────────────────────────────────────────

    public async Task SetPresetAsync(int index)
    {
        await commands.Send($"set_preset {index}");
        CurrentPresetIndex = index;
        Notify();
    }

    public async Task AddPresetAsync()
    {
        await commands.Send($"duplicate_preset {Presets.Count}");
        if (CurrentPresetIndex >= 0 && CurrentPresetIndex < Presets.Count)
        {
            var clone = new Preset
            {
                Name = Presets[CurrentPresetIndex].Name,
                Font = Presets[CurrentPresetIndex].Font,
                Track = Presets[CurrentPresetIndex].Track,
                Variation = Presets[CurrentPresetIndex].Variation,
                Styles = new Dictionary<int, string>(Presets[CurrentPresetIndex].Styles)
            };
            Presets.Add(clone);
        }
        Notify();
    }

    public async Task DeletePresetAsync(int index)
    {
        if (index < 0 || index >= Presets.Count || Presets.Count <= 1) return;
        await commands.Send($"delete_preset {index}");
        Presets.RemoveAt(index);
        CurrentPresetIndex = -1;
        await SetPresetAsync(0);
    }

    public async Task MovePresetAsync(int from, int to)
    {
        if (from == to) return;
        await commands.Send($"move_preset {to}");
        var preset = Presets[from];
        Presets.RemoveAt(from);
        Presets.Insert(to, preset);
        Notify();
    }

    // ── Edit actions ──────────────────────────────────────────────────────────

    public async Task SaveNameAsync(int index, string name)
    {
        if (index < 0 || index >= Presets.Count) return;
        if (Presets[index].Name == name) return;
        Presets[index].Name = name;
        await commands.Send($"set_name {name}");
        Notify();
    }

    public async Task SaveFontAsync(int index, string font)
    {
        if (index < 0 || index >= Presets.Count) return;
        if (Presets[index].Font == font) return;
        Presets[index].Font = font;
        await commands.Send($"set_font {font}");
        await SetPresetAsync(index);
    }

    public async Task SaveTrackAsync(int index, string track)
    {
        if (index < 0 || index >= Presets.Count) return;
        var wasPlaying = !string.IsNullOrEmpty(CurrentTrack);
        Presets[index].Track = track;
        await commands.Send($"set_track {track}");
        if (wasPlaying) await PlayTrackAsync(track);
    }

    public async Task SaveStyleAsync(int presetIndex, int blade, string style)
    {
        if (presetIndex < 0 || presetIndex >= Presets.Count) return;
        Presets[presetIndex].Styles[blade] = style;
        await commands.Send($"set_style{blade} {style}");
        await SetPresetAsync(presetIndex);
    }

    public async Task SaveVariationAsync(int presetIndex, int variation)
    {
        if (presetIndex < 0 || presetIndex >= Presets.Count) return;
        Presets[presetIndex].Variation = variation.ToString();
        await commands.Send($"variation {variation}");
        Notify();
    }

    // ── Control actions ───────────────────────────────────────────────────────

    public async Task TurnOnAsync()
    {
        await commands.Send("on");
        IsOn = true;
        Notify();
    }

    public async Task TurnOffAsync()
    {
        await commands.Send("off");
        IsOn = false;
        Notify();
    }

    public async Task SendControlAsync(string cmd) => await commands.Send(cmd);

    public async Task PlayTrackAsync(string track)
    {
        await commands.Send($"play_track {track}");
        CurrentTrack = track;
        Notify();
    }

    public async Task StopTrackAsync()
    {
        await commands.Send("stop_track");
        CurrentTrack = "";
        Notify();
    }

    public async Task SetVolumeAsync(int volume)
    {
        Volume = volume;
        await commands.Send($"set_volume {volume}");
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    public async Task<string> GetSettingAsync(string cmd) => await commands.Send(cmd, retry: true);
    public async Task SendSettingAsync(string cmd) => await commands.Send(cmd);

    /// <summary>Shows settings immediately if already loaded by startup, otherwise loads from board.</summary>
    public async Task LoadSettingsAsync()
    {
        if (SettingsLoaded)
        {
            // Data already loaded during startup — show immediately
            Notify();
            return;
        }

        Notify(); // show loading bar

        await LoadSettingsValuesAsync();
        SettingsLoaded = true;
        Notify();
    }

    private async Task LoadSettingsValuesAsync()
    {
        HasSdToggle = false;
        HasBrightness = false;
        HasClashThreshold = false;
        BladeLengths.Clear();
        GestureBoolSettings.Clear();
        GestureIntSettings.Clear();

        var sdStr = await GetOptional("sd");
        HasSdToggle = sdStr is not null;
        if (HasSdToggle) SdEnabled = float.TryParse(sdStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var sv) && sv > 0.5f;

        var dimStr = await GetOptional("get_blade_dimming");
        HasBrightness = dimStr is not null;
        if (HasBrightness && int.TryParse(dimStr!.Trim(), out var dim))
            Brightness = (int)Math.Round(Math.Pow(dim / 16384.0, 1.0 / 2.2) * 100);

        var threshStr = await GetOptional("get_clash_threshold");
        HasClashThreshold = threshStr is not null;
        if (HasClashThreshold && float.TryParse(threshStr!.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var thresh))
            ClashThreshold = thresh;

        if (MaxBladeLength > 0)
        {
            for (var blade = 1; ; blade++)
            {
                var lenStr = await GetOptional($"get_blade_length {blade}");
                if (lenStr is null) break;
                if (!int.TryParse(lenStr.Trim(), out var len) || len == 0) break;

                var bladeMax = 0;
                var bladeMaxStr = await GetOptional($"get_max_blade_length {blade}");
                if (bladeMaxStr is not null)
                    int.TryParse(bladeMaxStr.Trim(), out bladeMax);

                if (len == -1)
                {
                    // Match original app behavior: if per-blade max is not available,
                    // treat this as end-of-valid-blades.
                    if (bladeMax <= 0) break;
                    len = bladeMax;
                }

                BladeLengths.Add(len);
            }
        }

        await TryLoadBoolSetting("gesture", "gestureon",   "gesture ignition");
        await TryLoadBoolSetting("gesture", "swingon",     "swing ignition");
        await TryLoadBoolSetting("gesture", "twiston",     "twist ignition");
        await TryLoadBoolSetting("gesture", "thruston",    "thrust ignition");
        await TryLoadBoolSetting("gesture", "stabon",      "stab ignition");
        await TryLoadBoolSetting("gesture", "twistoff",    "twist off");
        await TryLoadBoolSetting("gesture", "powerlock",   "power lock");
        await TryLoadBoolSetting("gesture", "forcepush",   "force push");
        await TryLoadIntSetting ("gesture", "swingonspeed","swing on speed");
        await TryLoadIntSetting ("gesture", "forcepushlen","force push length");
        await TryLoadIntSetting ("gesture", "lockupdelay", "lockup delay");
        await TryLoadIntSetting ("gesture", "clashdetect", "clash detect");
        await TryLoadIntSetting ("gesture", "maxclash",    "max clash strength");
    }

    public async Task SaveSdAsync(bool val)
    {
        SdEnabled = val;
        await commands.Send($"sd {(val ? "1" : "0")}");
    }

    public async Task SaveBrightnessAsync(int percent)
    {
        Brightness = percent;
        var raw = (int)Math.Round(Math.Pow(percent / 100.0, 2.2) * 16384);
        await commands.Send($"set_blade_dimming {raw}");
    }

    public async Task SaveClashThresholdAsync(float val)
    {
        ClashThreshold = val;
        await commands.Send($"set_clash_threshold {val.ToString(CultureInfo.InvariantCulture)}");
    }

    public async Task SaveBladeLengthAsync(int blade, int length)
    {
        length = Math.Min(length, MaxBladeLength);
        if (blade - 1 < BladeLengths.Count) BladeLengths[blade - 1] = length;
        await commands.Send($"set_blade_length {blade} {length}");
    }

    public async Task SaveBoolGestureAsync(int idx, bool val)
    {
        if (idx < 0 || idx >= GestureBoolSettings.Count) return;
        var s = GestureBoolSettings[idx];
        GestureBoolSettings[idx] = s with { Value = val };
        await commands.Send($"set_{s.BaseCmd} {s.Variable} {(val ? "1" : "0")}");
    }

    public async Task SaveIntGestureAsync(int idx, int val)
    {
        if (idx < 0 || idx >= GestureIntSettings.Count) return;
        var s = GestureIntSettings[idx];
        GestureIntSettings[idx] = s with { Value = val };
        await commands.Send($"set_{s.BaseCmd} {s.Variable} {val}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public async Task<List<string>> GetList(string cmd)
    {
        var s = await commands.Send(cmd, retry: true);
        if (s.StartsWith("Whut?")) return [];
        var lines = s.Split('\n').ToList();
        if (lines.Count > 0 && string.IsNullOrEmpty(lines[^1])) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private async Task<bool> HasCmd(string cmd)
    {
        var s = await commands.Send(cmd, retry: true);
        return !string.IsNullOrWhiteSpace(s) && !s.StartsWith("Whut?");
    }

    private async Task<bool> HasDir(string dir)
    {
        var entries = await GetList($"dir {dir}");
        return !(entries.Count == 1 && entries[0] == "No such directory.");
    }

    /// <summary>Returns trimmed value, or null if the command is unsupported or returned empty.</summary>
    private async Task<string?> GetOptional(string cmd)
    {
        // Match original app behavior: probe settings with retries and wait for the response.
        var s = await commands.Send(cmd, retry: true);
        return s.StartsWith("Whut?") || string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private async Task TryLoadBoolSetting(string baseCmd, string variable, string label)
    {
        var val = await GetOptional($"get_{baseCmd} {variable}");
        if (val is null) return;
        GestureBoolSettings.Add(new BoolSettingItem(baseCmd, variable, label,
            float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && f > 0.5f));
    }

    private async Task TryLoadIntSetting(string baseCmd, string variable, string label)
    {
        var val = await GetOptional($"get_{baseCmd} {variable}");
        if (val is null) return;
        GestureIntSettings.Add(new IntSettingItem(baseCmd, variable, label,
            int.TryParse(val, out var i) ? i : 0));
    }

    private void Notify() => StateChanged?.Invoke();
}
