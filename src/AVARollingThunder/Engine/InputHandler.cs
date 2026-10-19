using Avalonia.Input;

namespace AVARollingThunder.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: walk, up and down, fire and jump. Up is -1, the way the engine
/// counts Y down the screen.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update()
    {
        _pad.Update();
    }

    /// <summary>
    /// Left and right to walk; up into a door, down to crouch (or out of a door); Z (or Ctrl) to fire, X (or Space) to
    /// jump, with up to leap to the walkway above or with down to drop from it. On a gamepad: the stick or D-pad, A to
    /// fire, B to jump.
    /// </summary>
    public Controls Player
    {
        get
        {
            var pad = _pad;
            int dx = (Any(Key.Right, Key.D, Key.NumPad6) ? 1 : 0) - (Any(Key.Left, Key.A, Key.NumPad4) ? 1 : 0);
            int dy = (Any(Key.Down, Key.S, Key.NumPad2) ? 1 : 0) - (Any(Key.Up, Key.W, Key.NumPad8) ? 1 : 0);
            if (dx == 0) dx = pad.DPadLeft || pad.LeftStickX < -0.4f ? -1 : pad.DPadRight || pad.LeftStickX > 0.4f ? 1 : 0;
            if (dy == 0) dy = pad.DPadUp || pad.LeftStickY < -0.5f ? -1 : pad.DPadDown || pad.LeftStickY > 0.5f ? 1 : 0;
            bool fire = Any(Key.Z, Key.LeftCtrl, Key.RightCtrl) || pad.ButtonA || pad.ButtonX;
            bool jump = Any(Key.X, Key.Space) || pad.ButtonB || pad.ButtonY;
            return new Controls(dx, dy, fire, jump);
        }
    }

    /// <summary>Enter or 1.</summary>
    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
