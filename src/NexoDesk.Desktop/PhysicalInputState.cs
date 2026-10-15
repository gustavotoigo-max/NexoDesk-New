using System.Collections.Concurrent;
using NexoDesk.Shared.Messages;

namespace NexoDesk.Desktop;

/// <summary>Estado físico transitório, sem caracteres, registro ou persistência.</summary>
public sealed class PhysicalInputState
{
    private readonly ConcurrentDictionary<(ushort Scan, bool Extended), byte> _keys = new();
    private readonly ConcurrentDictionary<PointerButton, byte> _buttons = new();
    public bool IsDown(ushort scan, bool extended) => _keys.ContainsKey((scan, extended));
    public bool IsDown(PointerButton button) => _buttons.ContainsKey(button);
    public void Key(ushort scan, bool extended, bool down)
    { if (down) _keys[(scan, extended)] = 0; else _keys.TryRemove((scan, extended), out _); }
    public void Button(PointerButton button, bool down)
    { if (down) _buttons[button] = 0; else _buttons.TryRemove(button, out _); }
    private int _lastX = int.MinValue, _lastY = int.MinValue;
    public bool ObserveKeyboard(uint message, ushort scan, uint flags)
    {
        if ((flags & 0x10) != 0) return false;
        if (message is not 0x100 and not 0x101 and not 0x104 and not 0x105) return false;
        bool down = message is 0x100 or 0x104;
        var identity = (scan, (flags & 1) != 0);
        // Auto-repeat de uma tecla já mantida não inicia outra transferência.
        if (down) return _keys.TryAdd(identity, 0);
        _keys.TryRemove(identity, out _);
        return false;
    }
    public bool ObserveMouse(uint message, int x, int y, uint flags)
    {
        if ((flags & 1) != 0) return false;
        switch (message)
        {
            case 0x201: Button(PointerButton.Left, true); break;
            case 0x202: Button(PointerButton.Left, false); break;
            case 0x204: Button(PointerButton.Right, true); break;
            case 0x205: Button(PointerButton.Right, false); break;
            case 0x207: Button(PointerButton.Middle, true); break;
            case 0x208: Button(PointerButton.Middle, false); break;
        }
        bool moved = message == 0x200 && (_lastX != x || _lastY != y);
        if (message == 0x200) { _lastX = x; _lastY = y; }
        return moved || message is 0x201 or 0x204 or 0x207 or 0x20A or 0x20B or 0x20E;
    }
    public void Clear() { _keys.Clear(); _buttons.Clear(); _lastX = _lastY = int.MinValue; }
}
