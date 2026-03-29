// Copyright (c) 2019-2022 The Bitcoin Core developers
// Distributed under the MIT software license, see the accompanying
// file COPYING or http://www.opensource.org/licenses/mit-license.php.
//
// C# port of bitcoin/src/test/logging_tests.cpp
//
// Tests are organized in the same order as the original C++ file.
// Tests marked [Fact(Skip = ...)] cover functionality not yet ported from C++ to
// managed code; they serve as placeholders and will be enabled as each module is
// migrated.

using BtcSharp.Interop;
using TextEncoding = System.Text.Encoding;
using Xunit;

namespace BtcSharp.Tests;

// All tests in this class share NativeLogBridge's static state, so they must
// run sequentially (xUnit parallelises at the collection level by default).
[Collection("NativeLogBridge")]
public class LoggingTests : IDisposable
{
    // Messages captured by the test sink, written by the background consumer thread.
    private readonly List<(LogLevel Level, string Message)> _captured = new();
    private readonly Action<LogLevel, string> _prevSink;

    public LoggingTests()
    {
        _prevSink = NativeLogBridge.OutputSink;
        NativeLogBridge.OutputSink = (lvl, msg) =>
        {
            lock (_captured) _captured.Add((lvl, msg));
        };
    }

    public void Dispose()
    {
        NativeLogBridge.OutputSink = _prevSink;
        lock (_captured) _captured.Clear();
    }

