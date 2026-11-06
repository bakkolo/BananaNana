using static BananaNana.Input.NativeMethods;
namespace BananaNana.Input;

public enum Skip { None, Excluded, Password, Admin }

// Decides whether to leave the app in front alone. Runs only when a trigger was just recognised: no timers,
// no polling, no hooks, no extra memory. Plain Win32 calls, so it adds almost nothing to the EXE.
public static class ForegroundGuard
{
    static readonly bool selfElevated = IsElevated(GetCurrentProcess());

    public static Skip Check(AppSettings s)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return Skip.None;
        uint thread = GetWindowThreadProcessId(foreground, out uint pid);

        if (s.UseExcludedApps && IsExcluded(ProcessName(pid), s.ExcludedApps)) return Skip.Excluded;
        if (s.ProtectPasswords && IsPasswordField(thread)) return Skip.Password;
        if (s.SkipAdminApps && !selfElevated && IsElevatedProcess(pid)) return Skip.Admin;
        return Skip.None;
    }

    // Classic Windows password boxes carry the ES_PASSWORD style. Browser and modern-app password fields
    // draw their own controls and cannot be told apart this cheaply.
    static bool IsPasswordField(uint thread)
    {
        var info = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(thread, ref info) || info.hwndFocus == IntPtr.Zero) return false;
        return ((long)GetWindowLongPtr(info.hwndFocus, GWL_STYLE) & ES_PASSWORD) != 0;
    }

    // If the process cannot even be opened, it is more privileged than we are: treat it as an admin app.
    static bool IsElevatedProcess(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return true;
        try { return IsElevated(h); } finally { CloseHandle(h); }
    }

    static bool IsElevated(IntPtr process)
    {
        if (!OpenProcessToken(process, TOKEN_QUERY, out var token)) return true;
        try { return GetTokenInformation(token, TokenElevation, out int elevated, sizeof(int), out _) && elevated != 0; }
        finally { CloseHandle(token); }
    }

    static string ProcessName(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return "";
        try
        {
            var buffer = new char[1024];
            int size = buffer.Length;
            return QueryFullProcessImageNameW(h, 0, buffer, ref size) ? System.IO.Path.GetFileName(new string(buffer, 0, size)) : "";
        }
        finally { CloseHandle(h); }
    }

    // The list is one program per line (commas and semicolons work too); ".exe" is optional, case is ignored.
    public static bool IsExcluded(string exeName, string list)
    {
        if (exeName.Length == 0) return false;
        foreach (var raw in list.Split(['\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var entry = raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? raw : raw + ".exe";
            if (entry.Equals(exeName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
