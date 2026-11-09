using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Serilog;

namespace TriggerPoint.Infrastructure.Win32;

public static class ProcessDeElevator
{
    private static readonly ILogger Logger = Log.ForContext(typeof(ProcessDeElevator));

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithTokenW(
        IntPtr hToken,
        uint dwLogonFlags,
        string? lpApplicationName,
        string? lpCommandLine,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(
        IntPtr ProcessHandle,
        uint DesiredAccess,
        out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        int ImpersonationLevel,
        int TokenType,
        out IntPtr phNewToken);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint processAccess,
        bool bInheritHandle,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint MAXIMUM_ALLOWED = 0x02000000;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;

    public static bool LaunchUnelevated(string applicationPath, string? arguments = null)
    {
        if (string.IsNullOrWhiteSpace(applicationPath) || !File.Exists(applicationPath))
        {
            Logger.Warning("Cannot launch unelevated: application path does not exist '{Path}'", applicationPath);
            return false;
        }

        try
        {
            // 1. Attempt token duplication from interactive explorer.exe
            var explorerProcesses = Process.GetProcessesByName("explorer");
            foreach (var explorerProc in explorerProcesses)
            {
                IntPtr hProcess = IntPtr.Zero;
                IntPtr hToken = IntPtr.Zero;
                IntPtr hNewToken = IntPtr.Zero;
                try
                {
                    hProcess = OpenProcess(PROCESS_QUERY_INFORMATION, false, explorerProc.Id);
                    if (hProcess == IntPtr.Zero)
                    {
                        hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, explorerProc.Id);
                    }

                    if (hProcess != IntPtr.Zero && OpenProcessToken(hProcess, TOKEN_DUPLICATE | TOKEN_QUERY, out hToken))
                    {
                        if (DuplicateTokenEx(hToken, MAXIMUM_ALLOWED, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out hNewToken))
                        {
                            var si = new STARTUPINFO();
                            si.cb = Marshal.SizeOf<STARTUPINFO>();

                            string commandLine = string.IsNullOrWhiteSpace(arguments)
                                ? $"\"{applicationPath}\""
                                : $"\"{applicationPath}\" {arguments}";

                            string? workingDir = Path.GetDirectoryName(applicationPath);

                            if (CreateProcessWithTokenW(
                                hNewToken,
                                0,
                                null,
                                commandLine,
                                0,
                                IntPtr.Zero,
                                workingDir,
                                ref si,
                                out var pi))
                            {
                                Logger.Information("Successfully launched unelevated process via Explorer token (PID: {Pid}).", pi.dwProcessId);
                                if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
                                if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
                                return true;
                            }
                            else
                            {
                                int error = Marshal.GetLastWin32Error();
                                Logger.Warning("CreateProcessWithTokenW failed with Win32 error {Error}.", error);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Failed to duplicate Explorer token for PID {Pid}.", explorerProc.Id);
                }
                finally
                {
                    if (hNewToken != IntPtr.Zero) CloseHandle(hNewToken);
                    if (hToken != IntPtr.Zero) CloseHandle(hToken);
                    if (hProcess != IntPtr.Zero) CloseHandle(hProcess);
                    explorerProc.Dispose();
                }
            }

            // 2. Fallback: Shell dispatch via explorer.exe
            Logger.Information("Attempting fallback shell launch via explorer.exe...");
            var psi = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{applicationPath}\"",
                UseShellExecute = false
            };
            using var fallbackProc = Process.Start(psi);
            return fallbackProc != null;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to launch unelevated process '{Path}'.", applicationPath);
            return false;
        }
    }
}

