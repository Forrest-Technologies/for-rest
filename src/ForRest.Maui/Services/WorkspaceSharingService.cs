using System.IO;
using System.Text;
using ForRest.Services;
using ForRest.Services.Sharing;

namespace ForRest.Maui.Services;

/// <summary>
/// One imported request document. <see cref="PreferredLocation"/> is honoured when it is
/// unique inside the target workspace (workspace-archive round trips); otherwise a fresh
/// location is generated from the title.
/// </summary>
public sealed record ImportedDocument(string Name, string? PreferredLocation, string Source);

/// <summary>
/// The result of converting one imported file or clipboard payload. When
/// <see cref="WorkspaceName"/> is non-null the batch represents a whole workspace (archive or
/// Postman collection) and is imported as a new workspace; otherwise the documents join the
/// active workspace.
/// </summary>
public sealed record ImportedDocumentBatch(
	string? WorkspaceName,
	string? Environment,
	IReadOnlyList<ImportedDocument> Documents,
	string Description);

public interface IWorkspaceSharingService
{
	string ExportDocumentSource(RequestWorkbenchDocumentState document, bool includeSecrets = false);

	byte[] ExportWorkspaceArchive(RequestWorkbenchWorkspaceState workspace, bool includeSecrets = false);

	bool LooksLikeCurl(string? text);

	ImportedDocumentBatch ImportCurl(string curlCommand);

	ImportedDocumentBatch ImportContent(string fileName, byte[] bytes);
}

public sealed class WorkspaceSharingService(
	IWorkspaceArchiveService workspaceArchiveService,
	ICurlImportService curlImportService,
	IPostmanImportService postmanImportService,
	IOpenApiScaffoldGenerator openApiScaffoldGenerator) : IWorkspaceSharingService
{
	#region Public Methods

	public string ExportDocumentSource(RequestWorkbenchDocumentState document, bool includeSecrets = false)
	{
		return includeSecrets ? document.RequestSource : FrsSecretRedactor.Redact(document.RequestSource);
	}

	public byte[] ExportWorkspaceArchive(RequestWorkbenchWorkspaceState workspace, bool includeSecrets = false)
	{
		PortableWorkspace portable = new(
			workspace.Name,
			workspace.SelectedEnvironment,
			[
				.. workspace.Documents.Select(static document =>
					new PortableWorkspaceDocument(document.Title, document.Location, document.RequestSource))
			]);

		return workspaceArchiveService.ExportArchive(portable, includeSecrets);
	}

	public bool LooksLikeCurl(string? text)
	{
		return curlImportService.LooksLikeCurl(text);
	}

	public ImportedDocumentBatch ImportCurl(string curlCommand)
	{
		string source = curlImportService.ConvertToScript(curlCommand, out string suggestedName);
		return new ImportedDocumentBatch(
			null,
			null,
			[new ImportedDocument(suggestedName, null, source)],
			"curl command");
	}

	public ImportedDocumentBatch ImportContent(string fileName, byte[] bytes)
	{
		ImportContentClassifier.ContentKind kind = ImportContentClassifier.ClassifyBytes(bytes);

		// The .frs extension is authoritative: a hand-written script may not match the
		// classifier's structural heuristics but should still import as a script.
		if (kind is ImportContentClassifier.ContentKind.Unknown or ImportContentClassifier.ContentKind.CurlCommand &&
		    (fileName?.EndsWith(".frs", StringComparison.OrdinalIgnoreCase) ?? false))
		{
			kind = ImportContentClassifier.ContentKind.Frs;
		}

		return kind switch
		{
			ImportContentClassifier.ContentKind.WorkspaceArchiveZip => ImportArchive(bytes),
			ImportContentClassifier.ContentKind.Frs => ImportFrs(fileName, bytes),
			ImportContentClassifier.ContentKind.CurlCommand => ImportCurl(DecodeText(bytes)),
			ImportContentClassifier.ContentKind.PostmanCollection => ImportPostman(bytes),
			ImportContentClassifier.ContentKind.OpenApiSpec => ImportOpenApi(fileName, bytes),
			_ => throw new FormatException(
				$"'{fileName}' is not a recognized import format. Supported: .frs scripts, For-Rest workspace archives (.zip), Postman collections (.json), OpenAPI specs (.json), and curl commands."),
		};
	}

	#endregion

	#region Private Methods

	private ImportedDocumentBatch ImportArchive(byte[] bytes)
	{
		PortableWorkspace portable = workspaceArchiveService.ImportArchive(bytes);
		return new ImportedDocumentBatch(
			portable.Name,
			portable.SelectedEnvironment,
			[
				.. portable.Documents.Select(static document =>
					new ImportedDocument(document.Name, document.Location, document.Source))
			],
			$"workspace archive '{portable.Name}'");
	}

	private static ImportedDocumentBatch ImportFrs(string fileName, byte[] bytes)
	{
		string name = BuildNameFromFileName(fileName, "Imported request");
		return new ImportedDocumentBatch(
			null,
			null,
			[new ImportedDocument(name, null, DecodeText(bytes))],
			$".frs script '{name}'");
	}

	private ImportedDocumentBatch ImportPostman(byte[] bytes)
	{
		IReadOnlyList<PortableWorkspaceDocument> documents = postmanImportService.Convert(DecodeText(bytes), out string collectionName);
		string workspaceName = string.IsNullOrWhiteSpace(collectionName) ? "Imported collection" : collectionName;
		return new ImportedDocumentBatch(
			workspaceName,
			null,
			[
				.. documents.Select(static document =>
					new ImportedDocument(document.Name, null, document.Source))
			],
			$"Postman collection '{workspaceName}'");
	}

	private ImportedDocumentBatch ImportOpenApi(string fileName, byte[] bytes)
	{
		string name = BuildNameFromFileName(fileName, "OpenAPI scaffold");
		string source = openApiScaffoldGenerator.Generate(DecodeText(bytes));
		return new ImportedDocumentBatch(
			null,
			null,
			[new ImportedDocument($"{name} scaffold", null, source)],
			$"OpenAPI spec '{name}'");
	}

	private static string BuildNameFromFileName(string fileName, string fallback)
	{
		string stem = Path.GetFileNameWithoutExtension(fileName ?? string.Empty)
			.Replace('-', ' ')
			.Replace('_', ' ')
			.Trim();
		return string.IsNullOrWhiteSpace(stem) ? fallback : stem;
	}

	private static string DecodeText(byte[] bytes)
	{
		string text = Encoding.UTF8.GetString(bytes);
		return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
	}

	#endregion
}
