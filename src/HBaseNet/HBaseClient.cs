using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CSharpTest.Net.Collections;
using CSharpTest.Net.IO;
using Google.Protobuf;
using HBaseNet.Const;
using HBaseNet.HRpc;
using HBaseNet.Region;
using HBaseNet.Utility;
using HBaseNet.Zk;
using Pb;
using RegionInfo = HBaseNet.Region.RegionInfo;
using RegionClient = HBaseNet.Region.RegionClient;

namespace HBaseNet
{
    public class HBaseClient
    {
        private readonly string _zkquorum;
        public static byte[] MetaTableName;
        public static Dictionary<string, string[]> InfoFamily;
        public static RegionInfo MetaRegionInfo;
        private RegionClient _metaClient;
        private ZkHelper _zkHelper;
        public ConcurrentDictionary<RegionInfo, RegionClient> RegionClientCache { get; private set; }
        public BTreeDictionary<byte[], RegionInfo> KeyRegionCache2 { get; private set; }

        public HBaseClient(string zkquorum)
        {
            _zkquorum = zkquorum;
            _zkHelper = new ZkHelper();
            InfoFamily = new Dictionary<string, string[]>
            {
                {"info", null}
            };
            MetaTableName = "hbase:meta".ToUtf8Bytes();
            MetaRegionInfo = new RegionInfo
            {
                Table = "hbase:meta".ToUtf8Bytes(),
                RegionName = "hbase:meta,,1".ToUtf8Bytes(),
                StopKey = new byte[0]
            };
            RegionClientCache = new ConcurrentDictionary<RegionInfo, RegionClient>();
            KeyRegionCache2 = new BTreeDictionary<byte[], RegionInfo>(new RegionNameComparer());
        }

        public async Task<bool> CheckTable(string table)
        {
            var get = new GetCall(table, "theKey", null);
            return null != await SendRPCToRegion<GetResponse>(get);
        }

        public async Task<GetResponse> Get(string table, string rowKey, IDictionary<string, string[]> families)
        {
            var get = new GetCall(table, rowKey, families);
            return await SendRPCToRegion<GetResponse>(get);
        }

        public async Task<MutateResponse> Mutate(string table, string rowKey,
            IDictionary<string, IDictionary<string, byte[]>> values, MutationProto.Types.MutationType mutationType)
        {
            var mutate = new MutateCall(table, rowKey, values, mutationType);
            return await SendRPCToRegion<MutateResponse>(mutate);
        }

        public async Task<MutateResponse> Put(string table, string rowKey,
            IDictionary<string, IDictionary<string, byte[]>> values)
        {
            return await Mutate(table, rowKey, values, MutationProto.Types.MutationType.Put);
        }

        public async Task<MutateResponse> Delete(string table, string rowKey,
            IDictionary<string, IDictionary<string, byte[]>> values)
        {
            return await Mutate(table, rowKey, values, MutationProto.Types.MutationType.Delete);
        }

        public async Task<MutateResponse> Append(string table, string rowKey,
            IDictionary<string, IDictionary<string, byte[]>> values)
        {
            return await Mutate(table, rowKey, values, MutationProto.Types.MutationType.Append);
        }

        public async Task<MutateResponse> Increment(string table, string rowKey,
            IDictionary<string, IDictionary<string, byte[]>> values)
        {
            return await Mutate(table, rowKey, values, MutationProto.Types.MutationType.Increment);
        }

        public async Task<IEnumerable<Result>> Scan(string table,
            IDictionary<string, string[]> families, byte[] startRow, byte[] stopRow)
        {
            var results = new List<Result>();
            ScanResponse scanres = null;
            ScanCall rpc = null;
            do
            {
                rpc = rpc == null
                    ? new ScanCall(table, families, startRow, stopRow)
                    : new ScanCall(table, families, rpc.RegionStop, stopRow);
                scanres = await SendRPCToRegion<ScanResponse>(rpc);
                if (scanres == null) return null;
                results.AddRange(scanres.Results);
            } while (true);
        }

