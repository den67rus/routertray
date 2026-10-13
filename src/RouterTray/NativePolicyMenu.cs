using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RouterTray;

internal sealed class NativePolicyMenu : IDisposable
{
    private const uint DefaultCommandId = 1;
    private const uint HeaderCommandId = 2;
    private const uint FirstPolicyCommandId = 100;

    private const uint MfString = 0x0000;
    private const uint MfByPosition = 0x0400;
    private const uint MfChecked = 0x0008;
    private const uint MfUnchecked = 0x0000;
    private const uint MfDisabled = 0x0002;
    private const uint MfGrayed = 0x0001;
    private const uint MfDefault = 0x1000;

    private const uint TpmLeftAlign = 0x0000;
    private const uint TpmRightAlign = 0x0008;
    private const uint TpmTopAlign = 0x0000;
    private const uint TpmBottomAlign = 0x0020;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmNoNotify = 0x0080;
    private const uint TpmReturnCommand = 0x0100;

    private const uint WmNull = 0x0000;
    private const uint RdwInvalidate = 0x0001;
    private const uint RdwErase = 0x0004;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdateNow = 0x0100;
    private const uint RdwFrame = 0x0400;

    private readonly IntPtr _menuHandle;
    private readonly Dictionary<uint, NativePolicyMenuSelection> _commands = new();
    private PolicyMenuSnapshot? _snapshot;
    private bool _isOpen;
    private bool _disposed;

