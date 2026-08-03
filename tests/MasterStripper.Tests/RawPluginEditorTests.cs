using MasterStripper;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace MasterStripper.Tests;

public sealed class RawPluginEditorTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "MasterStripperTests", Guid.NewGuid().ToString("N"));

    public RawPluginEditorTests() => Directory.CreateDirectory(_folder);

    [Fact]
    public void RemovesUnresolvableOverrideOwnedBySelectedMaster()
    {
        var masterKey = ModKey.FromFileName("MissingBase.esp");
        var patchKey = ModKey.FromFileName("Patch.esp");
        var masterPath = Path.Combine(_folder, masterKey.FileName.String);
        var patchPath = Path.Combine(_folder, patchKey.FileName.String);

        var master = new SkyrimMod(masterKey, SkyrimRelease.SkyrimSE);
        var npc = master.Npcs.AddNew();
        npc.EditorID = "RawFallbackNpc";
        master.BeginWrite.ToPath(masterPath).WithNoLoadOrder().Write();

        var patch = new SkyrimMod(patchKey, SkyrimRelease.SkyrimSE);
        patch.Npcs.GetOrAddAsOverride(npc).EditorID = "PatchedNpc";
        patch.BeginWrite.ToPath(patchPath).WithLoadOrder(master).Write();

        master.Npcs.Remove(npc.FormKey);
        master.BeginWrite.ToPath(masterPath).WithNoLoadOrder().Write();

        var removed = RawPluginEditor.RemoveRecordsOwnedByMaster(patchPath, masterKey, _folder);

        Assert.Equal(1, removed);
        var result = SkyrimMod.Create(SkyrimRelease.SkyrimSE)
            .FromPath(patchPath)
            .WithLoadOrder(new[] { masterKey })
            .WithDataFolder(_folder)
            .Mutable()
            .Construct();
        Assert.Empty(result.EnumerateMajorRecords());
    }

    [Fact]
    public void RemovesExactUnresolvableRecordFromAnotherMaster()
    {
        var missingKey = ModKey.FromFileName("MissingBase.esp");
        var selectedKey = ModKey.FromFileName("SelectedMaster.esp");
        var patchKey = ModKey.FromFileName("Patch.esp");
        var missingPath = Path.Combine(_folder, missingKey.FileName.String);
        var selectedPath = Path.Combine(_folder, selectedKey.FileName.String);
        var patchPath = Path.Combine(_folder, patchKey.FileName.String);

        var missing = new SkyrimMod(missingKey, SkyrimRelease.SkyrimSE);
        var npc = missing.Npcs.AddNew();
        missing.BeginWrite.ToPath(missingPath).WithNoLoadOrder().Write();
        var selected = new SkyrimMod(selectedKey, SkyrimRelease.SkyrimSE);
        selected.BeginWrite.ToPath(selectedPath).WithNoLoadOrder().Write();

        var patch = new SkyrimMod(patchKey, SkyrimRelease.SkyrimSE);
        patch.Npcs.GetOrAddAsOverride(npc).EditorID = "StaleNpc";
        patch.BeginWrite.ToPath(patchPath).WithLoadOrder(missing, selected).Write();

        missing.Npcs.Remove(npc.FormKey);
        missing.BeginWrite.ToPath(missingPath).WithNoLoadOrder().Write();

        var removed = RawPluginEditor.RemoveRecordOrContainers(patchPath, npc.FormKey, _folder);

        Assert.Equal(1, removed);
        var result = SkyrimMod.Create(SkyrimRelease.SkyrimSE)
            .FromPath(patchPath)
            .WithLoadOrder(new[] { missingKey, selectedKey })
            .WithDataFolder(_folder)
            .Mutable()
            .Construct();
        Assert.Empty(result.EnumerateMajorRecords());
    }

    [Fact]
    public void RemovesCompressedContainerHoldingMissingReference()
    {
        var missingKey = ModKey.FromFileName("MissingBase.esp");
        var patchKey = ModKey.FromFileName("Patch.esp");
        var missingPath = Path.Combine(_folder, missingKey.FileName.String);
        var patchPath = Path.Combine(_folder, patchKey.FileName.String);

        var missing = new SkyrimMod(missingKey, SkyrimRelease.SkyrimSE);
        var npc = missing.Npcs.AddNew();
        missing.BeginWrite.ToPath(missingPath).WithNoLoadOrder().Write();

        var patch = new SkyrimMod(patchKey, SkyrimRelease.SkyrimSE);
        var list = patch.FormLists.AddNew();
        list.EditorID = "CompressedStaleReference";
        list.IsCompressed = true;
        list.Items.Add(npc.FormKey);
        patch.BeginWrite.ToPath(patchPath).WithLoadOrder(missing).Write();

        missing.Npcs.Remove(npc.FormKey);
        missing.BeginWrite.ToPath(missingPath).WithNoLoadOrder().Write();

        var removed = RawPluginEditor.RemoveRecordOrContainers(patchPath, npc.FormKey, _folder);

        Assert.Equal(1, removed);
        var result = SkyrimMod.Create(SkyrimRelease.SkyrimSE)
            .FromPath(patchPath)
            .WithLoadOrder(new[] { missingKey })
            .WithDataFolder(_folder)
            .Mutable()
            .Construct();
        Assert.Empty(result.EnumerateMajorRecords());
    }

    [Fact]
    public void FullMasterIndexIncludesPrecedingLightMaster()
    {
        var lightKey = ModKey.FromFileName("LightBefore.esp");
        var targetKey = ModKey.FromFileName("NormalTarget.esp");
        var patchKey = ModKey.FromFileName("Patch.esp");
        var lightPath = Path.Combine(_folder, lightKey.FileName.String);
        var targetPath = Path.Combine(_folder, targetKey.FileName.String);
        var patchPath = Path.Combine(_folder, patchKey.FileName.String);

        var light = new SkyrimMod(lightKey, SkyrimRelease.SkyrimSE) { IsSmallMaster = true };
        var keyword = light.Keywords.AddNew();
        light.BeginWrite.ToPath(lightPath).WithNoLoadOrder().Write();

        var target = new SkyrimMod(targetKey, SkyrimRelease.SkyrimSE);
        var npc = target.Npcs.AddNew();
        target.BeginWrite.ToPath(targetPath).WithNoLoadOrder().Write();

        var patch = new SkyrimMod(patchKey, SkyrimRelease.SkyrimSE);
        patch.Npcs.GetOrAddAsOverride(npc).EditorID = "RemoveMe";
        var list = patch.FormLists.AddNew();
        list.Items.Add(keyword.FormKey);
        patch.BeginWrite.ToPath(patchPath).WithLoadOrder(light, target).Write();

        var removed = RawPluginEditor.RemoveRecordsOwnedByMaster(patchPath, targetKey, _folder);

        Assert.Equal(1, removed);
        var result = SkyrimMod.Create(SkyrimRelease.SkyrimSE)
            .FromPath(patchPath)
            .WithLoadOrder(new[] { lightKey, targetKey })
            .WithDataFolder(_folder)
            .Mutable()
            .Construct();
        Assert.DoesNotContain(result.EnumerateMajorRecords(), x => x.FormKey.ModKey == targetKey);
        Assert.Single(result.FormLists);
    }

    [Fact]
    public void RebuildsOverriddenFormsAfterRemovingMasterRecords()
    {
        var retainedKey = ModKey.FromFileName("RetainedMaster.esm");
        var removedKey = ModKey.FromFileName("RemovedMaster.esp");
        var patchKey = ModKey.FromFileName("GeneratedPatch.esm");

        var retained = new SkyrimMod(retainedKey, SkyrimRelease.SkyrimSE) { IsMaster = true };
        var retainedNpc = retained.Npcs.AddNew();
        var removed = new SkyrimMod(removedKey, SkyrimRelease.SkyrimSE);
        var removedNpc = removed.Npcs.AddNew();
        var patch = new SkyrimMod(patchKey, SkyrimRelease.SkyrimSE) { IsMaster = true };
        patch.Npcs.GetOrAddAsOverride(retainedNpc);
        patch.Npcs.GetOrAddAsOverride(removedNpc);
        patch.ModHeader.SetOverriddenForms(new[] { retainedNpc.FormKey, removedNpc.FormKey });

        patch.Npcs.Remove(removedNpc.FormKey);
        MainForm.RebuildOverriddenForms(patch);

        Assert.Equal(new[] { retainedNpc.FormKey }, patch.ModHeader.OverriddenForms!.Select(x => x.FormKey));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Mutagen's test load-order cache can retain a short-lived read handle.
            // The OS temp directory will clean up the fixture later.
        }
    }
}
