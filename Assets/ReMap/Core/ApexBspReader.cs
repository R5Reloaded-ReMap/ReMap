using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ReMap.Standalone.Core
{
    public struct BspVector2
    {
        public float x, y;
        public BspVector2(float x, float y) { this.x = x; this.y = y; }
    }

    public sealed class BspSurface
    {
        public string MaterialPath;
        public Float3[] Positions;
        public Float3[] Normals;
        public BspVector2[] TextureCoordinates;
        public int[] Indices;
    }

    public sealed class ApexBspMap
    {
        public readonly List<BspSurface> Surfaces = new List<BspSurface>();
        public readonly List<MprtPlacement> StaticProps = new List<MprtPlacement>();
        public readonly List<string> Warnings = new List<string>();
    }

    // Reads Apex render lumps directly. It does not depend on Legion/RSX conversion code.
    public static class ApexBspReader
    {
        private const int HeaderLumps = 128;
        private const int HeaderSize = 16 + HeaderLumps * 16;
        private const uint RespawnBspMagic = 0x50534272; // little-endian "rBSP"
        // Keep the guard below Int32.MaxValue: the lump buffer length is an int.
        private const int MaximumLumpBytes = 2_000_000_000;
        private const int MaximumElements = 100_000_000;
        private const int LumpTexData = 0x02, LumpVertices = 0x03, LumpModels = 0x0e;
        private const int LumpTexStrings = 0x0f, LumpSurfaceProperties = 0x11;
        private const int LumpNormals = 0x1e, LumpGame = 0x23;
        private const int LumpBvhNodes = 0x12, LumpBvhLeaves = 0x13;
        private const int LumpVertexUnlit = 0x47, LumpVertexLitFlat = 0x48, LumpVertexLitBump = 0x49;
        private const int LumpVertexUnlitTs = 0x4a, LumpFaces = 0x4f, LumpMeshes = 0x50, LumpMaterialSort = 0x52;
        private const int SprpId = 0x73707270;

        private sealed class Lump { public int Offset, Length, Version, UncompressedLength; }
        private sealed class SurfaceBuilder
        {
            public readonly string Material;
            public readonly List<Float3> Positions = new List<Float3>();
            public readonly List<Float3> Normals = new List<Float3>();
            public readonly List<BspVector2> Uv = new List<BspVector2>();
            public readonly List<int> Indices = new List<int>();
            public readonly Dictionary<long, int> VertexMap = new Dictionary<long, int>();
            public SurfaceBuilder(string material) { Material = material; }
        }

        public static ApexBspMap Read(string bspPath, bool includeStaticProps = true)
        {
            bspPath = Path.GetFullPath(bspPath);
            Lump[] lumps = ReadHeader(bspPath, out bool external);
            var result = new ApexBspMap();
            // Dedicated server VPKs can keep render-lump lengths in the BSP header while
            // omitting those external files entirely. In that case the collision BVH is
            // the authoritative geometry available in the archive.
            if (lumps[LumpFaces].Length == 0 ||
                (external && (!ExternalLumpExists(bspPath, LumpFaces) ||
                    !ExternalLumpExists(bspPath, LumpMeshes) ||
                    !ExternalLumpExists(bspPath, LumpMaterialSort))))
            {
                byte[] collisionPositions = ReadLump(bspPath, lumps[LumpVertices], LumpVertices, external, true);
                byte[] collisionNodes = ReadLump(bspPath, lumps[LumpBvhNodes], LumpBvhNodes, external, true);
                byte[] collisionLeaves = ReadLump(bspPath, lumps[LumpBvhLeaves], LumpBvhLeaves, external, true);
                byte[] collisionModels = ReadLump(bspPath, lumps[LumpModels], LumpModels, external, true);
                byte[] collisionSurfaceNames = ReadLump(bspPath, lumps[LumpTexStrings], LumpTexStrings, external, false);
                byte[] collisionSurfaceProperties = ReadLump(bspPath, lumps[LumpSurfaceProperties],
                    LumpSurfaceProperties, external, false);
                result.Surfaces.Add(ReadCollisionSurface(collisionPositions, collisionNodes, collisionLeaves,
                    collisionModels, collisionSurfaceNames, collisionSurfaceProperties, result.Warnings));
                ReadProps(bspPath, lumps, external, includeStaticProps, result);
                return result;
            }
            byte[] models = ReadLump(bspPath, lumps[LumpModels], LumpModels, external, true);
            byte[] meshes = ReadLump(bspPath, lumps[LumpMeshes], LumpMeshes, external, true);
            byte[] materials = ReadLump(bspPath, lumps[LumpMaterialSort], LumpMaterialSort, external, true);
            byte[] texData = ReadLump(bspPath, lumps[LumpTexData], LumpTexData, external, true);
            byte[] texStrings = ReadLump(bspPath, lumps[LumpTexStrings], LumpTexStrings, external, true);
            byte[] faces = ReadLump(bspPath, lumps[LumpFaces], LumpFaces, external, true);
            byte[] positions = ReadLump(bspPath, lumps[LumpVertices], LumpVertices, external, true);
            byte[] normals = ReadLump(bspPath, lumps[LumpNormals], LumpNormals, external, true);
            byte[][] vertexLumps = {
                ReadLump(bspPath, lumps[LumpVertexLitFlat], LumpVertexLitFlat, external, false),
                ReadLump(bspPath, lumps[LumpVertexLitBump], LumpVertexLitBump, external, false),
                ReadLump(bspPath, lumps[LumpVertexUnlit], LumpVertexUnlit, external, false),
                ReadLump(bspPath, lumps[LumpVertexUnlitTs], LumpVertexUnlitTs, external, false)
            };

            ValidateMultiple(models, 64, "MODELS"); ValidateMultiple(meshes, 28, "MESHES");
            ValidateMultiple(materials, 12, "MATERIAL_SORT"); ValidateMultiple(texData, 16, "TEXDATA");
            ValidateMultiple(faces, 2, "FACES"); ValidateMultiple(positions, 12, "VERTICES");
            ValidateMultiple(normals, 12, "VERTNORMALS");
            int[] vertexSizes = { 20, 32, 20, 24 };
            for (int i = 0; i < vertexLumps.Length; i++) ValidateMultiple(vertexLumps[i], vertexSizes[i], "VERTEX DATA");

            int modelCount = models.Length / 64, meshCount = meshes.Length / 28;
            var seenMeshes = new bool[meshCount];
            var builders = new Dictionary<string, SurfaceBuilder>(StringComparer.OrdinalIgnoreCase);
            for (int modelIndex = 0; modelIndex < modelCount; modelIndex++)
            {
                int modelOffset = modelIndex * 64;
                int firstMesh = I32(models, modelOffset + 24), count = I32(models, modelOffset + 28);
                if (firstMesh < 0 || count < 0 || firstMesh > meshCount - count) throw new InvalidDataException("BSP model references invalid meshes.");
                for (int meshIndex = firstMesh; meshIndex < firstMesh + count; meshIndex++)
                {
                    if (seenMeshes[meshIndex]) continue;
                    seenMeshes[meshIndex] = true;
                    ReadMesh(meshIndex, meshes, materials, texData, texStrings, faces, positions, normals,
                        vertexLumps, vertexSizes, builders, result.Warnings);
                }
            }

            foreach (var builder in builders.Values.OrderBy(value => value.Material, StringComparer.OrdinalIgnoreCase))
                if (builder.Indices.Count != 0)
                    result.Surfaces.Add(new BspSurface { MaterialPath = builder.Material, Positions = builder.Positions.ToArray(),
                        Normals = builder.Normals.ToArray(), TextureCoordinates = builder.Uv.ToArray(), Indices = builder.Indices.ToArray() });

            ReadProps(bspPath, lumps, external, includeStaticProps, result);
            return result;
        }

        private static void ReadProps(string bspPath, Lump[] lumps, bool external, bool includeStaticProps, ApexBspMap result)
        {
            if (!includeStaticProps || lumps[LumpGame].Length <= 0) return;
            byte[] game = ReadLump(bspPath, lumps[LumpGame], LumpGame, external, false);
            try { result.StaticProps.AddRange(ReadStaticProps(game)); }
            catch (InvalidDataException ex) { result.Warnings.Add("Static props: " + ex.Message); }
        }

        private static Lump[] ReadHeader(string path, out bool external)
        {
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, false))
            {
                if (stream.Length < HeaderSize || reader.ReadUInt32() != RespawnBspMagic) throw new InvalidDataException("Not an Apex rBSP file.");
                int version = reader.ReadInt32();
                if (version < 47) throw new InvalidDataException("Unsupported Apex BSP version: " + version + ".");
                reader.ReadInt32(); reader.ReadInt32();
                var result = new Lump[HeaderLumps];
                for (int i = 0; i < result.Length; i++) result[i] = new Lump { Offset = reader.ReadInt32(), Length = reader.ReadInt32(), Version = reader.ReadInt32(), UncompressedLength = reader.ReadInt32() };
                external = File.Exists(path + "." + LumpVertices.ToString("x4") + ".bsp_lump") ||
                    File.Exists(path + "." + LumpVertices.ToString("X4") + ".bsp_lump");
                return result;
            }
        }

        private static byte[] ReadLump(string bsp, Lump lump, int id, bool external, bool required)
        {
            if (lump.Length < 0 || lump.Length > MaximumLumpBytes) throw new InvalidDataException("Invalid BSP lump length.");
            if (lump.Length == 0) { if (required) throw new InvalidDataException("Required BSP lump " + id.ToString("x4") + " is empty."); return Array.Empty<byte>(); }
            string path = bsp;
            long offset = lump.Offset;
            if (external)
            {
                string lower = bsp + "." + id.ToString("x4") + ".bsp_lump";
                string upper = bsp + "." + id.ToString("X4") + ".bsp_lump";
                path = File.Exists(lower) ? lower : upper; offset = 0;
            }
            if (!File.Exists(path)) { if (!required) return Array.Empty<byte>(); throw new FileNotFoundException("Missing BSP lump " + id.ToString("x4") + ".", path); }
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (offset < 0 || offset > stream.Length - lump.Length) throw new InvalidDataException("BSP lump " + id.ToString("x4") + " lies outside its file.");
                stream.Position = offset; var data = new byte[lump.Length];
                int read = 0; while (read < data.Length) { int amount = stream.Read(data, read, data.Length - read); if (amount == 0) throw new EndOfStreamException(); read += amount; }
                return data;
            }
        }

        private static bool ExternalLumpExists(string bsp, int id)
        {
            return File.Exists(bsp + "." + id.ToString("x4") + ".bsp_lump") ||
                File.Exists(bsp + "." + id.ToString("X4") + ".bsp_lump");
        }

        private static void ReadMesh(int meshIndex, byte[] meshes, byte[] materialSort, byte[] texData, byte[] texStrings,
            byte[] faces, byte[] positions, byte[] normals, byte[][] vertexLumps, int[] vertexSizes,
            Dictionary<string, SurfaceBuilder> builders, List<string> warnings)
        {
            int o = meshIndex * 28, firstIndex = I32(meshes, o), triangleCount = I16(meshes, o + 4);
            int materialIndex = I16(meshes, o + 22), vertexType = (I32(meshes, o + 24) & 0x600) >> 9;
            if (triangleCount <= 0) return;
            if (vertexType < 0 || vertexType >= vertexLumps.Length || materialIndex < 0 || materialIndex >= materialSort.Length / 12 ||
                firstIndex < 0 || firstIndex > faces.Length / 2 - triangleCount * 3)
            { warnings.Add("Skipped malformed BSP mesh " + meshIndex + "."); return; }
            int materialOffset = materialIndex * 12, texIndex = I16(materialSort, materialOffset), firstVertex = I32(materialSort, materialOffset + 8);
            if (texIndex < 0 || texIndex >= texData.Length / 16) { warnings.Add("Skipped BSP mesh with invalid material " + meshIndex + "."); return; }
            int nameOffset = I32(texData, texIndex * 16);
            string material = ReadString(texStrings, nameOffset);
            if (material.Length == 0) material = "__missing_material_" + texIndex;
            if (IsInvisibleToolMaterial(material)) return;
            if (!builders.TryGetValue(material, out var builder)) builders.Add(material, builder = new SurfaceBuilder(material));
            byte[] complex = vertexLumps[vertexType]; int stride = vertexSizes[vertexType];
            for (int i = 0; i < triangleCount * 3; i++)
            {
                int complexIndex = U16(faces, (firstIndex + i) * 2) + firstVertex;
                if (complexIndex < 0 || complexIndex >= complex.Length / stride) throw new InvalidDataException("BSP face references an invalid vertex.");
                long key = ((long)vertexType << 32) | (uint)complexIndex;
                if (!builder.VertexMap.TryGetValue(key, out int destination))
                {
                    int vertexOffset = complexIndex * stride, positionIndex = I32(complex, vertexOffset), normalIndex = I32(complex, vertexOffset + 4);
                    if (positionIndex < 0 || positionIndex >= positions.Length / 12 || normalIndex < 0 || normalIndex >= normals.Length / 12)
                        throw new InvalidDataException("BSP complex vertex references invalid geometry.");
                    destination = builder.Positions.Count; builder.VertexMap.Add(key, destination);
                    builder.Positions.Add(V3(positions, positionIndex * 12)); builder.Normals.Add(V3(normals, normalIndex * 12));
                    builder.Uv.Add(new BspVector2(F32(complex, vertexOffset + 8), F32(complex, vertexOffset + 12)));
                }
                builder.Indices.Add(destination);
            }
        }

        public static List<MprtPlacement> ReadStaticProps(byte[] gameLump)
        {
            if (gameLump == null || gameLump.Length < 24) throw new InvalidDataException("GAME_LUMP is too small.");
            int count = I32(gameLump, 0);
            if (count < 1 || count > 1024) throw new InvalidDataException("Invalid game-lump directory.");
            int payload = -1;
            for (int i = 0; i < count; i++)
            {
                int entry = 4 + i * 20;
                if (entry > gameLump.Length - 20) throw new InvalidDataException("Truncated game-lump directory.");
                if (I32(gameLump, entry) == SprpId) payload = I32(gameLump, entry + 8);
            }
            if (payload < 0 || payload > gameLump.Length - 4) throw new InvalidDataException("Static-prop game lump not found.");
            int modelCount = I32(gameLump, payload); payload += 4;
            if (modelCount < 0 || modelCount > 1_000_000 || payload > gameLump.Length - modelCount * 128) throw new InvalidDataException("Invalid static-prop dictionary.");
            var names = new string[modelCount];
            for (int i = 0; i < modelCount; i++, payload += 128) names[i] = MprtReader.NormalizeModelPath(ReadFixedString(gameLump, payload, 128));
            if (payload > gameLump.Length - 4) throw new InvalidDataException("Missing static-prop count.");
            int propCount = I32(gameLump, payload); payload += 4;
            if (propCount < 0 || propCount > 2_000_000 || payload > gameLump.Length - (long)propCount * 64) throw new InvalidDataException("Invalid static-prop array.");
            var result = new List<MprtPlacement>(propCount);
            for (int i = 0; i < propCount; i++, payload += 64)
            {
                int model = U16(gameLump, payload + 36);
                if (model >= names.Length) continue;
                var placement = new MprtPlacement { ModelPath = names[model], Position = V3(gameLump, payload + 8),
                    Angles = V3(gameLump, payload + 20), Scale = F32(gameLump, payload + 32) };
                if (placement.Position.IsFinite && placement.Angles.IsFinite && placement.Scale > 0 && placement.Scale <= 1000) result.Add(placement);
            }
            return result;
        }

        private static BspSurface ReadCollisionSurface(byte[] vertexBytes, byte[] nodeBytes, byte[] leafBytes,
            byte[] modelBytes, byte[] surfaceNames, byte[] surfaceProperties, List<string> warnings)
        {
            ValidateMultiple(vertexBytes, 12, "VERTICES"); ValidateMultiple(nodeBytes, 64, "BVH_NODES");
            ValidateMultiple(leafBytes, 4, "BVH_LEAF_DATA");
            if (surfaceProperties.Length != 0) ValidateMultiple(surfaceProperties, 8, "SURFACE_PROPERTIES");
            if (vertexBytes.Length < 24 || nodeBytes.Length == 0 || modelBytes.Length < 24)
                throw new InvalidDataException("Incomplete BSP collision data.");

            int sourceCount = vertexBytes.Length / 12 - 1;
            var positions = new List<Float3>(sourceCount);
            for (int i = 0; i < sourceCount; i++) positions.Add(V3(vertexBytes, (i + 1) * 12));
            var indices = new List<int>(262144);
            int leafCount = leafBytes.Length / 4, nodeCount = nodeBytes.Length / 64;
            var visited = new bool[nodeCount];
            var nodes = new Stack<int>(); nodes.Push(0);
            int skippedPacked = 0, dropped = 0, filteredTools = 0;
            float minX = F32(modelBytes, 0) - 1, minY = F32(modelBytes, 4) - 1, minZ = F32(modelBytes, 8) - 1;
            float maxX = F32(modelBytes, 12) + 1, maxY = F32(modelBytes, 16) + 1, maxZ = F32(modelBytes, 20) + 1;

            bool InBounds(Float3 value) => value.x >= minX && value.x <= maxX && value.y >= minY &&
                value.y <= maxY && value.z >= minZ && value.z <= maxZ;
            int AddVertex(Float3 value) { positions.Add(value); return positions.Count - 1; }
            bool IsInvisibleToolProperty(uint header)
            {
                int property = (int)(header & 0xfff), offset = property * 8;
                if (surfaceNames.Length == 0 || offset < 0 || offset > surfaceProperties.Length - 8) return false;
                return IsInvisibleToolMaterial(ReadString(surfaceNames, I32(surfaceProperties, offset + 4)));
            }

            void EmitPoly(int offset, int type, List<int> local)
            {
                if (offset < 0 || offset >= leafCount) { dropped++; return; }
                if ((type & 1) != 0) { skippedPacked++; return; }
                bool quad = (type & 2) != 0;
                uint header = U32(leafBytes, offset * 4);
                int count = (int)((header >> 12) & 15) + 1;
                if (IsInvisibleToolProperty(header))
                {
                    filteredTools += count * (quad ? 2 : 1);
                    return;
                }
                uint running = (header >> 16) << 10;
                if (offset + 1 + count > leafCount) { dropped += count; return; }
                uint deltaMask = quad ? 0x3ffu : 0x7ffu;
                int v1Shift = quad ? 10 : 11, v2Shift = quad ? 19 : 20;
                int limit = local == null ? sourceCount : local.Count;
                for (int p = 0; p < count; p++)
                {
                    uint packed = U32(leafBytes, (offset + 1 + p) * 4);
                    running += packed & deltaMask;
                    uint a = running, b = running + ((packed >> v1Shift) & 0x1ff) + 1;
                    uint c = running + ((packed >> v2Shift) & 0x1ff) + 1;
                    if (a >= limit || b >= limit || c >= limit) { dropped++; continue; }
                    int ia = local == null ? (int)a : local[(int)a];
                    int ib = local == null ? (int)b : local[(int)b];
                    int ic = local == null ? (int)c : local[(int)c];
                    Float3 va = positions[ia], vb = positions[ib], vc = positions[ic];
                    if (!InBounds(va) || !InBounds(vb) || !InBounds(vc)) { dropped++; continue; }
                    indices.Add(ia); indices.Add(ib); indices.Add(ic);
                    if (!quad) continue;
                    var fourth = new Float3(vb.x + vc.x - va.x, vb.y + vc.y - va.y, vb.z + vc.z - va.z);
                    if (!InBounds(fourth)) { dropped++; continue; }
                    int id = AddVertex(fourth);
                    indices.Add(ic); indices.Add(ib); indices.Add(id);
                }
            }

            void EmitHull(int dwordOffset)
            {
                int byteOffset = dwordOffset * 4;
                if (dwordOffset < 0 || byteOffset > leafBytes.Length - 20) return;
                int vertexCount = leafBytes[byteOffset], planeCount = leafBytes[byteOffset + 1];
                int triangleLeaves = leafBytes[byteOffset + 2], quadLeaves = leafBytes[byteOffset + 3];
                Float3 origin = V3(leafBytes, byteOffset + 4); float scale = F32(leafBytes, byteOffset + 16);
                int vertexBase = byteOffset + 20;
                if (vertexBase > leafBytes.Length - vertexCount * 6) return;
                var local = new List<int>(vertexCount);
                for (int i = 0; i < vertexCount; i++)
                {
                    int at = vertexBase + i * 6;
                    local.Add(AddVertex(new Float3(origin.x + I16(leafBytes, at) * 65536f * scale,
                        origin.y + I16(leafBytes, at + 2) * 65536f * scale,
                        origin.z + I16(leafBytes, at + 4) * 65536f * scale)));
                }
                int cursor = dwordOffset + (20 + vertexCount * 6 + planeCount * 3 + 3) / 4;
                for (int i = 0; i < triangleLeaves + quadLeaves && cursor < leafCount; i++)
                {
                    uint header = U32(leafBytes, cursor * 4);
                    EmitPoly(cursor, i < triangleLeaves ? 4 : 6, local);
                    cursor += 1 + (int)((header >> 12) & 15) + 1;
                }
            }

            void EmitBundle(int start)
            {
                var work = new Stack<KeyValuePair<int, int>>(); work.Push(new KeyValuePair<int, int>(start, 0));
                while (work.Count > 0)
                {
                    var item = work.Pop(); int offset = item.Key, depth = item.Value;
                    if (depth > 8 || offset < 0 || offset >= leafCount) continue;
                    uint count = U32(leafBytes, offset * 4);
                    if (count == 0 || offset + 1 + count > leafCount) continue;
                    int payload = offset + 1 + (int)count;
                    for (int i = 0; i < count && payload < leafCount; i++)
                    {
                        uint descriptor = U32(leafBytes, (offset + 1 + i) * 4);
                        int type = (int)((descriptor >> 8) & 0xff), size = (int)(descriptor >> 16);
                        int current = payload; payload += size;
                        if (type >= 4 && type <= 7) EmitPoly(current, type, null);
                        else if (type == 3) work.Push(new KeyValuePair<int, int>(current, depth + 1));
                        else if (type == 8) EmitHull(current);
                    }
                }
            }

            while (nodes.Count > 0)
            {
                int node = nodes.Pop();
                if (node < 0 || node >= nodeCount || visited[node]) continue;
                visited[node] = true; int offset = node * 64;
                uint meta2 = U32(nodeBytes, offset + 56), meta3 = U32(nodeBytes, offset + 60);
                int[] types = { (int)(meta2 & 15), (int)((meta2 >> 4) & 15), (int)(meta3 & 15), (int)((meta3 >> 4) & 15) };
                for (int child = 0; child < 4; child++)
                {
                    int index = (int)(U32(nodeBytes, offset + 48 + child * 4) >> 8), type = types[child];
                    if (type == 0) { if (index != 0) nodes.Push(index); }
                    else if (type >= 4 && type <= 7) EmitPoly(index, type, null);
                    else if (type == 8) EmitHull(index);
                    else if (type == 3) EmitBundle(index);
                }
            }
            if (indices.Count == 0) throw new InvalidDataException("The BSP collision BVH produced no terrain triangles.");
            if (skippedPacked > 0) warnings.Add("Skipped " + skippedPacked + " packed BVH leaves.");
            if (dropped > 0) warnings.Add("Dropped " + dropped + " invalid BVH polygons.");
            if (filteredTools > 0) warnings.Add("Filtered " + filteredTools + " invisible BSP tool triangles.");
            return DenseCollisionSurface(positions, indices);
        }

        private static bool IsInvisibleToolMaterial(string material)
        {
            if (string.IsNullOrWhiteSpace(material)) return false;
            string normalized = material.Replace('\\', '/').TrimStart('/');
            return normalized.StartsWith("tools/", StringComparison.OrdinalIgnoreCase);
        }

        private static BspSurface DenseCollisionSurface(List<Float3> source, List<int> sourceIndices)
        {
            var map = new Dictionary<int, int>(); var positions = new List<Float3>(); var indices = new int[sourceIndices.Count];
            for (int i = 0; i < indices.Length; i++)
            {
                int old = sourceIndices[i];
                if (!map.TryGetValue(old, out int value)) { value = positions.Count; map.Add(old, value); positions.Add(source[old]); }
                indices[i] = value;
            }
            var normals = new Float3[positions.Count];
            for (int i = 0; i < indices.Length; i += 3)
            {
                Float3 a = positions[indices[i]], b = positions[indices[i + 1]], c = positions[indices[i + 2]];
                float ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z;
                float vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z;
                var n = new Float3(uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx);
                foreach (int index in new[] { indices[i], indices[i + 1], indices[i + 2] })
                    normals[index] = new Float3(normals[index].x + n.x, normals[index].y + n.y, normals[index].z + n.z);
            }
            for (int i = 0; i < normals.Length; i++)
            {
                Float3 n = normals[i]; float length = (float)Math.Sqrt(n.x * n.x + n.y * n.y + n.z * n.z);
                normals[i] = length > 1e-8f ? new Float3(n.x / length, n.y / length, n.z / length) : new Float3(0, 0, 1);
            }
            return new BspSurface { MaterialPath = "__bsp_collision", Positions = positions.ToArray(), Normals = normals,
                TextureCoordinates = new BspVector2[positions.Count], Indices = indices };
        }

        private static void ValidateMultiple(byte[] data, int stride, string name)
        {
            if (data.Length % stride != 0 || data.Length / stride > MaximumElements) throw new InvalidDataException("Invalid " + name + " lump size.");
        }
        private static string ReadString(byte[] data, int offset)
        {
            if (offset < 0 || offset >= data.Length) return "";
            int end = offset; while (end < data.Length && data[end] != 0 && end - offset < 1024) end++;
            return Encoding.UTF8.GetString(data, offset, end - offset).Replace('\\', '/');
        }
        private static string ReadFixedString(byte[] data, int offset, int length)
        {
            int end = offset; while (end < offset + length && data[end] != 0) end++;
            return Encoding.UTF8.GetString(data, offset, end - offset).Replace('\\', '/');
        }
        private static short I16(byte[] data, int offset) => BitConverter.ToInt16(data, offset);
        private static ushort U16(byte[] data, int offset) => BitConverter.ToUInt16(data, offset);
        private static uint U32(byte[] data, int offset) => BitConverter.ToUInt32(data, offset);
        private static int I32(byte[] data, int offset) => BitConverter.ToInt32(data, offset);
        private static float F32(byte[] data, int offset) => BitConverter.ToSingle(data, offset);
        private static Float3 V3(byte[] data, int offset) => new Float3(F32(data, offset), F32(data, offset + 4), F32(data, offset + 8));
    }
}
