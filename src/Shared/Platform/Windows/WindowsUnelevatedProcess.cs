using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Axorith.Shared.Platform.Windows;

[SupportedOSPlatform("windows")]
internal static class WindowsUnelevatedProcess
{
    public static Process? Start(ProcessStartInfo startInfo)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!GetTokenInformation(identity.AccessToken, 20, out var elevated, sizeof(int), out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (elevated == 0 && GetIntegrityLevel(identity.AccessToken) <= 0x2000)
            return Process.Start(startInfo);

        if (startInfo.UseShellExecute)
        {
            var target = Convert.ToBase64String(Encoding.UTF8.GetBytes(startInfo.FileName));
            var script = $"Start-Process -FilePath ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{target}')))";
            if (!string.IsNullOrEmpty(startInfo.Arguments))
            {
                var arguments = Convert.ToBase64String(Encoding.UTF8.GetBytes(startInfo.Arguments));
                script += $" -ArgumentList ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{arguments}')))";
            }
            startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                Arguments = "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = startInfo.WorkingDirectory
            };
        }

        // SAFER's normal-user policy removes administrative rights even when UAC is disabled.
        if (!SaferCreateLevel(2, 0x20000, 1, out var level, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        SafeAccessTokenHandle token;
        try
        {
            if (!SaferComputeTokenFromLevel(level, identity.AccessToken, out token, 0, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { SaferCloseLevel(level); }
        using (token)
        {
            if (!ConvertStringSidToSidW("S-1-16-8192", out var mediumSid))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var label = new SidAndAttributes { Sid = mediumSid, Attributes = 0x20 };
                if (!SetTokenInformation(token, 25, ref label, Marshal.SizeOf<SidAndAttributes>()))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { LocalFree(mediumSid); }

            using var restrictedIdentity = new WindowsIdentity(token.DangerousGetHandle());
            if (new WindowsPrincipal(restrictedIdentity).IsInRole(WindowsBuiltInRole.Administrator) ||
                GetIntegrityLevel(token) > 0x2000)
                throw new InvalidOperationException("Could not create an unelevated application token.");

            var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
            var commandLine = new StringBuilder($"\"{startInfo.FileName}\" {startInfo.Arguments}");
            if (!CreateProcessAsUserW(token, startInfo.FileName, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    0x4u | (startInfo.CreateNoWindow ? 0x08000000u : 0), IntPtr.Zero,
                    string.IsNullOrEmpty(startInfo.WorkingDirectory) ? null : startInfo.WorkingDirectory,
                    ref startup, out var info))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            using var processHandle = new SafeProcessHandle(info.Process, ownsHandle: true);
            using var threadHandle = new SafeWaitHandle(info.Thread, ownsHandle: true);
            Process? process = null;
            try
            {
                process = Process.GetProcessById(info.ProcessId);
                _ = process.Handle;
                if (ResumeThread(threadHandle) == uint.MaxValue)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                return process;
            }
            catch
            {
                TerminateProcess(processHandle, 1);
                process?.Dispose();
                throw;
            }
        }
    }

    private static int GetIntegrityLevel(SafeAccessTokenHandle token)
    {
        GetTokenInformationBuffer(token, 25, IntPtr.Zero, 0, out var size);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!GetTokenInformationBuffer(token, 25, buffer, size, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var sid = Marshal.ReadIntPtr(buffer);
            var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            return Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved, Desktop, Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, ReservedSize;
        public IntPtr ReservedData, StandardInput, StandardOutput, StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SaferCreateLevel(uint scope, uint levelId, uint flags, out IntPtr level,
        IntPtr reserved);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SaferComputeTokenFromLevel(IntPtr level, SafeAccessTokenHandle input,
        out SafeAccessTokenHandle token, uint flags, IntPtr reserved);

    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SaferCloseLevel(IntPtr level);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSidToSidW(string value, out IntPtr sid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(SafeWaitHandle thread);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass,
        out int information, int length, out int returnLength);

    [DllImport("advapi32.dll", EntryPoint = "GetTokenInformation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformationBuffer(SafeAccessTokenHandle token, int informationClass,
        IntPtr information, int length, out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetTokenInformation(SafeAccessTokenHandle token, int informationClass,
        ref SidAndAttributes information, int length);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessAsUserW(SafeAccessTokenHandle token,
        string application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags, IntPtr environment,
        string? currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);
}
