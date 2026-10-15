using System.Reflection;
using System.Runtime.InteropServices;
using NexoDesk.Desktop;
using NexoDesk.Shared.Messages;

namespace NexoDesk.Windows.Tests;

public class PhysicalInputTests
{
    [Fact]
    public void InjectedKeyboardAndMouseNeverCountAsPhysicalActivity()
    {
        var state = new PhysicalInputState();
        Assert.False(state.ObserveKeyboard(0x100, 29, 0x10));
        Assert.False(state.IsDown(29, false));
        Assert.False(state.ObserveMouse(0x201, 5, 8, 1));
        Assert.False(state.IsDown(PointerButton.Left));
        Assert.False(state.ObserveMouse(0x200, 5, 8, 3));
    }

    [Fact]
    public void PhysicalExtendedModifierIsKeptUntilItsRealRelease()
    {
        var state = new PhysicalInputState();
        Assert.True(state.ObserveKeyboard(0x104, 29, 0x21));
        Assert.True(state.IsDown(29, true)); Assert.False(state.IsDown(29, false));
        Assert.False(state.ObserveKeyboard(0x104, 29, 0x21));
        Assert.False(state.ObserveKeyboard(0x105, 29, 0xA1));
        Assert.False(state.IsDown(29, true));
    }

    [Fact]
    public void PhysicalMouseUpAndStationaryMessagesDoNotAcquireControl()
    {
        var state = new PhysicalInputState();
        Assert.True(state.ObserveMouse(0x201, 10, 10, 0)); Assert.True(state.IsDown(PointerButton.Left));
        Assert.False(state.ObserveMouse(0x202, 10, 10, 0)); Assert.False(state.IsDown(PointerButton.Left));
        Assert.True(state.ObserveMouse(0x200, 10, 10, 0));
        Assert.False(state.ObserveMouse(0x200, 10, 10, 0));
        Assert.True(state.ObserveMouse(0x200, 11, 10, 0));
        state.Clear(); Assert.True(state.ObserveMouse(0x200, 11, 10, 0));
    }

    [Fact]
    public void HookStructuresMatchWindows64Abi()
    {
        Assert.True(Environment.Is64BitProcess);
        foreach (var (name, size) in new[] { ("KEYBOARD",24), ("MOUSE",32), ("MSG",48) })
            Assert.Equal(size, Marshal.SizeOf(typeof(LocalInputMonitor).GetNestedType(name, BindingFlags.NonPublic)!));
    }
}
