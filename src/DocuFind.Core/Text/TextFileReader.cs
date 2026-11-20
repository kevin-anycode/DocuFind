using System.Text;

namespace DocuFind.Core.Text;

/// <summary>纯文本读取，自动识别 UTF-8 / UTF-16 BOM，无 BOM 时回退到 GB18030。</summary>
public static class TextFileReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static TextFileReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static string ReadAllText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Decode(bytes);
    }

    public static string Decode(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            try
            {
                return Encoding.GetEncoding("GB18030").GetString(bytes);
            }
            catch
            {
                return Encoding.Latin1.GetString(bytes);
            }
        }
    }
}
