using System.Text.Json;
using Microsoft.Maui.Dispatching;

namespace ForRest.Maui.Controls;

public partial class MonacoEditorSurface : ContentView
{
	private const string MonacoHostHtml = """
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <meta http-equiv="X-UA-Compatible" content="IE=edge" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <style>
    html, body, #container {
      height: 100%;
      margin: 0;
      padding: 0;
      overflow: hidden;
      background: #fcfdfe;
    }

    body {
      font-family: "Segoe UI", sans-serif;
    }
  </style>
</head>
<body>
  <div id="container"></div>
  <script>
    (function () {
      const version = "0.38.0";
      const baseUrl = `https://cdn.jsdelivr.net/npm/monaco-editor@${version}/min`;

      window.MonacoEnvironment = {
        getWorkerUrl: function () {
          const workerSource = `
            self.MonacoEnvironment = { baseUrl: '${baseUrl}/' };
            importScripts('${baseUrl}/vs/base/worker/workerMain.js');
          `;

          return "data:text/javascript;charset=utf-8," + encodeURIComponent(workerSource);
        }
      };

      function loadLoader() {
        return new Promise((resolve, reject) => {
          const script = document.createElement("script");
          script.src = `${baseUrl}/vs/loader.js`;
          script.onload = resolve;
          script.onerror = reject;
          document.head.appendChild(script);
        });
      }

      function registerLanguage(monaco) {
        monaco.languages.register({ id: "forrest" });
        monaco.languages.setMonarchTokensProvider("forrest", {
          tokenizer: {
            root: [
              [/^@\w[\w-]*/, "keyword.directive"],
              [/\b(GET|POST|PUT|PATCH|DELETE|OPTIONS|HEAD)\b/, "keyword.method"],
              [/\{\{[\w.\-]+\}\}/, "variable.placeholder"],
              [/\{[\w.\-]+\}/, "variable.placeholder"],
              [/[A-Za-z_][\w-]*(?=\s*:)/, "attribute.name"],
              [/\bhttps?:\/\/[^\s]+/, "string.url"],
              [/#[^\n]*/, "comment"],
              [/"([^"\\]|\\.)*"/, "string"],
              [/'([^'\\]|\\.)*'/, "string"],
              [/\b\d+\b/, "number"]
            ]
          }
        });

        monaco.languages.setLanguageConfiguration("forrest", {
          comments: { lineComment: "#" },
          autoClosingPairs: [
            { open: "{", close: "}" },
            { open: "[", close: "]" },
            { open: "(", close: ")" },
            { open: "\"", close: "\"" },
            { open: "'", close: "'" }
          ],
          surroundingPairs: [
            { open: "{", close: "}" },
            { open: "[", close: "]" },
            { open: "(", close: ")" },
            { open: "\"", close: "\"" },
            { open: "'", close: "'" }
          ]
        });

        monaco.editor.defineTheme("forrest-light", {
          base: "vs",
          inherit: true,
          rules: [
            { token: "keyword.directive", foreground: "1E6FB9", fontStyle: "bold" },
            { token: "keyword.method", foreground: "176AB8", fontStyle: "bold" },
            { token: "variable.placeholder", foreground: "A5691B" },
            { token: "attribute.name", foreground: "2F5D87" },
            { token: "string.url", foreground: "0B63A7" },
            { token: "comment", foreground: "7B8796" },
            { token: "number", foreground: "8F4B15" }
          ],
          colors: {
            "editor.background": "#FCFDFE",
            "editorLineNumber.foreground": "#8A96A5",
            "editorLineNumber.activeForeground": "#425160",
            "editorLineHighlightBackground": "#F4F7FA",
            "editor.selectionBackground": "#DCEBFA",
            "editor.inactiveSelectionBackground": "#EAF2FB",
            "editorCursor.foreground": "#16202A",
            "editorIndentGuide.background": "#E7ECF1",
            "editorIndentGuide.activeBackground": "#CDD7E1"
          }
        });
      }

      window.forRestHost = {
        editor: null,
        model: null,
        ready: false,
        create: function (monaco) {
          registerLanguage(monaco);
          this.model = monaco.editor.createModel("", "forrest");
          this.editor = monaco.editor.create(document.getElementById("container"), {
            model: this.model,
            theme: "forrest-light",
            automaticLayout: true,
            minimap: { enabled: false },
            scrollBeyondLastLine: false,
            lineNumbers: "on",
            lineNumbersMinChars: 4,
            glyphMargin: false,
            folding: false,
            readOnly: false,
            tabSize: 2,
            insertSpaces: true,
            fontFamily: "Cascadia Mono, Consolas, 'Courier New', monospace",
            fontSize: 13,
            wordWrap: "on",
            smoothScrolling: true,
            renderWhitespace: "selection",
            padding: { top: 16, bottom: 16 }
          });

          this.ready = true;
          this.editor.focus();
        },
        setValue: function (value) {
          if (!this.editor) {
            return;
          }

          const normalized = value ?? "";
          if (this.editor.getValue() === normalized) {
            return;
          }

          const viewState = this.editor.saveViewState();
          this.editor.setValue(normalized);
          if (viewState) {
            this.editor.restoreViewState(viewState);
          }
        },
        setLanguage: function (language) {
          if (!this.model || !window.monaco) {
            return;
          }

          window.monaco.editor.setModelLanguage(this.model, language || "forrest");
        },
        setReadOnly: function (value) {
          if (!this.editor) {
            return;
          }

          this.editor.updateOptions({ readOnly: !!value });
        },
        focus: function () {
          if (this.editor) {
            this.editor.focus();
          }
        }
      };

      loadLoader()
        .then(() => {
          require.config({ paths: { vs: `${baseUrl}/vs` } });
          require(["vs/editor/editor.main"], function () {
            window.forRestHost.create(window.monaco);
          });
        })
        .catch(() => {
          document.body.innerHTML = '<div style="padding:16px;color:#B2433D;font:12px Segoe UI, sans-serif;">Unable to load Monaco.</div>';
        });
    })();
  </script>
</body>
</html>
""";

