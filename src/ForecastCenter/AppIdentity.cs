using System.Runtime.InteropServices;

namespace ForecastCenter;

internal static class AppIdentity
{
    public const string ProductName = "Forecast Center";
    public const string DistributionName = "Forecast Center";
    public const string AppUserModelId = "ForecastCenter.Public";
    public const string StartupValueName = "ForecastCenterPublic";
    // Must match the StartupTask TaskId in Packaging\Package.appxmanifest.
    public const string StartupTaskId = "ForecastCenterStartup";
    public const string DataFolderName = "Forecast Center Public";
    public const string NetworkUserAgent = "ForecastCenter.Public/0.8 (+https://github.com/wrenchware/forecast-center)";

    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        DataFolderName);

    /// <summary>
    /// True when running from the Microsoft Store (MSIX) package. Store installs are
    /// updated by the Store and register for startup through the package manifest.
    /// </summary>
    public static bool IsPackaged { get; } = DetectPackage();

    public static void ApplyProcessIdentity()
    {
        // A packaged app already gets its identity from the package manifest.
        if (IsPackaged) return;
        _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
    }

    private static bool DetectPackage()
    {
        var length = 0;
        // APPMODEL_ERROR_NO_PACKAGE (15700) means the process has no package identity.
        return GetCurrentPackageFullName(ref length, null) != 15700;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);
}
