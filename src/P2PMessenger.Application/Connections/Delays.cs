using System.Security.Cryptography;

namespace P2PMessenger.Application.Connections;

internal static class Delays
{
    public static TimeSpan Jittered(TimeSpan delay) => delay * (0.8 + (RandomNumberGenerator.GetInt32(0, 401) / 1000.0));

    public static TimeSpan Doubled(TimeSpan delay, TimeSpan longest)
    {
        TimeSpan doubled = delay * 2;
        return doubled < longest ? doubled : longest;
    }
}
