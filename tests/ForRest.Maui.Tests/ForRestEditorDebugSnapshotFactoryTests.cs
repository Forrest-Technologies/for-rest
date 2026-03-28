using System;
using System.Linq;
using System.Text.Json;
using ForRest.Maui.Services;
using ForRest.Scripting;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class ForRestEditorDebugSnapshotFactoryTests
{
	[TestMethod]
	public void Create_returns_ready_snapshot_for_valid_script()
	{
		string source =
			"""
			name "Flow Echo"
			method GET
			url "https://api.example.test/echo?trace={{trace_id}}"
			max_send_iterations 2

			runtime trace_id = "trace-123"

			request.headers["X-Flow"] = "enabled"
			let sent = request.send()
			if response.status == 200 {
			  runtime last_attempt = response.attempt
			}
			foreach item in range(0, 2) {
			  log item
			}
			""";

		ForRestScriptCompiler compiler = new(new ForRestScriptParser());
		ForRestScriptCompilationResult compilation = compiler.Compile(
			source,
			new ForRestScriptCompilationOptions
			{
				WorkspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
				DefaultRequestName = "Fallback",
			});
		Assert.IsTrue(compilation.Succeeded, string.Join(" | ", compilation.Diagnostics.Select(static item => item.Message)));

		ForRestEditorDebugSnapshot snapshot = ForRestEditorDebugSnapshotFactory.Create(
			source,
			compilation,
			"Echo Lab",
			"Local",
			"Fallback",
			"https://fallback.local");

		Assert.AreEqual(ForRestEditorDebugState.Ready, snapshot.State);
		Assert.AreEqual("Ready", snapshot.StatusText);
		Assert.AreEqual("GET Flow Echo", snapshot.SummaryText);
		StringAssert.Contains(snapshot.DetailText, "https://api.example.test/echo?trace={{trace_id}}");
		StringAssert.Contains(snapshot.DetailText, "send<=2");
		StringAssert.Contains(snapshot.DetailText, "vars 1");
		StringAssert.Contains(snapshot.DetailText, "tests 0");
		Assert.AreEqual("[]", snapshot.DiagnosticsJson);
		StringAssert.Contains(snapshot.DebugOutputText, "Compilation state: Ready");
		StringAssert.Contains(snapshot.DebugOutputText, "Max request.send() iterations: 2");
	}

	[TestMethod]
	public void Create_keeps_locationless_diagnostics_out_of_editor_markers()
	{
		ForRestScriptCompilationResult compilation = new(
			Document: null,
			Payload: null,
			Diagnostics:
			[
				new(ForRestScriptDiagnosticSeverity.Error, "The request section must declare a URL.", 0, 0)
			]);

		ForRestEditorDebugSnapshot snapshot = ForRestEditorDebugSnapshotFactory.Create(
			"""
			name "Broken"
			method GET
			""",
			compilation,
			"Echo Lab",
			"Local",
			"Broken",
			"https://fallback.local");

		JsonElement markers = JsonSerializer.Deserialize<JsonElement>(snapshot.DiagnosticsJson);

		Assert.AreEqual(ForRestEditorDebugState.Error, snapshot.State);
		Assert.AreEqual("Compile error", snapshot.StatusText);
		Assert.AreEqual(0, markers.GetArrayLength());
		StringAssert.Contains(snapshot.DetailText, "first at L1:C1");
		StringAssert.Contains(snapshot.DebugOutputText, "[Error] L1:1 The request section must declare a URL.");
	}
}
