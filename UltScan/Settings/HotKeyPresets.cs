using System.Collections.Generic;
using System.Linq;

namespace UltScan;

public sealed class HotKeyPreset
{
    public HotKeyPreset(
        string id,
        string labelKey,
        string hintKey,
        ModifierKeys modifiers,
        uint virtualKey,
        string? warningKey = null)
    {
        Id = id;
        LabelKey = labelKey;
        HintKey = hintKey;
        Modifiers = modifiers;
        VirtualKey = virtualKey;
        WarningKey = warningKey;
    }

    public string Id { get; }
    public string LabelKey { get; }
    public string HintKey { get; }
    public string? WarningKey { get; }
    public ModifierKeys Modifiers { get; }
    public uint VirtualKey { get; }

    public HotKeyConfig ToConfig()
    {
        return new HotKeyConfig
        {
            Id = Id,
            Modifiers = Modifiers,
            VirtualKey = VirtualKey
        };
    }
}

public static class HotKeyPresets
{
    private const uint VkZ = 0x5A;
    private const uint VkS = 0x53;
    private const uint VkT = 0x54;
    private const uint VkO = 0x4F;
    private const uint VkControl = 0x11;

    private static readonly ModifierKeys WinShift = ModifierKeys.Win | ModifierKeys.Shift | ModifierKeys.NoRepeat;
    private static readonly ModifierKeys AltShift = ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.NoRepeat;
    private static readonly ModifierKeys CtrlShift = ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.NoRepeat;
    private static readonly ModifierKeys CtrlAlt = ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.NoRepeat;
    private static readonly ModifierKeys WinAlt = ModifierKeys.Win | ModifierKeys.Alt | ModifierKeys.NoRepeat;
    private static readonly ModifierKeys CtrlOnly = ModifierKeys.Control | ModifierKeys.NoRepeat;

    public static readonly HotKeyPreset Default = new(
        "win_shift_z",
        "HotKey.Preset.win_shift_z.Label",
        "HotKey.Preset.win_shift_z.Hint",
        WinShift,
        VkZ);

    public static readonly IReadOnlyList<HotKeyPreset> All = new List<HotKeyPreset>
    {
        Default,
        new HotKeyPreset(
            "win_shift_s",
            "HotKey.Preset.win_shift_s.Label",
            "HotKey.Preset.win_shift_s.Hint",
            WinShift,
            VkS),
        new HotKeyPreset(
            "win_shift_t",
            "HotKey.Preset.win_shift_t.Label",
            "HotKey.Preset.win_shift_t.Hint",
            WinShift,
            VkT),
        new HotKeyPreset(
            "win_shift_o",
            "HotKey.Preset.win_shift_o.Label",
            "HotKey.Preset.win_shift_o.Hint",
            WinShift,
            VkO),
        new HotKeyPreset(
            "alt_shift_z",
            "HotKey.Preset.alt_shift_z.Label",
            "HotKey.Preset.alt_shift_z.Hint",
            AltShift,
            VkZ),
        new HotKeyPreset(
            "ctrl_shift_z",
            "HotKey.Preset.ctrl_shift_z.Label",
            "HotKey.Preset.ctrl_shift_z.Hint",
            CtrlShift,
            VkZ),
        new HotKeyPreset(
            "ctrl_alt_z",
            "HotKey.Preset.ctrl_alt_z.Label",
            "HotKey.Preset.ctrl_alt_z.Hint",
            CtrlAlt,
            VkZ),
        new HotKeyPreset(
            "win_alt_z",
            "HotKey.Preset.win_alt_z.Label",
            "HotKey.Preset.win_alt_z.Hint",
            WinAlt,
            VkZ),
        new HotKeyPreset(
            "ctrl_only",
            "HotKey.Preset.ctrl_only.Label",
            "HotKey.Preset.ctrl_only.Hint",
            CtrlOnly,
            VkControl,
            "HotKey.Preset.ctrl_only.Warning")
    };

    public static HotKeyPreset? FindById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return All.FirstOrDefault(p => p.Id == id);
    }
}
