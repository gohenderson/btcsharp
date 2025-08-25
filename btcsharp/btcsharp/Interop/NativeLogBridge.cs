using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace BtcSharp.Interop;

public enum LogLevel : int { Trace = 0, Debug = 1, Info = 2, Warn = 3, Error = 4, Fatal = 5 }

internal static class NativeLogBridge
{
    // lock-free ring/queue for ingestion
    private static readonly BlockingCollection<(LogLevel lvl, string msg)> _q = new(new ConcurrentQueue<(LogLevel, string)>());

    static NativeLogBridge()
    {
        // detach a single consumer (you can swap for Serilog/NLog/etc. later)
        var t = new Thread(() =>
        {
            foreach (var (lvl, msg) in _q.GetConsumingEnumerable())
            {
                // TODO: route to your preferred logger. For now, Console.* is fine.
                switch (lvl)
                {
                    case LogLevel.Error: Console.Error.WriteLine(msg); break;
                    case LogLevel.Warn:  Console.Error.WriteLine(msg); break;
                    default:             Console.Out.WriteLine(msg);   break;
                }
            }
        });
        t.IsBackground = true;
        t.Name = "ManagedLogConsumer";
        t.Start();
    }

    [UnmanagedCallersOnly(EntryPoint = "BtcSharp_Log", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static unsafe void Log(int level, byte* utf8, int len)
    {
        // ultra‑cheap decode; avoid exceptions/allocs in the hot path
        if (utf8 == null || len <= 0) return;

        // stackalloc small messages; rent for larger
        const int StackThreshold = 1024;
        string msg;

        if (len <= StackThreshold)
        {
            Span<byte> tmp = stackalloc byte[len];
            new Span<byte>(utf8, len).CopyTo(tmp);
            msg = Encoding.UTF8.GetString(tmp);
        }
        else
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(len);
            try
            {
                Marshal.Copy((nint)utf8, buffer, 0, len);
                msg = Encoding.UTF8.GetString(buffer, 0, len);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        var lvl = (LogLevel)level;
        _q.Add((lvl, msg));
    }

    // Optional: graceful shutdown (call from native before process exit)
    [UnmanagedCallersOnly(EntryPoint = "BtcSharp_Log_Shutdown", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static void Shutdown() => _q.CompleteAdding();
}
