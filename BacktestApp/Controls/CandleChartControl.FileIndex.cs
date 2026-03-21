using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace BacktestApp.Controls;

public sealed partial class CandleChartControl
{
    public sealed class FileIndex : IDisposable
    {
        public const int IndexSize = 8;

        private MemoryMappedFile? _mmf;
        private MemoryMappedViewAccessor? _accessor;
        private long _fileSize;
        private long _count;

        private readonly byte[] _buffer = new byte[IndexSize];

        public long Count => _count;

        private struct FileSlot
        {
            public int Idx;
            public uint StartYmd;
            public uint EndYmd;

            public FileSlot(int idx, uint startYmd, uint endYmd)
            {
                Idx = idx;
                StartYmd = startYmd;
                EndYmd = endYmd;
            }
        }

        public sealed record FileItem(int Idx, uint StartYmd, uint EndYmd);

        public sealed record FileCursorStep(
            int CurrentIdx,
            int NextCursorIdx,
            int Range,
            List<FileItem> Window,
            List<FileItem> Added,
            List<FileItem> Removed);

        public sealed record FileCursorStepPrevious(
            int CurrentIdx,
            int PreviousCursorIdx,
            int Range,
            List<FileItem> Window,
            List<FileItem> Added,
            List<FileItem> Removed);

        // =========================
        // State NEXT (existant)
        // =========================
        private FileSlot[]? _window;
        private int _windowSize;
        private int _windowHead;
        private int _range = -1;
        private int _currentIdx = -1;
        private bool _hasState;

        // =========================
        // State PREVIOUS (nouveau)
        // =========================
        private FileSlot[]? _windowPrevious;
        private int _windowSizePrevious;
        private int _windowHeadPrevious;
        private int _rangePrevious = -1;
        private int _currentIdxPrevious = -1;
        private bool _hasStatePrevious;

        public FileIndex()
        {
            Debug.WriteLine("IndexReader Constructor");
        }

        public void Load(string fileNamePath)
        {
            Dispose();

            var info = new FileInfo(fileNamePath);
            _fileSize = info.Length;

            if (_fileSize % IndexSize != 0)
                throw new InvalidDataException($"Le fichier ne contient pas des records de {IndexSize} bytes.");

            _count = _fileSize / IndexSize;

            _mmf = MemoryMappedFile.CreateFromFile(
                fileNamePath,
                FileMode.Open,
                null,
                0,
                MemoryMappedFileAccess.Read);

            _accessor = _mmf.CreateViewAccessor(
                0,
                _fileSize,
                MemoryMappedFileAccess.Read);

            ResetCursor();
            ResetCursorPrevious();
        }

        public (uint StartYmd, uint EndYmd) Read(long index)
        {
            if (_accessor == null)
                throw new InvalidOperationException("Index non chargé.");

            if (index < 0 || index >= _count)
                throw new ArgumentOutOfRangeException(nameof(index));

            long offset = index * IndexSize;

            uint startYmd = _accessor.ReadUInt32(offset);
            uint endYmd = _accessor.ReadUInt32(offset + 4);

            return (startYmd, endYmd);
        }

