using Avalonia;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;


namespace BacktestApp.Controls;


public sealed partial class CandleChartControl
{
    // =========================
    // MMAP file reader (45 bytes record)
    // =========================
    public sealed class MmapCandleFile : IDisposable
    {
        public const int SymbolSize = 1;
        public const int CandleSize = 45;

        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _acc;

        public long Count { get; }

        public MmapCandleFile(string path)
        {
            var fi = new FileInfo(path);
            long byteLen = fi.Length;

            if (byteLen <= 0 || (byteLen % CandleSize) != 0)
                DebugMessage.Write($"[MmapCandleFile] WARNING: file size {byteLen} not multiple of {CandleSize}");

            Count = byteLen / CandleSize;

            _mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            _acc = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        }

        public bool ReadAt(
            long index,
            out long ts, out long o, out long h, out long l, out long c, out uint v,
            Span<byte> sym)
        {
            ts = o = h = l = c = 0;
            v = 0;

            if ((ulong)index >= (ulong)Count) return false;
            if (sym.Length < SymbolSize) throw new ArgumentException("sym must be length >= SymbolSize");

            long off = index * CandleSize;

            ts = _acc.ReadInt64(off + 0);
            o = _acc.ReadInt64(off + 8);
            h = _acc.ReadInt64(off + 16);
            l = _acc.ReadInt64(off + 24);
            c = _acc.ReadInt64(off + 32);
            v = _acc.ReadUInt32(off + 40);

            sym[0] = _acc.ReadByte(off + 44);

            return true;
        }

        public void Dispose()
        {
            _acc.Dispose();
            _mmf.Dispose();
        }
    }


    // =========================
    // Validation (filtre anti garbage)
    // =========================
    private static bool IsValidRecord(long ts, long o, long h, long l, long c)
    {
        const long MinTs = 946684800L * 1_000_000_000L;   // 2000-01-01
        const long MaxTs = 4102444800L * 1_000_000_000L;  // 2100-01-01

        if (ts < MinTs || ts > MaxTs) return false;
        if (o <= 0 || h <= 0 || l <= 0 || c <= 0) return false;
        if (h < l) return false;

        const long MaxReasonable = 10_000_000_000_000L; // 1e13
        if (o > MaxReasonable || h > MaxReasonable || l > MaxReasonable || c > MaxReasonable) return false;

        return true;
    }


    // =========================
    // Window loading (filtre invalides)
    // =========================
    private void LoadWindow(long startIndex)
    {
        if (_file is null) return;

        long idx = startIndex;
        int filled = 0;

        while (filled < WindowCount && idx < _fileCount)
        {
            Span<byte> sym = _sym.AsSpan(filled * MmapCandleFile.SymbolSize, MmapCandleFile.SymbolSize);

            if (!_file.ReadAt(idx, out var ts, out var o, out var h, out var l, out var c, out var v, sym))
                break;

            if (IsValidRecord(ts, o, h, l, c))
            {
                _ts[filled] = ts;
                _o[filled] = o;
                _h[filled] = h;
                _l[filled] = l;
                _c[filled] = c;
                _v[filled] = v;
                filled++;
            }

            idx++;
        }

        _windowLoaded = filled;
        _windowStart = startIndex;

        DebugMessage.Write($"windowLoaded={_windowLoaded} windowStart={_windowStart}");
    }


    // =========================
    // Reload window (sans sauts) + clamp centre
    // =========================
    private void ReloadWindow(long newStart)
    {
        if (_file is null) return;
        if (_reloadInProgress) return;

        newStart = ClampLong(newStart, 0, Math.Max(0, _fileCount - 1));
        if (newStart == _windowStart) return;
        if (newStart == _lastReloadStart) return;
        _lastReloadStart = newStart;

        _reloadInProgress = true;

        double keepCenterTime = _centerTimeSec;
        double keepSecondsPerPixel = _secondsPerPixel;
        double keepCenterPrice = _centerPrice;
        double keepPricePerPixel = _pricePerPixel;

        try
        {
            LoadWindow(newStart);

            _centerTimeSec = keepCenterTime;
            _secondsPerPixel = keepSecondsPerPixel;
            _centerPrice = keepCenterPrice;
            _pricePerPixel = keepPricePerPixel;

            var plot = GetPlotRect(new Rect(0, 0, Bounds.Width, Bounds.Height));
            if (plot.Width > 0) ClampCenterTimeToWindow(plot);

            InvalidateVisual();
        }
        finally
        {
            _reloadInProgress = false;
        }
    }


    private void EnsureWindowAroundView(Rect plot)
    {
        if (_file is null) return;
        if (_windowLoaded <= 0) return;
        if (_reloadInProgress) return;

        ClampCenterTimeToWindow(plot);

        int centerLocal = FindClosestIndexInWindow(_centerTimeSec);
        if (centerLocal < 0) return;

        int margin = GetPrefetchMargin();

        if (centerLocal < margin || centerLocal > (_windowLoaded - 1 - margin))
        {
            long centerGlobal = _windowStart + centerLocal;
            long newStart = centerGlobal - (WindowCount / 2);
            ReloadWindow(newStart);
        }
    }


    private int FindClosestIndexInWindow(double targetTimeSec)
    {
        if (_windowLoaded <= 0) return -1;

        int lo = 0, hi = _windowLoaded - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            double t = TsNsToEpochSeconds(_ts[mid]);

            if (t < targetTimeSec) lo = mid + 1;
            else if (t > targetTimeSec) hi = mid - 1;
            else return mid;
        }

        int i0 = ClampInt(lo, 0, _windowLoaded - 1);
        int i1 = ClampInt(lo - 1, 0, _windowLoaded - 1);

        double d0 = Math.Abs(TsNsToEpochSeconds(_ts[i0]) - targetTimeSec);
        double d1 = Math.Abs(TsNsToEpochSeconds(_ts[i1]) - targetTimeSec);

        return d0 < d1 ? i0 : i1;
    }


    // Taille du "pas" quand tu vas au précédent/suivant.
    // Ex: 1/2 fenêtre => overlap => navigation fluide
    private const int CursorStep = WindowCount / 2;


    public void loadPrevious()
    {
        DebugMessage.Write("previous clicked");
        CursorPrev();
    }

    public void CursorPrev()
    {
        if (_file is null) return;
        if (_windowLoaded <= 0) return;
        if (_reloadInProgress) return;

        int centerLocal = FindClosestIndexInWindow(_centerTimeSec);
        if (centerLocal < 0) centerLocal = _windowLoaded / 2;

        long centerGlobal = _windowStart + centerLocal;

        long newCenterGlobal = centerGlobal - CursorStep;
        long newStart = newCenterGlobal - (WindowCount / 2);

        ReloadWindow(ClampStart(newStart));
        InvalidateVisual();
    }

    public void CursorNext()
    {
        if (_file is null) return;
        if (_windowLoaded <= 0) return;
        if (_reloadInProgress) return;

        int centerLocal = FindClosestIndexInWindow(_centerTimeSec);
        if (centerLocal < 0) centerLocal = _windowLoaded / 2;

        long centerGlobal = _windowStart + centerLocal;

        long newCenterGlobal = centerGlobal + CursorStep;
        long newStart = newCenterGlobal - (WindowCount / 2);

        ReloadWindow(ClampStart(newStart));
        InvalidateVisual();
    }

    private long ClampStart(long start)
    {
        long maxStart = Math.Max(0, _fileCount - WindowCount);
        if (start < 0) return 0;
        if (start > maxStart) return maxStart;
        return start;
    }
}