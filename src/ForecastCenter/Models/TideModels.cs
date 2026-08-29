namespace ForecastCenter.Models;

public sealed record TideStation(string Id, string Name, string State, double Latitude, double Longitude)
{
    public string DisplayName => $"{Name}, {State}";
}

public sealed record TidePrediction(DateTime Time, double Height, bool IsHigh);
public sealed record TideSnapshot(TideStation Station, IReadOnlyList<TidePrediction> Predictions, DateTimeOffset RetrievedAt);
