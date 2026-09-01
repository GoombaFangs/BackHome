Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@

$procs = Get-Process -Name Unity | Where-Object { $_.MainWindowTitle -ne "" }
foreach ($p in $procs) {
    Write-Host "Focusing PID $($p.Id): $($p.MainWindowTitle)"
    [Win32]::ShowWindow($p.MainWindowHandle, 9) | Out-Null
    [Win32]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
}
