using System.IO.Compression;

namespace WinFrame.Core.Controls;

/// <summary>
/// How an image is persisted in a control sheet cell.
/// <list type="bullet">
/// <item><c>file=</c> path on the local machine (relative to WinFrame home or absolute)</item>
/// <item><c>b=</c> raw PNG bytes, Base64</item>
/// <item><c>c=</c> Deflate-compressed PNG bytes, Base64</item>
/// <item><c>7zc=</c> LZMA (7-Zip) compressed PNG bytes, Base64</item>
/// </list>
/// </summary>
public enum ImageStorageMode { File, Base64, Compressed, SevenZip }

public static class ImageCodec
{
    public const string FilePrefix = "file=";
    public const string Base64Prefix = "b=";
    public const string CompressedPrefix = "c=";
    public const string SevenZipPrefix = "7zc=";

    /// <summary>Encodes PNG bytes to a cell value. For File mode, writes <paramref name="fileName"/> into <paramref name="folder"/>.</summary>
    public static string Encode(byte[] png, ImageStorageMode mode, string folder = "", string fileName = "")
    {
        switch (mode)
        {
            case ImageStorageMode.File:
                Directory.CreateDirectory(folder);
                if (string.IsNullOrWhiteSpace(fileName)) fileName = Guid.NewGuid().ToString("N") + ".png";
                var path = Path.Combine(folder, fileName);
                File.WriteAllBytes(path, png);
                return FilePrefix + path;
            case ImageStorageMode.Base64:
                return Base64Prefix + Convert.ToBase64String(png);
            case ImageStorageMode.Compressed:
                return CompressedPrefix + Convert.ToBase64String(Deflate(png));
            case ImageStorageMode.SevenZip:
                return SevenZipPrefix + Convert.ToBase64String(Lzma(png));
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    /// <summary>Decodes a cell value back to PNG bytes (null when empty / missing file).</summary>
    public static byte[]? Decode(string? value, string? baseFolder = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.StartsWith(SevenZipPrefix, StringComparison.OrdinalIgnoreCase)) return UnLzma(Convert.FromBase64String(value[SevenZipPrefix.Length..]));
        if (value.StartsWith(CompressedPrefix, StringComparison.OrdinalIgnoreCase)) return Inflate(Convert.FromBase64String(value[CompressedPrefix.Length..]));
        if (value.StartsWith(Base64Prefix, StringComparison.OrdinalIgnoreCase)) return Convert.FromBase64String(value[Base64Prefix.Length..]);
        var path = value.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase) ? value[FilePrefix.Length..] : value;
        if (!Path.IsPathRooted(path) && baseFolder != null) path = Path.Combine(baseFolder, path);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    public static ImageStorageMode? DetectMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.StartsWith(SevenZipPrefix, StringComparison.OrdinalIgnoreCase)) return ImageStorageMode.SevenZip;
        if (value.StartsWith(CompressedPrefix, StringComparison.OrdinalIgnoreCase)) return ImageStorageMode.Compressed;
        if (value.StartsWith(Base64Prefix, StringComparison.OrdinalIgnoreCase)) return ImageStorageMode.Base64;
        return ImageStorageMode.File;
    }

    /// <summary>Re-encodes an existing cell value into another storage mode.</summary>
    public static string? Reencode(string? value, ImageStorageMode target, string folder, string fileName, string? baseFolder = null)
    {
        var bytes = Decode(value, baseFolder);
        return bytes == null ? value : Encode(bytes, target, folder, fileName);
    }

    public static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, CompressionLevel.SmallestSize, leaveOpen: true)) ds.Write(data);
        return ms.ToArray();
    }

    public static byte[] Inflate(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var ds = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        ds.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>LZMA stream layout: 5 property bytes, 8-byte little-endian length, payload.</summary>
    public static byte[] Lzma(byte[] data)
    {
        var encoder = new SevenZip.Compression.LZMA.Encoder();
        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        encoder.WriteCoderProperties(output);
        output.Write(BitConverter.GetBytes((long)data.Length), 0, 8);
        encoder.Code(input, output, data.Length, -1, null);
        return output.ToArray();
    }

    public static byte[] UnLzma(byte[] data)
    {
        var decoder = new SevenZip.Compression.LZMA.Decoder();
        using var input = new MemoryStream(data);
        var props = new byte[5];
        input.ReadExactly(props, 0, 5);
        var lenBytes = new byte[8];
        input.ReadExactly(lenBytes, 0, 8);
        long outLen = BitConverter.ToInt64(lenBytes, 0);
        decoder.SetDecoderProperties(props);
        using var output = new MemoryStream();
        decoder.Code(input, output, input.Length - input.Position, outLen, null);
        return output.ToArray();
    }
}