        private FileSlot ReadSlotOrEmpty(int idx)
        {
            if (idx < 0 || idx >= _count)
                return new FileSlot(-1, 0, 0);

            long offset = (long)idx * IndexSize;
            uint startYmd = _accessor!.ReadUInt32(offset);
            uint endYmd = _accessor.ReadUInt32(offset + 4);

            return new FileSlot(idx, startYmd, endYmd);
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

        public FileCursorStep FilesNext(int cursorIdx, int range)
        {
            if (_accessor == null)
                throw new InvalidOperationException("Index non chargé.");

            if (range < 0)
                throw new ArgumentOutOfRangeException(nameof(range), "range doit être >= 0.");

            if (_count <= 0)
                return new FileCursorStep(-1, -1, range, new List<FileItem>(), new List<FileItem>(), new List<FileItem>());

            if (cursorIdx < 0 || cursorIdx >= _count)
                return new FileCursorStep(-1, -1, range, new List<FileItem>(), new List<FileItem>(), new List<FileItem>());

            int step = range + 1;

            if (_hasState &&
                _window != null &&
                _range == range &&
                _currentIdx == cursorIdx &&
                GetAtLogical(_window!, _windowHead, _windowSize, _windowSize - 1).Idx == -1)
            {
                return new FileCursorStep(
                    _currentIdx,
                    -1,
                    _range,
                    ToListLogical(_window!, _windowHead, _windowSize),
                    new List<FileItem>(),
                    new List<FileItem>());
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

        public FileCursorStepPrevious FilesPrevious(int cursorIdx, int range)
        {
            if (_accessor == null)
                throw new InvalidOperationException("Index non chargé.");

            if (range < 0)
                throw new ArgumentOutOfRangeException(nameof(range), "range doit être >= 0.");

            if (_count <= 0)
                return new FileCursorStepPrevious(-1, -1, range, new List<FileItem>(), new List<FileItem>(), new List<FileItem>());

            if (cursorIdx < 0 || cursorIdx >= _count)
                return new FileCursorStepPrevious(-1, -1, range, new List<FileItem>(), new List<FileItem>(), new List<FileItem>());

            int step = range + 1;

            if (_hasStatePrevious &&
                _windowPrevious != null &&
                _rangePrevious == range &&
                _currentIdxPrevious == cursorIdx &&
                GetAtLogical(_windowPrevious, _windowHeadPrevious, _windowSizePrevious, 0).Idx == -1)
            {
                return new FileCursorStepPrevious(
                    _currentIdxPrevious,
                    -1,
                    _rangePrevious,
                    ToListLogical(_windowPrevious, _windowHeadPrevious, _windowSizePrevious),
                    new List<FileItem>(),
                    new List<FileItem>());
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

        private FileCursorStep BuildFullWindowNext(int cursorIdx, int range)
        {
            _range = range;
            _currentIdx = cursorIdx;
            _windowSize = range * 2 + 1;
            _windowHead = 0;
            _window = new FileSlot[_windowSize];

            int p = 0;
            for (int idx = cursorIdx - range; idx <= cursorIdx + range; idx++)
                _window[p++] = ReadSlotOrEmpty(idx);

            _hasState = true;

            var window = ToListLogical(_window, _windowHead, _windowSize);
            var added = FilterNonEmpty(window);
            var removed = new List<FileItem>();

            int nextCursorIdx = GetAtLogical(_window, _windowHead, _windowSize, _windowSize - 1).Idx == -1
                ? -1
                : cursorIdx + (range + 1);

            return new FileCursorStep(cursorIdx, nextCursorIdx, range, window, added, removed);
        }

        private FileCursorStepPrevious BuildFullWindowPrevious(int cursorIdx, int range)
        {
            _rangePrevious = range;
            _currentIdxPrevious = cursorIdx;
            _windowSizePrevious = range * 2 + 1;
            _windowHeadPrevious = 0;
            _windowPrevious = new FileSlot[_windowSizePrevious];

            int p = 0;
            for (int idx = cursorIdx - range; idx <= cursorIdx + range; idx++)
                _windowPrevious[p++] = ReadSlotOrEmpty(idx);

            _hasStatePrevious = true;

            var window = ToListLogical(_windowPrevious, _windowHeadPrevious, _windowSizePrevious);
            var added = FilterNonEmpty(window);
            var removed = new List<FileItem>();

            int previousCursorIdx = GetAtLogical(_windowPrevious, _windowHeadPrevious, _windowSizePrevious, 0).Idx == -1
                ? -1
                : cursorIdx - (range + 1);

            return new FileCursorStepPrevious(cursorIdx, previousCursorIdx, range, window, added, removed);
        }

        private FileCursorStep AdvanceWindowIncrementalNext(int cursorIdx, int range)
        {
            int step = range + 1;
            int size = _windowSize;

            var removed = new List<FileItem>(step);
            var added = new List<FileItem>(step);

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

            return new FileCursorStep(cursorIdx, nextCursorIdx, range, window, added, removed);
        }

        private FileCursorStepPrevious AdvanceWindowIncrementalPrevious(int cursorIdx, int range)
        {
            int step = range + 1;
            int size = _windowSizePrevious;

            var removed = new List<FileItem>(step);
            var added = new List<FileItem>(step);

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

            return new FileCursorStepPrevious(cursorIdx, previousCursorIdx, range, window, added, removed);
        }

        private static List<FileItem> FilterNonEmpty(List<FileItem> items)
        {
            var list = new List<FileItem>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Idx != -1)
                    list.Add(items[i]);
            }
            return list;
        }

        public int[] BuildPreviewIndexes(int cursorIdx, int range)
        {
            if (_accessor == null)
                throw new InvalidOperationException("Index non chargé.");

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

        private static int RingPhysicalIndex(int head, int size, int logicalIndex)
            => (head + logicalIndex) % size;

        private static FileSlot GetAtLogical(FileSlot[] window, int head, int size, int logicalIndex)
            => window[RingPhysicalIndex(head, size, logicalIndex)];

        private static void SetAtLogical(FileSlot[] window, int head, int size, int logicalIndex, FileSlot slot)
            => window[RingPhysicalIndex(head, size, logicalIndex)] = slot;

        private static FileItem ToItem(FileSlot s)
            => new FileItem(s.Idx, s.StartYmd, s.EndYmd);

        private static List<FileItem> ToListLogical(FileSlot[] window, int head, int size)
        {
            var list = new List<FileItem>(size);
            for (int i = 0; i < size; i++)
                list.Add(ToItem(GetAtLogical(window, head, size, i)));
            return list;
        }

        public void Dispose()
        {
            _accessor?.Dispose();
            _mmf?.Dispose();
            _accessor = null;
            _mmf = null;
            _fileSize = 0;
            _count = 0;

            ResetCursor();
            ResetCursorPrevious();
        }
    }
}