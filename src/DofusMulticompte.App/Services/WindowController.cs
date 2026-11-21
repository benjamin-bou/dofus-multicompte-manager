using System.Runtime.InteropServices;
using DofusMulticompte.App.Interop;
using DofusMulticompte.Core;
using static DofusMulticompte.App.Interop.NativeMethods;

namespace DofusMulticompte.App.Services;

/// <summary>Met des fenêtres au premier plan et leur envoie des touches.</summary>
public static class WindowController
{
    /// <summary>
    /// Met une fenêtre au premier plan. Windows refuse souvent SetForegroundWindow à un processus
    /// qui n'est pas actif : un appui sur Alt simulé lève cette restriction, et on retente en
    /// attachant les files d'entrée si besoin.
    /// </summary>
    public static bool Focus(IntPtr hwnd)
    {
        if (!IsWindow(hwnd)) return false;
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        if (GetForegroundWindow() == hwnd) return true;

        SendKeys([Key(VK_MENU, false), Key(VK_MENU, true)]);
        if (SetForegroundWindow(hwnd) && WaitForeground(hwnd)) return true;

        var foreground = GetForegroundWindow();
        var fgThread = GetWindowThreadProcessId(foreground, out _);
        var myThread = GetCurrentThreadId();
        var attached = fgThread != myThread && AttachThreadInput(myThread, fgThread, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(myThread, fgThread, false);
        }
        return WaitForeground(hwnd);
    }

    private static bool WaitForeground(IntPtr hwnd)
    {
        for (var i = 0; i < 10; i++)
        {
            if (GetForegroundWindow() == hwnd) return true;
            Thread.Sleep(15);
        }
        return GetForegroundWindow() == hwnd;
    }

    /// <summary>Envoie une touche à une fenêtre selon le mode choisi. Renvoie faux si l'envoi a échoué.</summary>
    public static bool SendKey(IntPtr hwnd, string keyText, InputMode mode)
    {
        if (!KeyGesture.TryParse(keyText, out var gesture) || !KeyMap.TryGetVirtualKey(gesture.Key, out var vk))
            return false;

        if (mode == InputMode.Background)
        {
            if (gesture.Modifiers != KeyModifiers.None) return false; // impossible de simuler des modificateurs en arrière-plan
            var scan = MapVirtualKey(vk, MAPVK_VK_TO_VSC);
            var down = (IntPtr)(1 | (scan << 16));
            var up = (IntPtr)(1 | (scan << 16) | (1u << 30) | (1u << 31));
            return PostMessage(hwnd, WM_KEYDOWN, (IntPtr)vk, down) && PostMessage(hwnd, WM_KEYUP, (IntPtr)vk, up);
        }

        if (!Focus(hwnd)) return false;
        Thread.Sleep(60); // laisse le client traiter le changement de focus

        var inputs = new List<INPUT>();
        var mods = ModifierKeys(gesture.Modifiers);
        foreach (var m in mods) inputs.Add(Key(m, false));
        inputs.Add(Key((ushort)vk, false));
        inputs.Add(Key((ushort)vk, true));
        foreach (var m in Enumerable.Reverse(mods)) inputs.Add(Key(m, true));

        // Certains clients ignorent un appui trop bref : on sépare l'appui et le relâchement.
        var half = mods.Count + 1;
        SendKeys(inputs.Take(half).ToArray());
        Thread.Sleep(40);
        SendKeys(inputs.Skip(half).ToArray());
        return true;
    }

    /// <summary>
    /// Clique à une position relative de la zone client : met la fenêtre au premier plan, déplace
    /// le curseur, clique, puis remet le curseur où il était.
    /// </summary>
    public static bool ClickClient(IntPtr hwnd, RelativePoint point)
    {
        if (!Focus(hwnd) || !GetClientRect(hwnd, out var rect)) return false;
        var target = new POINT
        {
            X = (int)Math.Round((rect.Right - rect.Left) * Math.Clamp(point.X, 0, 1)),
            Y = (int)Math.Round((rect.Bottom - rect.Top) * Math.Clamp(point.Y, 0, 1)),
        };
        if (!ClientToScreen(hwnd, ref target)) return false;

        GetCursorPos(out var previous);
        SetCursorPos(target.X, target.Y);
        Thread.Sleep(120); // laisse le jeu voir le survol du bouton
        SendMouse(MOUSEEVENTF_LEFTDOWN);
        Thread.Sleep(70);
        SendMouse(MOUSEEVENTF_LEFTUP);
        Thread.Sleep(80);
        SetCursorPos(previous.X, previous.Y);
        return true;
    }

    /// <summary>Position écran → position relative (0 à 1) dans la zone client, ou null si elle est en dehors.</summary>
    internal static RelativePoint? ToRelativeClient(IntPtr hwnd, POINT screen)
    {
        if (!GetClientRect(hwnd, out var rect)) return null;
        var origin = new POINT();
        if (!ClientToScreen(hwnd, ref origin)) return null;
        double w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
        if (w <= 0 || h <= 0) return null;
        var x = (screen.X - origin.X) / w;
        var y = (screen.Y - origin.Y) / h;
        return x is >= 0 and <= 1 && y is >= 0 and <= 1 ? new RelativePoint(x, y) : null;
    }

    private static void SendMouse(uint flags)
    {
        var input = new INPUT { type = INPUT_MOUSE, U = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = flags } } };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static List<ushort> ModifierKeys(KeyModifiers m)
    {
        var list = new List<ushort>();
        if (m.HasFlag(KeyModifiers.Ctrl)) list.Add(0x11);
        if (m.HasFlag(KeyModifiers.Alt)) list.Add(VK_MENU);
        if (m.HasFlag(KeyModifiers.Shift)) list.Add(0x10);
        if (m.HasFlag(KeyModifiers.Win)) list.Add(0x5B);
        return list;
    }

    private static INPUT Key(ushort vk, bool up)
    {
        var flags = up ? KEYEVENTF_KEYUP : 0;
        if (KeyMap.IsExtended(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new INPUTUNION
            {
                ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC), dwFlags = flags },
            },
        };
    }

    private static void SendKeys(INPUT[] inputs)
    {
        if (inputs.Length == 0) return;
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }
}
