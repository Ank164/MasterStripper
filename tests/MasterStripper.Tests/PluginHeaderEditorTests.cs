using System.Buffers.Binary;
using MasterStripper;
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
        var original = HeaderWithFlags(0x0000_0001);
        File.WriteAllBytes(path, original);

        Assert.True(PluginHeaderEditor.FlagLight(path));

        var changed = File.ReadAllBytes(path);
        Assert.Equal(0x0000_0201u, BinaryPrimitives.ReadUInt32LittleEndian(changed.AsSpan(8, 4)));
        Assert.Equal(original, File.ReadAllBytes(path + ".backup"));
    }

    [Fact]
    public void SkipsPluginThatIsAlreadyLightFlagged()
    {
        var path = Path.Combine(_folder, "AlreadyLight.esp");
        File.WriteAllBytes(path, HeaderWithFlags(0x0000_0200));

        Assert.False(PluginHeaderEditor.FlagLight(path));
        Assert.False(File.Exists(path + ".backup"));
    }

    private static byte[] HeaderWithFlags(uint flags)
    {
        var bytes = new byte[16];
        "TES4"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), flags);
        return bytes;
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
