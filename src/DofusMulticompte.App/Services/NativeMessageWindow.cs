using System.Windows.Interop;
using System.Windows.Threading;
using DofusMulticompte.App.Interop;
using DofusMulticompte.Core;

namespace DofusMulticompte.App.Services;

public enum HotkeyAction { NextCharacter = 1, PreviousCharacter = 2, FocusLeader = 3 }

/// <summary>
/// Fenêtre invisible qui reçoit les notifications du shell Windows (création et fermeture de
/// fenêtres, changement de titre, clignotement dans la barre des tâches) et les raccourcis globaux.
/// Doit être créée sur le thread de l'interface.
/// </summary>
public sealed class NativeMessageWindow : IDisposable
{
    private readonly HwndSource _source;
    private readonly uint _shellHookMessage;
    private readonly HashSet<int> _registeredHotkeys = [];
    private readonly MouseHotkeyHook _mouseHook;

    /// <summary>Une fenêtre clignote dans la barre des tâches (Dofus le fait en début de tour).</summary>
    public event Action<IntPtr>? WindowFlashed;
    /// <summary>Une fenêtre a été créée, fermée, renommée ou activée.</summary>
    public event Action<IntPtr>? WindowsChanged;
    public event Action<HotkeyAction>? HotkeyPressed;

    public IntPtr Handle => _source.Handle;

    public NativeMessageWindow()
    {
        var p = new HwndSourceParameters("DofusMulticompteMessages")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, jamais affichée
            ExtendedWindowStyle = 0x80,               // WS_EX_TOOLWINDOW
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
        _mouseHook = new MouseHotkeyHook(Dispatcher.CurrentDispatcher, a => HotkeyPressed?.Invoke(a));
        _shellHookMessage = NativeMethods.RegisterWindowMessage("SHELLHOOK");
        NativeMethods.RegisterShellHookWindow(_source.Handle);
    }

    /// <summary>Enregistre les raccourcis globaux. Renvoie la liste de ceux qui n'ont pas pu l'être.</summary>
    public IReadOnlyList<string> RegisterHotkeys(HotkeySettings hotkeys)
    {
        UnregisterHotkeys();
        var failures = new List<string>();
        var mouse = new List<(HotkeyAction, KeyGesture)>();
        Register(HotkeyAction.NextCharacter, hotkeys.NextCharacter, failures, mouse);
        Register(HotkeyAction.PreviousCharacter, hotkeys.PreviousCharacter, failures, mouse);
        Register(HotkeyAction.FocusLeader, hotkeys.FocusLeader, failures, mouse);
        failures.AddRange(_mouseHook.SetBindings(mouse));
        return failures;
    }

    private void Register(HotkeyAction action, string? text, List<string> failures, List<(HotkeyAction, KeyGesture)> mouse)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (KeyGesture.TryParse(text, out var mg) && mg.IsMouse)
        {
            mouse.Add((action, mg));
            return;
        }
        if (!KeyGesture.TryParse(text, out var g) || !KeyMap.TryGetVirtualKey(g.Key, out var vk))
        {
            failures.Add(text);
            return;
        }
        var id = (int)action;
        if (NativeMethods.RegisterHotKey(Handle, id, (uint)g.Modifiers | NativeMethods.MOD_NOREPEAT, vk))
            _registeredHotkeys.Add(id);
        else
            failures.Add(text);
    }

    private void UnregisterHotkeys()
    {
        foreach (var id in _registeredHotkeys) NativeMethods.UnregisterHotKey(Handle, id);
        _registeredHotkeys.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (Enum.IsDefined(typeof(HotkeyAction), id)) HotkeyPressed?.Invoke((HotkeyAction)id);
            handled = true;
        }
        else if (msg == (int)_shellHookMessage)
        {
            switch (wParam.ToInt32())
            {
                case NativeMethods.HSHELL_FLASH:
                    WindowFlashed?.Invoke(lParam);
                    break;
                case NativeMethods.HSHELL_WINDOWCREATED:
                case NativeMethods.HSHELL_WINDOWDESTROYED:
                case NativeMethods.HSHELL_REDRAW:
                case NativeMethods.HSHELL_WINDOWACTIVATED:
                case NativeMethods.HSHELL_RUDEAPPACTIVATED:
                    WindowsChanged?.Invoke(lParam);
                    break;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterHotkeys();
        _mouseHook.Dispose();
        NativeMethods.DeregisterShellHookWindow(_source.Handle);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
