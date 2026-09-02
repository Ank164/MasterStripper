using System.Buffers.Binary;
using MasterStripper;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace MasterStripper.Tests;

public sealed class PluginHeaderEditorTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "MasterStripperTests", Guid.NewGuid().ToString("N"));

    public PluginHeaderEditorTests() => Directory.CreateDirectory(_folder);

    [Fact]
    public void SetsOnlyLightFlagAndBacksUpOriginal()
    {
        var path = Path.Combine(_folder, "AlreadyCompacted.esp");
        WritePlugin(path, isLight: false);
        var original = File.ReadAllBytes(path);

        Assert.True(PluginHeaderEditor.FlagLight(path));

        var changed = File.ReadAllBytes(path);
        var originalFlags = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(8, 4));
        Assert.Equal(originalFlags | 0x0000_0200u, BinaryPrimitives.ReadUInt32LittleEndian(changed.AsSpan(8, 4)));
        Assert.Equal(original, File.ReadAllBytes(path + ".backup"));
    }

    [Fact]
    public void SkipsPluginThatIsAlreadyLightFlagged()
    {
        var path = Path.Combine(_folder, "AlreadyLight.esp");
        WritePlugin(path, isLight: true);

        Assert.False(PluginHeaderEditor.FlagLight(path));
        Assert.False(File.Exists(path + ".backup"));
    }

    private static void WritePlugin(string path, bool isLight)
    {
        var mod = new SkyrimMod(ModKey.FromFileName(Path.GetFileName(path)), SkyrimRelease.SkyrimSE)
        {
            IsSmallMaster = isLight
        };
        mod.BeginWrite.ToPath(path).WithNoLoadOrder().Write();
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
