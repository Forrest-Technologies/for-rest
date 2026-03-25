namespace ForRest.Services.Licensing;

public enum LicenseValidationStatus
{
	Grace,
	Licensed,
	Required,
	Invalid
}

public sealed record LicenseValidationOptions(
	string PublicKey,
	int GracePeriodDays = 30);

public sealed record LicenseValidationResult(
	LicenseValidationStatus Status,
	bool IsExecutionAllowed,
	DateTimeOffset BuildDateUtc,
	DateTimeOffset GraceExpiresUtc,
	int GraceDaysRemaining,
	bool IsGraceActive,
	string? RegisteredTo,
	string? RegisteredEmail,
	DateTimeOffset? LicenseExpirationUtc,
	string Summary,
	string Detail);

public interface ILicenseValidationService
{
	LicenseValidationResult Evaluate(
		string? licenseText,
		DateTimeOffset buildDateUtc,
		DateTimeOffset nowUtc);
}
