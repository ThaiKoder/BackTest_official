using DatasetTool;
using System;
using System.Collections.Generic;
using System.IO;

namespace BacktestApp.Controls;

public sealed partial class CandleChartControl
{
    private FileIndex? _uiFileIndex;
    private CandleIndex? _uiCandleIndex;
    private FileIndex.FileCursorStep? _uiFileStep;
    private CandleIndex.CandleCursorStep? _uiCandleStep;

    private FileIndex.FileCursorStepPrevious? _uiFileStepPrevious;
    private CandleIndex.CandleCursorStepPrevious? _uiCandleStepPrevious;

    private readonly List<int> _uiPendingFileIdxs = new();
    private int _uiPendingFilePos = -1;

    private readonly List<int> _uiPendingPreviousFileIdxs = new();
    private int _uiPendingPreviousFilePos = -1;

    private int _uiCurrentFileIdx = -1;

    private void ResetNextNavigationState()
    {
        _uiFileStep = null;
        _uiCandleStep = null;
        _uiPendingFileIdxs.Clear();
        _uiPendingFilePos = -1;
    }

    private void ResetPreviousNavigationState()
    {
        _uiFileStepPrevious = null;
        _uiCandleStepPrevious = null;
        _uiPendingPreviousFileIdxs.Clear();
        _uiPendingPreviousFilePos = -1;
    }

    private int GetCurrentCursorForNext()
    {
        if (_uiCandleStepPrevious is not null)
            return _uiCandleStepPrevious.CurrentIdx;

        if (_uiCandleStep is not null)
            return _uiCandleStep.CurrentIdx;

        return (int)Math.Max(0, _windowStart);
    }

    private int GetCurrentCursorForPrevious()
    {
        if (_uiCandleStep is not null)
            return _uiCandleStep.CurrentIdx;

        if (_uiCandleStepPrevious is not null)
            return _uiCandleStepPrevious.CurrentIdx;

        return (int)Math.Max(0, _windowStart);
    }

    private void RebuildNextNavigationStateFromCurrentPosition()
    {
        if (_uiFileIndex is null || _uiCandleIndex is null || _uiCurrentFileIdx < 0)
            return;

        _uiFileStep = _uiFileIndex.FilesNext(_uiCurrentFileIdx, UiFileRange);
        _uiPendingFileIdxs.Clear();

        foreach (var file in _uiFileStep.Window)
        {
            if (file.Idx == -1)
                continue;

            if (file.Idx < _uiCurrentFileIdx)
                continue;

            _uiPendingFileIdxs.Add(file.Idx);
        }

        _uiPendingFilePos = _uiPendingFileIdxs.IndexOf(_uiCurrentFileIdx);
        if (_uiPendingFilePos < 0 && _uiPendingFileIdxs.Count > 0)
            _uiPendingFilePos = 0;

        int currentCursor = GetCurrentCursorForNext();
        _uiCandleStep = _uiCandleIndex.CandlesNext(currentCursor, UiCandleRange);
    }

    private void RebuildPreviousNavigationStateFromCurrentPosition()
    {
        if (_uiFileIndex is null || _uiCandleIndex is null || _uiCurrentFileIdx < 0)
            return;

        _uiFileStepPrevious = _uiFileIndex.FilesPrevious(_uiCurrentFileIdx, UiFileRange);

        if (_uiFileStepPrevious.CurrentIdx < 0)
            return;

        SetPendingPreviousFilesFromStep(_uiFileStepPrevious, useWindow: true, currentFileIdx: _uiCurrentFileIdx);

        int currentCursor = GetCurrentCursorForPrevious();
        _uiCandleStepPrevious = _uiCandleIndex.CandlesPrevious(currentCursor, UiCandleRange);
    }


    private int RingPhysicalIndex(int logicalIndex)
    {
        return (_ringHead + logicalIndex) % WindowCount;
    }

    private void RingClear()
    {
        _ringHead = 0;
        _ringCount = 0;
        _ringFirstGlobalIdx = 0;
    }

    private void RingPushBack(long ts, long o, long h, long l, long c, uint v, byte sym)
    {
        if (_ringCount < WindowCount)
        {
            int p = (_ringHead + _ringCount) % WindowCount;
            _ts[p] = ts;
            _o[p] = o;
            _h[p] = h;
            _l[p] = l;
            _c[p] = c;
            _v[p] = v;
            _sym[p] = sym;
            _ringCount++;
            return;
        }

        _ts[_ringHead] = ts;
        _o[_ringHead] = o;
        _h[_ringHead] = h;
        _l[_ringHead] = l;
        _c[_ringHead] = c;
        _v[_ringHead] = v;
        _sym[_ringHead] = sym;

        _ringHead = (_ringHead + 1) % WindowCount;
        _ringFirstGlobalIdx++;
    }

