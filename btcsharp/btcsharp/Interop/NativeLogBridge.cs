using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TextEncoding = System.Text.Encoding;

namespace BtcSharp.Interop;

public enum LogLevel : int { Trace = 0, Debug = 1, Info = 2, Warn = 3, Error = 4, Fatal = 5 }

internal static class NativeLogBridge
{
    // lock-free ring/queue for ingestion
    private static readonly BlockingCollection<(LogLevel lvl, string msg)> _q = new(new ConcurrentQueue<(LogLevel, string)>());

    // Replaceable in tests to capture output without touching Console.
    internal static readonly Action<LogLevel, string> DefaultOutputSink =
        static (_, msg) => Console.Out.WriteLine($"[BtcSharp] {msg}");
    internal static Action<LogLevel, string> OutputSink = DefaultOutputSink;

    static NativeLogBridge()
    {
        // detach a single consumer (you can swap for Serilog/NLog/etc. later)
        var t = new Thread(() =>
        {
            foreach (var (lvl, msg) in _q.GetConsumingEnumerable())
                OutputSink(lvl, msg);
        });
        t.IsBackground = true;
        t.Name = "ManagedLogConsumer";
        t.Start();
    }

    // Called by the [UnmanagedCallersOnly] entry point after UTF-8 decoding,
    // and directly by managed callers (e.g. tests).
    internal static void Enqueue(LogLevel level, string message)
    {
        if (!_q.IsAddingCompleted)
            _q.TryAdd((level, message));
    }

    // Exposed for unit-testing the two decode paths (stack vs. ArrayPool).
    internal static unsafe string DecodeUtf8(byte* utf8, int len)
    {
        if (utf8 == null || len <= 0) return string.Empty;

        const int StackThreshold = 1024;
        if (len <= StackThreshold)
        {
            Span<byte> tmp = stackalloc byte[len];
            new Span<byte>(utf8, len).CopyTo(tmp);
            return TextEncoding.UTF8.GetString(tmp);
        }
        else
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(len);
            try
            {
                Marshal.Copy((nint)utf8, buffer, 0, len);
                return TextEncoding.UTF8.GetString(buffer, 0, len);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "BtcSharp_Log", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static unsafe void Log(int level, byte* utf8, int len)
    {
        if (utf8 == null || len <= 0) return;
        Enqueue((LogLevel)level, DecodeUtf8(utf8, len));
    }

    // Optional: graceful shutdown (call from native before process exit)
    [UnmanagedCallersOnly(EntryPoint = "BtcSharp_Log_Shutdown", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void Shutdown() => _q.CompleteAdding();
}
