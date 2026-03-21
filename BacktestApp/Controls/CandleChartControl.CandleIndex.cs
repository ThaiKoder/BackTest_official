using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace BacktestApp.Controls;

public sealed partial class CandleChartControl
{
    public sealed class CandleIndex : IDisposable
    {
        private MmapCandleFile? _file;
        private long _count;

        // =========================
        // State NEXT
        // =========================
        private CandleSlot[]? _window;
        private int _windowSize;
        private int _windowHead;
        private int _range = -1;
        private int _currentIdx = -1;
        private bool _hasState;

        // =========================
        // State PREVIOUS
        // =========================
        private CandleSlot[]? _windowPrevious;
        private int _windowSizePrevious;
        private int _windowHeadPrevious;
        private int _rangePrevious = -1;
        private int _currentIdxPrevious = -1;
        private bool _hasStatePrevious;

        private int _maxAllowedRange = -1;

        private struct CandleSlot
        {
            public int Idx;
            public long Ts;
            public long O;
            public long H;
            public long L;
            public long C;
            public uint V;
            public byte Sym;

            public CandleSlot(int idx, long ts, long o, long h, long l, long c, uint v, byte sym)
            {
                Idx = idx;
                Ts = ts;
                O = o;
                H = h;
                L = l;
                C = c;
                V = v;
                Sym = sym;
            }
        }

        public sealed record CandleItem(
            int Idx,
            long Ts,
            long O,
            long H,
            long L,
            long C,
            uint V,
            byte Sym);

        public sealed record CandleCursorStep(
            int CurrentIdx,
            int NextCursorIdx,
            int Range,
            List<CandleItem> Window,
            List<CandleItem> Added,
            List<CandleItem> Removed);

        public sealed record CandleCursorStepPrevious(
            int CurrentIdx,
            int PreviousCursorIdx,
            int Range,
            List<CandleItem> Window,
            List<CandleItem> Added,
            List<CandleItem> Removed);

        public long Count => _count;
        public int MaxAllowedRange => _maxAllowedRange;

        public CandleIndex()
        {
            DebugMessage.Write("CandleIndex Constructor");
        }

        public void Load(string filePath)
        {
            Dispose();

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Le chemin du fichier est vide.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Fichier candle introuvable.", filePath);

            _file = new MmapCandleFile(filePath);
            _count = _file.Count;

            ResetCursor();
            ResetCursorPrevious();
        }

        public void ResetCursor()
        {
            _window = null;
            _windowSize = 0;
            _windowHead = 0;
            _range = -1;
            _currentIdx = -1;
            _hasState = false;
        }

        public void ResetCursorPrevious()
        {
            _windowPrevious = null;
            _windowSizePrevious = 0;
            _windowHeadPrevious = 0;
            _rangePrevious = -1;
            _currentIdxPrevious = -1;
            _hasStatePrevious = false;
        }

        public CandleItem Read(long index)
        {
            if (_file == null)
                throw new InvalidOperationException("Fichier candle non chargé.");

            if (index < 0 || index >= _count)
                throw new ArgumentOutOfRangeException(nameof(index));

            Span<byte> sym = stackalloc byte[MmapCandleFile.SymbolSize];

            if (!_file.ReadAt(index, out var ts, out var o, out var h, out var l, out var c, out var v, sym))
                throw new InvalidOperationException($"Impossible de lire la candle à l'index {index}.");

            return new CandleItem((int)index, ts, o, h, l, c, v, sym[0]);
        }

        private CandleSlot ReadSlotOrEmpty(int idx)
        {
            if (_file == null || idx < 0 || idx >= _count)
                return new CandleSlot(-1, 0, 0, 0, 0, 0, 0, 0);

            Span<byte> sym = stackalloc byte[MmapCandleFile.SymbolSize];

            if (!_file.ReadAt(idx, out var ts, out var o, out var h, out var l, out var c, out var v, sym))
                return new CandleSlot(-1, 0, 0, 0, 0, 0, 0, 0);

            return new CandleSlot(idx, ts, o, h, l, c, v, sym[0]);
        }