    private void RingPushFront(long ts, long o, long h, long l, long c, uint v, byte sym)
    {
        if (_ringCount < WindowCount)
        {
            _ringHead = (_ringHead - 1 + WindowCount) % WindowCount;

            _ts[_ringHead] = ts;
            _o[_ringHead] = o;
            _h[_ringHead] = h;
            _l[_ringHead] = l;
            _c[_ringHead] = c;
            _v[_ringHead] = v;
            _sym[_ringHead] = sym;

            _ringCount++;
            _ringFirstGlobalIdx--;
            if (_ringFirstGlobalIdx < 0)
                _ringFirstGlobalIdx = 0;

            return;
        }

        int tail = (_ringHead + _ringCount - 1) % WindowCount;

        _ringHead = (_ringHead - 1 + WindowCount) % WindowCount;

        _ts[_ringHead] = ts;
        _o[_ringHead] = o;
        _h[_ringHead] = h;
        _l[_ringHead] = l;
        _c[_ringHead] = c;
        _v[_ringHead] = v;
        _sym[_ringHead] = sym;

        _ringFirstGlobalIdx--;
        if (_ringFirstGlobalIdx < 0)
            _ringFirstGlobalIdx = 0;
    }

    private void RingPopFront(int count)
    {
        if (count <= 0 || _ringCount <= 0)
            return;

        if (count > _ringCount)
            count = _ringCount;

        _ringHead = (_ringHead + count) % WindowCount;
        _ringCount -= count;
        _ringFirstGlobalIdx += count;

        if (_ringCount == 0)
            _ringHead = 0;
    }

    private void RingPopBack(int count)
    {
        if (count <= 0 || _ringCount <= 0)
            return;

        if (count > _ringCount)
            count = _ringCount;

        _ringCount -= count;

        if (_ringCount == 0)
        {
            _ringHead = 0;
            _ringFirstGlobalIdx = 0;
        }
    }

    private long RingTsAtLogical(int logicalIndex) => _ts[RingPhysicalIndex(logicalIndex)];
    private long RingOAtLogical(int logicalIndex) => _o[RingPhysicalIndex(logicalIndex)];
    private long RingHAtLogical(int logicalIndex) => _h[RingPhysicalIndex(logicalIndex)];
    private long RingLAtLogical(int logicalIndex) => _l[RingPhysicalIndex(logicalIndex)];
    private long RingCAtLogical(int logicalIndex) => _c[RingPhysicalIndex(logicalIndex)];
    private uint RingVAtLogical(int logicalIndex) => _v[RingPhysicalIndex(logicalIndex)];
    private byte RingSymAtLogical(int logicalIndex) => _sym[RingPhysicalIndex(logicalIndex)];

    private void InitializeFilesAndCandlesMode()
    {
        if (_uiFileIndex != null)
            return;

        _uiFileIndex = new FileIndex();
        _uiFileIndex.Load(Path.Combine("data", "bin", "_index.bin"));

        _uiFileStep = _uiFileIndex.FilesNext(0, UiFileRange);
        if (_uiFileStep.CurrentIdx < 0)
            return;

        SetPendingFilesFromStep(_uiFileStep, useWindow: true);

        if (_uiPendingFileIdxs.Count == 0)
            return;

        LoadCandlesForCurrentFileStep();
    }

    private void LoadCandlesForCurrentFileStep()
    {
        if (_uiFileIndex == null || _uiFileStep == null)
            return;

        if (_uiPendingFilePos < 0 || _uiPendingFilePos >= _uiPendingFileIdxs.Count)
            return;

        int fileIdx = _uiPendingFileIdxs[_uiPendingFilePos];
        LoadCandlesForFile(fileIdx, resetView: true);

        _uiCandleStep = _uiCandleIndex!.CandlesNext(0, UiCandleRange);
        ApplyCandleStepToWindow(_uiCandleStep, resetView: true);

        _uiCurrentFileIdx = fileIdx;
    }

    private void LoadCandlesForCurrentPreviousFileStep()
    {
        if (_uiFileIndex == null || _uiFileStepPrevious == null)
            return;

        if (_uiPendingPreviousFilePos < 0 || _uiPendingPreviousFilePos >= _uiPendingPreviousFileIdxs.Count)
            return;

        int fileIdx = _uiPendingPreviousFileIdxs[_uiPendingPreviousFilePos];
        LoadCandlesForFile(fileIdx, resetView: true);

        int startCursor = Math.Max(0, (int)(_uiCandleIndex!.Count - 1));
        _uiCandleStepPrevious = _uiCandleIndex.CandlesPrevious(startCursor, UiCandleRange);
        ApplyPreviousCandleStepToWindow(_uiCandleStepPrevious, resetView: true);

        _uiCurrentFileIdx = fileIdx;
    }

