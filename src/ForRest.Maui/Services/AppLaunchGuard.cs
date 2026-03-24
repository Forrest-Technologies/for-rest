using System.Text;
namespace ForRest.Maui.Services;

public static class AppLaunchGuard
{
	private static readonly object SyncRoot = new();
	private static bool _initialized;
	private static string _appDataDirectory = string.Empty;
	private static string _markerFilePath = string.Empty;
	private static string _logFilePath = string.Empty;

	public static bool IsSafeModeEnabled { get; private set; }

	public static string StartupLogPath => _logFilePath;

	public static string SafeModeReason { get; private set; } = string.Empty;

	public static void Initialize()
	{
		lock (SyncRoot)
		{
			if (_initialized)
			{
				return;
			}

			_appDataDirectory = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"ForRest",
				"diagnostics");
			_markerFilePath = Path.Combine(_appDataDirectory, "launch.pending");
			_logFilePath = Path.Combine(_appDataDirectory, "startup.log");
			Directory.CreateDirectory(_appDataDirectory);

			if (File.Exists(_markerFilePath))
			{
				IsSafeModeEnabled = true;
				SafeModeReason = "ForRest detected an unclean previous launch and started in safe mode.";
				AppendLog("Detected previous unfinished launch marker. Safe mode enabled.");
			}

			File.WriteAllText(
				_markerFilePath,
				$"Started: {DateTimeOffset.Now:O}{Environment.NewLine}SafeMode: {IsSafeModeEnabled}{Environment.NewLine}",
				Encoding.UTF8);

			_initialized = true;
		}
	}

	public static void MarkLaunchCompleted()
	{
		lock (SyncRoot)
		{
			if (!_initialized)
			{
				return;
			}

			TryDelete(_markerFilePath);
			AppendLog("Launch marked as completed.");
		}
	}

	public static void RecordException(string context, Exception? exception)
	{
		StringBuilder builder = new();
		builder.AppendLine($"[{DateTimeOffset.Now:O}] {context}");
		if (exception is not null)
		{
			builder.AppendLine(exception.ToString());
		}
		else
		{
			builder.AppendLine("No exception payload was available.");
		}

		lock (SyncRoot)
		{
			if (!_initialized)
			{
				Initialize();
			}

			File.AppendAllText(_logFilePath, builder.ToString() + Environment.NewLine, Encoding.UTF8);
		}
	}

	public static void RecordMessage(string context, string message)
	{
		lock (SyncRoot)
		{
			if (!_initialized)
			{
				Initialize();
			}

			File.AppendAllText(
				_logFilePath,
				$"[{DateTimeOffset.Now:O}] {context}{Environment.NewLine}{message}{Environment.NewLine}{Environment.NewLine}",
				Encoding.UTF8);
		}
	}

	private static void AppendLog(string message)
	{
		File.AppendAllText(
			_logFilePath,
			$"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}",
			Encoding.UTF8);
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception exception)
		{
			RecordException("Failed to delete launch marker.", exception);
		}
	}
}