        public CandleCursorStep CandlesNext(int cursorIdx, int range)
        {
            if (_file == null)
                throw new InvalidOperationException("Fichier candle non chargé.");

            if (range < 0)
                throw new ArgumentOutOfRangeException(nameof(range), "range doit être >= 0.");

            if (_count <= 0)
                return new CandleCursorStep(-1, -1, range, new List<CandleItem>(), new List<CandleItem>(), new List<CandleItem>());

            if (cursorIdx < 0 || cursorIdx >= _count)
                return new CandleCursorStep(-1, -1, range, new List<CandleItem>(), new List<CandleItem>(), new List<CandleItem>());

            int step = range + 1;

            if (_hasState &&
                _window != null &&
                _range == range &&
                _currentIdx == cursorIdx &&
                GetAtLogical(_window!, _windowHead, _windowSize, _windowSize - 1).Idx == -1)
            {
                return new CandleCursorStep(
                    _currentIdx,
                    -1,
                    _range,
                    ToListLogical(_window!, _windowHead, _windowSize),
                    new List<CandleItem>(),
                    new List<CandleItem>());
            }

            int expectedNext = _currentIdx >= 0 ? _currentIdx + step : -1;

            bool canIncremental =
                _hasState &&
                _window != null &&
                _range == range &&
                cursorIdx == expectedNext;

            if (!canIncremental)
                return BuildFullWindowNext(cursorIdx, range);

            return AdvanceWindowIncrementalNext(cursorIdx, range);
        }

        public CandleCursorStepPrevious CandlesPrevious(int cursorIdx, int range)
        {
            if (_file == null)
                throw new InvalidOperationException("Fichier candle non chargé.");

            if (range < 0)
                throw new ArgumentOutOfRangeException(nameof(range), "range doit être >= 0.");

            if (_count <= 0)
                return new CandleCursorStepPrevious(-1, -1, range, new List<CandleItem>(), new List<CandleItem>(), new List<CandleItem>());

            if (cursorIdx < 0 || cursorIdx >= _count)
                return new CandleCursorStepPrevious(-1, -1, range, new List<CandleItem>(), new List<CandleItem>(), new List<CandleItem>());

            int step = range + 1;

            if (_hasStatePrevious &&
                _windowPrevious != null &&
                _rangePrevious == range &&
                _currentIdxPrevious == cursorIdx &&
                GetAtLogical(_windowPrevious, _windowHeadPrevious, _windowSizePrevious, 0).Idx == -1)
            {
                return new CandleCursorStepPrevious(
                    _currentIdxPrevious,
                    -1,
                    _rangePrevious,
                    ToListLogical(_windowPrevious, _windowHeadPrevious, _windowSizePrevious),
                    new List<CandleItem>(),
                    new List<CandleItem>());
            }

            int expectedPrevious = _currentIdxPrevious >= 0 ? _currentIdxPrevious - step : -1;

            bool canIncremental =
                _hasStatePrevious &&
                _windowPrevious != null &&
                _rangePrevious == range &&
                cursorIdx == expectedPrevious;

            if (!canIncremental)
                return BuildFullWindowPrevious(cursorIdx, range);

            return AdvanceWindowIncrementalPrevious(cursorIdx, range);
        }

        private CandleCursorStep BuildFullWindowNext(int cursorIdx, int range)
        {
            _range = range;
            _currentIdx = cursorIdx;
            _windowSize = range * 2 + 1;
            _windowHead = 0;
            _window = new CandleSlot[_windowSize];

            int p = 0;
            for (int idx = cursorIdx - range; idx <= cursorIdx + range; idx++)
                _window[p++] = ReadSlotOrEmpty(idx);

            _hasState = true;

            var window = ToListLogical(_window, _windowHead, _windowSize);
            var added = FilterNonEmpty(window);
            var removed = new List<CandleItem>();

            int nextCursorIdx = GetAtLogical(_window, _windowHead, _windowSize, _windowSize - 1).Idx == -1
                ? -1
                : cursorIdx + (range + 1);

            return new CandleCursorStep(cursorIdx, nextCursorIdx, range, window, added, removed);
        }

        private CandleCursorStepPrevious BuildFullWindowPrevious(int cursorIdx, int range)
        {
            _rangePrevious = range;
            _currentIdxPrevious = cursorIdx;
            _windowSizePrevious = range * 2 + 1;
            _windowHeadPrevious = 0;
            _windowPrevious = new CandleSlot[_windowSizePrevious];

            int p = 0;
            for (int idx = cursorIdx - range; idx <= cursorIdx + range; idx++)
                _windowPrevious[p++] = ReadSlotOrEmpty(idx);

            _hasStatePrevious = true;

            var window = ToListLogical(_windowPrevious, _windowHeadPrevious, _windowSizePrevious);
            var added = FilterNonEmpty(window);
            var removed = new List<CandleItem>();

            int previousCursorIdx = GetAtLogical(_windowPrevious, _windowHeadPrevious, _windowSizePrevious, 0).Idx == -1
                ? -1
                : cursorIdx - (range + 1);

            return new CandleCursorStepPrevious(cursorIdx, previousCursorIdx, range, window, added, removed);
        }

