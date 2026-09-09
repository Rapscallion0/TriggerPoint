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
    private const string MutexName = @"Global\TriggerPoint_SingleInstance";
    private const string PipeName = "TriggerPoint_IpcPipe";

    private readonly ILogger _logger = Log.ForContext<SingleInstanceService>();
    private Mutex? _mutex;
    private bool _isFirstInstance;
    private CancellationTokenSource? _cts;

    public event Action<string>? SecondInstanceSignaled;

    public bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out _isFirstInstance);
            if (!_isFirstInstance)
            {
                _logger.Information("Another instance of TriggerPoint is already running.");
                return false;
            }

            _logger.Information("Single-instance mutex acquired successfully.");
            StartIpcServer();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to acquire single instance mutex. Assuming first instance.");
            _isFirstInstance = true;
            return true;
        }
    }

    public static async Task SignalPrimaryInstanceAsync(string argument = "")
    {
        try
        {
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            await client.ConnectAsync(1000).ConfigureAwait(false);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync(argument).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not signal primary instance via IPC pipe.");
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
                        PipeName,
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
