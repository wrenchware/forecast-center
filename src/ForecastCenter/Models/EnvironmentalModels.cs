namespace ForecastCenter.Models;

public sealed record EnvironmentalSnapshot(
    double UvIndex,
    double PeakUvIndex,
    DateTime? PeakUvTime,
    int UsAqi,
    string DominantPollutant,
    double? Pm25,
    DateTime UpdatedAt);
