using System;
using System.Collections.Generic;

namespace SA.Inbetween.Soma;

public readonly struct Result<T, TFault> : IEquatable<Result<T, TFault>>
{
    private readonly T value;
    private readonly TFault fault;

    internal Result(bool isOk, T value, TFault fault)
    {
        IsOk = isOk;
        this.value = value;
        this.fault = fault;
    }

    public bool IsOk { get; }

    public bool TryGetValue(out T value)
    {
        value = this.value;
        return IsOk;
    }

    public bool TryGetFault(out TFault fault)
    {
        fault = this.fault;
        return !IsOk;
    }

    public TResult Match<TResult>(Func<T, TResult> ok, Func<TFault, TResult> error) =>
        IsOk ? ok(value) : error(fault);

    public bool Equals(Result<T, TFault> other) =>
        IsOk == other.IsOk
        && (IsOk
            ? EqualityComparer<T>.Default.Equals(value, other.value)
            : EqualityComparer<TFault>.Default.Equals(fault, other.fault));

    public override bool Equals(object? obj) => obj is Result<T, TFault> other && Equals(other);

    public override int GetHashCode() =>
        IsOk
            ? EqualityComparer<T>.Default.GetHashCode(value!)
            : ~EqualityComparer<TFault>.Default.GetHashCode(fault!);

    public static bool operator ==(Result<T, TFault> left, Result<T, TFault> right) => left.Equals(right);

    public static bool operator !=(Result<T, TFault> left, Result<T, TFault> right) => !left.Equals(right);
}

public static class Result
{
    public static Result<T, TFault> Ok<T, TFault>(T value) => new(true, value, default!);

    public static Result<T, TFault> Error<T, TFault>(TFault fault) => new(false, default!, fault);

    public static Result<TOut, TFault> Select<T, TOut, TFault>(
        this Result<T, TFault> result,
        Func<T, TOut> map) =>
        result.TryGetValue(out var value)
            ? Ok<TOut, TFault>(map(value))
            : Error<TOut, TFault>(Fault(result));

    public static Result<TOut, TFault> SelectMany<T, TOut, TFault>(
        this Result<T, TFault> result,
        Func<T, Result<TOut, TFault>> bind) =>
        result.TryGetValue(out var value) ? bind(value) : Error<TOut, TFault>(Fault(result));

    public static Result<TOut, TFault> SelectMany<T, TMid, TOut, TFault>(
        this Result<T, TFault> result,
        Func<T, Result<TMid, TFault>> bind,
        Func<T, TMid, TOut> project) =>
        result.TryGetValue(out var value)
            ? bind(value).Select(mid => project(value, mid))
            : Error<TOut, TFault>(Fault(result));

    private static TFault Fault<T, TFault>(Result<T, TFault> result)
    {
        result.TryGetFault(out var fault);
        return fault;
    }
}