        private CandleCursorStep AdvanceWindowIncrementalNext(int cursorIdx, int range)
        {
            int step = range + 1;
            int size = _windowSize;

            var removed = new List<CandleItem>(step);
            var added = new List<CandleItem>(step);

            for (int i = 0; i < step && i < size; i++)
            {
                var slot = GetAtLogical(_window!, _windowHead, _windowSize, i);
                if (slot.Idx != -1)
                    removed.Add(ToItem(slot));
            }

            _windowHead = (_windowHead + step) % size;

            int rightStartIdx = _currentIdx + range + 1;
            for (int j = 0; j < step; j++)
            {
                int newIdx = rightStartIdx + j;
                var slot = ReadSlotOrEmpty(newIdx);

                SetAtLogical(_window!, _windowHead, _windowSize, size - step + j, slot);

                if (slot.Idx != -1)
                    added.Add(ToItem(slot));
            }

            _currentIdx = cursorIdx;

            var window = ToListLogical(_window!, _windowHead, _windowSize);

            int nextCursorIdx = GetAtLogical(_window!, _windowHead, _windowSize, size - 1).Idx == -1
                ? -1
                : cursorIdx + step;

            return new CandleCursorStep(cursorIdx, nextCursorIdx, range, window, added, removed);
        }

        private CandleCursorStepPrevious AdvanceWindowIncrementalPrevious(int cursorIdx, int range)
        {
            int step = range + 1;
            int size = _windowSizePrevious;

            var removed = new List<CandleItem>(step);
            var added = new List<CandleItem>(step);

            for (int i = size - step; i < size; i++)
            {
                if (i < 0) continue;

                var slot = GetAtLogical(_windowPrevious!, _windowHeadPrevious, _windowSizePrevious, i);
                if (slot.Idx != -1)
                    removed.Add(ToItem(slot));
            }

            _windowHeadPrevious = (_windowHeadPrevious - step) % size;
            if (_windowHeadPrevious < 0)
                _windowHeadPrevious += size;

            int leftStartIdx = _currentIdxPrevious - range - step;
            for (int j = 0; j < step; j++)
            {
                int newIdx = leftStartIdx + j;
                var slot = ReadSlotOrEmpty(newIdx);

                SetAtLogical(_windowPrevious!, _windowHeadPrevious, _windowSizePrevious, j, slot);

                if (slot.Idx != -1)
                    added.Add(ToItem(slot));
            }

            _currentIdxPrevious = cursorIdx;

            var window = ToListLogical(_windowPrevious!, _windowHeadPrevious, _windowSizePrevious);

            int previousCursorIdx = GetAtLogical(_windowPrevious!, _windowHeadPrevious, _windowSizePrevious, 0).Idx == -1
                ? -1
                : cursorIdx - step;

            return new CandleCursorStepPrevious(cursorIdx, previousCursorIdx, range, window, added, removed);
        }

        public int[] BuildPreviewIndexes(int cursorIdx, int range)
        {
            if (_file == null)
                throw new InvalidOperationException("Fichier candle non chargé.");

            if (range < 0)
                throw new ArgumentOutOfRangeException(nameof(range), "range doit être >= 0.");

            if (_count <= 0 || cursorIdx < 0 || cursorIdx >= _count)
                return Array.Empty<int>();

            int[] result = new int[range * 2 + 1];
            int p = 0;

            for (int idx = cursorIdx - range; idx <= cursorIdx + range; idx++)
                result[p++] = (idx < 0 || idx >= _count) ? -1 : idx;

            return result;
        }

        private static CandleItem ToItem(CandleSlot s)
            => new CandleItem(s.Idx, s.Ts, s.O, s.H, s.L, s.C, s.V, s.Sym);

        private static int RingPhysicalIndex(int head, int size, int logicalIndex)
            => (head + logicalIndex) % size;

        private static CandleSlot GetAtLogical(CandleSlot[] window, int head, int size, int logicalIndex)
            => window[RingPhysicalIndex(head, size, logicalIndex)];

        private static void SetAtLogical(CandleSlot[] window, int head, int size, int logicalIndex, CandleSlot slot)
            => window[RingPhysicalIndex(head, size, logicalIndex)] = slot;

        private static List<CandleItem> FilterNonEmpty(List<CandleItem> items)
        {
            var list = new List<CandleItem>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Idx != -1)
                    list.Add(items[i]);
            }
            return list;
        }

        private static List<CandleItem> ToListLogical(CandleSlot[] window, int head, int size)
        {
            var list = new List<CandleItem>(size);
            for (int i = 0; i < size; i++)
                list.Add(ToItem(GetAtLogical(window, head, size, i)));
            return list;
        }

        public void Dispose()
        {
            _file?.Dispose();
            _file = null;
            _count = 0;
            _maxAllowedRange = -1;

            ResetCursor();
            ResetCursorPrevious();
        }
    }
}