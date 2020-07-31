﻿using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using HBaseNet.HRpc;
using HBaseNet.HRpc.Descriptors;
using HBaseNet.Utility;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace HBaseNet.Console
{
    using System;

    class Program
    {
        private const string ZkQuorum = "hbase-docker";
        public const string Table = "student";
        public static Dictionary<string, string[]> Family;
        public static Dictionary<string, IDictionary<string, byte[]>> Values;
        public static string GenerateRandomKey()
        {
            return Guid.NewGuid().ToString();
        }
        static async Task Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Debug()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            HBaseConfig.Instance.ServiceProvider = new ServiceCollection()
                .AddLogging(cfg => cfg.AddSerilog(Log.Logger))
                .BuildServiceProvider();

            Family = new Dictionary<string, string[]>
            {
                {"default", new[] {"key"}}
            };
            Values = new Dictionary<string, IDictionary<string, byte[]>>
            {
                {
                    "default", new Dictionary<string, byte[]>
                    {
                        {"key", "value".ToUtf8Bytes()}
                    }
                }
            };
            var client = await new StandardClient(ZkQuorum).Build();
            if (client == null) return;
            var admin = await new AdminClient(ZkQuorum).Build();
            if (admin == null) return;
            var ado = new AdminClientOperation(admin);
            await ado.ExecAll();


            var sth = new Stopwatch();
            var sto = new SingleThreadOperation(client);

            var create = new CreateTableCall(Table.ToUtf8Bytes(), new[] { new ColumnFamily("default"), })
            {
                SplitKeys = Enumerable.Range('1', 9).Concat(Enumerable.Range('a', 6)).Select(t => $"{(char)t}").ToArray()
            };

            var tables = await ado._admin.ListTableNames(new ListTableNamesCall { Regex = Table });
            if (true != tables?.Any())
            {
                await admin.CreateTable(create);
            }

            await sto.ExecCheckAndPut();

            const int putCount = 10000;

            var mto = new MultiThreadOperation(client);
            sth.Restart();
            await mto.ExecPut(putCount);
            Log.Logger.Information($"exec multi thread put ,count: {putCount},take :{sth.Elapsed}");

            sth.Restart();
            await sto.ExecPut(putCount / 100);
            Log.Logger.Information($"exec single thread put ,count: {putCount / 100},take :{sth.Elapsed}");

            await sto.ExecAppend();
            await sto.ExecIncrement();

            sth.Restart();
            await sto.ExecScan();
            Log.Logger.Information($"exec scan,take :{sth.Elapsed}");
            await sto.ExecScanAndDelete();

            Console.WriteLine($"Do you want to delete table {Table}?(y)");
            if (Console.ReadKey().Key == ConsoleKey.Y)
            {
                await admin.DisableTable(new DisableTableCall(Table.ToUtf8Bytes()));
                var dt = await admin.DeleteTable(new DeleteTableCall(Table.ToUtf8Bytes()));
                Log.Logger.Information($"del table:{Table},result:{dt}");
            }
        }
    }
}