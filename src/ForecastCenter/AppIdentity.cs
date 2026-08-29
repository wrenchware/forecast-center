using System.Runtime.InteropServices;

namespace ForecastCenter;

internal static class AppIdentity
{
    public const string ProductName = "Forecast Center";
    public const string DistributionName = "Forecast Center (Public Preview)";
    public const string AppUserModelId = "ForecastCenter.Public";
    public const string StartupValueName = "ForecastCenterPublic";
    public const string DataFolderName = "Forecast Center Public";
    public const string NetworkUserAgent = "ForecastCenter.Public/0.8 (+https://github.com/wrenchware/forecast-center)";

    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        DataFolderName);

    public static void ApplyProcessIdentity()
    {
        _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
