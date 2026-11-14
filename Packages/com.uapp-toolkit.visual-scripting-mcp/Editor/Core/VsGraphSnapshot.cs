using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    internal sealed class VsGraphSnapshot
    {
        public const int MaximumRecords = 20000;
        private const int PageByteBudget = 240 * 1024;
        private const int MaximumWarnings = 32;
        private readonly JArray _records = new JArray();
        private readonly JArray _warnings = new JArray();
        public System.Collections.Generic.IEnumerable<JToken> Records => _records;
        public System.Collections.Generic.IEnumerable<JToken> Warnings => _warnings;
        public bool IsFull => _records.Count >= MaximumRecords;

        public void Warn(string warning)
        {
            foreach (var value in _warnings)
                if ((string)value == warning) return;
            if (_warnings.Count < MaximumWarnings) _warnings.Add(warning);
        }

        public void Add(JObject record)
        {
            if (IsFull) { Warn("record_limit_reached"); return; }
            if (Encoding.UTF8.GetByteCount(record.ToString(Formatting.None)) > PageByteBudget)
            {
                Warn("oversized_record_summarized");
                record = new JObject { ["kind"] = record["kind"]?.DeepClone(), ["guid"] = record["guid"]?.DeepClone(),
                    ["target"] = record["target"]?.DeepClone(), ["path"] = record["path"]?.DeepClone(), ["omitted"] = true };
            }
            _records.Add(record);
        }

        public JObject Page(VsGraphRequest request, string operation)
        {
            var query = new JObject { ["operation"] = operation, ["scope"] = request.Scope,
                ["folder"] = request.Folder, ["target"] = request.Target, ["path"] = request.ParentGuids,
                ["max_depth"] = request.MaxDepth, ["page_size"] = request.PageSize };
            var revision = Hash(query.ToString(Formatting.None) + _records.ToString(Formatting.None) + _warnings.ToString(Formatting.None));
            var offset = 0;
            if (request.Cursor != null)
            {
                string decoded;
                try { decoded = Encoding.UTF8.GetString(Convert.FromBase64String(request.Cursor)); }
                catch (FormatException) { throw new ArgumentException("Invalid cursor encoding."); }
                var parts = decoded.Split(':');
                if (parts.Length != 2 || !int.TryParse(parts[1], out offset) || offset < 0)
                    throw new ArgumentException("Invalid cursor.");
                if (parts[0] != revision)
                    throw new VsGraphException("stale_cursor", "Snapshot or query changed; restart from the first page.");
                if (offset >= _records.Count)
                    throw new ArgumentException("Cursor is outside the snapshot.");
            }

            var items = new JArray();
            var bytes = 0;
            var next = offset;
            while (next < _records.Count && items.Count < request.PageSize)
            {
                var size = Encoding.UTF8.GetByteCount(_records[next].ToString(Formatting.None)) + 1;
                if (items.Count > 0 && bytes + size > PageByteBudget) break;
                items.Add(_records[next].DeepClone());
                bytes += size;
                next++;
            }
            var response = VsResponse.Success(new JObject { ["revision"] = revision,
                ["total_records"] = _records.Count, ["records"] = items });
            var data = (JObject)response["data"];
            var more = next < _records.Count;
            data["complete"] = !more && _warnings.Count == 0;
            data["warnings"] = _warnings.DeepClone();
            if (more)
            {
                ((JArray)data["warnings"]).Add("more_pages_available");
                data["next_cursor"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(revision + ":" + next));
            }
            return response;
        }

        private static string Hash(string value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty);
        }
    }
}
