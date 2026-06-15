using System.Text;

namespace FinanceBot.Application.Telegram;

/// <summary>
/// Encode/decode helper for Telegram inline-button callback_data.
/// Format: "action:{entityId:N}" or "action:{entityId:N}:{shortArg}".
/// Enforces Telegram's 64-byte hard limit.
/// </summary>
public static class CallbackPayload
{
    private const int MaxBytes = 64;

    /// <summary>
    /// Encodes an action + entity id (+ optional short arg) into a callback_data string.
    /// Throws <see cref="InvalidOperationException"/> when the UTF-8 byte count would exceed 64.
    /// </summary>
    public static string Encode(string action, Guid entityId, string? shortArg = null)
    {
        var payload = shortArg is null
            ? $"{action}:{entityId:N}"
            : $"{action}:{entityId:N}:{shortArg}";

        if (Encoding.UTF8.GetByteCount(payload) > MaxBytes)
            throw new InvalidOperationException(
                $"Callback payload exceeds {MaxBytes} bytes ({Encoding.UTF8.GetByteCount(payload)}): {payload}");

        return payload;
    }

    /// <summary>
    /// Parses a callback_data string produced by <see cref="Encode"/>.
    /// Returns <c>false</c> without throwing on malformed input.
    /// </summary>
    public static bool TryDecode(
        string data,
        out string action,
        out Guid entityId,
        out string? shortArg)
    {
        action = string.Empty;
        entityId = Guid.Empty;
        shortArg = null;

        if (string.IsNullOrEmpty(data))
            return false;

        var firstColon = data.IndexOf(':');
        if (firstColon <= 0)
            return false;

        action = data[..firstColon];
        var rest = data[(firstColon + 1)..];

        // N-form GUID is exactly 32 hex chars with no hyphens.
        if (rest.Length < 32 || !Guid.TryParseExact(rest[..32], "N", out entityId))
            return false;

        if (rest.Length == 32)
            return true;

        if (rest[32] != ':')
            return false;

        shortArg = rest[33..];
        return true;
    }
}
