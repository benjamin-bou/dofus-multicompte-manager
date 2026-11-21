using System.Runtime.InteropServices;
using System.Windows.Threading;
using DofusMulticompte.Core;
using static DofusMulticompte.App.Interop.NativeMethods;

namespace DofusMulticompte.App.Services;

/// <summary>
/// Raccourcis globaux sur les boutons de souris (latéraux et molette), que RegisterHotKey ne gère pas.
/// Un hook souris bas niveau tourne sur son propre thread, pour que le curseur ne rame jamais même
/// quand l'interface est occupée. Un bouton associé à un raccourci est intercepté : il n'atteint pas
/// la fenêtre active, comme une touche de raccourci clavier.
/// </summary>
public sealed class MouseHotkeyHook : IDisposable
{
    private sealed record Binding(MouseButton Button, KeyModifiers Modifiers, HotkeyAction Action);

    private readonly Dispatcher _dispatcher;
    private readonly Action<HotkeyAction> _onPressed;
    private readonly LowLevelMouseProc _proc; // gardé en champ : le hook ne doit jamais pointer vers un délégué collecté
    private volatile Binding[] _bindings = [];
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    // Boutons dont l'appui a été intercepté, pour intercepter aussi le relâchement correspondant.
    private readonly bool[] _swallowed = new bool[4];

    /// <summary>Vrai pendant qu'un champ de saisie de raccourci a le focus : les boutons passent alors à l'application.</summary>
    public static volatile bool Suspended;

    public MouseHotkeyHook(Dispatcher dispatcher, Action<HotkeyAction> onPressed)
    {
        _dispatcher = dispatcher;
        _onPressed = onPressed;
        _proc = HookProc;
    }

    /// <summary>
    /// Remplace les raccourcis souris. Renvoie ceux qui n'ont pas pu être pris (doublons).
    /// Le hook n'est installé qu'au premier raccourci souris.
    /// </summary>
    public IReadOnlyList<string> SetBindings(IEnumerable<(HotkeyAction Action, KeyGesture Gesture)> gestures)
    {
        var list = new List<Binding>();
        var failures = new List<string>();
        foreach (var (action, g) in gestures)
        {
            if (list.Any(b => b.Button == g.MouseButton && b.Modifiers == g.Modifiers))
                failures.Add($"{g} (déjà utilisé)");
            else
                list.Add(new Binding(g.MouseButton, g.Modifiers, action));
        }
        _bindings = [.. list];
        if (list.Count > 0) EnsureStarted();
        return failures;
    }

    private void EnsureStarted()
    {
        if (_thread is not null) return;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Raccourcis souris" };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    private void Run(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        ready.Set();
        if (_hook == IntPtr.Zero) return;

        // Le hook bas niveau n'est appelé que si ce thread pompe ses messages.
        while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == HC_ACTION && !Suspended)
        {
            var msg = wParam.ToInt32();
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var button = msg switch
            {
                WM_MBUTTONDOWN or WM_MBUTTONUP => MouseButton.Middle,
                WM_XBUTTONDOWN or WM_XBUTTONUP => (info.mouseData >> 16) switch
                {
                    1 => MouseButton.X1,
                    2 => MouseButton.X2,
                    _ => MouseButton.None,
                },
                _ => MouseButton.None,
            };

            if (button != MouseButton.None && (info.flags & LLMHF_INJECTED) == 0)
            {
                var down = msg is WM_MBUTTONDOWN or WM_XBUTTONDOWN;
                if (down)
                {
                    var mods = CurrentModifiers();
                    var binding = Array.Find(_bindings, b => b.Button == button && b.Modifiers == mods);
                    if (binding is not null)
                    {
                        _swallowed[(int)button] = true;
                        _dispatcher.BeginInvoke(() => _onPressed(binding.Action));
                        return 1;
                    }
                }
                else if (_swallowed[(int)button])
                {
                    _swallowed[(int)button] = false;
                    return 1;
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static KeyModifiers CurrentModifiers()
    {
        static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
        var mods = KeyModifiers.None;
        if (Down(VK_CONTROL)) mods |= KeyModifiers.Ctrl;
        if (Down(VK_MENU)) mods |= KeyModifiers.Alt;
        if (Down(VK_SHIFT)) mods |= KeyModifiers.Shift;
        if (Down(VK_LWIN) || Down(VK_RWIN)) mods |= KeyModifiers.Win;
        return mods;
    }

    public void Dispose()
    {
        if (_thread is null) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(1));
        _thread = null;
    }
}
