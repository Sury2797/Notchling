using System.Text;

namespace Notch.Core.Providers;

/// <summary>Reads a selected calendar with limits enforced while reading, including files being replaced or appended.</summary>
public static class CalendarImporter
{
    public static async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        const int maximumBytes = 5 * 1024 * 1024;
        await using var source = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (source.Length > maximumBytes) throw new InvalidDataException("Calendar file exceeds the 5 MB limit.");
        using var buffer = new MemoryStream();
        var block = new byte[16 * 1024];
        int read;
        while ((read = await source.ReadAsync(block, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + read > maximumBytes) throw new InvalidDataException("Calendar file exceeds the 5 MB limit.");
            buffer.Write(block, 0, read);
        }
        cancellationToken.ThrowIfCancellationRequested();
        buffer.Position = 0;
        // Keep decoding strict even for BOM-selected UTF-16/UTF-32. The default
        // StreamReader BOM switch would silently replace malformed code units.
        var bytes = buffer.GetBuffer();
        Encoding encoding = new UTF8Encoding(false, true);
        if (buffer.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
        { encoding = new UTF32Encoding(false, true, true); buffer.Position = 4; }
        else if (buffer.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        { encoding = new UTF32Encoding(true, true, true); buffer.Position = 4; }
        else if (buffer.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        { encoding = new UnicodeEncoding(false, true, true); buffer.Position = 2; }
        else if (buffer.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        { encoding = new UnicodeEncoding(true, true, true); buffer.Position = 2; }
        else if (buffer.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        { buffer.Position = 3; }
        using var text = new StreamReader(buffer, encoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        try { return await text.ReadToEndAsync(cancellationToken).ConfigureAwait(false); }
        catch (DecoderFallbackException) { throw new InvalidDataException("The calendar file contains invalid text encoding. Export it as UTF-8 and import it again."); }
    }
}
