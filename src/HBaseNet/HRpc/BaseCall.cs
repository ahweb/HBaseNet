using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Pb;

namespace HBaseNet.HRpc
{
    public abstract class BaseCall : ICall
    {
        public byte[] Table { get; protected set; }
        public byte[] Key { get; protected set; }
        public abstract string Name { get; }
        public byte[] RegionName { get; private set; }

        public virtual void SetRegion(byte[] region, byte[] regionStop)
        {
            RegionName = region;
        }

        public abstract byte[] Serialize();
        public abstract IMessage ResponseParseFrom(byte[] bts);

        protected RegionSpecifier GetRegionSpecifier()
        {
            return new RegionSpecifier
            {
                Type = RegionSpecifier.Types.RegionSpecifierType.RegionName,
                Value = ByteString.CopyFrom(RegionName)
            };
        }

        protected IEnumerable<Column> ConvertToColumns(IDictionary<string, string[]> families)
        {
            var columns = families
                .Select(t =>
                {
                    var col = new Column
                    {
                        Family = ByteString.CopyFromUtf8(t.Key),
                    };
                    if (t.Value != null)
                    {
                        col.Qualifier.AddRange(t.Value.Select(ByteString.CopyFromUtf8).ToList());
                    }

                    return col;
                })
                .ToArray();
            return columns;
        }
    }
}