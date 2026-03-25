using System.Text;
using Standard.Licensing;
using Standard.Licensing.Validation;

namespace ForRest.Services.Licensing;

public sealed class StandardLicenseValidationService : ILicenseValidationService
{
	private readonly LicenseValidationOptions _options;

	public StandardLicenseValidationService(LicenseValidationOptions options)
	{
		_options = options;
	}

	public LicenseValidationResult Evaluate(string? licenseText, DateTimeOffset buildDateUtc, DateTimeOffset nowUtc)
	{
		DateTimeOffset normalizedBuildDate = buildDateUtc == default ? nowUtc : buildDateUtc;
		DateTimeOffset graceExpiresUtc = normalizedBuildDate.AddDays(Math.Max(1, _options.GracePeriodDays));
		int graceDaysRemaining = Math.Max(0, (int)Math.Ceiling((graceExpiresUtc - nowUtc).TotalDays));
		bool graceActive = nowUtc <= graceExpiresUtc;

		if (string.IsNullOrWhiteSpace(licenseText))
		{
			return graceActive
				? CreateResult(
					LicenseValidationStatus.Grace,
					true,
					normalizedBuildDate,
					graceExpiresUtc,
					graceDaysRemaining,
					graceActive,
					null,
					null,
					null,
					$"Grace: {graceDaysRemaining} day{(graceDaysRemaining == 1 ? string.Empty : "s")} left",
					$"No license is required until {graceExpiresUtc:yyyy-MM-dd}.")
				: CreateResult(
					LicenseValidationStatus.Required,
					false,
					normalizedBuildDate,
					graceExpiresUtc,
					0,
					false,
					null,
					null,
					null,
					"License required",
					$"The 30-day build grace expired on {graceExpiresUtc:yyyy-MM-dd}.");
		}

		try
		{
			string licenseXml = DecodeLicenseText(licenseText);
			License license = License.Load(licenseXml);
			List<IValidationFailure> failures =
			[
				.. license.Validate()
					.ExpirationDate(nowUtc.UtcDateTime)
					.And()
					.Signature(_options.PublicKey)
					.AssertValidLicense()
			];

			if (failures.Count == 0)
			{
				string summary = "Licensed";
				string detail = string.IsNullOrWhiteSpace(license.Customer?.Name)
					? "A valid license is active."
					: $"A valid license is active for {license.Customer.Name}.";
				return CreateResult(
					LicenseValidationStatus.Licensed,
					true,
					normalizedBuildDate,
					graceExpiresUtc,
					graceDaysRemaining,
					graceActive,
					license.Customer?.Name,
					license.Customer?.Email,
					license.Expiration,
					summary,
					detail);
			}

			string message = string.Join("  ", failures.Select(static failure => failure.Message).Where(static message => !string.IsNullOrWhiteSpace(message)));
			return graceActive
				? CreateResult(
					LicenseValidationStatus.Grace,
					true,
					normalizedBuildDate,
					graceExpiresUtc,
					graceDaysRemaining,
					graceActive,
					license.Customer?.Name,
					license.Customer?.Email,
					license.Expiration,
					$"Grace: {graceDaysRemaining} day{(graceDaysRemaining == 1 ? string.Empty : "s")} left",
					$"The configured license is not valid yet, but this build is still inside grace. {message}".Trim())
				: CreateResult(
					LicenseValidationStatus.Invalid,
					false,
					normalizedBuildDate,
					graceExpiresUtc,
					0,
					false,
					license.Customer?.Name,
					license.Customer?.Email,
					license.Expiration,
					"License invalid",
					string.IsNullOrWhiteSpace(message) ? "The configured license could not be validated." : message);
		}
		catch (Exception exception)
		{
			return graceActive
				? CreateResult(
					LicenseValidationStatus.Grace,
					true,
					normalizedBuildDate,
					graceExpiresUtc,
					graceDaysRemaining,
					graceActive,
					null,
					null,
					null,
					$"Grace: {graceDaysRemaining} day{(graceDaysRemaining == 1 ? string.Empty : "s")} left",
					$"The configured license could not be read, but this build is still inside grace. {exception.Message}")
				: CreateResult(
					LicenseValidationStatus.Invalid,
					false,
					normalizedBuildDate,
					graceExpiresUtc,
					0,
					false,
					null,
					null,
					null,
					"License invalid",
					exception.Message);
		}
	}

	private static string DecodeLicenseText(string licenseText)
	{
		string trimmed = licenseText.Trim();
		if (trimmed.StartsWith('<'))
		{
			return trimmed;
		}

		try
		{
			byte[] bytes = Convert.FromBase64String(trimmed);
			string decoded = Encoding.UTF8.GetString(bytes);
			return string.IsNullOrWhiteSpace(decoded) ? trimmed : decoded;
		}
		catch (FormatException)
		{
			return trimmed;
		}
	}

	private static LicenseValidationResult CreateResult(
		LicenseValidationStatus status,
		bool isExecutionAllowed,
		DateTimeOffset buildDateUtc,
		DateTimeOffset graceExpiresUtc,
		int graceDaysRemaining,
		bool isGraceActive,
		string? registeredTo,
		string? registeredEmail,
		DateTimeOffset? licenseExpirationUtc,
		string summary,
		string detail)
	{
		return new LicenseValidationResult(
			status,
			isExecutionAllowed,
			buildDateUtc,
			graceExpiresUtc,
			graceDaysRemaining,
			isGraceActive,
			registeredTo,
			registeredEmail,
			licenseExpirationUtc,
			summary,
			detail);
	}
}
