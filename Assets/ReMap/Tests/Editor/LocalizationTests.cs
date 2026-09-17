using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ReMap.Standalone.Core;
using UnityEngine;
namespace ReMap.Standalone.Tests
{
    public sealed class LocalizationTests
    {
        private static string ReadText(string code) => new UTF8Encoding(false,true).GetString(
            File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath,"Localization",code+".json")));
        private static Dictionary<string,string> Read(string code) => AppLocalization.ReadCatalog(JsonUtility.FromJson<AppLocalization.Catalog>(
            ReadText(code)));
        [TearDown] public void Reset() => L.Configure("en");
        [Test] public void EnglishIsDefaultAndUnknownTagsRemainReadable()
        {
            L.Configure("en"); Assert.That(L.T("#SAVE"),Is.EqualTo("Save"));
            Assert.That(L.T("#NEW_FEATURE"),Is.EqualTo("New feature"));
        }
        [TestCase("en")][TestCase("fr")]
        public void CatalogFilesUseStrictUtf8WithoutReplacementCharacters(string code)
        {
            Assert.That(ReadText(code).IndexOf('\uFFFD'),Is.EqualTo(-1));
        }
        [Test] public void PartialAndEmptyTranslationsFallBackToEnglish()
        {
            L.Configure("fr",new Dictionary<string,string>{{"#SAVE","Save map"}},new Dictionary<string,string>{{"#SAVE",""},{"#OPEN","Ouvrir"}});
            Assert.That(L.T("#SAVE"),Is.EqualTo("Save map")); Assert.That(L.T("#OPEN"),Is.EqualTo("Ouvrir"));
            Assert.That(L.T("#UNKNOWN"),Is.EqualTo("Unknown"));
        }
        [Test] public void InvalidFormatInTranslationDoesNotBreakOperation()
        {
            L.Configure("fr",new Dictionary<string,string>{{"#SAVED_FILE","Saved: {0}"}},new Dictionary<string,string>{{"#SAVED_FILE","Enregistré : {9}"}});
            Assert.That(L.F("#SAVED_FILE","map.json"),Is.EqualTo("Saved: map.json"));
        }
        [Test] public void CatalogsHaveMatchingKeysAndFormatArguments()
        {
            var en=Read("en");var fr=Read("fr");Assert.That(en.Count,Is.GreaterThan(300));
            CollectionAssert.AreEquivalent(en.Keys,fr.Keys);
            foreach(var key in en.Keys)
            {
                Assert.That(fr[key],Is.Not.Null.And.Not.Empty,key);
                Assert.That(L.IsTag(key), Is.True, key);
                Assert.That(key.Length, Is.LessThanOrEqualTo(64), key);
                var source=Regex.Matches(en[key],@"\{(\d+)(?:[^}]*)\}").Cast<Match>().Select(m=>m.Groups[1].Value).OrderBy(v=>v).ToArray();
                var target=Regex.Matches(fr[key],@"\{(\d+)(?:[^}]*)\}").Cast<Match>().Select(m=>m.Groups[1].Value).OrderBy(v=>v).ToArray();
                CollectionAssert.AreEqual(source,target,key);
                if(source.Length>0)Assert.DoesNotThrow(()=>string.Format(fr[key],Enumerable.Repeat<object>(1,source.Select(int.Parse).Max()+1).ToArray()),key);
            }
            Assert.That(en["#CABLE_WIDTH"], Is.EqualTo("Cable width (u)"));
        }
        [Test] public void DuplicateKeysAreRejected()
        {
            var catalog=new AppLocalization.Catalog{language="fr",name="Français",entries=new[]{new AppLocalization.Entry{key="#SAVE",text="A"},new AppLocalization.Entry{key="#SAVE",text="B"}}};
            Assert.Throws<FormatException>(()=>AppLocalization.ReadCatalog(catalog));
        }
        [Test] public void CatalogRejectsEnglishSentencesAsKeys()
        {
            var catalog=new AppLocalization.Catalog{language="fr",name="Français",entries=new[]{new AppLocalization.Entry{key="Save",text="Enregistrer"}}};
            Assert.Throws<FormatException>(()=>AppLocalization.ReadCatalog(catalog));
        }
        [Test] public void LanguageDoesNotChangeExistingMapData()
        {
            var doc=new MapDocument{name="Mon atelier 日本語"};doc.objects.Add(new MapObject{displayName="Mur personnalisé",assetId="apex:0123456789abcdef"});
            var codec=new UnityMapCodec();string before=codec.Encode(doc);
            L.Configure("fr",Read("en"),Read("fr"));
            Assert.That(L.T("#SAVE"),Is.EqualTo("Enregistrer"));
            Assert.That(codec.Encode(codec.Decode(before)),Is.EqualTo(before));
        }
        [Test] public void BackgroundReadersUseAnIndependentCatalogSnapshot()
        {
            var translations=new Dictionary<string,string>{{"#SAVE","Enregistrer"}};L.Configure("fr",null,translations);
            translations["#SAVE"]="Wrong";Assert.That(L.T("#SAVE"),Is.EqualTo("Enregistrer"));
        }
    }
}
