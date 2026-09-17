using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ReMap.Standalone.Core
{
    // Static mesh subset of the public CAST v1 specification. No RSX code is embedded here.
    public sealed class CastNode
    {
        public uint Type;
        public ulong Hash;
        public readonly Dictionary<string, object> Properties = new Dictionary<string, object>();
        public readonly List<CastNode> Children = new List<CastNode>();
        public string Text(string key) => Properties.TryGetValue(key, out var v) ? v as string : null;
        public float[] Floats(string key) => Properties.TryGetValue(key, out var v) ? v as float[] : null;
        public ulong[] Integers(string key) => Properties.TryGetValue(key, out var v) ? v as ulong[] : null;
        public ulong Link(string key) { var values = Integers(key); return values != null && values.Length > 0 ? values[0] : 0; }
        public IEnumerable<CastNode> Descendants(uint type)
        {
            if (Type == type) yield return this;
            foreach (var child in Children) foreach (var node in child.Descendants(type)) yield return node;
        }
    }

    public static class CastReader
    {
        public const uint Model = 0x6c646f6d, Mesh = 0x6873656d, Material = 0x6c74616d, FileNode = 0x656c6966;
        public static List<CastNode> Read(string path)
        {
            using (var stream = System.IO.File.OpenRead(path)) return Read(stream);
        }
        public static List<CastNode> Read(Stream stream)
        {
            if (!stream.CanSeek || stream.Length > 128 * 1024 * 1024 || stream.Length < 16)
                throw new InvalidDataException(L.T("#UNSUPPORTED_CAST_FILE_SIZE"));
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                if (reader.ReadUInt32() != 0x74736163 || reader.ReadUInt32() != 1)
                    throw new InvalidDataException(L.T("#EXPECTED_CAST_V1_FORMAT"));
                uint roots = reader.ReadUInt32(); reader.ReadUInt32();
                if (roots > 64) throw new InvalidDataException(L.T("#TOO_MANY_CAST_ROOTS"));
                var nodes = new List<CastNode>();
                for (int i = 0; i < roots; i++) nodes.Add(ReadNode(reader, stream.Length, 0));
                if (stream.Position != stream.Length) throw new InvalidDataException(L.T("#TRAILING_CAST_DATA"));
                return nodes;
            }
        }
        private static CastNode ReadNode(BinaryReader reader, long parentEnd, int depth)
        {
            var stream = reader.BaseStream;
            long start = stream.Position;
            Need(stream, 24, parentEnd);
            var node = new CastNode { Type = reader.ReadUInt32() };
            uint length = reader.ReadUInt32(); node.Hash = reader.ReadUInt64();
            uint propertyCount = reader.ReadUInt32(), childCount = reader.ReadUInt32();
            long end = start + length;
            if (length < 24 || end > parentEnd || depth > 32 || propertyCount > 4096 || childCount > 10000)
                throw new InvalidDataException(L.T("#INVALID_CAST_STRUCTURE"));
            for (int i = 0; i < propertyCount; i++)
            {
                Need(stream, 8, end);
                string type = Encoding.ASCII.GetString(reader.ReadBytes(2)).TrimEnd('\0');
                int nameLength = reader.ReadUInt16(); uint count = reader.ReadUInt32();
                if (nameLength > 1024 || count > 4000000) throw new InvalidDataException(L.T("#CAST_PROPERTY_TOO_LARGE"));
                Need(stream, nameLength, end);
                string name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
                if (type == "s")
                {
                    if (count != 1) throw new InvalidDataException(L.T("#INVALID_CAST_STRING"));
                    var bytes = new List<byte>(); byte value;
                    do { Need(stream, 1, end); value = reader.ReadByte(); if (value != 0) bytes.Add(value);
                        if (bytes.Count > 65536) throw new InvalidDataException(L.T("#CAST_STRING_TOO_LONG")); } while (value != 0);
                    node.Properties[name] = Encoding.UTF8.GetString(bytes.ToArray());
                    continue;
                }
                int width = type == "b" ? 1 : type == "h" ? 2 : type == "i" || type == "f" ? 4 :
                    type == "l" || type == "d" || type == "2v" ? 8 : type == "3v" ? 12 : type == "4v" ? 16 : 0;
                if (width == 0) throw new InvalidDataException(L.T("#UNKNOWN_CAST_TYPE") + type);
                Need(stream, (long)width * count, end);
                // Skin weights, animations and extra UV/color channels are outside the static-prop prototype.
                bool keep = name == "vp" || name == "vn" || name == "u0" || name == "f" || name == "m" ||
                    name == "albedo" || name == "diffuse" || name == "rgba";
                if (!keep) { stream.Position += (long)width * count; continue; }
                if (type == "f" || type.EndsWith("v", StringComparison.Ordinal))
                {
                    var values = new float[checked((int)count * width / 4)];
                    for (int j = 0; j < values.Length; j++) { values[j] = reader.ReadSingle();
                        if (float.IsNaN(values[j]) || float.IsInfinity(values[j])) throw new InvalidDataException(L.T("#NON_FINITE_CAST_VALUE")); }
                    node.Properties[name] = values;
                }
                else if (type == "d") { stream.Position += (long)width * count; }
                else
                {
                    var values = new ulong[count];
                    for (int j = 0; j < values.Length; j++) values[j] = width == 1 ? reader.ReadByte() : width == 2 ? reader.ReadUInt16() : width == 4 ? reader.ReadUInt32() : reader.ReadUInt64();
                    node.Properties[name] = values;
                }
            }
            for (int i = 0; i < childCount; i++) node.Children.Add(ReadNode(reader, end, depth + 1));
            if (stream.Position != end) throw new InvalidDataException(L.T("#INCONSISTENT_CAST_NODE_LENGTH"));
            return node;
        }
        private static void Need(Stream stream, long bytes, long end)
        { if (bytes < 0 || stream.Position + bytes > end) throw new InvalidDataException(L.T("#TRUNCATED_CAST_FILE")); }
    }
}
