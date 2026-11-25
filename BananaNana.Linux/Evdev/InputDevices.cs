using System.Globalization;

namespace BananaNana.Linux.Evdev;

public enum DeviceKind { Other, Keyboard, Mouse }

public sealed record InputDeviceInfo(string EventName, string Node, string Name, DeviceKind Kind);

// Finds input devices through sysfs. The bit parsing is pure so it can be unit-tested without hardware.
public static class InputDevices
{
    public const string OwnDeviceName = "BananaNana virtual keyboard";
    public const string DevDir = "/dev/input";
    const string SysDir = "/sys/class/input";
    const int KeyA = 30, KeySpace = 57, BtnLeft = 0x110;

    // Describes /dev/input/<eventName> from sysfs. Null while sysfs is not ready yet, or for our own device.
    public static InputDeviceInfo? Describe(string eventName)
    {
        string dev = Path.Combine(SysDir, eventName, "device");
        try
        {
            string name = File.ReadAllText(Path.Combine(dev, "name")).Trim();
            if (name == OwnDeviceName) return null;
            var kind = Classify(File.ReadAllText(Path.Combine(dev, "capabilities", "key")));
            return new InputDeviceInfo(eventName, Path.Combine(DevDir, eventName), name, kind);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static IEnumerable<InputDeviceInfo> Enumerate()
    {
        if (!Directory.Exists(SysDir)) yield break;
        // Entries in /sys/class/input are symlinks, so enumerate entries rather than directories.
        foreach (var entry in Directory.EnumerateFileSystemEntries(SysDir, "event*"))
            if (Describe(Path.GetFileName(entry)!) is { } info) yield return info;
    }

    // keyCaps is the content of capabilities/key: 64-bit hex words, most significant word first.
    public static DeviceKind Classify(string keyCaps)
    {
        var bits = ParseBits(keyCaps);
        if (HasBit(bits, KeyA) && HasBit(bits, KeySpace)) return DeviceKind.Keyboard;
        return HasBit(bits, BtnLeft) ? DeviceKind.Mouse : DeviceKind.Other;
    }

    public static ulong[] ParseBits(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) // any whitespace, sysfs adds a newline
            .Select(w => ulong.Parse(w, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .ToArray();

    // words[0] is the most significant word, so bit b lives in words[Length - 1 - b / 64].
    public static bool HasBit(ulong[] words, int bit)
    {
        int index = words.Length - 1 - bit / 64;
        return index >= 0 && ((words[index] >> (bit % 64)) & 1) != 0;
    }
}
