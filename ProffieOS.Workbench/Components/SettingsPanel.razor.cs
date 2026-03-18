using MudBlazor;
using ProffieOS.Workbench.Models;

namespace ProffieOS.Workbench.Components;

public partial class SettingsPanel : IDisposable
{
    private static readonly HashSet<string> IgnitionBoolVariables =
    [
        "gestureon",
        "swingon",
        "twiston",
        "thruston",
        "stabon"
    ];

    private static readonly HashSet<string> ActionBoolVariables =
    [
        "twistoff",
        "powerlock",
        "forcepush"
    ];

    private static readonly HashSet<string> TimingIntVariables =
    [
        "forcepushlen",
        "lockupdelay"
    ];

    private static readonly HashSet<string> SensitivityIntVariables =
    [
        "swingonspeed",
        "clashdetect",
        "maxclash"
    ];

    private bool HasSystemSettings => State.HasSdToggle;
    private bool HasBladeSettings => State.HasBrightness || State.HasClashThreshold || State.BladeLengths.Count > 0;

    private IEnumerable<(int Index, BoolSettingItem Setting)> IgnitionBoolSettings => BoolSettings(IgnitionBoolVariables);
    private IEnumerable<(int Index, BoolSettingItem Setting)> ActionBoolSettings => BoolSettings(ActionBoolVariables);
    private IEnumerable<(int Index, BoolSettingItem Setting)> OtherBoolSettings => BoolSettingsExcept(IgnitionBoolVariables, ActionBoolVariables);

    private IEnumerable<(int Index, IntSettingItem Setting)> TimingIntSettings => IntSettings(TimingIntVariables);
    private IEnumerable<(int Index, IntSettingItem Setting)> SensitivityIntSettings => IntSettings(SensitivityIntVariables);
    private IEnumerable<(int Index, IntSettingItem Setting)> OtherIntSettings => IntSettingsExcept(TimingIntVariables, SensitivityIntVariables);

    protected override void OnInitialized()
    {
        State.StateChanged += OnStateChanged;
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    private IEnumerable<(int Index, BoolSettingItem Setting)> BoolSettings(HashSet<string> variables) =>
        State.GestureBoolSettings
            .Select((setting, index) => (Index: index, Setting: setting))
            .Where(x => variables.Contains(x.Setting.Variable));

    private IEnumerable<(int Index, BoolSettingItem Setting)> BoolSettingsExcept(params HashSet<string>[] categories)
    {
        var excluded = categories.SelectMany(x => x).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return State.GestureBoolSettings
            .Select((setting, index) => (Index: index, Setting: setting))
            .Where(x => !excluded.Contains(x.Setting.Variable));
    }

    private IEnumerable<(int Index, IntSettingItem Setting)> IntSettings(HashSet<string> variables) =>
        State.GestureIntSettings
            .Select((setting, index) => (Index: index, Setting: setting))
            .Where(x => variables.Contains(x.Setting.Variable));

    private IEnumerable<(int Index, IntSettingItem Setting)> IntSettingsExcept(params HashSet<string>[] categories)
    {
        var excluded = categories.SelectMany(x => x).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return State.GestureIntSettings
            .Select((setting, index) => (Index: index, Setting: setting))
            .Where(x => !excluded.Contains(x.Setting.Variable));
    }

    private async Task SaveSd(bool val)
    {
        try { await State.SaveSdAsync(val); }
        catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    private async Task SaveBrightness(int percent)
    {
        try { await State.SaveBrightnessAsync(percent); }
        catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    private async Task SaveClashThreshold(float val)
    {
        try { await State.SaveClashThresholdAsync(val); }
        catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    private async Task SaveBladeLength(int blade, int length)
    {
        try { await State.SaveBladeLengthAsync(blade, length); }
        catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    private async Task SaveBoolGesture(int idx, bool val)
    {
        try { await State.SaveBoolGestureAsync(idx, val); }
        catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    private async Task SaveIntGesture(int idx, int val)
    {
        try { await State.SaveIntGestureAsync(idx, val); }
        catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    public void Dispose() => State.StateChanged -= OnStateChanged;
}