    private void LoadCandlesForFile(int fileIdx, bool resetView)
    {
        if (_uiFileIndex == null)
            return;

        var (startYmd, endYmd) = _uiFileIndex.Read(fileIdx);

        string filePath = Path.Combine(
            "data",
            "bin",
            $"glbx-mdp3-{startYmd}-{endYmd}.ohlcv-1m.bin");

        _uiCandleIndex?.Dispose();
        _uiCandleIndex = new CandleIndex();
        _uiCandleIndex.Load(filePath);

        if (resetView)
        {
            _xInited = false;
            _yInited = false;
        }
    }

    private void RebuildIndicatorsFromRing()
    {
        ResetIndicators();

        for (int i = 0; i < _ringCount; i++)
        {
            FeedIndicators(
                RingTsAtLogical(i),
                RingOAtLogical(i),
                RingHAtLogical(i),
                RingLAtLogical(i),
                RingCAtLogical(i),
                RingVAtLogical(i),
                RingSymAtLogical(i));
        }
    }

    private void ApplyCandleStepToWindow(CandleIndex.CandleCursorStep step, bool resetView)
    {
        if (resetView || _ringCount == 0)
        {
            RingClear();

            int firstValidIdx = -1;

            for (int i = 0; i < step.Window.Count; i++)
            {
                var candle = step.Window[i];
                if (candle.Idx == -1)
                    continue;

                if (firstValidIdx == -1)
                    firstValidIdx = candle.Idx;

                RingPushBack(
                    candle.Ts,
                    candle.O,
                    candle.H,
                    candle.L,
                    candle.C,
                    candle.V,
                    candle.Sym);
            }

            _ringFirstGlobalIdx = Math.Max(0, firstValidIdx);
            RebuildIndicatorsFromRing();
        }
        else
        {
            RingPopFront(step.Removed.Count);

            for (int i = 0; i < step.Added.Count; i++)
            {
                var candle = step.Added[i];
                if (candle.Idx == -1)
                    continue;

                RingPushBack(
                    candle.Ts,
                    candle.O,
                    candle.H,
                    candle.L,
                    candle.C,
                    candle.V,
                    candle.Sym);
            }

            RebuildIndicatorsFromRing();
        }

        _windowLoaded = _ringCount;
        _windowStart = _ringFirstGlobalIdx;
        _fileCount = _uiCandleIndex?.Count ?? 0;

        if (resetView)
        {
            _xInited = false;
            _yInited = false;
        }

        InvalidateVisual();
    }

    private void ApplyPreviousCandleStepToWindow(CandleIndex.CandleCursorStepPrevious step, bool resetView)
    {
        if (resetView || _ringCount == 0)
        {
            RingClear();

            int firstValidIdx = -1;

            for (int i = 0; i < step.Window.Count; i++)
            {
                var candle = step.Window[i];
                if (candle.Idx == -1)
                    continue;

                if (firstValidIdx == -1)
                    firstValidIdx = candle.Idx;

                RingPushBack(
                    candle.Ts,
                    candle.O,
                    candle.H,
                    candle.L,
                    candle.C,
                    candle.V,
                    candle.Sym);
            }

            _ringFirstGlobalIdx = Math.Max(0, firstValidIdx);
            RebuildIndicatorsFromRing();
        }
        else
        {
            RingPopBack(step.Removed.Count);

            for (int i = step.Added.Count - 1; i >= 0; i--)
            {
                var candle = step.Added[i];
                if (candle.Idx == -1)
                    continue;

                RingPushFront(
                    candle.Ts,
                    candle.O,
                    candle.H,
                    candle.L,
                    candle.C,
                    candle.V,
                    candle.Sym);
            }

            RebuildIndicatorsFromRing();
        }

        _windowLoaded = _ringCount;
        _windowStart = _ringFirstGlobalIdx;
        _fileCount = _uiCandleIndex?.Count ?? 0;

        if (resetView)
        {
            _xInited = false;
            _yInited = false;
        }

        InvalidateVisual();
    }

