using System.Diagnostics;
using System.Text;
using DofusMulticompte.App.Interop;

namespace DofusMulticompte.App.Services;

public sealed record DofusWindow(IntPtr Handle, int ProcessId, string Title);

/// <summary>Liste les fenêtres principales des processus Dofus.</summary>
public sealed class DofusWindowScanner
{
    private readonly Dictionary<int, string> _processNames = new();

    public IReadOnlyList<DofusWindow> Scan(IReadOnlyCollection<string> processNames)
    {
        var result = new List<DofusWindow>();
        var alive = new HashSet<int>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            if (NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) != IntPtr.Zero) return true;

            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            alive.Add((int)pid);
            var name = GetProcessName((int)pid);
            if (name is null || !processNames.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase)))
                return true;

            var title = GetTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title)) return true;

            result.Add(new DofusWindow(hwnd, (int)pid, title));
            return true;
        }, IntPtr.Zero);

        // Oublie les processus disparus (les PID peuvent être réutilisés).
        foreach (var pid in _processNames.Keys.Where(p => !alive.Contains(p)).ToList())
            _processNames.Remove(pid);

        return result;
    }

    public static string GetTitle(IntPtr hwnd)
    {
        var len = NativeMethods.GetWindowTextLength(hwnd);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private string? GetProcessName(int pid)
    {
        if (_processNames.TryGetValue(pid, out var cached)) return cached;
        try
        {
            using var p = Process.GetProcessById(pid);
            cached = p.ProcessName;
        }
        catch (ArgumentException) { cached = null; }
        catch (InvalidOperationException) { cached = null; }
        if (cached is not null) _processNames[pid] = cached;
        return cached;
    }
}