    // Waits for the background consumer thread to deliver the expected number of
    // messages, matching the synchronous file-read pattern used in the C++ tests.
    private List<(LogLevel Level, string Message)> WaitForMessages(int count, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            lock (_captured)
                if (_captured.Count >= count)
                    return new List<(LogLevel, string)>(_captured);
            Thread.Sleep(1);
        }
        lock (_captured)
            return new List<(LogLevel, string)>(_captured);
    }

    // -------------------------------------------------------------------------
    // logging_timer (C++ equivalent)
    //
    // BCLog::Timer<T> is a RAII helper that logs "category: msg (Xµs/ms/s)".
    // Example assertion from the original:
    //   auto micro_timer = BCLog::Timer<std::chrono::microseconds>("tests", "end_msg");
    //   BOOST_CHECK_EQUAL(micro_timer.LogMsg("msg").substr(0, prefix.size()), "tests: msg (");
    //
    // Not yet ported: there is no managed BCLog::Timer equivalent.
    // -------------------------------------------------------------------------
    [Fact(Skip = "BCLog::Timer not yet ported to managed code")]
    public void Timer_LogMsg_StartsWithCategoryAndMessagePrefix()
    {
        // Once ported, the managed timer should produce:
        //   "tests: msg (Xµs)"
        // where the prefix up to the opening parenthesis is deterministic.
        const string expectedPrefix = "tests: msg (";
        string timerOutput = string.Empty; // replace with: new ManagedTimer("tests", "end_msg").LogMsg("msg")
        Assert.StartsWith(expectedPrefix, timerOutput);
    }

    // -------------------------------------------------------------------------
    // logging_LogPrintStr (C++ equivalent)
    //
    // The original test verifies that source-location and category/level prefixes
    // are prepended correctly by BCLog::Logger::LogPrintStr().  On the managed
    // side NativeLogBridge is a *receiver* – it accepts already-formatted strings
    // from C++.  These tests verify that whatever arrives is forwarded verbatim.
    // -------------------------------------------------------------------------
    [Fact]
    public void Enqueue_FormattedMessages_AreForwardedVerbatim()
    {
        // Mirrors the six LogPrintStr calls in the C++ test.
        var inputs = new[]
        {
            (LogLevel.Debug, "[src1:1] [fn1] [net] foo1: bar1"),
            (LogLevel.Info,  "[src2:2] [fn2] [net:info] foo2: bar2"),
            (LogLevel.Debug, "[src3:3] [fn3] [debug] foo3: bar3"),
            (LogLevel.Info,  "[src4:4] [fn4] foo4: bar4"),
            (LogLevel.Debug, "[src5:5] [fn5] [debug] foo5: bar5"),
            (LogLevel.Info,  "[src6:6] [fn6] foo6: bar6"),
        };

        foreach (var (lvl, msg) in inputs)
            NativeLogBridge.Enqueue(lvl, msg);

        var lines = WaitForMessages(inputs.Length);
        Assert.Equal(inputs.Length, lines.Count);

        for (int i = 0; i < inputs.Length; i++)
        {
            Assert.Equal(inputs[i].Item1, lines[i].Level);
            Assert.Equal(inputs[i].Item2, lines[i].Message);
        }
    }

    // -------------------------------------------------------------------------
    // logging_LogPrintMacrosDeprecated (C++ equivalent)
    //
    // The C++ test checks that LogPrintf and LogPrintLevel produce specific
    // category/level prefixes.  Here we verify:
    //   1. The LogLevel enum integer values match the native-side contract.
    //   2. All five levels accepted by LogPrintLevel are distinct enum members.
    // -------------------------------------------------------------------------
    [Fact]
    public void LogLevel_EnumValues_MatchNativeContract()
    {
        // These values are fixed in btcsharp_log_bridge.h:
        //   TRACE=0, DEBUG_L=1, INFO=2, WARN=3, ERROR_L=4, FATAL=5
        Assert.Equal(0, (int)LogLevel.Trace);
        Assert.Equal(1, (int)LogLevel.Debug);
        Assert.Equal(2, (int)LogLevel.Info);
        Assert.Equal(3, (int)LogLevel.Warn);
        Assert.Equal(4, (int)LogLevel.Error);
        Assert.Equal(5, (int)LogLevel.Fatal);
    }

    // -------------------------------------------------------------------------
    // logging_LogPrintMacros (C++ equivalent)
    //
    // The original test logs at Trace (filtered), Debug, Info, Warning, Error
    // and checks that each surviving line carries the right prefix.  The managed
    // bridge does not filter; filtering is still done in C++.  We verify that
    // all four non-filtered levels are accepted and delivered in order.
    // -------------------------------------------------------------------------
    [Fact]
    public void Enqueue_DebugInfoWarnError_AllDeliveredInOrder()
    {
        // Mirrors: LogDebug, LogInfo, LogWarning, LogError calls in the C++ test.
        var inputs = new[]
        {
            (LogLevel.Debug, "[net] foo7: bar7"),
            (LogLevel.Info,  "foo8: bar8"),
            (LogLevel.Warn,  "[warning] foo9: bar9"),
            (LogLevel.Error, "[error] foo10: bar10"),
        };

        foreach (var (lvl, msg) in inputs)
            NativeLogBridge.Enqueue(lvl, msg);

        var lines = WaitForMessages(inputs.Length);
        Assert.Equal(inputs.Length, lines.Count);

        for (int i = 0; i < inputs.Length; i++)
        {
            Assert.Equal(inputs[i].Item1, lines[i].Level);
            Assert.Equal(inputs[i].Item2, lines[i].Message);
        }
    }

    // -------------------------------------------------------------------------
    // logging_LogPrintMacros_CategoryName (C++ equivalent)
    //
    // Iterates all BCLog::LogFlags categories and asserts each name round-trips
    // through GetLogCategory().  Requires the category registry, which lives in
    // C++ logging.cpp and has not yet been ported.
    // -------------------------------------------------------------------------
    [Fact(Skip = "Log category registry (BCLog::LogFlags) not yet ported to managed code")]
    public void CategoryName_RoundTrips_ThroughRegistry()
    {
        // Once the category registry is ported, iterate all known LogCategory
        // values, look each up by name, and assert the name round-trips.
    }

    // -------------------------------------------------------------------------
    // logging_SeverityLevels (C++ equivalent)
    //
    // Part 1 (enum ordering): verifiable now.
    // Part 2 (per-category level filtering): requires the managed logger, not
    // yet ported.
    // -------------------------------------------------------------------------
    [Fact]
    public void LogLevel_Ordering_IsStrictlyAscending()
    {
        // Trace < Debug < Info < Warn < Error < Fatal
        Assert.True((int)LogLevel.Trace  < (int)LogLevel.Debug);
        Assert.True((int)LogLevel.Debug  < (int)LogLevel.Info);
        Assert.True((int)LogLevel.Info   < (int)LogLevel.Warn);
        Assert.True((int)LogLevel.Warn   < (int)LogLevel.Error);
        Assert.True((int)LogLevel.Error  < (int)LogLevel.Fatal);
    }

    [Fact(Skip = "Per-category log-level filtering not yet ported to managed code")]
    public void SeverityLevels_CategorySpecificLevel_TakesPrecedenceOverGlobal()
    {
        // Mirrors the C++ test:
        //   global level = Debug
        //   net category level = Info
        //   → BCLog::NET + Debug message is suppressed
        //   → BCLog::NET + Warning/Error messages are emitted
    }

    // -------------------------------------------------------------------------
    // logging_Conf (C++ equivalent)
    //
    // Parses -loglevel=debug and -loglevel=net:trace via ArgsManager.
    // ArgsManager and init::SetLoggingLevel are not yet ported.
    // -------------------------------------------------------------------------
    [Fact(Skip = "Log configuration via ArgsManager not yet ported to managed code")]
    public void Configuration_GlobalLogLevel_CanBeSetViaArgument()
    {
        // Once ArgsManager is ported:
        //   Parse("-loglevel=debug") → LogInstance().LogLevel() == Level.Debug
    }

    [Fact(Skip = "Log configuration via ArgsManager not yet ported to managed code")]
    public void Configuration_CategoryLogLevel_CanBeSetViaArgument()
    {
        // Once ArgsManager is ported:
        //   Parse("-loglevel=net:trace") → CategoryLevels()[NET] == Level.Trace
    }

    [Fact(Skip = "Log configuration via ArgsManager not yet ported to managed code")]
    public void Configuration_GlobalAndCategoryLevels_CanBeSetTogether()
    {
        // Parse("-loglevel=debug", "-loglevel=net:trace", "-loglevel=http:info")
        // → global Debug, NET Trace, HTTP Info
    }

    // =========================================================================
    // C#-specific tests — cover the managed UTF-8 decode paths that have no
    // direct C++ equivalent because decoding happens inside the CLR bridge.
    // =========================================================================

    [Fact]
    public unsafe void DecodeUtf8_SmallAsciiMessage_DecodesCorrectly()
    {
        // Exercises the stackalloc path (len <= 1024).
        const string text = "foo1: bar1";
        byte[] bytes = TextEncoding.UTF8.GetBytes(text);
        fixed (byte* p = bytes)
        {
            string result = NativeLogBridge.DecodeUtf8(p, bytes.Length);
            Assert.Equal(text, result);
        }
    }

    [Fact]
    public unsafe void DecodeUtf8_LargeMessage_DecodesCorrectly()
    {
        // Exercises the ArrayPool path (len > 1024).
        string text = new string('x', 2048) + " end";
        byte[] bytes = TextEncoding.UTF8.GetBytes(text);
        fixed (byte* p = bytes)
        {
            string result = NativeLogBridge.DecodeUtf8(p, bytes.Length);
            Assert.Equal(text, result);
        }
    }

    [Fact]
    public unsafe void DecodeUtf8_MultibyteCharacters_DecodesCorrectly()
    {
        // Bitcoin log messages may contain non-ASCII (e.g. filepath separators
        // on some platforms, or user-supplied strings in RPC responses).
        const string text = "block: \u00e9\u00e0\u00fc";  // é à ü
        byte[] bytes = TextEncoding.UTF8.GetBytes(text);
        fixed (byte* p = bytes)
        {
            string result = NativeLogBridge.DecodeUtf8(p, bytes.Length);
            Assert.Equal(text, result);
        }
    }

    [Fact]
    public unsafe void DecodeUtf8_NullPointer_ReturnsEmpty()
    {
        string result = NativeLogBridge.DecodeUtf8(null, 10);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public unsafe void DecodeUtf8_ZeroLength_ReturnsEmpty()
    {
        byte b = 0x41;
        string result = NativeLogBridge.DecodeUtf8(&b, 0);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public unsafe void DecodeUtf8_NegativeLength_ReturnsEmpty()
    {
        byte b = 0x41;
        string result = NativeLogBridge.DecodeUtf8(&b, -1);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Enqueue_NullMessage_DoesNotThrow()
    {
        // BlockingCollection.TryAdd handles any non-null item; passing null!
        // should not crash the consumer thread.
        var ex = Record.Exception(() => NativeLogBridge.Enqueue(LogLevel.Info, null!));
        Assert.Null(ex);
    }

    [Fact]
    public void Enqueue_EmptyMessage_IsDelivered()
    {
        NativeLogBridge.Enqueue(LogLevel.Info, string.Empty);
        var lines = WaitForMessages(1);
        Assert.Single(lines);
        Assert.Equal(string.Empty, lines[0].Message);
    }

    [Fact]
    public void Enqueue_AllSixLevels_AreDelivered()
    {
        var levels = new[] { LogLevel.Trace, LogLevel.Debug, LogLevel.Info,
                             LogLevel.Warn,  LogLevel.Error, LogLevel.Fatal };
        foreach (var lvl in levels)
            NativeLogBridge.Enqueue(lvl, lvl.ToString());

        var lines = WaitForMessages(levels.Length);
        Assert.Equal(levels.Length, lines.Count);
        for (int i = 0; i < levels.Length; i++)
            Assert.Equal(levels[i], lines[i].Level);
    }
}

// Prevents xUnit from running NativeLogBridge tests in parallel with each other
// (they share static sink state).
[CollectionDefinition("NativeLogBridge")]
public class NativeLogBridgeCollection { }
