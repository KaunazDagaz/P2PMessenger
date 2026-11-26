using System.Buffers.Binary;
using System.Globalization;

namespace P2PMessenger.Core.Copy;

public readonly struct Ipv4Address : IEquatable<Ipv4Address>, IComparable<Ipv4Address>
{
    public const int Length = 4;

    private Ipv4Address(uint value)
    {
        Value = value;
    }

    public uint Value { get; }

    public byte A => (byte)(Value >> 24);

    public byte B => (byte)(Value >> 16);

    public byte C => (byte)(Value >> 8);

    public byte D => (byte)Value;

    public static bool TryCreate(ReadOnlySpan<byte> bytes, out Ipv4Address address)
    {
        address = default;
        if (bytes.Length != Length || !IsDialable(bytes[0]))
        {
            return false;
        }

        address = new Ipv4Address(BinaryPrimitives.ReadUInt32BigEndian(bytes));
        return true;
    }

    public static bool TryCreate(byte a, byte b, byte c, byte d, out Ipv4Address address)
    {
        Span<byte> bytes = [a, b, c, d];
        return TryCreate(bytes, out address);
    }

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Length)
        {
            throw new ArgumentException("The destination buffer is too small.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination, Value);
    }

    public byte[] ToBytes()
    {
        byte[] bytes = new byte[Length];
        WriteTo(bytes);
        return bytes;
    }

    public int CompareTo(Ipv4Address other) => Value.CompareTo(other.Value);

    public bool Equals(Ipv4Address other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is Ipv4Address other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{A}.{B}.{C}.{D}");

    public static bool operator ==(Ipv4Address left, Ipv4Address right) => left.Equals(right);

    public static bool operator !=(Ipv4Address left, Ipv4Address right) => !left.Equals(right);

    public static bool operator <(Ipv4Address left, Ipv4Address right) => left.CompareTo(right) < 0;

    public static bool operator <=(Ipv4Address left, Ipv4Address right) => left.CompareTo(right) <= 0;

    public static bool operator >(Ipv4Address left, Ipv4Address right) => left.CompareTo(right) > 0;

    public static bool operator >=(Ipv4Address left, Ipv4Address right) => left.CompareTo(right) >= 0;

    private static bool IsDialable(byte firstOctet) => firstOctet is >= 1 and <= 223;
}
