using System.Text;
using Microsoft.Maui.Storage;
namespace ForRest.Maui.Services;

public static class AppLaunchGuard
{
	private static readonly object SyncRoot = new();
	private static bool _initialized;
	private static bool _storageAvailable;
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

			IsSafeModeEnabled = false;
			SafeModeReason = string.Empty;
			_storageAvailable = TryInitializeStorage();
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

			TryAppendToLog(builder.ToString() + Environment.NewLine);
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

			TryAppendToLog(
				$"[{DateTimeOffset.Now:O}] {context}{Environment.NewLine}{message}{Environment.NewLine}{Environment.NewLine}");
		}
	}

	private static void AppendLog(string message)
	{
		TryAppendToLog($"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
	}

	private static bool TryInitializeStorage()
	{
		foreach (string candidate in GetDiagnosticsDirectoryCandidates())
		{
			if (string.IsNullOrWhiteSpace(candidate))
			{
				continue;
			}

			try
			{
				_appDataDirectory = candidate;
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
				return true;
			}
			catch
			{
				_appDataDirectory = string.Empty;
				_markerFilePath = string.Empty;
				_logFilePath = string.Empty;
			}
		}

		return false;
	}

	private static IEnumerable<string> GetDiagnosticsDirectoryCandidates()
	{
		string? appDataDirectory = null;
		try
		{
			appDataDirectory = FileSystem.Current.AppDataDirectory;
		}
		catch
		{
		}

		if (!string.IsNullOrWhiteSpace(appDataDirectory))
		{
			yield return Path.Combine(appDataDirectory, "diagnostics");
		}

		string? localAppData = null;
		try
		{
			localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		}
		catch
		{
		}

		if (!string.IsNullOrWhiteSpace(localAppData))
		{
			yield return Path.Combine(localAppData, "ForRest", "diagnostics");
		}

		yield return Path.Combine(Path.GetTempPath(), "ForRest", "diagnostics");
	}

	private static void TryAppendToLog(string content)
	{
		if (!_storageAvailable || string.IsNullOrWhiteSpace(_logFilePath))
		{
			return;
		}

		try
		{
			File.AppendAllText(_logFilePath, content, Encoding.UTF8);
		}
		catch
		{
			_storageAvailable = false;
		}
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
