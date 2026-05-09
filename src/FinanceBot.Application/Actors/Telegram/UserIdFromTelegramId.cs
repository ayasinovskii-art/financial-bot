using System.Security.Cryptography;
using System.Text;

namespace FinanceBot.Application.Actors.Telegram;

/// <summary>
/// Детерминистический маппинг telegramId → userId (Guid).
/// Намеренно стабилен: один и тот же telegramId всегда даёт один и тот же Guid,
/// что устраняет необходимость в централизованном реестре «id → guid».
/// Алгоритм: SHA-256(telegramId.ToString()), берём первые 16 байт, оборачиваем в Guid (UUIDv5-style).
/// </summary>
public static class UserIdFromTelegramId
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static Guid Resolve(long telegramId)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Utf8NoBom.GetBytes(telegramId.ToString(System.Globalization.CultureInfo.InvariantCulture)), hash);

        Span<byte> guidBytes = stackalloc byte[16];
        hash[..16].CopyTo(guidBytes);

        // Установим биты варианта/версии RFC 4122 v5, чтобы Guid был «чистым» UUIDv5.
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

        return new Guid(guidBytes);
    }
}
