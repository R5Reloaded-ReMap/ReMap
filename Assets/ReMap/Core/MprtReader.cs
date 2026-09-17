using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ReMap.Standalone.Core
{
    public sealed class MprtPlacement
    {
        public string ModelPath;
        public Float3 Position;
        // Native Apex pitch, yaw and roll, in degrees.
        public Float3 Angles;
        public float Scale;
    }

    public static class MprtReader
    {
        public const uint Magic = 0x7472706d; // "mprt"
        private const int MaximumPlacements = 2_000_000;
        private const int MaximumNameBytes = 1024;

        public static List<MprtPlacement> Read(string path)
        {
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return Read(stream);
        }

        public static List<MprtPlacement> Read(Stream stream)
        {
            if (stream == null || !stream.CanRead) throw new ArgumentException("The MPRT stream is not readable.");
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Invalid MPRT signature.");
                uint version = reader.ReadUInt32();
                if (version == 0 || version > 3) throw new InvalidDataException("Unsupported MPRT version: " + version + ".");
                int count = reader.ReadInt32();
                if (count < 0 || count > MaximumPlacements) throw new InvalidDataException("Invalid MPRT placement count.");

                var result = new List<MprtPlacement>(count);
                for (int i = 0; i < count; i++)
                {
                    string name = ReadNullTerminatedUtf8(reader);
                    var position = new Float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    // Legion/ReMap MPRT v3 stores game angles in roll, pitch, yaw order.
                    float roll = reader.ReadSingle();
                    float pitch = reader.ReadSingle();
                    float yaw = reader.ReadSingle();
                    float scale = reader.ReadSingle();
                    if (string.IsNullOrWhiteSpace(name) || !position.IsFinite || !Finite(pitch) || !Finite(yaw) ||
                        !Finite(roll) || !Finite(scale) || scale <= 0 || scale > 1000)
                        throw new InvalidDataException("Invalid MPRT placement at index " + i + ".");
                    result.Add(new MprtPlacement
                    {
                        ModelPath = NormalizeModelPath(name), Position = position,
                        Angles = new Float3(pitch, yaw, roll), Scale = scale
                    });
                }
                return result;
            }
        }

        public static void Write(string path, IReadOnlyList<MprtPlacement> placements)
        {
            using (var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, false))
            {
                writer.Write(Magic); writer.Write(3u); writer.Write(placements.Count);
                foreach (var placement in placements)
                {
                    byte[] name = Encoding.UTF8.GetBytes(NormalizeModelPath(placement.ModelPath));
                    if (name.Length == 0 || name.Length > MaximumNameBytes) throw new InvalidDataException("Invalid MPRT model name.");
                    writer.Write(name); writer.Write((byte)0);
                    writer.Write(placement.Position.x); writer.Write(placement.Position.y); writer.Write(placement.Position.z);
                    writer.Write(placement.Angles.z); writer.Write(placement.Angles.x); writer.Write(placement.Angles.y);
                    writer.Write(placement.Scale);
                }
            }
        }

        public static string NormalizeModelPath(string value)
        {
            value = (value ?? "").Trim().Replace('\\', '/');
            if (value.Length == 0) return value;
            if (!value.EndsWith(".rmdl", StringComparison.OrdinalIgnoreCase)) value += ".rmdl";
            if (!value.StartsWith("mdl/", StringComparison.OrdinalIgnoreCase) && value.IndexOf('/') >= 0) value = "mdl/" + value.TrimStart('/');
            return value;
        }

        private static string ReadNullTerminatedUtf8(BinaryReader reader)
        {
            var bytes = new List<byte>(64);
            for (int i = 0; i <= MaximumNameBytes; i++)
            {
                byte value = reader.ReadByte();
                if (value == 0) return new UTF8Encoding(false, true).GetString(bytes.ToArray());
                bytes.Add(value);
            }
            throw new InvalidDataException("MPRT model name is too long or unterminated.");
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
