# HBaseNet

This is a pure CSharp client for HBase.

## Supported Versions

HBase >= 1.0

## Example Usage

### Create a client

```csharp
var ZkQuorum = "hbase-host-ip";
var admin = await new AdminClient(ZkQuorum).Build();
if (admin == null) return;
var client = await new StandardClient(ZkQuorum).Build();
if (client == null) return;
```

### Admin operation

```csharp
var table = "student".ToUtf8Bytes();
var cols = new[]
{
    new ColumnFamily("info")
    {
        Compression = Compression.GZ,
        KeepDeletedCells = KeepDeletedCells.TRUE
    },
    new ColumnFamily("special")
    {
        Compression = Compression.GZ,
        KeepDeletedCells = KeepDeletedCells.TTL,
        DataBlockEncoding = DataBlockEncoding.PREFIX
    }
};
var create = new CreateTableCall(table, cols)
{
    SplitKeys = new[] { "0", "5" }
};
var listTable = new ListTableNamesCall();
var disable = new DisableTableCall(table);
var delete = new DeleteTableCall(table);

var ct = await admin.CreateTable(create);

var tables = await admin.ListTableNames(listTable);

var dt = await admin.DisableTable(disable);

var del = await admin.DeleteTable(delete);
```

### Generally operation

```csharp
var table = "student";

// put
var rowKey = "123";
var values = new Dictionary<string, IDictionary<string,byte[]>>
{
    {
        "default", new Dictionary<string, byte[]>
        {
            {"key", "value".ToUtf8Bytes()}
        }
    }
};
var rs = await client.Put(new MutateCall(table, rowKey, values));

// scan
var sc = new ScanCall(Program.Table, "1".ToUtf8Bytes(), "".ToUtf8Bytes())
{
    NumberOfRows = 100000
};
using var scanner = _client.Scan(sc);
var scanResults = new List<Result>();
do
{
    var per = await scanner.Next();
    if (true != per?.Any()) break;
    scanResults.AddRange(per);
} while (scanResults.Count < 100);

// get
var getResult = await client.Get(new GetCall(table, rowKey));

// delete
var delResult = await client.Delete(new MutateCall(table, rowKey, null));

```

You can also refer to the "[Samples/HBaseNet.Console](Samples/HBaseNet.Console/Program.cs)" project.
