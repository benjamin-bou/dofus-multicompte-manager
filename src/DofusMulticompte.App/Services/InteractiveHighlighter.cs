using System.Runtime.InteropServices;
using DofusMulticompte.App.Interop;
using DofusMulticompte.Core;
using static DofusMulticompte.App.Interop.NativeMethods;

namespace DofusMulticompte.App.Services;

/// <summary>
/// Appuie régulièrement (2 à 3 fois par seconde) sur la touche du jeu qui met en évidence les éléments
/// interactifs (Y par défaut), pour repérer les ressources pendant la récolte.
/// <para>
/// Garde-fous : rien n'est envoyé si la fenêtre au premier plan n'est pas une fenêtre Dofus, ni pendant
/// 2 secondes après une vraie frappe au clavier (sinon des « y » s'écriraient dans le chat pendant que tu tapes).
/// Les vraies frappes sont repérées par un crochet clavier qui ignore les touches simulées.
/// </para>
/// </summary>
public sealed class InteractiveHighlighter : IDisposable
{
    private const uint LLKHF_INJECTED = 0x10;
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
    private static readonly TimeSpan TypingPause = TimeSpan.FromSeconds(2);

    private readonly Func<IntPtr, bool> _isDofusWindow;
    private readonly LowLevelMouseProc _proc; // même signature que le crochet clavier ; gardé en champ pour le ramasse-miettes
    private Thread? _hookThread, _pressThread;
    private uint _hookThreadId;
    private IntPtr _hook;
    private volatile bool _running;
    private long _lastRealKeyTicks;

    public InteractiveHighlighter(Func<IntPtr, bool> isDofusWindow)
    {
        _isDofusWindow = isDofusWindow;
        _proc = HookProc;
    }

    public bool IsRunning => _running;

    /// <summary>Touche envoyée (nom WPF : « Y », « F2 »…).</summary>
    public string Key { get; set; } = "Y";

    public void Start()
    {
        if (_running) return;
        _running = true;
        EnsureHook();
        _pressThread = new Thread(PressLoop) { IsBackground = true, Name = "Mise en évidence" };
        _pressThread.Start();
    }

    public void Stop()
    {
        _running = false;
        _pressThread?.Join(TimeSpan.FromSeconds(1));
        _pressThread = null;
    }

    private void PressLoop()
    {
        // Cycle de 330 à 500 ms (2 à 3 appuis par seconde) : touche enfoncée 220 à 300 ms pour laisser le temps
        // de voir la mise en évidence, puis relâchée 110 à 200 ms.
        while (_running)
        {
            Thread.Sleep(Random.Shared.Next(110, 201));
            if (!_running) break;

            var typedRecently = DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastRealKeyTicks) < TypingPause.Ticks;
            if (typedRecently || !_isDofusWindow(GetForegroundWindow())) continue;
            if (!KeyMap.TryGetVirtualKey(Key, out var vk)) continue;

            // Appui directement dans la fenêtre au premier plan, sans changer le focus.
            var scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
            SendKey((ushort)vk, scan, up: false);
            Thread.Sleep(Random.Shared.Next(220, 301));
            SendKey((ushort)vk, scan, up: true); // toujours relâchée, même si l'option vient d'être coupée
        }
    }

    private static void SendKey(ushort vk, ushort scan, bool up)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = up ? KEYEVENTF_KEYUP : 0 } },
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    // ---------------------------------------------------------------- vraies frappes

    private void EnsureHook()
    {
        if (_hookThread is not null) return;
        using var ready = new ManualResetEventSlim();
        _hookThread = new Thread(() =>
        {
            _hookThreadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            ready.Set();
            if (_hook == IntPtr.Zero) return;
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { } // le crochet n'est appelé que si ce fil pompe ses messages
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }) { IsBackground = true, Name = "Frappes clavier" };
        _hookThread.Start();
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() is WM_KEYDOWN or WM_SYSKEYDOWN)
        {
            // KBDLLHOOKSTRUCT : vkCode, scanCode, flags… — les touches simulées (les nôtres) portent LLKHF_INJECTED.
            var flags = (uint)Marshal.ReadInt32(lParam, 8);
            if ((flags & LLKHF_INJECTED) == 0) Interlocked.Exchange(ref _lastRealKeyTicks, DateTime.UtcNow.Ticks);
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
        if (_hookThread is null) return;
        PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _hookThread.Join(TimeSpan.FromSeconds(1));
        _hookThread = null;
    }
}