	public static readonly BindableProperty TextProperty = BindableProperty.Create(
		nameof(Text),
		typeof(string),
		typeof(MonacoEditorSurface),
		string.Empty,
		defaultBindingMode: BindingMode.TwoWay,
		propertyChanged: OnTextChanged);

	public static readonly BindableProperty LanguageProperty = BindableProperty.Create(
		nameof(Language),
		typeof(string),
		typeof(MonacoEditorSurface),
		"forrest",
		propertyChanged: OnLanguageChanged);

	public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
		nameof(IsReadOnly),
		typeof(bool),
		typeof(MonacoEditorSurface),
		false,
		propertyChanged: OnIsReadOnlyChanged);

	private bool _isEditorReady;
	private bool _isWaitingForReady;
	private bool _isPullingEditorText;
	private bool _isPushingEditorText;
	private IDispatcherTimer? _syncTimer;

	public MonacoEditorSurface()
	{
		InitializeComponent();
		EditorWebView.Source = new HtmlWebViewSource
		{
			Html = MonacoHostHtml
		};
	}

	public string Text
	{
		get => (string)GetValue(TextProperty);
		set => SetValue(TextProperty, value);
	}

	public string Language
	{
		get => (string)GetValue(LanguageProperty);
		set => SetValue(LanguageProperty, value);
	}

	public bool IsReadOnly
	{
		get => (bool)GetValue(IsReadOnlyProperty);
		set => SetValue(IsReadOnlyProperty, value);
	}

	private static void OnTextChanged(BindableObject bindable, object? oldValue, object? newValue)
	{
		MonacoEditorSurface editor = (MonacoEditorSurface)bindable;
		if (editor._isPullingEditorText)
		{
			return;
		}

		_ = editor.ApplyTextAsync(newValue as string ?? string.Empty);
	}

	private static void OnLanguageChanged(BindableObject bindable, object? oldValue, object? newValue)
	{
		_ = ((MonacoEditorSurface)bindable).ApplyLanguageAsync(newValue as string ?? "forrest");
	}

	private static void OnIsReadOnlyChanged(BindableObject bindable, object? oldValue, object? newValue)
	{
		_ = ((MonacoEditorSurface)bindable).ApplyReadOnlyAsync((bool)(newValue ?? false));
	}

	private async void OnEditorWebViewNavigated(object? sender, WebNavigatedEventArgs e)
	{
		await EnsureEditorReadyAsync();
	}

	private async Task EnsureEditorReadyAsync()
	{
		if (_isEditorReady || _isWaitingForReady)
		{
			return;
		}

		_isWaitingForReady = true;

		try
		{
			for (int attempt = 0; attempt < 100; attempt++)
			{
				string? result = await SafeEvaluateAsync("window.forRestHost && window.forRestHost.ready ? 'true' : 'false';");
				if (result?.Contains("true", StringComparison.OrdinalIgnoreCase) == true)
				{
					_isEditorReady = true;
					break;
				}

				await Task.Delay(100);
			}

			if (_isEditorReady)
			{
				await ApplyLanguageAsync(Language);
				await ApplyReadOnlyAsync(IsReadOnly);
				await ApplyTextAsync(Text);
				await SafeEvaluateAsync("window.forRestHost && window.forRestHost.focus();");
				StartSyncTimer();
			}
		}
		finally
		{
			_isWaitingForReady = false;
		}
	}

	private async Task ApplyTextAsync(string value)
	{
		if (!_isEditorReady)
		{
			return;
		}

		_isPushingEditorText = true;

		try
		{
			string serializedValue = JsonSerializer.Serialize(value);
			await SafeEvaluateAsync($"window.forRestHost && window.forRestHost.setValue({serializedValue});");
		}
		finally
		{
			_isPushingEditorText = false;
		}
	}

	private async Task ApplyLanguageAsync(string language)
	{
		if (!_isEditorReady)
		{
			return;
		}

		string serializedLanguage = JsonSerializer.Serialize(language);
		await SafeEvaluateAsync($"window.forRestHost && window.forRestHost.setLanguage({serializedLanguage});");
	}

	private async Task ApplyReadOnlyAsync(bool isReadOnly)
	{
		if (!_isEditorReady)
		{
			return;
		}

		await SafeEvaluateAsync($"window.forRestHost && window.forRestHost.setReadOnly({isReadOnly.ToString().ToLowerInvariant()});");
	}

	private async Task<string?> SafeEvaluateAsync(string script)
	{
		try
		{
			return await EditorWebView.EvaluateJavaScriptAsync(script);
		}
		catch
		{
			return null;
		}
	}

	private void StartSyncTimer()
	{
		if (_syncTimer is not null || Dispatcher is null)
		{
			return;
		}

		_syncTimer = Dispatcher.CreateTimer();
		_syncTimer.Interval = TimeSpan.FromMilliseconds(450);
		_syncTimer.Tick += async (_, _) => await SyncEditorTextAsync();
		_syncTimer.Start();
	}

	private async Task SyncEditorTextAsync()
	{
		if (!_isEditorReady || _isPushingEditorText || _isPullingEditorText)
		{
			return;
		}

		string? result = await SafeEvaluateAsync("window.forRestHost && window.forRestHost.editor ? JSON.stringify(window.forRestHost.editor.getValue()) : null;");
		if (string.IsNullOrWhiteSpace(result) || string.Equals(result, "null", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		string editorText = ParseJavascriptStringResult(result);
		if (editorText == Text)
		{
			return;
		}

		_isPullingEditorText = true;
		try
		{
			Text = editorText;
		}
		finally
		{
			_isPullingEditorText = false;
		}
	}

	private static string ParseJavascriptStringResult(string result)
	{
		try
		{
			return JsonSerializer.Deserialize<string>(result) ?? string.Empty;
		}
		catch
		{
			return result.Trim('"');
		}
	}
}
