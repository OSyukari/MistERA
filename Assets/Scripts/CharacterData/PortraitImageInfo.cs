using System.Collections.Generic;
using System.IO;

/// <summary>
/// Image orientation lookup from the file header only (no texture load), used to pick portrait-only images
/// for boxes that cannot show landscape. Results are cached per path for the session.
/// Anything that cannot be read (Resources assets, missing files, unknown formats) counts as portrait.
/// </summary>
public static class PortraitImageInfo
{
    static readonly Dictionary<string, bool> _isLandscape = new Dictionary<string, bool>();

    public static bool IsLandscape(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (_isLandscape.TryGetValue(path, out var cached)) return cached;

        bool result = false;
        if (TryGetSize(path, out int width, out int height)) result = width > height;
        _isLandscape[path] = result;
        return result;
    }

    public static bool TryGetSize(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (scr_System_Serializer.current == null) return false;
        var fullPath = scr_System_Serializer.current.GetFullPath(path);
        if (!File.Exists(fullPath)) return false;

        try
        {
            using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var header = new byte[30];
                int read = stream.Read(header, 0, header.Length);
                if (read < 4) return false;

                // PNG: IHDR width/height, big endian
                if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
                {
                    if (read < 24) return false;
                    width = ReadBE32(header, 16);
                    height = ReadBE32(header, 20);
                    return width > 0 && height > 0;
                }

                // JPEG: walk markers until a SOF segment
                if (header[0] == 0xFF && header[1] == 0xD8)
                {
                    stream.Position = 2;
                    return TryReadJpegSize(stream, out width, out height);
                }

                // WebP: RIFF....WEBP + VP8 / VP8L / VP8X chunk
                if (read >= 30 && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
                    && header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P')
                {
                    if (header[12] == 'V' && header[13] == 'P' && header[14] == '8')
                    {
                        if (header[15] == 'X')
                        {
                            width = 1 + (header[24] | header[25] << 8 | header[26] << 16);
                            height = 1 + (header[27] | header[28] << 8 | header[29] << 16);
                        }
                        else if (header[15] == 'L')
                        {
                            if (header[20] != 0x2F) return false;
                            width = 1 + (header[21] | (header[22] & 0x3F) << 8);
                            height = 1 + (header[22] >> 6 | header[23] << 2 | (header[24] & 0x0F) << 10);
                        }
                        else if (header[15] == ' ')
                        {
                            if (header[23] != 0x9D || header[24] != 0x01 || header[25] != 0x2A) return false;
                            width = (header[26] | header[27] << 8) & 0x3FFF;
                            height = (header[28] | header[29] << 8) & 0x3FFF;
                        }
                        return width > 0 && height > 0;
                    }
                }
            }
        }
        catch (IOException) { }
        catch (System.UnauthorizedAccessException) { }

        return false;
    }

    static bool TryReadJpegSize(Stream stream, out int width, out int height)
    {
        width = 0;
        height = 0;
        while (true)
        {
            int b = stream.ReadByte();
            if (b < 0) return false;
            if (b != 0xFF) continue;

            int marker = stream.ReadByte();
            while (marker == 0xFF) marker = stream.ReadByte();   // fill bytes
            if (marker < 0) return false;

            // standalone markers, no length
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD8)) continue;
            if (marker == 0xD9 || marker == 0xDA) return false;  // end of image / start of scan before any SOF

            int hi = stream.ReadByte();
            int lo = stream.ReadByte();
            if (hi < 0 || lo < 0) return false;
            int length = hi << 8 | lo;
            if (length < 2) return false;

            bool isSOF = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (isSOF)
            {
                var sof = new byte[5];
                if (stream.Read(sof, 0, 5) < 5) return false;
                height = sof[1] << 8 | sof[2];
                width = sof[3] << 8 | sof[4];
                return width > 0 && height > 0;
            }
            stream.Position += length - 2;
        }
    }

    static int ReadBE32(byte[] data, int offset)
    {
        return data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3];
    }
}
