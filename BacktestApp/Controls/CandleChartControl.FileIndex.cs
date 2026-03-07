using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading.Tasks;



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

        // Buffer réutilisé pour éviter les allocations
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

        private FileSlot[]? _window;
        private int _windowSize;
        private int _range = -1;
        private int _currentIdx = -1;
        private bool _hasState;


        //Constructor
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
            _range = -1;
            _currentIdx = -1;
            _hasState = false;
        }

        /// <summary>
        /// Version incrémentale rapide.
        /// - Premier appel: construit la fenêtre centrée sur cursorIdx
        /// - Appels suivants:
        ///   - si même range et cursorIdx == _currentIdx + (range + 1)
        ///     alors mise à jour incrémentale (remove + add)
        ///   - sinon reconstruction complète
        /// </summary>
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

        private FileCursorStep BuildFullWindow(int cursorIdx, int range)
        {
            _range = range;
            _currentIdx = cursorIdx;
            _windowSize = range * 2 + 1;
            _window = new FileSlot[_windowSize];

            int p = 0;
            for (int idx = cursorIdx - range; idx <= cursorIdx + range; idx++)
            {
                _window[p++] = ReadSlotOrEmpty(idx);
            }

            _hasState = true;

            var window = ToList(_window);
            var added = FilterNonEmpty(window);
            var removed = new List<FileItem>();

            int nextCursorIdx = cursorIdx + (range + 1);
            if (nextCursorIdx >= _count)
                nextCursorIdx = -1;

            return new FileCursorStep(cursorIdx, nextCursorIdx, range, window, added, removed);
        }

        private FileCursorStep AdvanceWindowIncremental(int cursorIdx, int range)
        {
            int step = range + 1;
            int size = _windowSize;
            var removed = new List<FileItem>(step);
            var added = new List<FileItem>(step);

            // 1) Capturer les sortants (à gauche)
            for (int i = 0; i < step && i < size; i++)
            {
                var slot = _window![i];
                if (slot.Idx != -1)
                    removed.Add(new FileItem(slot.Idx, slot.StartYmd, slot.EndYmd));
            }

            // 2) Décaler à gauche
            int remain = size - step;
            if (remain > 0)
            {
                Array.Copy(_window!, step, _window!, 0, remain);
            }

            // 3) Calculer et ajouter les nouveaux entrants à droite
            int rightStartIdx = _currentIdx + range + 1;
            for (int j = 0; j < step; j++)
            {
                int newIdx = rightStartIdx + j;
                var slot = ReadSlotOrEmpty(newIdx);
                _window![remain + j] = slot;

                if (slot.Idx != -1)
                    added.Add(new FileItem(slot.Idx, slot.StartYmd, slot.EndYmd));
            }

            _currentIdx = cursorIdx;

            var window = ToList(_window!);

            int nextCursorIdx = cursorIdx + step;
            if (nextCursorIdx >= _count)
                nextCursorIdx = -1;

            return new FileCursorStep(cursorIdx, nextCursorIdx, range, window, added, removed);
        }

        private static List<FileItem> ToList(FileSlot[] slots)
        {
            var list = new List<FileItem>(slots.Length);
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                list.Add(new FileItem(s.Idx, s.StartYmd, s.EndYmd));
            }
            return list;
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
            {
                result[p++] = (idx < 0 || idx >= _count) ? -1 : idx;
            }

            return result;
        }



        public void Dispose()
        {
            _accessor?.Dispose();
            _mmf?.Dispose();
            _accessor = null;
            _mmf = null;
            _fileSize = 0;
            _count = 0;
        }
    }

}
