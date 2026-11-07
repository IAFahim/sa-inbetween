namespace SA.Inbetween.Soma;

public readonly record struct Meters(float Value);

public readonly record struct Centimeters(float Value);

public readonly record struct Radians(float Value);

public readonly record struct Degrees(float Value);

public static class Units
{
    private const double DegreesPerRadian = 180.0 / System.Math.PI;

    public static Centimeters ToCentimeters(Meters meters) => new(meters.Value * 100f);

    public static Meters ToMeters(Centimeters centimeters) => new(centimeters.Value / 100f);

    public static Degrees ToDegrees(Radians radians) => new((float)(radians.Value * DegreesPerRadian));

    public static Radians ToRadians(Degrees degrees) => new((float)(degrees.Value / DegreesPerRadian));
}
