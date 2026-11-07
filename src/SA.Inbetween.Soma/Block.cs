using System;

namespace SA.Inbetween.Soma;

public readonly struct Block<T> : IEquatable<Block<T>>
    where T : IEquatable<T>
{
    private readonly T[]? items;

    internal Block(T[] items)
    {
        this.items = items;
    }

    public int Length => items is null ? 0 : items.Length;

    public ReadOnlySpan<T> Span => items;

    public bool Equals(Block<T> other) => Span.SequenceEqual(other.Span);

    public override bool Equals(object? obj) => obj is Block<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var item in Span)
            {
                hash = (hash ^ item.GetHashCode()) * 16777619;
            }

            return hash;
        }
    }

    public static bool operator ==(Block<T> left, Block<T> right) => left.Equals(right);

    public static bool operator !=(Block<T> left, Block<T> right) => !left.Equals(right);
}

public static class Blocks
{
    public static Block<T> Empty<T>()
        where T : IEquatable<T> => new(Array.Empty<T>());

    public static Block<T> Copy<T>(ReadOnlySpan<T> items)
        where T : IEquatable<T> => new(items.ToArray());

    public static Block<T> Of<T>(params T[] items)
        where T : IEquatable<T> => new(((ReadOnlySpan<T>)items).ToArray());

    public static bool BitwiseEqual(Block<float> left, Block<float> right)
    {
        var a = left.Span;
        var b = right.Span;
        if (a.Length != b.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i]))
            {
                return false;
            }
        }

        return true;
    }
}
