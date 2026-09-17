using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace ReMap.Standalone.Core
{
    public static class CastAlbedo
    {
        // Exact exporter conventions only. Never select another material's PNG or a normal map.
        public static IEnumerable<string> Candidates(CastNode material,string modelName)
        {
            foreach(string role in new[]{"albedo","diffuse"})
            {
                ulong link=material.Link(role);
                var file=material.Descendants(CastReader.FileNode).FirstOrDefault(n=>link!=0&&n.Hash==link);
                if(!string.IsNullOrEmpty(file?.Text("p")))yield return file.Text("p");
            }
            string name=material.Text("n");if(string.IsNullOrEmpty(name))yield break;
            string stem=Path.GetFileNameWithoutExtension(name.Replace('\\','/'));
            foreach(string suffix in new[]{"_shadow","_prepass","_vsm","_shadow_tight","_colpass"})
                if(stem.Length>suffix.Length&&stem.EndsWith(suffix,StringComparison.Ordinal)){stem=stem.Substring(0,stem.Length-suffix.Length);break;}
            foreach(string suffix in new[]{"_albedoTexture.png","_diffuseTexture.png","_col.png"})yield return modelName+"/"+stem+suffix;
        }
        public static bool IsColorTextureName(string path)
        {
            string name=Path.GetFileName(path);
            return System.Text.RegularExpressions.Regex.IsMatch(name,@"_(col|(?:albedo[0-9]*|albedoMultiply|diffuse|baseColor|color)Texture)\.png$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        public static string ModelName(string castPath)
        {
            string name=Path.GetFileNameWithoutExtension(castPath);int lod=name.LastIndexOf("_LOD",StringComparison.Ordinal);
            return lod>=0&&int.TryParse(name.Substring(lod+4),out _)?name.Substring(0,lod):name;
        }
    }
}
