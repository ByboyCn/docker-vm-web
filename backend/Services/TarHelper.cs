using System.Formats.Tar;
using System.IO;
using System.IO.Compression;

namespace DockerVm.Services;

/// <summary>
/// 把目录打包成 Docker API 接受的 gzip 压缩 tar 流。
/// </summary>
public static class TarHelper
{
    public static Stream CreateFromDirectory(string sourceDir)
    {
        var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            WriteTar(gzip, sourceDir);
        }

        ms.Position = 0;
        return ms;
    }

    private static void WriteTar(Stream stream, string sourceDir)
    {
        using var writer = new TarWriter(stream);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
            var entry = new PaxTarEntry(TarEntryType.RegularFile, relative)
            {
                ModificationTime = DateTimeOffset.UtcNow,
                Mode = UnixFileMode.OtherRead | UnixFileMode.GroupRead | UnixFileMode.UserRead | UnixFileMode.UserWrite,
            };

            using var fs = File.OpenRead(file);
            entry.DataStream = fs;
            writer.WriteEntry(entry);
        }
    }
}