    private bool AdvanceCandlesNext()
    {
        if (_uiFileIndex == null)
            return false;

        if (_uiCurrentFileIdx < 0)
            return false;

        if (_uiFileStep == null || _uiCandleStep == null)
        {
            if (_uiCandleIndex == null)
                return false;

            RebuildNextNavigationStateFromCurrentPosition();

            if (_uiFileStep == null || _uiCandleStep == null)
                return false;
        }

        if (_uiCandleStep.NextCursorIdx != -1)
        {
            _uiCandleStep = _uiCandleIndex!.CandlesNext(_uiCandleStep.NextCursorIdx, UiCandleRange);
            ApplyCandleStepToWindow(_uiCandleStep, resetView: false);
            ResetPreviousNavigationState();
            return true;
        }

        if (MoveToNextPendingFileInCurrentStep())
        {
            LoadCandlesForCurrentFileStep();
            ResetPreviousNavigationState();
            return _uiCandleStep != null && _windowLoaded > 0;
        }

        if (_uiFileStep.NextCursorIdx == -1)
            return false;

        _uiFileStep = _uiFileIndex.FilesNext(_uiFileStep.NextCursorIdx, UiFileRange);

        if (_uiFileStep.CurrentIdx < 0)
            return false;

        SetPendingFilesFromStep(_uiFileStep, useWindow: false);

        if (_uiPendingFileIdxs.Count == 0)
            return false;

        LoadCandlesForCurrentFileStep();
        ResetPreviousNavigationState();
        return _uiCandleStep != null && _windowLoaded > 0;
    }

    private bool AdvanceCandlesPrevious()
    {
        if (_uiFileIndex == null)
            return false;

        if (_uiCurrentFileIdx < 0)
            return false;

        if (_uiFileStepPrevious == null || _uiCandleStepPrevious == null)
        {
            if (_uiCandleIndex == null)
                return false;

            RebuildPreviousNavigationStateFromCurrentPosition();

            if (_uiFileStepPrevious == null || _uiCandleStepPrevious == null)
                return false;
        }

        if (_uiCandleIndex != null && _uiCandleStepPrevious.PreviousCursorIdx != -1)
        {
            _uiCandleStepPrevious = _uiCandleIndex.CandlesPrevious(_uiCandleStepPrevious.PreviousCursorIdx, UiCandleRange);
            ApplyPreviousCandleStepToWindow(_uiCandleStepPrevious, resetView: false);
            ResetNextNavigationState();
            return true;
        }

        if (MoveToPreviousPendingFileInCurrentStep())
        {
            LoadCandlesForCurrentPreviousFileStep();
            ResetNextNavigationState();
            return _uiCandleStepPrevious != null && _windowLoaded > 0;
        }

        if (_uiFileStepPrevious == null || _uiFileStepPrevious.PreviousCursorIdx == -1)
            return false;

        _uiFileStepPrevious = _uiFileIndex.FilesPrevious(_uiFileStepPrevious.PreviousCursorIdx, UiFileRange);
        if (_uiFileStepPrevious.CurrentIdx < 0)
            return false;

        SetPendingPreviousFilesFromStep(_uiFileStepPrevious, useWindow: false, currentFileIdx: _uiCurrentFileIdx);

        if (_uiPendingPreviousFileIdxs.Count == 0)
            return false;

        LoadCandlesForCurrentPreviousFileStep();
        ResetNextNavigationState();
        return _uiCandleStepPrevious != null && _windowLoaded > 0;
    }

    private void SetPendingFilesFromStep(FileIndex.FileCursorStep step, bool useWindow)
    {
        _uiPendingFileIdxs.Clear();

        var source = useWindow ? step.Window : step.Added;

        foreach (var file in source)
        {
            if (file.Idx == -1)
                continue;

            _uiPendingFileIdxs.Add(file.Idx);
        }

        _uiPendingFilePos = 0;
    }

    private void SetPendingPreviousFilesFromStep(
        FileIndex.FileCursorStepPrevious step,
        bool useWindow,
        int currentFileIdx)
    {
        _uiPendingPreviousFileIdxs.Clear();

        var source = useWindow ? step.Window : step.Added;

        foreach (var file in source)
        {
            if (file.Idx == -1)
                continue;

            if (file.Idx >= currentFileIdx)
                continue;

            _uiPendingPreviousFileIdxs.Add(file.Idx);
        }

        _uiPendingPreviousFileIdxs.Sort();
        _uiPendingPreviousFileIdxs.Reverse();

        _uiPendingPreviousFilePos = 0;
    }

    private bool MoveToNextPendingFileInCurrentStep()
    {
        if (_uiPendingFileIdxs.Count == 0)
            return false;

        int nextPos = _uiPendingFilePos + 1;
        if (nextPos >= _uiPendingFileIdxs.Count)
            return false;

        _uiPendingFilePos = nextPos;
        return true;
    }

    private bool MoveToPreviousPendingFileInCurrentStep()
    {
        if (_uiPendingPreviousFileIdxs.Count == 0)
            return false;

        int nextPos = _uiPendingPreviousFilePos + 1;
        if (nextPos >= _uiPendingPreviousFileIdxs.Count)
            return false;

        _uiPendingPreviousFilePos = nextPos;
        return true;
    }
}