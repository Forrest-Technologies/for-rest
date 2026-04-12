using ForRest.Maui.Services;

namespace ForRest.Maui.Tests;

[TestClass]
public sealed class AiDebugSummaryFormatterTests
{
	[TestMethod]
	public void BuildSummary_returns_empty_when_input_is_blank()
	{
		Assert.AreEqual(string.Empty, AiDebugSummaryFormatter.BuildSummary(null));
		Assert.AreEqual(string.Empty, AiDebugSummaryFormatter.BuildSummary(string.Empty));
		Assert.AreEqual(string.Empty, AiDebugSummaryFormatter.BuildSummary("   \n\t  \n"));
	}

	[TestMethod]
	public void BuildSummary_extracts_runtime_fingerprint_and_executor_exception()
	{
		string trace = string.Join(
			"\n",
			"Runtime preparation summary",
			"Provider: Grok",
			"Transport: ChatCompletions",
			"Model: grok-4-fast-reasoning",
			"System prompt override: (blank)",
			"Docs search enabled: True",
			"Document patch enabled: True",
			"Registered tools: foo, bar",
			"Selected topics: alpha (embedded)",
			"Settings issues: none",
			string.Empty,
			"System prompt handed to agent",
			"You are the For-Rest AI assistant.",
			"...long boring system prompt that we never want pasted on Android...",
			"Operating rules:",
			"- Keep responses brief.",
			string.Empty,
			"Original user request: think like a hacker, build a fun api workspace",
			string.Empty,
			"Executor exception",
			"System.Net.WebException: Socket closed",
			" ---> Java.Net.SocketException: Socket closed",
			"   at Java.Interop.JniEnvironment.InstanceMethods.CallIntMethod(...)",
			"   at System.ClientModel.Primitives.HttpClientPipelineTransport.ProcessCoreAsync(...)",
			"--- End of stack trace from previous location ---",
			string.Empty,
			"Outbound Grok request body (post-sanitize)",
			"{\"messages\":[{\"role\":\"system\",\"content\":\"...giant payload...\"}]}");

		string summary = AiDebugSummaryFormatter.BuildSummary(trace);

		StringAssert.Contains(summary, "Model info");
		StringAssert.Contains(summary, "Provider: Grok");
		StringAssert.Contains(summary, "Transport: ChatCompletions");
		StringAssert.Contains(summary, "Model: grok-4-fast-reasoning");
		StringAssert.Contains(summary, "Settings issues: none");

		StringAssert.Contains(summary, "Original user request");
		StringAssert.Contains(summary, "think like a hacker, build a fun api workspace");

		StringAssert.Contains(summary, "Executor exception");
		StringAssert.Contains(summary, "System.Net.WebException: Socket closed");
		StringAssert.Contains(summary, "Java.Net.SocketException: Socket closed");
		StringAssert.Contains(summary, "JniEnvironment.InstanceMethods.CallIntMethod");

		// Drop the giant body and the verbose system prompt entirely so we
		// fit inside Android's clipboard ceiling.
		Assert.IsFalse(summary.Contains("...long boring system prompt", StringComparison.Ordinal));
		Assert.IsFalse(summary.Contains("Operating rules:", StringComparison.Ordinal));
		Assert.IsFalse(summary.Contains("...giant payload...", StringComparison.Ordinal));
		Assert.IsFalse(summary.Contains("Outbound Grok request body", StringComparison.Ordinal));
		Assert.IsFalse(summary.Contains("System prompt handed to agent", StringComparison.Ordinal));
	}

	[TestMethod]
	public void BuildSummary_includes_provider_error_body_when_present()
	{
		string trace = string.Join(
			"\n",
			"Runtime preparation summary",
			"Provider: OpenAI",
			"Transport: Responses",
			"Model: gpt-5",
			"Settings issues: none",
			string.Empty,
			"Provider error body",
			"{\"error\":{\"message\":\"context_length_exceeded\",\"code\":400}}",
			string.Empty,
			"Executor exception",
			"System.ClientModel.ClientResultException: HTTP 400");

		string summary = AiDebugSummaryFormatter.BuildSummary(trace);

		StringAssert.Contains(summary, "Provider error body");
		StringAssert.Contains(summary, "context_length_exceeded");
		StringAssert.Contains(summary, "Executor exception");
		StringAssert.Contains(summary, "ClientResultException");
	}

	[TestMethod]
	public void BuildSummary_falls_back_to_truncated_input_when_no_known_sections()
	{
		string trace = "Some unstructured exception text\n  at Foo.Bar()\n  at Baz.Qux()";

		string summary = AiDebugSummaryFormatter.BuildSummary(trace);

		Assert.AreEqual(trace, summary);
	}

	[TestMethod]
	public void BuildSummary_truncates_overlong_stack_traces()
	{
		string longStack = string.Join(
			"\n",
			Enumerable.Range(0, 5_000)
				.Select(static index => $"   at Frame{index}.Method()"));
		string trace = string.Join(
			"\n",
			"Executor exception",
			longStack);

		string summary = AiDebugSummaryFormatter.BuildSummary(trace);

		StringAssert.Contains(summary, "Executor exception");
		StringAssert.Contains(summary, "[truncated]");
		Assert.IsTrue(summary.Length < 16 * 1024, $"Summary exceeded clipboard ceiling: {summary.Length}");
	}

	[TestMethod]
	public void BuildSummary_handles_crlf_line_endings()
	{
		string trace = "Runtime preparation summary\r\nProvider: Grok\r\nTransport: ChatCompletions\r\nModel: grok-4\r\nSettings issues: none\r\n\r\nExecutor exception\r\nSystem.Exception: boom";

		string summary = AiDebugSummaryFormatter.BuildSummary(trace);

		StringAssert.Contains(summary, "Provider: Grok");
		StringAssert.Contains(summary, "System.Exception: boom");
	}
}
