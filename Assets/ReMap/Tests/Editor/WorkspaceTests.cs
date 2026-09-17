using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ReMap.Standalone.Core;

namespace ReMap.Standalone.Tests
{
    public sealed class WorkspaceTests
    {
        private string folder;
        private MapFiles files;
        [SetUp] public void Setup()
        {
            folder = Path.Combine(Path.GetTempPath(), "ReMapTests-" + Guid.NewGuid().ToString("N"));
            files = new MapFiles(folder, new UnityMapCodec());
        }
        [TearDown] public void Cleanup()
        { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        private static MapDocument Example()
        {
            var doc = new MapDocument { name = "Map française", gameTarget = GameTargets.R5Flowstate, editingMap = "mp_rr_desertlands_hu", originOffset = new Float3(4, 5, 6) };
            doc.targetMaps.Add(doc.editingMap);
            doc.objects.Add(new MapObject { assetId = "future-rsx:0x1234", displayName = "Mur renommé",
                position = new Float3(-12.5f, 6, 4), rotation = new Float3(10, 30, 90), scale = new Float3(2, 3, 4) });
            return doc;
        }
        [Test] public void RevisionTracksUndoRedoAndNeverReusesAnAbandonedBranch()
        {
            var session = new MapSession(); session.Replace(Example()); long saved = session.Revision;
            session.Edit(d => d.objects[0].position.x += 1); long edited = session.Revision;
            Assert.That(edited, Is.Not.EqualTo(saved));
            session.Undo(); Assert.That(session.Revision, Is.EqualTo(saved));
            session.Redo(); Assert.That(session.Revision, Is.EqualTo(edited));
            session.Undo(); session.Edit(d => d.objects[0].position.x += 2);
            Assert.That(session.Revision, Is.GreaterThan(edited)); long valid = session.Revision;
            Assert.Throws<ArgumentException>(() => session.Edit(d => d.objects[0].scale.x = 0));
            Assert.That(session.Revision, Is.EqualTo(valid));
        }
        [Test] public void SaveRoundTripPreservesReferencesIdsAndTransforms()
        {
            var original = Example(); files.Save("roundtrip", original); var loaded = files.Load("roundtrip");
            Assert.That(loaded.name, Is.EqualTo(original.name));
            Assert.That(loaded.gameTarget, Is.EqualTo(GameTargets.R5Flowstate));
            Assert.That(loaded.editingMap, Is.EqualTo(original.editingMap));
            Assert.That(loaded.targetMaps, Does.Contain(original.editingMap));
            Assert.That(loaded.objects[0].assetId, Is.EqualTo(original.objects[0].assetId));
            Assert.That(loaded.objects[0].id, Is.EqualTo(original.objects[0].id));
            Assert.That(loaded.objects[0].displayName, Is.EqualTo("Mur renommé"));
            Assert.That(loaded.objects[0].position.x, Is.EqualTo(-12.5f));
            Assert.That(loaded.objects[0].rotation.z, Is.EqualTo(90));
            Assert.That(loaded.objects[0].scale.y, Is.EqualTo(3));
            Assert.That(loaded.originOffset.x, Is.EqualTo(4));
            Assert.That(loaded.originOffset.y, Is.EqualTo(5));
            Assert.That(loaded.originOffset.z, Is.EqualTo(6));
        }
        [Test] public void ReplacementKeepsPreviousSaveAsBackup()
        {
            var doc = Example(); files.Save("backup", doc);
            doc.name = "Deuxième"; files.Save("backup", doc);
            Assert.That(files.Load("backup").name, Is.EqualTo("Deuxième"));
            Assert.That(new UnityMapCodec().Decode(File.ReadAllText(files.PathFor("backup") + ".bak")).name, Is.EqualTo("Map française"));
            doc.name = "Troisième"; files.Save("backup", doc);
            Assert.That(new UnityMapCodec().Decode(File.ReadAllText(files.PathFor("backup") + ".bak")).name, Is.EqualTo("Deuxième"));
        }
        [Test] public void FailedEditLeavesStateAndHistoryUnchanged()
        {
            var session = new MapSession(); session.Replace(Example()); session.Undo();
            Assert.Throws<ArgumentException>(() => session.Edit(doc => { doc.objects.Add(new MapObject { scale = new Float3(0,1,1) }); }));
            Assert.That(session.Snapshot().objects, Is.Empty); Assert.That(session.CanRedo, Is.True);
        }
        [Test] public void SnapshotsAndReplacementsDoNotAliasCallerData()
        {
            var source = Example(); var session = new MapSession(); session.Replace(source);
            source.objects[0].displayName = "changed";
            var copy = session.Snapshot(); copy.objects.Clear();
            Assert.That(session.Snapshot().objects[0].displayName, Is.EqualTo("Mur renommé"));
        }
        [Test] public void UndoRedoAndNewEditProduceExpectedBranch()
        {
            var session = new MapSession(); session.Replace(Example());
            session.Edit(doc => doc.objects[0].position.x = 8);
            session.Undo(); Assert.That(session.Snapshot().objects[0].position.x, Is.EqualTo(-12.5f));
            session.Redo(); Assert.That(session.Snapshot().objects[0].position.x, Is.EqualTo(8));
            session.Undo(); session.Edit(doc => doc.objects[0].position.x = 22);
            Assert.That(session.CanRedo, Is.False);
        }
        [Test] public void UnknownVersionAndDuplicateIdsAreRejected()
        {
            var doc = Example(); doc.schemaVersion = 100; Assert.Throws<ArgumentException>(() => doc.Validate());
            doc.schemaVersion = 1; doc.objects.Add(doc.objects[0].Copy()); Assert.Throws<ArgumentException>(() => doc.Validate());
        }
        [Test] public void InvalidNumbersAreRejected()
        {
            var doc = Example(); doc.objects[0].position.x = float.NaN; Assert.Throws<ArgumentException>(() => doc.Validate());
        }
        [TestCase("../escape")][TestCase("sub/file")][TestCase("F:\\elsewhere")][TestCase("")]
        public void SaveSlotsCannotEscapeFolder(string name)
        { Assert.Throws<ArgumentException>(() => files.PathFor(name)); }
        [Test] public void AProjectSaveCanBeRenamedWithoutLeavingTheOldSlot()
        {
            files.Save("before", Example()); files.Rename("before", "after");
            Assert.That(files.Exists("before"), Is.False); Assert.That(files.Exists("after"), Is.True);
            Assert.That(files.Load("after").name, Is.EqualTo("Map française"));
            Assert.Throws<IOException>(() => { files.Save("occupied", Example()); files.Rename("after", "occupied"); });
        }
        [Test] public void SavedProjectsCanBeListedForTheProjectMenu()
        {
            files.Save("zeta", Example()); files.Save("Alpha", Example());
            Assert.That(files.ListSlots(), Is.EqualTo(new[] { "Alpha", "zeta" }));
            Assert.That(files.Exists("Alpha"), Is.True);
        }
        [Test] public void PortableProjectRoundTripPreservesTheShareableDocument()
        {
            string exported = Path.Combine(folder, "shared" + MapFiles.PortableExtension);
            Directory.CreateDirectory(folder);
            files.ExportPortable(exported, Example());
            var imported = files.ImportPortable(exported, "received");
            Assert.That(imported.name, Is.EqualTo("Map française"));
            Assert.That(imported.gameTarget, Is.EqualTo(GameTargets.R5Flowstate));
            Assert.That(imported.objects.Single().displayName, Is.EqualTo("Mur renommé"));
            Assert.That(files.Load("received").objects.Single().assetId, Is.EqualTo("future-rsx:0x1234"));
        }
        [TestCase("map", "map.remap-project.json")]
        [TestCase("map.json", "map.json.remap-project.json")]
        [TestCase("MAP.REMAP-PROJECT.JSON", "MAP.REMAP-PROJECT.JSON")]
        public void ExportDialogAlwaysReturnsThePortableProjectExtension(string input, string expected)
        {
            Assert.That(MapFiles.EnsurePortableExtension(input), Is.EqualTo(expected));
        }
        [Test] public void InvalidPortableProjectNeverCreatesALocalSave()
        {
            Directory.CreateDirectory(folder);
            string exported = Path.Combine(folder, "invalid" + MapFiles.PortableExtension);
            File.WriteAllText(exported, "{}");
            Assert.Throws<ArgumentException>(() => files.ImportPortable(exported, "received"));
            Assert.That(files.Exists("received"), Is.False);
        }
        [Test] public void SavedProjectsAreFilteredByTargetGame()
        {
            var flowstate = Example();
            var reloaded = Example(); reloaded.gameTarget = GameTargets.R5Reloaded; reloaded.editingMap = "mp_rr_desertlands_64k_x_64k"; reloaded.targetMaps.Clear(); reloaded.targetMaps.Add(reloaded.editingMap);
            files.Save("flowstate-map", flowstate); files.Save("reloaded-map", reloaded);
            Assert.That(files.ListSlots(GameTargets.R5Reloaded), Is.EqualTo(new[] { "reloaded-map" }));
            Assert.That(files.ListSlots(GameTargets.R5Flowstate), Is.EqualTo(new[] { "flowstate-map" }));
        }
        [Test] public void InvalidEditedMapIsRejected()
        {
            var doc = Example(); doc.editingMap = new string('x', 129);
            Assert.Throws<ArgumentException>(() => doc.Validate());
        }
        [Test] public void MissingTargetMigratesToR5ReloadedButUnknownTargetIsRejected()
        {
            var doc = Example(); doc.gameTarget = ""; doc.Validate();
            Assert.That(doc.gameTarget, Is.EqualTo(GameTargets.R5Reloaded));
            doc.gameTarget = "unknown";
            Assert.Throws<ArgumentException>(() => doc.Validate());
        }
        [Test] public void IncompleteJsonCannotReplaceCurrentMap()
        {
            var session = new MapSession(); session.Replace(Example());
            Assert.Throws<ArgumentException>(() => session.Replace(new UnityMapCodec().Decode("{}")));
            Assert.That(session.Snapshot().objects.Count, Is.EqualTo(1));
        }
        [Test] public void HistoryIsBounded()
        {
            var session = new MapSession(2);
            session.Edit(d => d.name = "A"); session.Edit(d => d.name = "B"); session.Edit(d => d.name = "C");
            Assert.That(session.Undo(), Is.True); Assert.That(session.Undo(), Is.True); Assert.That(session.Undo(), Is.False);
            Assert.That(session.Snapshot().name, Is.EqualTo("A"));
        }
        [TestCase(.5f, 1f)][TestCase(-.5f, -1f)][TestCase(1.49f, 1f)]
        public void GridRoundingIsSymmetric(float value, float expected)
        { Assert.That(MapSession.Snap(value, 1), Is.EqualTo(expected)); }
    }
}
