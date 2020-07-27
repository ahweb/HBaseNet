using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using HBaseNet.Const;
using HBaseNet.Logging;
using Microsoft.Extensions.Logging;
using org.apache.zookeeper;

namespace HBaseNet.Zk
{
    public class ZkHelper
    {
        private ILogger _logger;
        public ZkHelper()
        {
            _logger = HBaseConfig.Instance.LoggerFactory?.CreateLogger<ZkHelper>() ?? new DebugLogger<ZkHelper>();
        }

        public async Task<TResult> LocateResource<TResult>(ZooKeeper zk, string resource,
            Func<byte[], TResult> getResultFunc)
        {
            var result = default(TResult);
            DataResult data = null;
            try
            {
                data = await zk.getDataAsync(resource);
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message);
                return result;
            }

            if (data?.Data?.Any() != true) return result;
            var buf = data.Data;
            if (buf[0] != 0xff) return result;
            var metadataLen = BinaryPrimitives.ReadInt32BigEndian(buf[1..]);
            if (0 >= metadataLen || metadataLen >= 65001) return result;
            buf = buf[(1 + 4 + metadataLen)..];
            var magic = BinaryPrimitives.ReadInt32BigEndian(buf);
            if (ConstInt.PBufMagic != magic) return result;
            buf = buf[4..];
            result = getResultFunc(buf);
            return result;
        }

        public ZooKeeper CreateClient(string connectString, TimeSpan timeout, Watcher watcher = null,
            bool canBeReadOnly = false)
        {
            var client = new ZooKeeper(connectString, (int)timeout.TotalMilliseconds, watcher ?? new ZkLogWatcher(),
                canBeReadOnly);
            return client;
        }
    }
}