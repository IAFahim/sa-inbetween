namespace SA.Inbetween.Soma;

public readonly record struct Meters(float Value);

public readonly record struct Centimeters(float Value);

public readonly record struct Radians(float Value);

public readonly record struct Degrees(float Value);

public static class Units
{
    public static Centimeters ToCentimeters(Meters meters) => new(float.NaN);

    public static Meters ToMeters(Centimeters centimeters) => new(float.NaN);

    public static Degrees ToDegrees(Radians radians) => new(float.NaN);

    public static Radians ToRadians(Degrees degrees) => new(float.NaN);
}
