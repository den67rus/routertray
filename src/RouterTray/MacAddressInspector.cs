using System.Globalization;
using System.Text;

namespace RouterTray;

internal static class MacAddressInspector
{
    private const int MacAddressByteCount = 6;

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var hexadecimal = new StringBuilder(MacAddressByteCount * 2);
        foreach (var character in value)
        {
            if (Uri.IsHexDigit(character))
            {
                hexadecimal.Append(char.ToUpperInvariant(character));
                continue;
            }

            if (character is not (':' or '-' or '.') && !char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        if (hexadecimal.Length != MacAddressByteCount * 2)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[MacAddressByteCount];
        for (var index = 0; index < bytes.Length; index++)
        {
            if (!byte.TryParse(
                    hexadecimal.ToString(index * 2, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out bytes[index]))
            {
                return false;
            }
        }

        if (bytes.SequenceEqual(stackalloc byte[MacAddressByteCount]) ||
            bytes.ToArray().All(static item => item == byte.MaxValue) ||
            (bytes[0] & 0x01) != 0)
        {
            return false;
        }

        normalized = string.Join(
            ":",
            bytes.ToArray().Select(static item =>
                item.ToString("X2", CultureInfo.InvariantCulture)));
        return true;
    }

    public static bool IsLocallyAdministered(string? value)
    {
        if (!TryNormalize(value, out var normalized))
        {
            return false;
        }

        return (byte.Parse(
                    normalized.AsSpan(0, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture) &
                0x02) != 0;
    }
}
