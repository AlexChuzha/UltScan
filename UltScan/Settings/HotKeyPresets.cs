using System.Collections.Generic;
using System.Linq;

namespace UltScan;

public sealed class HotKeyPreset
{
    public HotKeyPreset(string id, string displayName, string hint, ModifierKeys modifiers, uint virtualKey, string? warning = null)
    {
        Id = id;
        DisplayName = displayName;
        Hint = hint;
        Modifiers = modifiers;
        VirtualKey = virtualKey;
        Warning = warning;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Hint { get; }
    public string? Warning { get; }
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
        "Win + Shift + Z",
        "Быстрое выделение области. Нейтральный вариант с умеренной частотой конфликтов.",
        WinShift,
        VkZ);

    public static readonly IReadOnlyList<HotKeyPreset> All = new List<HotKeyPreset>
    {
        Default,
        new HotKeyPreset(
            "win_shift_s",
            "Win + Shift + S",
            "Привычно для пользователей Windows (скриншот), но может конфликтовать со Snipping Tool.",
            WinShift,
            VkS),
        new HotKeyPreset(
            "win_shift_t",
            "Win + Shift + T",
            "Ассоциация с Text. Часто свободно.",
            WinShift,
            VkT),
        new HotKeyPreset(
            "win_shift_o",
            "Win + Shift + O",
            "Ассоциация с Overlay. Обычно свободно.",
            WinShift,
            VkO),
        new HotKeyPreset(
            "alt_shift_z",
            "Alt + Shift + Z",
            "Удобно для левой руки, но Alt+Shift часто занято переключением раскладки.",
            AltShift,
            VkZ),
        new HotKeyPreset(
            "ctrl_shift_z",
            "Ctrl + Shift + Z",
            "Привычная связка, но часто занята в редакторах.",
            CtrlShift,
            VkZ),
        new HotKeyPreset(
            "ctrl_alt_z",
            "Ctrl + Alt + Z",
            "Редко занята, но менее удобна одной рукой.",
            CtrlAlt,
            VkZ),
        new HotKeyPreset(
            "win_alt_z",
            "Win + Alt + Z",
            "Популярно в играх/оверлеях (NVIDIA), возможны конфликты.",
            WinAlt,
            VkZ),
        new HotKeyPreset(
            "ctrl_only",
            "Ctrl",
            "Самый быстрый, но и самый рискованный вариант.",
            CtrlOnly,
            VkControl,
            "Внимание: одиночный Ctrl часто перехватывается системой и приложениями; возможно, хоткей не сработает или будет мешать набору текста.")
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
