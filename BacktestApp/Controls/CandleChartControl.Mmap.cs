using Avalonia;
using Avalonia.Threading;
using DatasetTool;
using System;
using System.Collections.Generic;
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
        int isInvalidCount = 0;

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
                DebugMessage.Write($"Loaded record idx={idx} ts={ts} o={o} h={h} l={l} c={c} v={v} sym={(char)sym[0]}");
            }
            else { isInvalidCount++; }

            idx++;
        }

        _windowLoaded = filled;
        _windowStart = startIndex;

        DebugMessage.Write($"Loaded window: start={startIndex} loaded={_windowLoaded} invalid skipped={isInvalidCount}");

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

    public void loadNext()
    {
        DebugMessage.Write("next clicked");
        CursorNext();
    }

    public void CursorPrev()
    {
        if (_file is null) return;
        if (_windowLoaded <= 0) return;
        if (_reloadInProgress) return;

        // Au début du fichier ? => passer au contrat précédent
        if (IsAtStartOfFile())
        {
            int prevIdx = _currentIdx - 1;
            if (_starts != null && prevIdx >= 0)
            {
                LoadContractIndex(prevIdx, goToStart: false);
            }
            return;
        }

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

        // Au bout du fichier ? => passer au contrat suivant
        if (IsAtEndOfFile())
        {
            int nextIdx = _currentIdx + 1;
            if (_starts != null && nextIdx < _starts.Length)
            {
                LoadContractIndex(nextIdx, goToStart: true);
            }
            return;
        }

        int centerLocal = FindClosestIndexInWindow(_centerTimeSec);
        if (centerLocal < 0) centerLocal = _windowLoaded / 2;

        long centerGlobal = _windowStart + centerLocal;

        long newCenterGlobal = centerGlobal + CursorStep;
        long newStart = newCenterGlobal - (WindowCount / 2);

        ReloadWindow(ClampStart(newStart));
        InvalidateVisual();
    }
    public void loadIndex()
    {

        // 1) Charger l'index une fois
        (_starts, _ends) = JsonToBinaryIndex.LoadAll("data/bin/_index.bin");
    }


    public int FindFileIndex(uint targetYmd)
    {
        if (_starts.Length == 0)
            return -1;

        int idx = JsonToBinaryIndex.FindBestIndex(_starts, _ends, targetYmd);

        return idx;
    }


    private long ClampStart(long start)
    {
        long maxStart = Math.Max(0, _fileCount - WindowCount);
        if (start < 0) return 0;
        if (start > maxStart) return maxStart;
        return start;
    }


    private string[]? _bins;               // liste triée des .bin (sans _index.bin)
    private string? _currentBinPath;       // pour éviter de recharger le même fichier

    private void EnsureBinListLoaded()
    {
        if (_bins != null) return;

        string binDir = Path.Combine("data", "bin");

        var all = Directory.GetFiles(binDir, "*.bin");
        var binsList = new List<string>(all.Length);

        foreach (var f in all)
        {
            if (!f.EndsWith("_index.bin", StringComparison.OrdinalIgnoreCase))
                binsList.Add(f);
        }

        _bins = binsList.ToArray();
        Array.Sort(_bins, StringComparer.OrdinalIgnoreCase);

        if (_bins.Length == 0)
            DebugMessage.Write("[CandleChartControl] Aucun .bin trouvé");
    }

    public void LoadBinFile(string path, long? startOverride = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        // évite de recharger si c’est déjà le fichier courant
        if (string.Equals(_currentBinPath, path, StringComparison.OrdinalIgnoreCase))
        {
            // si tu veux quand même repositionner la fenêtre, tu peux laisser passer ici
        }

        _currentBinPath = path;

        _file?.Dispose();
        _file = new MmapCandleFile(path);
        _fileCount = _file.Count;

        // fenêtre de départ
        long start = startOverride ?? Math.Max(0, _fileCount - 5000);
        LoadWindow(start);

        // reset axes
        _xInited = false;
        _yInited = false;

        InvalidateVisual();
    }

    public (int from, int to) GetSurroundingRange(int idx, int length)
    {
        int from = Math.Max(0, idx - 2);
        int to = Math.Min(length - 1, idx + 2);
        return (from, to);
    }

    public List<(int index, uint start, uint end)> GetSurroundingContracts(int idx, uint[] starts, uint[] ends)
    {
        var result = new List<(int, uint, uint)>();

        int from = Math.Max(0, idx - 2);
        int to = Math.Min(starts.Length - 1, idx + 2);

        for (int i = from; i <= to; i++)
        {
            result.Add((i, starts[i], ends[i]));
        }

        return result;
    }


    public (uint start, uint end)? OpenBinByIndex(int idx)
    {
        if (_starts == null || _ends == null) return null;
        if (idx < 0 || idx >= _starts.Length) return null;

        return (_starts[idx], _ends[idx]);
    }

    public (int left2, int left1, int current, int right1, int right2)
        GetNeighborIndexes(int idx)
    {
        if (_starts == null)
            throw new InvalidOperationException("Index non chargé.");

        int len = _starts.Length;

        if (idx < 0 || idx >= len)
            throw new ArgumentOutOfRangeException(nameof(idx));

        int Safe(int i) => (i >= 0 && i < len) ? i : -1;

        return (
            Safe(idx - 2),
            Safe(idx - 1),
            idx,
            Safe(idx + 1),
            Safe(idx + 2)
        );
    }

    public uint getStart(int idx)
    {
        if (idx < 0 || _starts == null || idx >= _starts.Length)
            throw new ArgumentOutOfRangeException(nameof(idx));
        return _starts[idx];
    }

    public uint getEnd(int idx)
    {
        if (idx < 0 || _ends == null || idx >= _ends.Length)
            throw new ArgumentOutOfRangeException(nameof(idx));
        return _ends[idx];
    }


    private string BuildPathByIndex(int idx)
    {
        uint start = _starts[idx];
        uint end = _ends[idx];
        return Path.Combine("data", "bin", $"glbx-mdp3-{start}-{end}.ohlcv-1m.bin");
    }

    private MmapCandleFile? TryOpenFile(int idx)
    {
        if (idx < 0 || idx >= _starts.Length) return null;

        string path = BuildPathByIndex(idx);
        if (!File.Exists(path))
        {
            DebugMessage.Write($"[CandleChartControl] Missing file: {path}");
            return null;
        }

        return new MmapCandleFile(path);
    }

    private static void DisposeSlot(ref MmapCandleFile? f, ref int i)
    {
        f?.Dispose();
        f = null;
        i = -1;
    }


    private MmapCandleFile? GetLoadedFileForIndex(int idx)
    {
        if (idx == _i0) return _f0;
        if (idx == _iM1) return _fM1;
        if (idx == _iM2) return _fM2;
        if (idx == _iP1) return _fP1;
        if (idx == _iP2) return _fP2;
        return null;
    }


    private void CleanupFive(int m2, int m1, int cur, int p1, int p2)
    {
        bool Keep(int i) => i == m2 || i == m1 || i == cur || i == p1 || i == p2;

        if (_iM2 != -1 && !Keep(_iM2)) DisposeSlot(ref _fM2, ref _iM2);
        if (_iM1 != -1 && !Keep(_iM1)) DisposeSlot(ref _fM1, ref _iM1);
        if (_i0 != -1 && !Keep(_i0)) DisposeSlot(ref _f0, ref _i0);
        if (_iP1 != -1 && !Keep(_iP1)) DisposeSlot(ref _fP1, ref _iP1);
        if (_iP2 != -1 && !Keep(_iP2)) DisposeSlot(ref _fP2, ref _iP2);
    }

    private void EnsureSlot(ref MmapCandleFile? slotFile, ref int slotIdx, int wantedIdx)
    {
        if (wantedIdx < 0 || wantedIdx >= _starts.Length)
        {
            // extrémité => slot vide
            if (slotIdx != -1) DisposeSlot(ref slotFile, ref slotIdx);
            return;
        }

        if (slotIdx == wantedIdx && slotFile != null)
            return; // déjà bon

        // si slot contient autre chose, on le libère
        if (slotIdx != -1 && slotIdx != wantedIdx)
            DisposeSlot(ref slotFile, ref slotIdx);

        // si déjà chargé dans un autre slot, on réutilise (sans dupliquer)
        var existing = GetLoadedFileForIndex(wantedIdx);
        if (existing != null)
        {
            slotFile = existing;
            slotIdx = wantedIdx;
            return;
        }

        // sinon on ouvre
        slotFile = TryOpenFile(wantedIdx);
        slotIdx = (slotFile != null) ? wantedIdx : -1;
    }


    public void LoadByIndexWithNeighbors(int idx)
    {

        if (_starts == null || _ends == null) return;
        if (_starts.Length == 0) return;
        if (idx < 0 || idx >= _starts.Length) return;

        var (m2, m1, cur, p1, p2) = GetNeighborIndexes(idx);

        // 1) cleanup tout ce qui n'est pas dans la nouvelle plage
        CleanupFive(m2, m1, cur, p1, p2);

        // 2) remplir les slots requis (ouvre ce qui manque)
        EnsureSlot(ref _fM2, ref _iM2, m2);
        EnsureSlot(ref _fM1, ref _iM1, m1);
        EnsureSlot(ref _f0, ref _i0, cur);
        EnsureSlot(ref _fP1, ref _iP1, p1);
        EnsureSlot(ref _fP2, ref _iP2, p2);

        // 3) current = slot 0
        if (_f0 == null)
        {
            DebugMessage.Write($"[CandleChartControl] Cannot open current idx={cur}");
            return;
        }

        _file = _f0;
        _fileCount = _file.Count;

        long start = Math.Max(0, _fileCount - 5000);
        LoadWindow(start);

        _xInited = false;
        _yInited = false;

        InvalidateVisual();
    }



    private bool IsAtEndOfFile()
    {
        long maxStart = Math.Max(0, _fileCount - WindowCount);
        return _windowStart >= maxStart;
    }

    private bool IsAtStartOfFile()
    {
        return _windowStart <= 0;
    }

    public void LoadContractIndex(int idx, bool goToStart)
    {
        if (_starts == null || _ends == null) return;
        if (idx < 0 || idx >= _starts.Length) return;

        _currentIdx = idx;

        uint start = _starts[idx];
        uint end = _ends[idx];

        string filePath = Path.Combine("data", "bin", $"glbx-mdp3-{start}-{end}.ohlcv-1m.bin");

        // Charger le fichier
        _file?.Dispose();
        _file = new MmapCandleFile(filePath);
        _fileCount = _file.Count;

        // Charger une fenêtre au début ou à la fin
        long startIndex;
        if (goToStart)
            startIndex = 0;
        else
            startIndex = Math.Max(0, _fileCount - LoadStartTail);

        LoadWindow(startIndex);

        _xInited = false;
        _yInited = false;

        InvalidateVisual();
    }

}