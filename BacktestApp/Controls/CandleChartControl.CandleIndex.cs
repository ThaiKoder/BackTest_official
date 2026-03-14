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

        private CandleSlot[]? _window;
        private int _windowSize;
        private int _range = -1;
        private int _currentIdx = -1;
        private bool _hasState;

        // contrainte métier : candleRange <= fileRange
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

        public long Count => _count;
        public int MaxAllowedRange => _maxAllowedRange;

        public CandleIndex()
        {
            Debug.WriteLine("CandleIndex Constructor");
        }

        public void Load(string filePath, int maxAllowedRange)
        {
            Dispose();

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Le chemin du fichier est vide.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Fichier candle introuvable.", filePath);

            if (maxAllowedRange < 0)
                throw new ArgumentOutOfRangeException(nameof(maxAllowedRange), "maxAllowedRange doit être >= 0.");

            _file = new MmapCandleFile(filePath);
            _count = _file.Count;
            _maxAllowedRange = maxAllowedRange;

            ResetCursor();
        }

        public void ResetCursor()
        {
            _window = null;
            _windowSize = 0;
            _range = -1;
            _currentIdx = -1;
            _hasState = false;
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

            if (_maxAllowedRange >= 0 && range > _maxAllowedRange)
                throw new ArgumentOutOfRangeException(
                    nameof(range),
                    $"Le range candle ({range}) doit être <= au range files ({_maxAllowedRange}).");

            if (_count <= 0)
                return new CandleCursorStep(-1, -1, range, new List<CandleItem>(), new List<CandleItem>(), new List<CandleItem>());

            if (cursorIdx < 0 || cursorIdx >= _count)
                return new CandleCursorStep(-1, -1, range, new List<CandleItem>(), new List<CandleItem>(), new List<CandleItem>());

            int step = range + 1;

            // appel après fin : no-op comme FilesNext
            if (_hasState &&
                _window != null &&
                _range == range &&
                _currentIdx == cursorIdx &&
                _window[_windowSize - 1].Idx == -1)
            {
                return new CandleCursorStep(
                    _currentIdx,
                    -1,
                    _range,
                    ToList(_window),
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
                return BuildFullWindow(cursorIdx, range);

            return AdvanceWindowIncremental(cursorIdx, range);
        }

        private CandleCursorStep BuildFullWindow(int cursorIdx, int range)
        {
            _range = range;
            _currentIdx = cursorIdx;
            _windowSize = range * 2 + 1;
            _window = new CandleSlot[_windowSize];

            int p = 0;
            for (int idx = cursorIdx - range; idx <= cursorIdx + range; idx++)
            {
                _window[p++] = ReadSlotOrEmpty(idx);
            }

            _hasState = true;

            var window = ToList(_window);
            var added = FilterNonEmpty(window);
            var removed = new List<CandleItem>();

            int nextCursorIdx = _window[_windowSize - 1].Idx == -1
                ? -1
                : cursorIdx + (range + 1);

            return new CandleCursorStep(cursorIdx, nextCursorIdx, range, window, added, removed);
        }

        private CandleCursorStep AdvanceWindowIncremental(int cursorIdx, int range)
        {
            int step = range + 1;
            int size = _windowSize;

            var removed = new List<CandleItem>(step);
            var added = new List<CandleItem>(step);

            // 1) sortants à gauche
            for (int i = 0; i < step && i < size; i++)
            {
                var slot = _window![i];
                if (slot.Idx != -1)
                    removed.Add(ToItem(slot));
            }

            // 2) décalage à gauche
            int remain = size - step;
            if (remain > 0)
                Array.Copy(_window!, step, _window!, 0, remain);

            // 3) nouveaux entrants à droite
            int rightStartIdx = _currentIdx + range + 1;
            for (int j = 0; j < step; j++)
            {
                int newIdx = rightStartIdx + j;
                var slot = ReadSlotOrEmpty(newIdx);
                _window![remain + j] = slot;

                if (slot.Idx != -1)
                    added.Add(ToItem(slot));
            }

            _currentIdx = cursorIdx;

            var window = ToList(_window!);

            int nextCursorIdx = _window![size - 1].Idx == -1
                ? -1
                : cursorIdx + step;

            return new CandleCursorStep(cursorIdx, nextCursorIdx, range, window, added, removed);
        }

        public int[] BuildPreviewIndexes(int cursorIdx, int range)
        {
            if (_file == null)
                throw new InvalidOperationException("Fichier candle non chargé.");

            if (range < 0)
                throw new ArgumentOutOfRangeException(nameof(range), "range doit être >= 0.");

            if (_maxAllowedRange >= 0 && range > _maxAllowedRange)
                throw new ArgumentOutOfRangeException(
                    nameof(range),
                    $"Le range candle ({range}) doit être <= au range files ({_maxAllowedRange}).");

            if (_count <= 0 || cursorIdx < 0 || cursorIdx >= _count)
                return Array.Empty<int>();

            int[] result = new int[range * 2 + 1];
            int p = 0;

            for (int idx = cursorIdx - range; idx <= cursorIdx + range; idx++)
            {
                result[p++] = (idx < 0 || idx >= _count) ? -1 : idx;
            }

            return result;
        }

        private static CandleItem ToItem(CandleSlot s)
            => new CandleItem(s.Idx, s.Ts, s.O, s.H, s.L, s.C, s.V, s.Sym);

        private static List<CandleItem> ToList(CandleSlot[] slots)
        {
            var list = new List<CandleItem>(slots.Length);
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                list.Add(ToItem(s));
            }
            return list;
        }

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

        public void Dispose()
        {
            _file?.Dispose();
            _file = null;
            _count = 0;
            _maxAllowedRange = -1;
            ResetCursor();
        }
    }
}