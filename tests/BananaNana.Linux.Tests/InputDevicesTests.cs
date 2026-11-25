using BananaNana.Linux.Evdev;
using Xunit;

namespace BananaNana.Linux.Tests;

public class InputDevicesTests
{
    // 12 words (768 key codes). The last word is fffffffffffffffe: bit 0 clear, bits 1..63 set,
    // so KEY_A (30) and KEY_SPACE (57) are set and bit 0 is not.
    const string KeyboardCaps = "0 0 0 0 0 0 0 0 0 0 0 fffffffffffffffe";

    // BTN_LEFT is 272 = word 4, bit 16. With 5 words it sits in the first (most significant) word.
    const string MouseCaps = "10000 0 0 0 0";

    [Fact]
    public void Keyboard_capabilities_are_classified_as_keyboard()
    {
        Assert.Equal(DeviceKind.Keyboard, InputDevices.Classify(KeyboardCaps));
    }

    [Fact]
    public void Mouse_capabilities_are_classified_as_mouse()
    {
        Assert.Equal(DeviceKind.Mouse, InputDevices.Classify(MouseCaps));
    }

    [Fact]
    public void Devices_without_letters_and_space_or_buttons_are_other()
    {
        // Only KEY_ESC (1) and KEY_F1 (59) set: a power button, not a keyboard.
        Assert.Equal(DeviceKind.Other, InputDevices.Classify("0 0 0 0 0 0 0 0 0 0 0 0 800000000000002"));
    }

    [Fact]
    public void Words_are_parsed_most_significant_first()
    {
        var bits = InputDevices.ParseBits("1 0");
        Assert.Equal(new ulong[] { 1, 0 }, bits);
        Assert.True(InputDevices.HasBit(bits, 64));   // the "1" is the high word
        Assert.False(InputDevices.HasBit(bits, 0));
    }

    [Fact]
    public void HasBit_reads_the_right_bit_of_a_64_bit_word()
    {
        var bits = InputDevices.ParseBits(KeyboardCaps);
        Assert.True(InputDevices.HasBit(bits, 30));
        Assert.True(InputDevices.HasBit(bits, 57));
        Assert.False(InputDevices.HasBit(bits, 0));
        Assert.False(InputDevices.HasBit(bits, 700)); // beyond the words: not set
    }

    [Fact]
    public void Trailing_and_repeated_spaces_are_ignored()
    {
        Assert.Equal(InputDevices.ParseBits(KeyboardCaps), InputDevices.ParseBits("  " + KeyboardCaps.Replace(" ", "  ") + " \n"));
    }
}