    public NativePolicyMenu()
    {
        _menuHandle = CreatePopupMenu();
        if (_menuHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public bool IsOpen => _isOpen;

    public void Update(PolicyMenuSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (_snapshot?.ContentEquals(snapshot) == true)
        {
            return;
        }

        if (_snapshot?.HasSameStructure(snapshot) == true)
        {
            UpdateChecks(snapshot);
        }
        else
        {
            Rebuild(snapshot);
        }

        _snapshot = snapshot;

        if (_isOpen)
        {
            RedrawOpenMenu();
        }
    }

    public NativePolicyMenuSelection? Show(
        IntPtr ownerHandle,
        Point location,
        PolicyMenuSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ownerHandle == IntPtr.Zero)
        {
            throw new ArgumentException("A valid owner window is required.", nameof(ownerHandle));
        }

        Update(snapshot);

        var workingArea = Screen.FromPoint(location).WorkingArea;
        var alignRight = location.X >= workingArea.Left + (workingArea.Width / 2);
        var alignBottom = location.Y >= workingArea.Top + (workingArea.Height / 2);
        var flags = TpmRightButton | TpmNoNotify | TpmReturnCommand |
                    (alignRight ? TpmRightAlign : TpmLeftAlign) |
                    (alignBottom ? TpmBottomAlign : TpmTopAlign);

        _ = SetForegroundWindow(ownerHandle);
        _isOpen = true;
        try
        {
            var commandId = TrackPopupMenuEx(
                _menuHandle,
                flags,
                location.X,
                location.Y,
                ownerHandle,
                IntPtr.Zero);

            return commandId != 0 && _commands.TryGetValue(commandId, out var selection)
                ? selection
                : null;
        }
        finally
        {
            _isOpen = false;
            _ = PostMessage(ownerHandle, WmNull, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    private void Rebuild(PolicyMenuSnapshot snapshot)
    {
        var itemCount = GetMenuItemCount(_menuHandle);
        if (itemCount < 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        while (itemCount-- > 0)
        {
            ThrowIfFalse(DeleteMenu(_menuHandle, 0, MfByPosition));
        }

        _commands.Clear();

        AppendText(
            UiText.MenuPolicies,
            HeaderCommandId,
            enabled: false,
            isChecked: false,
            isDefault: true);
        AppendText(
            UiText.PolicyDefaultDisplay,
            DefaultCommandId,
            enabled: true,
            snapshot.IsDefaultSelected);
        _commands[DefaultCommandId] = NativePolicyMenuSelection.Default;

        switch (snapshot.State)
        {
            case PolicyMenuLoadState.Loading:
                AppendText(UiText.Loading, commandId: 0, enabled: false, isChecked: false);
                break;
            case PolicyMenuLoadState.Failed:
                AppendText(
                    UiText.PoliciesLoadFailedMenu,
                    commandId: 0,
                    enabled: false,
                    isChecked: false);
                break;
            case PolicyMenuLoadState.Loaded when snapshot.Policies.Count == 0:
                AppendText(UiText.PoliciesNone, commandId: 0, enabled: false, isChecked: false);
                break;
            case PolicyMenuLoadState.Loaded:
                for (var index = 0; index < snapshot.Policies.Count; index++)
                {
                    var policy = snapshot.Policies[index];
                    var commandId = checked(FirstPolicyCommandId + (uint)index);
                    AppendText(
                        policy.DisplayName,
                        commandId,
                        enabled: true,
                        policy.IsSelected);
                    _commands[commandId] = new NativePolicyMenuSelection(
                        policy.Id,
                        policy.DisplayName,
                        IsDefault: false);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(snapshot));
        }
    }

    private void UpdateChecks(PolicyMenuSnapshot snapshot)
    {
        ThrowIfCheckFailed(CheckMenuItem(
            _menuHandle,
            DefaultCommandId,
            snapshot.IsDefaultSelected ? MfChecked : MfUnchecked));

        for (var index = 0; index < snapshot.Policies.Count; index++)
        {
            var commandId = checked(FirstPolicyCommandId + (uint)index);
            ThrowIfCheckFailed(CheckMenuItem(
                _menuHandle,
                commandId,
                snapshot.Policies[index].IsSelected ? MfChecked : MfUnchecked));
        }
    }

    private void AppendText(
        string text,
        uint commandId,
        bool enabled,
        bool isChecked,
        bool isDefault = false)
    {
        var flags = MfString;
        if (!enabled)
        {
            flags |= MfDisabled | MfGrayed;
        }

        if (isChecked)
        {
            flags |= MfChecked;
        }

        if (isDefault)
        {
            flags |= MfDefault;
        }

        // Win32 treats '&' as the keyboard mnemonic marker. Doubling it keeps
        // policy names intact while retaining the native menu renderer.
        var escapedText = text.Replace("&", "&&", StringComparison.Ordinal);
        ThrowIfFalse(AppendMenu(
            _menuHandle,
            flags,
            new UIntPtr(commandId),
            escapedText));
    }

    private void RedrawOpenMenu()
    {
        if (!GetMenuItemRect(IntPtr.Zero, _menuHandle, 0, out var itemRect))
        {
            return;
        }

        var menuWindow = WindowFromPoint(new NativePoint
        {
            X = itemRect.Left + ((itemRect.Right - itemRect.Left) / 2),
            Y = itemRect.Top + ((itemRect.Bottom - itemRect.Top) / 2)
        });
        if (menuWindow == IntPtr.Zero)
        {
            return;
        }

        _ = RedrawWindow(
            menuWindow,
            IntPtr.Zero,
            IntPtr.Zero,
            RdwInvalidate | RdwErase | RdwAllChildren | RdwUpdateNow | RdwFrame);
    }

    private static void ThrowIfFalse(bool result)
    {
        if (!result)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static void ThrowIfCheckFailed(uint result)
    {
        if (result == uint.MaxValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _snapshot = null;
        _commands.Clear();
        _ = DestroyMenu(_menuHandle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menuHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(
        IntPtr menuHandle,
        uint flags,
        UIntPtr newItemId,
        string? newItem);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteMenu(IntPtr menuHandle, uint position, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint CheckMenuItem(IntPtr menuHandle, uint itemId, uint checkFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMenuItemCount(IntPtr menuHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(
        IntPtr menuHandle,
        uint flags,
        int x,
        int y,
        IntPtr ownerHandle,
        IntPtr parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMenuItemRect(
        IntPtr windowHandle,
        IntPtr menuHandle,
        uint item,
        out NativeRect itemRect);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(
        IntPtr windowHandle,
        IntPtr updateRect,
        IntPtr updateRegion,
        uint flags);

}

internal sealed record NativePolicyMenuSelection(
    string? PolicyId,
    string DisplayName,
    bool IsDefault)
{
    public static NativePolicyMenuSelection Default { get; } = new(
        PolicyId: null,
        UiText.PolicyDefaultDisplay,
        IsDefault: true);
}
