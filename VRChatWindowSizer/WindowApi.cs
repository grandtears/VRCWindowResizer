using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VRChatWindowSizer;

/// <summary>
/// ゲーム画面の幅と高さ、およびウィンドウ外枠の左上位置です。
/// 幅と高さは枠やタイトルバーを含まないクライアント領域です。
/// </summary>
public record WindowState(int Width, int Height, int X, int Y);

/// <summary>
/// VRChat のウィンドウを Windows API だけで探してサイズ変更します。
/// プロセスメモリや VRChat の設定ファイルは読み書きしません。
/// </summary>
internal static class WindowApi
{
    /// <summary>Win32 の RECT です。右と下は座標に含まれません。</summary>
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, nint param);
    private delegate bool EnumProc(nint window, nint param);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint id);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(nint window, out Rect rect);
    /// <summary>GetWindowLongW を使い、ウィンドウスタイルを 32bit 値として受け取ります。</summary>
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool AdjustWindowRectExForDpi(ref Rect rect, uint style, bool menu, uint exStyle, uint dpi);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint window);

    /// <summary>
    /// 見えている VRChat ウィンドウのうち、クライアント領域がいちばん広いものを返します。
    /// 見つからないときは 0 です。列挙は最後まで続けます。
    /// </summary>
    public static nint Find()
    {
        var ids = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName("VRChat"))
        {
            using (process) ids.Add((uint)process.Id);
        }
        nint found = 0;
        long largest = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var id);
            if (ids.Contains(id) && IsWindowVisible(window) && GetClientRect(window, out var r))
            {
                long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                if (area > largest) { largest = area; found = window; }
            }
            return true;
        }, 0);
        return found;
    }

    /// <summary>クライアント領域の幅と高さと、外枠の左上座標を読みます。</summary>
    public static WindowState Read(nint window)
    {
        if (!GetClientRect(window, out var c) || !GetWindowRect(window, out var r)) throw new Win32Exception();
        return new(c.Right - c.Left, c.Bottom - c.Top, r.Left, r.Top);
    }

    /// <summary>
    /// サイズを変更してよい通常のウィンドウか確認します。
    /// -16 は GWL_STYLE、0x00C00000 は WS_CAPTION です。タイトルバーが無いときは枠なし表示とみなします。
    /// </summary>
    public static void Check(nint window)
    {
        if (window == 0) throw new InvalidOperationException("VRChatのウィンドウが見つかりません。");
        if (IsIconic(window) || IsZoomed(window)) throw new InvalidOperationException("VRChatの最小化・最大化を解除してください。");
        if ((GetWindowLong(window, -16) & 0x00C00000) == 0)
            throw new InvalidOperationException("VRChatをウィンドウモードに切り替えてください（Alt + Enter）。");
    }

    /// <summary>
    /// 指定できるサイズかを確認します。通常の最小値より小さい指定は、負荷軽減用の 1×1 だけ受け付けます。
    /// </summary>
    public static void EnsureSize(int width, int height)
    {
        if (width == 1 && height == 1) return;
        if (width is < 320 or > 16384 || height is < 200 or > 16384)
            throw new InvalidOperationException("幅320～16384、高さ200～16384で指定してください。1×1も指定できます。");
    }

    /// <summary>
    /// 希望するクライアントサイズへ外枠を合わせます。
    /// 枠とタイトルバーの厚みは、そのウィンドウの DPI で計算して外側のサイズへ足します。
    /// </summary>
    public static void Apply(nint window, WindowState state)
    {
        Check(window);
        EnsureSize(state.Width, state.Height);
        var rect = new Rect { Right = state.Width, Bottom = state.Height };
        // -16 は GWL_STYLE、-20 は GWL_EXSTYLE です。
        if (!AdjustWindowRectExForDpi(ref rect, (uint)GetWindowLong(window, -16), false,
            (uint)GetWindowLong(window, -20), GetDpiForWindow(window))) throw new Win32Exception();
        // 0x0014 は SWP_NOZORDER | SWP_NOACTIVATE で、重なり順とフォーカスを変えない指定です。
        // 1×1 のときだけ 0x0400（SWP_NOSENDCHANGING）を足し、Windows の最小サイズへ広げられないようにします。
        uint flags = state.Width == 1 && state.Height == 1 ? 0x0414u : 0x0014u;
        if (!SetWindowPos(window, 0, state.X, state.Y, rect.Right - rect.Left, rect.Bottom - rect.Top, flags))
            throw new Win32Exception();
    }
}
