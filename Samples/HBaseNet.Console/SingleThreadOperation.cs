using System;
using System.ComponentModel.Design;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HBaseNet.HRpc;
using HBaseNet.Utility;
using Pb;
using Serilog;

namespace HBaseNet.Console
{
    public class SingleThreadOperation
    {
        private readonly HBaseClient _client;

        public SingleThreadOperation(HBaseClient client)
        {
            _client = client;
        }

        public async Task<bool> CheckTable()
        {
            var result = await _client.CheckTable(Program.Table);
            Log.Logger.Information($"check table '{Program.Table}': {result}");
            return result;
        }

        public async Task ExecPut(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var rowKey = new string(DateTime.Now.Ticks.ToString().Reverse().ToArray());
                var rs = await _client.SendRPC<MutateResponse>(new MutateCall(Program.Table, rowKey, Program.Values,
                    MutationProto.Types.MutationType.Put));
            }
        }

        public async Task ExecScan()
        {
            var scanResults = await _client.Scan(
                new ScanCall(Program.Table, Program.Family, "1".ToUtf8Bytes(), "5".ToUtf8Bytes())
                { Families = Program.Family });
            Log.Information($"scan result count:{scanResults.Count}");
        }

        public async Task ExecScanAndDelete()
        {
            var scanResults =
                await _client.Scan(new ScanCall(Program.Table, Program.Family, "0".ToUtf8Bytes(), "1".ToUtf8Bytes()));
            Log.Information($"scan result count:{scanResults.Count}");
            foreach (var result in scanResults)
            {
                var rowKey = result.Cell.Select(t => t.Row.ToStringUtf8()).Single();
                var delResult = await _client.SendRPC<MutateResponse>(new MutateCall(Program.Table, rowKey, null,
                    MutationProto.Types.MutationType.Delete));
                Log.Logger.Information($"delete row at key: {rowKey}, processed:{delResult.Processed}");
            }
        }

        public async Task ExecCheckAndPut()
        {
            var rowKey = new string(DateTime.Now.Ticks.ToString().Reverse().ToArray());
            var put = new MutateCall(Program.Table, rowKey, Program.Values,
                MutationProto.Types.MutationType.Put);

            var rs = await _client.SendRPC<MutateResponse>(put);
            if (rs?.Processed == true)
            {
                var resultF = await _client.CheckAndPut(put, "default", "key", "ex".ToUtf8Bytes(), new CancellationToken());
            }
            put.Key = new string(DateTime.Now.Ticks.ToString().Reverse().ToArray()).ToUtf8Bytes();
            var resultT = await _client.CheckAndPut(put, "default", "key", "ex".ToUtf8Bytes(), new CancellationToken());
        }
    }
}