using System.Buffers.Binary;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace MasterStripper;

internal static class PluginHeaderEditor
{
    private const uint MasterFlag = 0x0000_0001;
    private const uint LightFlag = 0x0000_0200;

    internal static bool FlagLight(string path) =>
        SetFlag(path, LightFlag, mod => mod.IsSmallMaster, "ESL/light");

    internal static bool FlagMaster(string path) =>
        SetFlag(path, MasterFlag, mod => mod.IsMaster, "ESM/master");

    private static bool SetFlag(string path, uint flag, Func<ISkyrimModGetter, bool> verify, string description)
    {
        if (!Path.GetExtension(path).Equals(".esp", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Only .esp plugins can be flagged {description} by this action.");

        var header = new byte[12];
        using (var input = File.OpenRead(path))
        {
            if (input.Read(header) != header.Length || !header.AsSpan(0, 4).SequenceEqual("TES4"u8))
                throw new InvalidDataException("The file does not begin with a valid TES4 plugin header.");
        }

        var flags = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4));
        if ((flags & flag) != 0) return false;

        var directory = Path.GetDirectoryName(path)!;
        var staged = Path.Combine(directory, $".{Path.GetFileNameWithoutExtension(path)}.{Guid.NewGuid():N}.esp");
        var backup = UniquePath(path + ".backup");
        try
        {
            File.Copy(path, staged);
            using (var output = new FileStream(staged, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), flags | flag);
                output.Position = 8;
                output.Write(header, 8, 4);
                output.Flush(flushToDisk: true);
            }

            using (var verified = SkyrimMod.Create(SkyrimRelease.SkyrimSE)
                       .FromPath(staged)
                       .WithLoadOrder(Array.Empty<ModKey>())
                       .WithDataFolder(directory)
                       .Construct())
            {
                if (!verify(verified))
                    throw new InvalidDataException($"The saved plugin did not reopen with the {description} flag set.");
            }

            File.Move(path, backup);
            try { File.Move(staged, path); }
            catch
            {
                File.Move(backup, path);
                throw;
            }
        }
        finally
        {
            if (File.Exists(staged)) File.Delete(staged);
        }

        return true;
    }

    private static string UniquePath(string desired)
    {
        if (!File.Exists(desired)) return desired;
        var directory = Path.GetDirectoryName(desired)!;
        var stem = Path.GetFileNameWithoutExtension(desired);
        var extension = Path.GetExtension(desired);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(directory, $"{stem}-{i}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
