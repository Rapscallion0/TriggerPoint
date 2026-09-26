using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TriggerPoint.Infrastructure.Win32;

namespace TriggerPoint.Infrastructure.Services;

public class SingleInstanceService : IDisposable
{
    public const string DefaultMutexName = @"Global\TriggerPoint_SingleInstance";
    public const string DefaultPipeName = "TriggerPoint_IpcPipe";

    private readonly ILogger _logger = Log.ForContext<SingleInstanceService>();
    private readonly string _mutexName;
    private readonly string _pipeName;
    private Mutex? _mutex;
    private bool _isFirstInstance;
    private CancellationTokenSource? _cts;

    public string MutexName => _mutexName;
    public string PipeName => _pipeName;

    public event Action<string>? SecondInstanceSignaled;

    public SingleInstanceService(string? instanceScope = null)
    {
        _mutexName = string.IsNullOrWhiteSpace(instanceScope)
            ? DefaultMutexName
            : $@"Global\TriggerPoint_Portable_{instanceScope.Trim()}";

        _pipeName = string.IsNullOrWhiteSpace(instanceScope)
            ? DefaultPipeName
            : $@"TriggerPoint_Portable_IpcPipe_{instanceScope.Trim()}";
    }

    public static string ComputeScopeHash(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return "default";
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(directory.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash)[..8];
    }

    public bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(true, _mutexName, out _isFirstInstance);
            if (!_isFirstInstance)
            {
                _logger.Information("Another instance of TriggerPoint is already running (Mutex: {MutexName}).", _mutexName);
                return false;
            }

            _logger.Information("Single-instance mutex acquired successfully (Mutex: {MutexName}).", _mutexName);
            StartIpcServer();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to acquire single instance mutex {MutexName}. Assuming first instance.", _mutexName);
            _isFirstInstance = true;
            return true;
        }
    }

    public static async Task SignalPrimaryInstanceAsync(string argument = "", string? instanceScope = null)
    {
        var targetPipe = string.IsNullOrWhiteSpace(instanceScope)
            ? DefaultPipeName
            : $@"TriggerPoint_Portable_IpcPipe_{instanceScope.Trim()}";

        try
        {
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            using var client = new NamedPipeClientStream(".", targetPipe, PipeDirection.Out);
            await client.ConnectAsync(1000).ConfigureAwait(false);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync(argument).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not signal primary instance via IPC pipe ({PipeName}).", targetPipe);
        }
    }

    private void StartIpcServer()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    using var reader = new StreamReader(server);
                    var message = await reader.ReadLineAsync(token).ConfigureAwait(false);

                    if (message != null)
                    {
                        SecondInstanceSignaled?.Invoke(message);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error in IPC server loop.");
                    await Task.Delay(500, token).ConfigureAwait(false);
                }
            }
        }, token);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();

        if (_mutex != null)
        {
            if (_isFirstInstance)
            {
                try { _mutex.ReleaseMutex(); } catch { }
            }
            _mutex.Dispose();
            _mutex = null;
        }

        GC.SuppressFinalize(this);
    }
}
