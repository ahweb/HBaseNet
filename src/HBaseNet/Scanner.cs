using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HBaseNet.HRpc;
using System.Threading;
using Pb;
using RegionInfo = HBaseNet.Region.RegionInfo;
using CSharpTest.Net.IO;
using Microsoft.Extensions.Logging;
using HBaseNet.Logging;
using HBaseNet.Utility;

namespace HBaseNet
{
    public class Scanner : IScanner
    {
        private ScanCall _rpc;
        private ulong? _scannerID;
        private byte[] _startRow;
        public CancellationToken CancellationToken { get; set; }
        public bool CanContinueNext { get; private set; } = true;
        private List<Result> _results;
        private bool _closed;
        private readonly IStandardClient _client;
        protected readonly ILogger<Scanner> _logger;
        private static byte[] _rowPadding = new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff };
        public Scanner(IStandardClient client, ScanCall rpc)
        {
            _logger = HBaseConfig.Instance.LoggerFactory?.CreateLogger<Scanner>() ?? new DebugLogger<Scanner>();
            _client = client;
            _rpc = rpc;
            _startRow = _rpc.StartRow;
        }
        public async Task<List<Result>> Next()
        {
            if (_rpc.AllowPartialResults)
            {
                var one = await Peek();
                Shift();
                if (one == null)
                {
                    return null;
                }
                return new List<Result> { one };
            }
            var result = await Fetch();
            return result;
        }
        private async Task<(ScanResponse, RegionInfo)> Request()
        {
            ScanCall rpc;
            if (IsRegionScannerClosed())
            {
                rpc = new ScanCall(_rpc.Table, _startRow, _rpc.StopRow)
                {
                    Families = _rpc.Families,
                    Filters = _rpc.Filters,
                    TimeRange = _rpc.TimeRange,
                    MaxVersions = _rpc.MaxVersions,
                    NumberOfRows = _rpc.NumberOfRows,
                    Reversed = _rpc.Reversed
                };
            }
            else
            {
                rpc = new ScanCall(_rpc.Table, _scannerID, _startRow, false) { NumberOfRows = _rpc.NumberOfRows };
            }
            var result = await _client.SendRPCToRegion<ScanResponse>(rpc, CancellationToken);
            return (result, rpc.Info);
        }

        private async Task Update(ScanResponse resp, RegionInfo region)
        {
            if (IsRegionScannerClosed() && resp.ScannerId != 0)
            {
                OpenRegionScanner(resp.ScannerId);
            }

            if (true != resp?.MoreResultsInRegion)
            {
                await CloseRegionScanner();

                if (false == _rpc.Reversed)
                {
                    _startRow = region.StopKey;
                    return;
                }

                if (true != region.StartKey?.Any())
                {
                    _startRow = region.StartKey;
                    return;
                }
                _startRow = GetReversedStartKey(region.StartKey);
            }
        }
        public static byte[] GetReversedStartKey(byte[] startKey)
        {
            if (true != startKey?.Any())
            {
                return null;
            }
            var rsk = new byte[startKey.Length];
            Array.Copy(startKey, rsk, startKey.Length);
            if (rsk.Last() == 0)
            {
                return rsk[..(rsk.Length - 1)];
            }

            var tmp = BinaryEx.ConcatInOrder(rsk, _rowPadding);
            tmp[rsk.Length - 1]--;
            return tmp;
        }
        private async Task<List<Result>> Fetch()
        {
            _results = new List<Result>();
            while (false == CancellationToken.IsCancellationRequested && false == _closed)
            {
                var (resp, region) = await Request();
                if (null == resp || null == region)
                {
                    await Close();
                    return _results;
                }
                _results.AddRange(resp.Results);
                await Update(resp, region);
                if (IsDone(resp, region))
                {
                    await Close();
                }
                return _results;
            }
            return _results;
        }
        private async Task<Result> Peek()
        {
            if (true != _results?.Any())
            {
                if (_closed)
                {
                    return null;
                }
                _results = await Fetch();
            }
            return _results.FirstOrDefault();
        }
        private void Shift()
        {
            if (true != _results?.Any())
            {
                return;
            }
            _results.RemoveAt(0);
        }
        private bool IsDone(ScanResponse resp, RegionInfo region)
        {
            if (false == resp.MoreResults)
            {
                return true;
            }
            if (false == IsRegionScannerClosed())
            {
                return false;
            }

            if (true != region.StopKey?.Any() && false == _rpc.Reversed)
            {
                return true;
            }

            if (_rpc.Reversed && true != region.StartKey?.Any())
            {
                return true;
            }

            if (false == _rpc.Reversed)
            {
                return true == _rpc.StopRow?.Any() && BinaryComparer.Compare(_rpc.StopRow, region.StopKey) <= 0;
            }

            return true == _rpc.StopRow?.Any() && BinaryComparer.Compare(_rpc.StopRow, region.StartKey) >= 0;
        }
        private bool IsRegionScannerClosed()
        {
            return _scannerID == null;
        }
        private void OpenRegionScanner(ulong scannerId)
        {
            if (false == IsRegionScannerClosed())
            {
                throw new Exception("should not happen: previous region scanner was not closed");
            }
            _scannerID = scannerId;
        }
        private async Task CloseRegionScanner()
        {
            if (IsRegionScannerClosed())
            {
                return;
            }
            if (_rpc.CloseScanner)
            {
                var rpc = new ScanCall(_rpc.Table, _startRow, null)
                {
                    ScannerID = _scannerID,
                    NumberOfRows = 0
                };
                _logger.LogDebug($"Scanner close region:{_rpc.Info}");
                await _client.SendRPCToRegion<ScanResponse>(rpc, CancellationToken);
            }
            _scannerID = null;
        }

        public void Dispose()
        {
            CanContinueNext = false;
        }

        public async Task Close()
        {
            CanContinueNext = false;
            if (_closed)
            {
                _logger.LogWarning($"Scanner has already been closed. region:{_rpc.Info}");
                return;
            }
            _closed = true;
            await CloseRegionScanner();
        }
    }
}