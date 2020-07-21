using System.Buffers.Binary;
using System;
using System.Linq;
using System.Text;

namespace HBaseNet.Utility
{
    public static class BinaryEx
    {
        public static byte[] Initialize(this byte[] arr, byte value)
        {
            for (var i = 0; i < arr.Length; i++)
            {
                arr[i] = value;
            }

            return arr;
        }

        public static byte[] ConcatInOrder(params byte[][] item)
        {
            var len = item.Sum(t => t.Length);
            var result = new byte[len];
            var pos = 0;
            foreach (var arr in item)
            {
                arr.CopyTo(result, pos);
                pos += arr.Length;
            }

            return result;
        }

        public static byte[] ToUtf8Bytes(this string str)
        {
            return Encoding.UTF8.GetBytes(str);
        }

        public static string ToUtf8String(this byte[] arr)
        {
            return Encoding.UTF8.GetString(arr);
        }
    }
}