        private async Task<TResponse> SendRPCToRegion<TResponse>(ICall rpc) where TResponse : class, IMessage
        {
            var reg = GetRegionInfo(rpc.Table, rpc.Key);
            RegionClient client = null;
            if (reg != null)
            {
                client = ClientFor(reg);
            }

            if (client == null)
            {
                var lr = await LocateRegion(rpc.Table, rpc.Key);
                if (lr != null)
                {
                    client = lr.Value.client;
                    reg = lr.Value.info;
                }
                else
                {
                    return null;
                }
            }


            rpc.SetRegion(reg.RegionName, reg.StopKey);
            return await client.SendRPC<TResponse>(rpc);
        }

        private Task QueueRPC<TResponse>(ICall rpc)
        {
            var reg = GetRegionInfo(rpc.Table, rpc.Key);


            return null;
        }

        private async Task<(RegionClient client, RegionInfo info)?> LocateRegion(byte[] table, byte[] key)
        {
            if (_metaClient == null)
            {
                await LocateMeta();
            }

            var metaKey = RegionInfo.CreateRegionSearchKey(table, key);
            var rpc = GetCall.CreateGetBefore(MetaTableName, metaKey, InfoFamily);
            rpc.SetRegion(MetaRegionInfo.RegionName, MetaRegionInfo.StopKey);
            var resp = await _metaClient.SendRPC<GetResponse>(rpc);
            var discover = await DiscoverRegion(resp);
            if (discover?.client != null)
            {
                Debug.WriteLine(
                    $"Locate region server at : {discover.Value.client.Host}:{discover.Value.client.Port}, RegionName: {discover.Value.info.RegionName.ToUtf8String()}");
            }

            return discover;
        }

        private async Task<(RegionClient client, RegionInfo info)?> DiscoverRegion(GetResponse metaRow)
        {
            if (metaRow?.HasResult != true) return null;

            var regCell = metaRow.Result.Cell
                .FirstOrDefault(t => t.Qualifier.ToStringUtf8().Equals(ConstString.RegionInfo));
            var reg = RegionInfo.ParseFromCell(regCell);
            if (reg == null) return null;
            var server = metaRow.Result.Cell
                .FirstOrDefault(t => t.Qualifier.ToStringUtf8().Equals(ConstString.Server) && t.HasValue);

            if (server == null) return null;
            var serverData = server.Value.ToArray();
            var ss = serverData.ToUtf8String();
            var idxColon = Array.IndexOf(serverData, ConstByte.Colon);
            if (idxColon < 1) return null;
            var host = serverData[..idxColon].ToUtf8String();
            if (!ushort.TryParse(serverData[(idxColon + 1)..].ToUtf8String(), out var port)) return null;
            var client = new RegionClient(host, port);
            KeyRegionCache2.TryAdd(reg.RegionName, reg);
            RegionClientCache.TryAdd(reg, client);
            return await Task.FromResult((client, reg));
        }

        private async Task LocateMeta()
        {
            var zkc = _zkHelper.CreateClient(_zkquorum, TimeSpan.FromSeconds(30));
            var meta = await _zkHelper.LocateResource(zkc, ZkHelper.HBaseMeta, MetaRegionServer.Parser.ParseFrom);
            _metaClient = new RegionClient(meta.Server.HostName, (ushort) meta.Server.Port);
            if (_metaClient != null) Debug.WriteLine($"Locate meta server at : {_metaClient.Host}:{_metaClient.Port}");
        }

        private RegionClient ClientFor(RegionInfo info)
        {
            if (info == MetaRegionInfo) return _metaClient;
            return RegionClientCache.TryGetValue(info, out var reg) ? reg : null;
        }

        private bool IsCacheKeyForTable(byte[] table, byte[] cacheKey)
        {
            for (var i = 0; i < table.Length; i++)
            {
                if (table[i] != cacheKey[i]) return false;
            }

            return cacheKey[table.Length] == ConstByte.Comma;
        }


        private RegionInfo GetRegionInfo(byte[] table, byte[] key)
        {
            if (table == MetaTableName) return MetaRegionInfo;

            var search = RegionInfo.CreateRegionSearchKey(table, key);
            var (_, info) = KeyRegionCache2.EnumerateFrom(search).FirstOrDefault();
            if (
                info == null
                || BinaryComparer.Compare(key, info.StopKey) >= 0
                || false == IsCacheKeyForTable(table, info.RegionName)
            )
                return null;
            Debug.WriteLine($"Check get region info from cache, search key:{search.ToUtf8String()},match region name:{info.RegionName.ToUtf8String()}");
            return info;
        }
    }
}