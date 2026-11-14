using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    internal sealed class VsTraceBuffer
    {
        private const int MaximumRecordBytes = 8192;
        private readonly Queue<JObject> _records = new Queue<JObject>();
        private readonly int _capacity;
        private long _sequence;
        public string Id { get; } = Guid.NewGuid().ToString("N");

        public VsTraceBuffer(int capacity)
        {
            if (capacity < 1 || capacity > VsTraceRequest.MaximumCapacity) throw new ArgumentException("Invalid trace capacity.");
            _capacity = capacity;
        }

        public void Add(JObject record)
        {
            var copy = (JObject)record.DeepClone();
            if (Encoding.UTF8.GetByteCount(copy.ToString(Formatting.None)) > MaximumRecordBytes)
                copy = new JObject { ["kind"] = copy["kind"], ["omitted"] = "record_byte_limit" };
            copy["sequence"] = ++_sequence;
            if (_records.Count == _capacity) _records.Dequeue();
            _records.Enqueue(copy);
        }

        public JObject Read(string cursor, int pageSize)
        {
            if (pageSize < 1 || pageSize > VsTraceRequest.MaximumPageSize) throw new ArgumentException("Invalid trace page size.");
            var after = 0L;
            if (cursor != null)
            {
                if (cursor.Length > 256) throw new ArgumentException("Invalid trace cursor.");
                string decoded;
                try { decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)); }
                catch (FormatException) { throw new ArgumentException("Invalid trace cursor."); }
                var parts = decoded.Split(':');
                if (parts.Length != 2 || !long.TryParse(parts[1], out after) || after < 0)
                    throw new ArgumentException("Invalid trace cursor.");
                if (parts[0] != Id) throw new VsGraphException("stale_cursor", "Cursor belongs to another trace.");
                if (after > _sequence) throw new ArgumentException("Cursor is ahead of this trace.");
            }
            var oldest = _records.Count == 0 ? _sequence + 1 : (long)_records.Peek()["sequence"];
            var lost = Math.Max(0, oldest - after - 1);
            var next = Math.Max(after, oldest - 1);
            var records = new JArray();
            foreach (var record in _records)
            {
                if ((long)record["sequence"] <= after) continue;
                if (records.Count == pageSize) break;
                records.Add(record.DeepClone());
                next = (long)record["sequence"];
            }
            return new JObject { ["trace_id"] = Id, ["records"] = records, ["lost_records"] = lost,
                ["dropped_total"] = oldest - 1, ["has_more"] = next < _sequence,
                ["cursor"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(Id + ":" + next)) };
        }
    }
}
