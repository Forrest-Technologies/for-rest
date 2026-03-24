using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ForRest.Maui.Theming;
using Microsoft.Maui.Dispatching;

namespace ForRest.Maui.Controls;

public partial class MonacoEditorSurface : ContentView
{
	public event EventHandler? SendRequested;

	private const string MonacoHostHtml = """
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <meta http-equiv="X-UA-Compatible" content="IE=edge" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <style>
    :root {
      --host-background: #fbfcfd;
      --editable-span-bg: rgba(30, 111, 185, 0.08);
      --editable-span-border: rgba(30, 111, 185, 0.18);
    }

    html, body, #container {
      height: 100%;
      margin: 0;
      padding: 0;
      overflow: hidden;
      background: var(--host-background);
    }

    body {
      font-family: "Segoe UI", sans-serif;
      -webkit-font-smoothing: antialiased;
      text-rendering: optimizeLegibility;
    }

    .editable-span {
      background: var(--editable-span-bg);
      border-bottom: 1px solid var(--editable-span-border);
      border-radius: 2px;
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

      function defineThemes(monaco) {
        const definitions = {
          "forrest-light": {
            base: "vs",
            inherit: true,
            rules: [
              { token: "keyword.directive", foreground: "566B84", fontStyle: "bold" },
              { token: "keyword.method", foreground: "4E6D8B", fontStyle: "bold" },
              { token: "keyword.flow", foreground: "54697F", fontStyle: "bold" },
              { token: "keyword.literal", foreground: "6B5E9C" },
              { token: "variable.definition", foreground: "7A6122" },
              { token: "variable.placeholder", foreground: "A06C28" },
              { token: "attribute.name", foreground: "4D6277" },
              { token: "string", foreground: "2B6B58" },
              { token: "string.url", foreground: "446F98" },
              { token: "comment", foreground: "8A95A3", fontStyle: "italic" },
              { token: "number", foreground: "94551C" },
              { token: "operator", foreground: "708090" }
            ],
            colors: {
              "editor.background": "#FCFDFE",
              "editor.foreground": "#16202A",
              "editorGutter.background": "#F2F4F7",
              "editorLineNumber.foreground": "#9CA7B3",
              "editorLineNumber.activeForeground": "#384552",
              "editorLineHighlightBackground": "#F5F7FA",
              "editor.selectionBackground": "#E1EAF2",
              "editor.inactiveSelectionBackground": "#EDF3F8",
              "editorCursor.foreground": "#16202A",
              "editorWhitespace.foreground": "#D5DDE6",
              "editorBracketMatch.background": "#ECF1F6",
              "editorBracketMatch.border": "#D2DCE6",
              "editorIndentGuide.background": "#E6EBF0",
              "editorIndentGuide.activeBackground": "#C9D2DB"
            }
          },
          "forrest-azure": {
            base: "vs",
            inherit: true,
            rules: [
              { token: "keyword.directive", foreground: "0E5FA5", fontStyle: "bold" },
              { token: "keyword.method", foreground: "176AB8", fontStyle: "bold" },
              { token: "keyword.flow", foreground: "345E86", fontStyle: "bold" },
              { token: "keyword.literal", foreground: "5B5D98" },
              { token: "variable.definition", foreground: "7B5B18" },
              { token: "variable.placeholder", foreground: "A5691B" },
              { token: "attribute.name", foreground: "46617D" },
              { token: "string", foreground: "1F6953" },
              { token: "string.url", foreground: "0B63A7" },
              { token: "comment", foreground: "8190A0", fontStyle: "italic" },
              { token: "number", foreground: "95511A" },
              { token: "operator", foreground: "6A7786" }
            ],
            colors: {
              "editor.background": "#EDF5FD",
              "editor.foreground": "#0F2236",
              "editorGutter.background": "#E2EDF8",
              "editorLineNumber.foreground": "#5E7B97",
              "editorLineNumber.activeForeground": "#1E3B57",
              "editorLineHighlightBackground": "#E1F0FC",
              "editor.selectionBackground": "#CFE4F8",
              "editor.inactiveSelectionBackground": "#DCEBFA",
              "editorCursor.foreground": "#0F2236",
              "editorWhitespace.foreground": "#B3C9DE",
              "editorBracketMatch.background": "#D7EBFB",
              "editorBracketMatch.border": "#93BCDF",
              "editorIndentGuide.background": "#C5D8EB",
              "editorIndentGuide.activeBackground": "#9FBAD5"
            }
          },
          "forrest-dark": {
            base: "vs-dark",
            inherit: true,
            rules: [
              { token: "keyword.directive", foreground: "7EBEFF", fontStyle: "bold" },
              { token: "keyword.method", foreground: "5EA9F1", fontStyle: "bold" },
              { token: "keyword.flow", foreground: "91BCE8", fontStyle: "bold" },
              { token: "keyword.literal", foreground: "B59BFF" },
              { token: "variable.definition", foreground: "D7B36B" },
              { token: "variable.placeholder", foreground: "F0BE72" },
              { token: "attribute.name", foreground: "A2C2DD" },
              { token: "string", foreground: "7FD2B3" },
              { token: "string.url", foreground: "7BC1FF" },
              { token: "comment", foreground: "7F8C9C", fontStyle: "italic" },
              { token: "number", foreground: "F2A665" },
              { token: "operator", foreground: "8FA2B5" }
            ],
            colors: {
              "editor.background": "#141B24",
              "editor.foreground": "#EEF3F7",
              "editorGutter.background": "#1A232D",
              "editorLineNumber.foreground": "#6F8092",
              "editorLineNumber.activeForeground": "#C5D2DE",
              "editorLineHighlightBackground": "#19222D",
              "editor.selectionBackground": "#29425F",
              "editor.inactiveSelectionBackground": "#223648",
              "editorCursor.foreground": "#EEF3F7",
              "editorWhitespace.foreground": "#2E3A46",
              "editorBracketMatch.background": "#233547",
              "editorBracketMatch.border": "#33526E",
              "editorIndentGuide.background": "#27313A",
              "editorIndentGuide.activeBackground": "#3E4B57"
            }
          },
          "forrest-black": {
            base: "vs-dark",
            inherit: true,
            rules: [
              { token: "keyword.directive", foreground: "A7C8F2", fontStyle: "bold" },
              { token: "keyword.method", foreground: "8FB4E5", fontStyle: "bold" },
              { token: "keyword.flow", foreground: "C2D6EF", fontStyle: "bold" },
              { token: "keyword.literal", foreground: "C4AEFF" },
              { token: "variable.definition", foreground: "D8B46E" },
              { token: "variable.placeholder", foreground: "EDC583" },
              { token: "attribute.name", foreground: "C0D3E6" },
              { token: "string", foreground: "8FD7BD" },
              { token: "string.url", foreground: "9EC5F2" },
              { token: "comment", foreground: "7D8793", fontStyle: "italic" },
              { token: "number", foreground: "EEAA73" },
              { token: "operator", foreground: "95A4B3" }
            ],
            colors: {
              "editor.background": "#101419",
              "editor.foreground": "#F3F6F8",
              "editorGutter.background": "#151B21",
              "editorLineNumber.foreground": "#66727F",
              "editorLineNumber.activeForeground": "#E4EBF1",
              "editorLineHighlightBackground": "#141A20",
              "editor.selectionBackground": "#243341",
              "editor.inactiveSelectionBackground": "#1D2833",
              "editorCursor.foreground": "#F3F6F8",
              "editorWhitespace.foreground": "#27313B",
              "editorBracketMatch.background": "#1E2B37",
              "editorBracketMatch.border": "#314656",
              "editorIndentGuide.background": "#202932",
              "editorIndentGuide.activeBackground": "#34414D"
            }
          },
          "forrest-amber": {
            base: "vs",
            inherit: true,
            rules: [
              { token: "keyword.directive", foreground: "9A5C1A", fontStyle: "bold" },
              { token: "keyword.method", foreground: "B36B1E", fontStyle: "bold" },
              { token: "keyword.flow", foreground: "8A5D27", fontStyle: "bold" },
              { token: "keyword.literal", foreground: "875D47" },
              { token: "variable.definition", foreground: "8B6430" },
              { token: "variable.placeholder", foreground: "B36B1E" },
              { token: "attribute.name", foreground: "7A6545" },
              { token: "string", foreground: "6D7152" },
              { token: "string.url", foreground: "9B6B2F" },
              { token: "comment", foreground: "9A8B75", fontStyle: "italic" },
              { token: "number", foreground: "A45D1F" },
              { token: "operator", foreground: "857661" }
            ],
            colors: {
              "editor.background": "#FFFCF6",
              "editor.foreground": "#2B2218",
              "editorGutter.background": "#F5ECDD",
              "editorLineNumber.foreground": "#A08F79",
              "editorLineNumber.activeForeground": "#6A5034",
              "editorLineHighlightBackground": "#FBF3E6",
              "editor.selectionBackground": "#F2DFC0",
              "editor.inactiveSelectionBackground": "#F7ECDD",
              "editorCursor.foreground": "#6D4A20",
              "editorWhitespace.foreground": "#E1D3BE",
              "editorBracketMatch.background": "#F8E8D2",
              "editorBracketMatch.border": "#DDBD8E",
              "editorIndentGuide.background": "#E9DCC8",
              "editorIndentGuide.activeBackground": "#D5BE97"
            }
          }
        };

        Object.entries(definitions).forEach(([name, definition]) => {
          monaco.editor.defineTheme(name, definition);
        });
      }

      function decodeBase64Utf8(value) {
        if (!value) {
          return "";
        }

        const binary = atob(value);
        const bytes = new Uint8Array(binary.length);
        for (let index = 0; index < binary.length; index++) {
          bytes[index] = binary.charCodeAt(index);
        }

        return new TextDecoder().decode(bytes);
      }

      function applyHostThemeChrome(themeKey) {
        const chrome = {
          "forrest-light": {
            background: "#FCFDFE",
            editableBackground: "rgba(78, 109, 139, 0.10)",
            editableBorder: "rgba(78, 109, 139, 0.22)"
          },
          "forrest-azure": {
            background: "#EDF5FD",
            editableBackground: "rgba(0, 120, 212, 0.14)",
            editableBorder: "rgba(0, 120, 212, 0.30)"
          },
          "forrest-dark": {
            background: "#141B24",
            editableBackground: "rgba(115, 183, 246, 0.16)",
            editableBorder: "rgba(115, 183, 246, 0.30)"
          },
          "forrest-black": {
            background: "#101419",
            editableBackground: "rgba(143, 180, 229, 0.16)",
            editableBorder: "rgba(143, 180, 229, 0.30)"
          },
          "forrest-amber": {
            background: "#FFFCF6",
            editableBackground: "rgba(179, 107, 30, 0.12)",
            editableBorder: "rgba(179, 107, 30, 0.24)"
          }
        };

        const next = chrome[themeKey] || chrome["forrest-azure"];
        document.documentElement.style.setProperty("--host-background", next.background);
        document.documentElement.style.setProperty("--editable-span-bg", next.editableBackground);
        document.documentElement.style.setProperty("--editable-span-border", next.editableBorder);
      }

      function registerLanguage(monaco) {
        monaco.languages.register({ id: "forrest" });
        monaco.languages.setMonarchTokensProvider("forrest", {
          tokenizer: {
            root: [
              [/#[^\n]*/, "comment"],
              [/\b(name|method|url|timeout|max_send_iterations|redirects|ssl|history|content_type|header|query|body|form|multipart|extract|expect|repeat|retry|auth)\b/, "keyword.directive"],
              [/\b(GET|POST|PUT|PATCH|DELETE|OPTIONS|HEAD)\b/, "keyword.method"],
              [/\b(request\.send)\b/, "keyword.flow"],
              [/\b(await|runtime|request|response|workspace|variables|json|console|encoding|crypto|regex|log|warn|error|let|if|else|while|for|foreach|in)\b/, "keyword.flow"],
              [/\b(true|false|null)\b/, "keyword.literal"],
              [/[A-Za-z_][A-Za-z0-9_]*(?=\s*=)/, "variable.definition"],
              [/\{\{[\w.\-]+\}\}/, "variable.placeholder"],
              [/\{[\w.\-]+\}/, "variable.placeholder"],
              [/[A-Za-z_][A-Za-z0-9_]*(?=\s*:)/, "attribute.name"],
              [/\bhttps?:\/\/[^\s]+/, "string.url"],
              [/[“„«][^”»\n]*[”»]/, "string"],
              [/"([^"\\]|\\.)*"/, "string"],
              [/\b\d+\b/, "number"],
              [/[><=!]=?/, "operator"]
            ]
          }
        });

        monaco.languages.setLanguageConfiguration("forrest", {
          comments: { lineComment: "#" },
          wordPattern: /\{\{[\w.\-]+\}\}|[A-Za-z_][A-Za-z0-9_\.]*/,
          autoClosingPairs: [
            { open: "{", close: "}" },
            { open: "[", close: "]" },
            { open: "(", close: ")" },
            { open: "\"", close: "\"" },
            { open: "“", close: "”" },
            { open: "«", close: "»" },
            { open: "'", close: "'" }
          ],
          surroundingPairs: [
            { open: "{", close: "}" },
            { open: "[", close: "]" },
            { open: "(", close: ")" },
            { open: "\"", close: "\"" },
            { open: "“", close: "”" },
            { open: "«", close: "»" },
            { open: "'", close: "'" }
          ]
        });

        monaco.languages.registerCompletionItemProvider("forrest", {
          provideCompletionItems: function (model, position) {
            const word = model.getWordUntilPosition(position) || {
              startColumn: position.column,
              endColumn: position.column
            };
            const range = {
              startLineNumber: position.lineNumber,
              endLineNumber: position.lineNumber,
              startColumn: word.startColumn,
              endColumn: word.endColumn
            };

            const insertAsSnippet = monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet;
            const kind = monaco.languages.CompletionItemKind;
            return {
              suggestions: [
                {
                  label: "name",
                  kind: kind.Keyword,
                  insertText: "name \"${1:Request Name}\"",
                  insertTextRules: insertAsSnippet,
                  documentation: "Give the current .frs program a request name.",
                  range
                },
                {
                  label: "method",
                  kind: kind.Keyword,
                  insertText: "method ${1|GET,POST,PUT,PATCH,DELETE,OPTIONS,HEAD|}",
                  insertTextRules: insertAsSnippet,
                  documentation: "Set the HTTP method.",
                  range
                },
                {
                  label: "url",
                  kind: kind.Keyword,
                  insertText: "url \"${1:https://httpbin.org/anything}\"",
                  insertTextRules: insertAsSnippet,
                  documentation: "Set the target URL.",
                  range
                },
                {
                  label: "header",
                  kind: kind.Keyword,
                  insertText: "header \"${1:Header-Name}\" = ${2:\"value\"}",
                  insertTextRules: insertAsSnippet,
                  documentation: "Add a request header.",
                  range
                },
                {
                  label: "expect",
                  kind: kind.Keyword,
                  insertText: "expect status == ${1:200} \"${2:returns 200}\"",
                  insertTextRules: insertAsSnippet,
                  documentation: "Add a response assertion.",
                  range
                },
                {
                  label: "body json",
                  kind: kind.Snippet,
                  insertText: "body json \"\"\"\n${1:{\n  \\\"id\\\": \\\"{{trace_id}}\\\"\n}}\n\"\"\"",
                  insertTextRules: insertAsSnippet,
                  documentation: "Add a JSON request body.",
                  range
                },
                {
                  label: "if",
                  kind: kind.Snippet,
                  insertText: "if ${1:condition} {\n  $0\n}",
                  insertTextRules: insertAsSnippet,
                  documentation: "Conditional flow block.",
                  range
                },
                {
                  label: "while",
                  kind: kind.Snippet,
                  insertText: "while ${1:condition} {\n  $0\n}",
                  insertTextRules: insertAsSnippet,
                  documentation: "Loop while a condition is true.",
                  range
                },
                {
                  label: "foreach",
                  kind: kind.Snippet,
                  insertText: "foreach ${1:item} in ${2:response.items} {\n  $0\n}",
                  insertTextRules: insertAsSnippet,
                  documentation: "Iterate a collection.",
                  range
                },
                {
                  label: "request.send()",
                  kind: kind.Method,
                  insertText: "request.send()",
                  documentation: "Send the current request and update the global response.",
                  range
                },
                {
                  label: "response.json()",
                  kind: kind.Method,
                  insertText: "response.json()",
                  documentation: "Parse the latest response body as JSON when the body is valid JSON.",
                  range
                },
                {
                  label: "runtime",
                  kind: kind.Keyword,
                  insertText: "runtime ${1:name} = ${2:value}",
                  insertTextRules: insertAsSnippet,
                  documentation: "Persist a runtime variable for later interpolation or tests.",
                  range
                },
                {
                  label: "request.headers",
                  kind: kind.Property,
                  insertText: "request.headers[\"${1:Header-Name}\"] = ${2:\"value\"}",
                  insertTextRules: insertAsSnippet,
                  documentation: "Set a request header from flow.",
                  range
                },
                {
                  label: "response.status",
                  kind: kind.Property,
                  insertText: "response.status",
                  documentation: "Latest response status code.",
                  range
                },
                {
                  label: "log",
                  kind: kind.Function,
                  insertText: "log ${1:\"message\"}",
                  insertTextRules: insertAsSnippet,
                  documentation: "Write an info entry to the debug console.",
                  range
                },
                {
                  label: "warn",
                  kind: kind.Function,
                  insertText: "warn ${1:\"message\"}",
                  insertTextRules: insertAsSnippet,
                  documentation: "Write a warning entry to the debug console.",
                  range
                },
                {
                  label: "error",
                  kind: kind.Function,
                  insertText: "error ${1:\"message\"}",
                  insertTextRules: insertAsSnippet,
                  documentation: "Write an error entry to the debug console.",
                  range
                },
                {
                  label: "range",
                  kind: kind.Function,
                  insertText: "range(${1:0}, ${2:3})",
                  insertTextRules: insertAsSnippet,
                  documentation: "Produce a sequence that works well with foreach loops.",
                  range
                },
                {
                  label: "encoding.base64",
                  kind: kind.Function,
                  insertText: "encoding.Base64Encode(${1:value})",
                  insertTextRules: insertAsSnippet,
                  documentation: "Encode a value to Base64.",
                  range
                },
                {
                  label: "crypto.sha256",
                  kind: kind.Function,
                  insertText: "crypto.Sha256(${1:value})",
                  insertTextRules: insertAsSnippet,
                  documentation: "Hash a value with SHA-256.",
                  range
                },
                {
                  label: "regex.match",
                  kind: kind.Function,
                  insertText: "regex.Match(${1:input}, ${2:pattern})",
                  insertTextRules: insertAsSnippet,
                  documentation: "Extract a regex match or capture group.",
                  range
                }
              ]
            };
          }
        });

        monaco.languages.registerHoverProvider("forrest", {
          provideHover: function (model, position) {
            const lineText = model.getLineContent(position.lineNumber) || "";
            const wordInfo = model.getWordAtPosition(position);
            const word = wordInfo ? wordInfo.word : "";
            const docs = {
              "request": [
                "**request**",
                "Mutable request API for the current `.frs` program.",
                "Core members: `request.method`, `request.url`, `request.body`, `request.headers`, `request.send()`."
              ],
              "request.send": [
                "**request.send()**",
                "Sends the current request, updates the global `response`, and returns the latest response snapshot.",
                "Guarded by `max_send_iterations` to keep scripted send loops safe."
              ],
              "response": [
                "**response**",
                "Latest response snapshot. Properties are available directly from JSON payload fields as dynamic members.",
                "Examples: `response.status`, `response.headers`, `response.traceId`, `response.items[0]`, `response.json()`."
              ],
              "response.json": [
                "**response.json()**",
                "Parses the latest response body as JSON and returns a dynamic JSON node when possible."
              ],
              "runtime": [
                "**runtime name = value**",
                "Creates or updates a runtime variable that can be reused later in the script and in `{{templates}}`."
              ],
              "expect": [
                "**expect ...**",
                "Adds a response assertion. Examples: `expect status == 200 \\\"ok\\\"`, `expect header \\\"Content-Type\\\" contains \\\"json\\\" \\\"json body\\\"`."
              ],
              "foreach": [
                "**foreach item in source { }**",
                "Preferred loop form in ForRest. Iterate arrays, `range(...)`, header collections, or JSON arrays from `response`."
              ],
              "while": [
                "**while condition { }**",
                "Repeat while the condition stays truthy. Use `request.remaining_send_iterations` to keep loops safe."
              ],
              "if": [
                "**if / else if / else**",
                "Standard conditional control flow for `.frs` scripts."
              ],
              "log": [
                "**log**",
                "Writes an info entry to the debug console."
              ],
              "warn": [
                "**warn**",
                "Writes a warning entry to the debug console."
              ],
              "error": [
                "**error**",
                "Writes an error entry to the debug console."
              ]
            };

            const lookupWord = docs[word]
              ? word
              : (word.includes(".") ? word.split(".")[0] : word);
            const content = docs[lookupWord];
            if (!content) {
              return null;
            }

            const startColumn = wordInfo ? wordInfo.startColumn : 1;
            const endColumn = wordInfo ? wordInfo.endColumn : Math.max(1, lineText.length + 1);
            return {
              range: new monaco.Range(position.lineNumber, startColumn, position.lineNumber, endColumn),
              contents: content.map((value) => ({ value }))
            };
          }
        });

        monaco.languages.register({ id: "settings-toml" });
        monaco.languages.setMonarchTokensProvider("settings-toml", {
          tokenizer: {
            root: [
              [/#[^\n]*/, "comment"],
              [/\[[^\]]+\]/, "keyword.directive"],
              [/[A-Za-z][\w-]*(?=\s*=)/, "attribute.name"],
              [/\b(true|false)\b/i, "keyword.literal"],
              [/"([^"\\]|\\.)*"/, "string"],
              [/\b\d+\b/, "number"]
            ]
          }
        });

        monaco.languages.setLanguageConfiguration("settings-toml", {
          comments: { lineComment: "#" }
        });

        defineThemes(monaco);
      }

      function requestHostCommand(commandName) {
        try {
          window.location.href = `forrest://command/${commandName}`;
        } catch {
        }
      }

      const supportedSettingsKeys = ["light", "azure", "dark", "black", "amber"];

      window.forRestHost = {
        editor: null,
        model: null,
        ready: false,
        pendingValue: "",
        pendingLanguage: "forrest",
        pendingTheme: "forrest-azure",
        pendingReadOnly: false,
        pendingEditableRanges: [],
        pendingDiagnostics: [],
        pendingShouldApplyText: true,
        editableDecorations: [],
        currentEditableRanges: [],
        lastKnownValue: "",
        isApplyingProtectedEdit: false,
        create: function (monaco) {
          registerLanguage(monaco);
          this.model = monaco.editor.createModel(this.pendingValue || "", this.pendingLanguage || "forrest");
          this.editor = monaco.editor.create(document.getElementById("container"), {
            model: this.model,
            theme: this.pendingTheme,
            automaticLayout: true,
            minimap: { enabled: false },
            scrollBeyondLastLine: false,
            lineNumbers: "on",
            lineNumbersMinChars: 3,
            lineDecorationsWidth: 10,
            glyphMargin: false,
            folding: false,
            readOnly: this.pendingReadOnly,
            tabSize: 2,
            insertSpaces: true,
            fontFamily: "Cascadia Mono, Consolas, monospace",
            fontSize: 13.5,
            lineHeight: 20,
            letterSpacing: 0.1,
            wordWrap: "on",
            wordBasedSuggestions: "off",
            smoothScrolling: true,
            renderLineHighlight: "line",
            renderWhitespace: "selection",
            cursorBlinking: "smooth",
            cursorSmoothCaretAnimation: "on",
            cursorWidth: 2,
            guides: {
              indentation: true,
              highlightActiveIndentation: true
            },
            bracketPairColorization: {
              enabled: false
            },
            matchBrackets: "always",
            overviewRulerBorder: false,
            suggest: {
              showWords: false
            },
            scrollbar: {
              verticalScrollbarSize: 10,
              horizontalScrollbarSize: 10,
              useShadows: false,
              alwaysConsumeMouseWheel: false
            },
            padding: { top: 8, bottom: 24 }
          });

          this.editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter, function () {
            requestHostCommand("send");
          });
          this.editor.addCommand(monaco.KeyCode.F5, function () {
            requestHostCommand("send");
          });

          this.lastKnownValue = this.editor.getValue();
          this.editor.onDidChangeModelContent((event) => {
            this.handleModelContentChanged(event);
          });
          this.ready = true;
          this.applyState({
            text: this.pendingValue,
            language: this.pendingLanguage,
            themeKey: this.pendingTheme,
            isReadOnly: this.pendingReadOnly,
            editableRanges: this.pendingEditableRanges,
            diagnostics: this.pendingDiagnostics,
            applyText: this.pendingShouldApplyText
          });
          this.editor.focus();
        },
        replaceEditorValue: function (value, restoreViewState) {
          const normalized = value ?? "";
          const viewState = restoreViewState ? this.editor.saveViewState() : null;
          this.isApplyingProtectedEdit = true;
          try {
            this.editor.setValue(normalized);
          } finally {
            this.isApplyingProtectedEdit = false;
          }

          if (viewState) {
            this.editor.restoreViewState(viewState);
          }

          this.lastKnownValue = this.editor.getValue();
          this.refreshEditableDecorations();
          this.editor.layout();
        },
        parseEditableRanges: function (state) {
          if (Array.isArray(state?.editableRanges)) {
            return state.editableRanges;
          }

          if (typeof state?.editableRangesJson === "string" && state.editableRangesJson.length > 0) {
            try {
              const parsed = JSON.parse(state.editableRangesJson);
              return Array.isArray(parsed) ? parsed : [];
            } catch {
              return [];
            }
          }

          return [];
        },
        parseDiagnostics: function (state) {
          if (Array.isArray(state?.diagnostics)) {
            return state.diagnostics;
          }

          if (typeof state?.diagnosticsJson === "string" && state.diagnosticsJson.length > 0) {
            try {
              const parsed = JSON.parse(state.diagnosticsJson);
              return Array.isArray(parsed) ? parsed : [];
            } catch {
              return [];
            }
          }

          return [];
        },
        applyDiagnostics: function () {
          if (!this.model || !window.monaco) {
            return;
          }

          if (this.model.getLanguageId() !== "forrest") {
            window.monaco.editor.setModelMarkers(this.model, "forrest-diagnostics", []);
            return;
          }

          const severityLookup = {
            "error": window.monaco.MarkerSeverity.Error,
            "warning": window.monaco.MarkerSeverity.Warning
          };

          const markers = (this.pendingDiagnostics || [])
            .filter((item) => item && typeof item.startLineNumber === "number")
            .map((item) => ({
              startLineNumber: item.startLineNumber,
              startColumn: item.startColumn,
              endLineNumber: item.endLineNumber,
              endColumn: item.endColumn,
              message: typeof item.message === "string" ? item.message : "ForRest diagnostic",
              severity: severityLookup[String(item.severity || "error").toLowerCase()] || window.monaco.MarkerSeverity.Error
            }));

          window.monaco.editor.setModelMarkers(this.model, "forrest-diagnostics", markers);
        },
        applyState: function (state) {
          const nextState = state || {};
          const shouldApplyText = !!nextState.applyText;
          this.pendingShouldApplyText = shouldApplyText;
          if (shouldApplyText || !this.model) {
            this.pendingValue = typeof nextState.text === "string" ? nextState.text : "";
          }
          this.pendingLanguage = typeof nextState.language === "string" && nextState.language.length > 0
            ? nextState.language
            : "forrest";
          this.pendingTheme = typeof nextState.themeKey === "string" && nextState.themeKey.length > 0
            ? nextState.themeKey
            : "forrest-azure";
          this.pendingReadOnly = !!nextState.isReadOnly;
          this.pendingEditableRanges = this.parseEditableRanges(nextState);
          this.pendingDiagnostics = this.parseDiagnostics(nextState);
          applyHostThemeChrome(this.pendingTheme);

          if (this.model && window.monaco && this.model.getLanguageId() !== this.pendingLanguage) {
            window.monaco.editor.setModelLanguage(this.model, this.pendingLanguage);
          }

          if (this.editor) {
            this.editor.updateOptions({ readOnly: this.pendingReadOnly });

            if (shouldApplyText) {
              const normalized = this.pendingValue ?? "";
              if (this.editor.getValue() !== normalized) {
                this.replaceEditorValue(normalized, true);
              } else {
                this.refreshEditableDecorations();
                this.editor.layout();
              }
            } else {
              this.refreshEditableDecorations();
              this.editor.layout();
            }
          }

          if (this.editor && window.monaco) {
            window.monaco.editor.setTheme(this.pendingTheme);
          }

          this.applyDiagnostics();

          this.lastKnownValue = this.editor ? this.editor.getValue() : this.pendingValue;

          return JSON.stringify({
            ok: true,
            valueLength: (this.lastKnownValue || "").length,
            lineCount: this.model ? this.model.getLineCount() : 0,
            language: this.pendingLanguage,
            themeKey: this.pendingTheme
          });
        },
        applyStateFromBase64: function (base64State) {
          try {
            const json = decodeBase64Utf8(base64State);
            const state = json ? JSON.parse(json) : {};
            return this.applyState(state);
          } catch (error) {
            return JSON.stringify({
              ok: false,
              error: String(error)
            });
          }
        },
        isProtectedSettingsEditor: function () {
          return !this.pendingReadOnly &&
            this.pendingLanguage === "settings-toml" &&
            Array.isArray(this.pendingEditableRanges) &&
            this.pendingEditableRanges.length > 0;
        },
        normalizeRangeForCurrentLine: function (descriptor) {
          if (!this.model ||
              !descriptor ||
              typeof descriptor.startLineNumber !== "number" ||
              typeof descriptor.startColumn !== "number" ||
              typeof descriptor.endColumn !== "number") {
            return null;
          }

          if (descriptor.startLineNumber < 1 || descriptor.startLineNumber > this.model.getLineCount()) {
            return null;
          }

          const lineText = this.model.getLineContent(descriptor.startLineNumber);
          const match = lineText.match(/^(\s*[A-Za-z][\w-]*\s*=\s*)(.*?)(\s*(#.*)?)$/);
          if (!match) {
            return descriptor;
          }

          const startColumn = match[1].length + 1;
          const valueLength = Math.max(match[2].length, 1);
          return {
            startLineNumber: descriptor.startLineNumber,
            startColumn: startColumn,
            endLineNumber: descriptor.startLineNumber,
            endColumn: startColumn + valueLength
          };
        },
        refreshEditableDecorations: function () {
          if (!this.editor || !window.monaco) {
            return;
          }

          this.currentEditableRanges = this.pendingEditableRanges
            .map((range) => this.normalizeRangeForCurrentLine(range))
            .filter((range) => !!range);
          const decorations = this.isProtectedSettingsEditor()
            ? this.currentEditableRanges.map((range) => ({
                range: new window.monaco.Range(range.startLineNumber, range.startColumn, range.endLineNumber, range.endColumn),
                options: {
                  inlineClassName: "editable-span",
                  stickiness: window.monaco.editor.TrackedRangeStickiness.NeverGrowsWhenTypingAtEdges
                }
              }))
            : [];

          this.editableDecorations = this.editor.deltaDecorations(this.editableDecorations, decorations);
        },
        isChangeWithinEditableRanges: function (change) {
          if (!this.isProtectedSettingsEditor()) {
            return true;
          }

          if ((change.text || "").includes("\n")) {
            return false;
          }

          return this.currentEditableRanges.some((range) =>
            change.range.startLineNumber === range.startLineNumber &&
            change.range.endLineNumber === range.endLineNumber &&
            change.range.startColumn >= range.startColumn &&
            change.range.endColumn <= range.endColumn);
        },
        restoreLastKnownValue: function () {
          if (!this.editor) {
            return;
          }

          this.replaceEditorValue(this.lastKnownValue, true);
        },
        getSettingsThemeLine: function (lineNumber) {
          if (!this.model) {
            return null;
          }

          const lineText = this.model.getLineContent(lineNumber);
          const match = lineText.match(/^\s*(light|azure|dark|black|amber)\s*=\s*(true|false)\s*(#.*)?$/i);
          if (!match) {
            return null;
          }

          return {
            key: match[1].toLowerCase(),
            value: match[2].toLowerCase()
          };
        },
        normalizeThemeSelectionFromChange: function (changes) {
          if (!this.isProtectedSettingsEditor() || !this.model || !this.editor) {
            return;
          }

          const changedLines = [...new Set(changes.map((change) => change.range.startLineNumber))];
          const newlySelectedLine = changedLines
            .map((lineNumber) => ({ lineNumber, parsed: this.getSettingsThemeLine(lineNumber) }))
            .find((entry) => entry.parsed && entry.parsed.value === "true");

          if (!newlySelectedLine) {
            return;
          }

          const nextLines = this.model.getValue().split(/\r?\n/);
          let changed = false;

          for (let index = 0; index < nextLines.length; index++) {
            const parsed = this.getSettingsThemeLine(index + 1);
            if (!parsed || !supportedSettingsKeys.includes(parsed.key)) {
              continue;
            }

            const nextValue = index + 1 === newlySelectedLine.lineNumber ? "true" : "false";
            const normalizedLine = nextLines[index].replace(/\b(true|false)\b/i, nextValue);
            if (normalizedLine !== nextLines[index]) {
              nextLines[index] = normalizedLine;
              changed = true;
            }
          }

          if (changed) {
            const selection = this.editor.getSelection();
            this.replaceEditorValue(nextLines.join("\n"), false);
            if (selection) {
              this.editor.setSelection(selection);
            }
          }
        },
        handleModelContentChanged: function (event) {
          if (this.isApplyingProtectedEdit) {
            this.lastKnownValue = this.editor ? this.editor.getValue() : this.pendingValue;
            this.refreshEditableDecorations();
            return;
          }

          if (!this.isProtectedSettingsEditor()) {
            this.lastKnownValue = this.editor ? this.editor.getValue() : this.pendingValue;
            return;
          }

          const isAllowed = event.changes.every((change) => this.isChangeWithinEditableRanges(change));
          if (!isAllowed) {
            this.restoreLastKnownValue();
            return;
          }

          this.normalizeThemeSelectionFromChange(event.changes);
          this.lastKnownValue = this.editor ? this.editor.getValue() : this.pendingValue;
          this.refreshEditableDecorations();
        },
        getValueAsBase64: function () {
          if (!this.editor) {
            return null;
          }

          const value = this.editor.getValue() || "";
          const bytes = new TextEncoder().encode(value);
          let binary = "";
          for (let index = 0; index < bytes.length; index++) {
            binary += String.fromCharCode(bytes[index]);
          }

          return btoa(binary);
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

	public static readonly BindableProperty ThemeKeyProperty = BindableProperty.Create(
		nameof(ThemeKey),
		typeof(string),
		typeof(MonacoEditorSurface),
		"forrest-azure",
		propertyChanged: OnThemeKeyChanged);

	public static readonly BindableProperty EditableRangesJsonProperty = BindableProperty.Create(
		nameof(EditableRangesJson),
		typeof(string),
		typeof(MonacoEditorSurface),
		"[]",
		propertyChanged: OnEditableRangesJsonChanged);

	public static readonly BindableProperty DiagnosticsJsonProperty = BindableProperty.Create(
		nameof(DiagnosticsJson),
		typeof(string),
		typeof(MonacoEditorSurface),
		"[]",
		propertyChanged: OnDiagnosticsJsonChanged);

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
	private bool _initialStateApplied;
	private IDispatcherTimer? _syncTimer;
	private readonly SemaphoreSlim _stateApplyLock = new(1, 1);
	private int _requestedStateVersion;
	private int _appliedStateVersion;
	private string _lastNonEmptySettingsText = string.Empty;
	private string _pendingText = string.Empty;
	private string _pendingLanguage = "forrest";
	private string _pendingThemeKey = "forrest-azure";
	private string _pendingEditableRangesJson = "[]";
	private string _pendingDiagnosticsJson = "[]";
	private bool _pendingIsReadOnly;
	private bool _contentHydrated;
	private bool _shouldApplyTextToEditor = true;

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

	public string ThemeKey
	{
		get => (string)GetValue(ThemeKeyProperty);
		set => SetValue(ThemeKeyProperty, value);
	}

	public string EditableRangesJson
	{
		get => (string)GetValue(EditableRangesJsonProperty);
		set => SetValue(EditableRangesJsonProperty, value);
	}

	public string DiagnosticsJson
	{
		get => (string)GetValue(DiagnosticsJsonProperty);
		set => SetValue(DiagnosticsJsonProperty, value);
	}

	public bool IsReadOnly
	{
		get => (bool)GetValue(IsReadOnlyProperty);
		set => SetValue(IsReadOnlyProperty, value);
	}

	private static void OnTextChanged(BindableObject bindable, object? oldValue, object? newValue)
	{
		MonacoEditorSurface editor = (MonacoEditorSurface)bindable;
		string nextValue = newValue as string ?? string.Empty;
		editor._pendingText = nextValue;
		if (!string.IsNullOrWhiteSpace(nextValue))
		{
			editor._contentHydrated = false;
		}

		if (string.Equals(editor.Language, "settings-toml", StringComparison.Ordinal) &&
		    !string.IsNullOrWhiteSpace(nextValue))
		{
			editor._lastNonEmptySettingsText = nextValue;
		}

		if (editor._isPullingEditorText)
		{
			return;
		}

		editor._shouldApplyTextToEditor = true;
		editor.RequestStateApply();
	}

	private static void OnLanguageChanged(BindableObject bindable, object? oldValue, object? newValue)
	{
		MonacoEditorSurface editor = (MonacoEditorSurface)bindable;
		editor._pendingLanguage = newValue as string ?? "forrest";
		editor.RequestStateApply();
	}

	private static void OnThemeKeyChanged(BindableObject bindable, object? oldValue, object? newValue)
	{
		MonacoEditorSurface editor = (MonacoEditorSurface)bindable;
		editor._pendingThemeKey = newValue as string ?? "forrest-azure";
		editor.RequestStateApply();
	}

	private static void OnEditableRangesJsonChanged(BindableObject bindable, object? oldValue, object? newValue)
	{
		MonacoEditorSurface editor = (MonacoEditorSurface)bindable;
		editor._pendingEditableRangesJson = newValue as string ?? "[]";
		editor.RequestStateApply();
	}

	private static void OnDiagnosticsJsonChanged(BindableObject bindable, object? oldValue, object? newValue)
	{
		MonacoEditorSurface editor = (MonacoEditorSurface)bindable;
		editor._pendingDiagnosticsJson = newValue as string ?? "[]";
		editor.RequestStateApply();
	}

	private static void OnIsReadOnlyChanged(BindableObject bindable, object? oldValue, object? newValue)
	{
		MonacoEditorSurface editor = (MonacoEditorSurface)bindable;
		editor._pendingIsReadOnly = (bool)(newValue ?? false);
		editor.RequestStateApply();
	}

	private async void OnEditorWebViewNavigated(object? sender, WebNavigatedEventArgs e)
	{
		await EnsureEditorReadyAsync();
	}

	private void OnEditorWebViewNavigating(object? sender, WebNavigatingEventArgs e)
	{
		if (e.Url is null ||
		    !Uri.TryCreate(e.Url, UriKind.Absolute, out Uri? uri) ||
		    !string.Equals(uri.Scheme, "forrest", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		e.Cancel = true;
		if (string.Equals(uri.Host, "command", StringComparison.OrdinalIgnoreCase) &&
		    string.Equals(uri.AbsolutePath.Trim('/'), "send", StringComparison.OrdinalIgnoreCase))
		{
			SendRequested?.Invoke(this, EventArgs.Empty);
		}
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
				string? result = await EvaluateOptionalAsync("window.forRestHost && window.forRestHost.ready ? 'true' : 'false';");
				if (result?.Contains("true", StringComparison.OrdinalIgnoreCase) == true)
				{
					_isEditorReady = true;
					break;
				}

				await Task.Delay(100);
			}

			if (_isEditorReady)
			{
				_pendingLanguage = Language;
				_pendingThemeKey = ThemeKey;
				_pendingEditableRangesJson = EditableRangesJson;
				_pendingDiagnosticsJson = DiagnosticsJson;
				_pendingIsReadOnly = IsReadOnly;
				_pendingText = Text;
				_shouldApplyTextToEditor = true;
				_contentHydrated = string.IsNullOrWhiteSpace(_pendingText);

				RequestStateApply();
				await FlushPendingStateAsync();
				await EvaluateOptionalAsync("window.forRestHost && window.forRestHost.focus();");
				if (_initialStateApplied)
				{
					StartSyncTimer();
				}
			}
		}
		finally
		{
			_isWaitingForReady = false;
		}
	}

	private void RequestStateApply()
	{
		if (!_isEditorReady)
		{
			return;
		}

		Interlocked.Increment(ref _requestedStateVersion);
		_ = FlushPendingStateAsync();
	}

	private async Task FlushPendingStateAsync()
	{
		if (!_isEditorReady)
		{
			return;
		}

		await _stateApplyLock.WaitAsync();
		try
		{
			while (_isEditorReady)
			{
				int requestedVersion = Volatile.Read(ref _requestedStateVersion);
				if (requestedVersion == _appliedStateVersion)
				{
					return;
				}

				EditorStatePayload state = BuildCurrentStatePayload();
				try
				{
					await ApplyEditorStateAsync(state);
					_appliedStateVersion = requestedVersion;
					_initialStateApplied = true;
					_contentHydrated = true;
				}
				catch (Exception exception)
				{
					Debug.WriteLine($"[MonacoEditorSurface] Failed to apply editor state v{requestedVersion}: {exception}");
					return;
				}

				if (requestedVersion == Volatile.Read(ref _requestedStateVersion))
				{
					return;
				}
			}
		}
		finally
		{
			_stateApplyLock.Release();
		}
	}

	private EditorStatePayload BuildCurrentStatePayload()
	{
		string text = _pendingText;
		if (string.Equals(_pendingLanguage, "settings-toml", StringComparison.Ordinal) &&
		    string.IsNullOrWhiteSpace(text) &&
		    !string.IsNullOrWhiteSpace(_lastNonEmptySettingsText))
		{
			text = _lastNonEmptySettingsText;
		}

		return new EditorStatePayload(
			Text: text,
			Language: string.IsNullOrWhiteSpace(_pendingLanguage) ? "forrest" : _pendingLanguage,
			ThemeKey: string.IsNullOrWhiteSpace(_pendingThemeKey) ? "forrest-azure" : _pendingThemeKey,
			IsReadOnly: _pendingIsReadOnly,
			EditableRangesJson: string.IsNullOrWhiteSpace(_pendingEditableRangesJson) ? "[]" : _pendingEditableRangesJson,
			DiagnosticsJson: string.IsNullOrWhiteSpace(_pendingDiagnosticsJson) ? "[]" : _pendingDiagnosticsJson,
			ApplyText: _shouldApplyTextToEditor);
	}

	private async Task ApplyEditorStateAsync(EditorStatePayload state)
	{
		if (string.Equals(state.Language, "settings-toml", StringComparison.Ordinal) &&
		    !string.IsNullOrWhiteSpace(state.Text))
		{
			_lastNonEmptySettingsText = state.Text;
		}

		string payloadJson = JsonSerializer.Serialize(
			state,
			new JsonSerializerOptions
			{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			});
		string payloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson));
		string script = $"window.forRestHost ? window.forRestHost.applyStateFromBase64({JsonSerializer.Serialize(payloadBase64)}) : null;";

		_isPushingEditorText = true;
		try
		{
			string result = await EvaluateRequiredAsync(script);
			if (string.IsNullOrWhiteSpace(result) || result.Contains("\"ok\":false", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException($"Monaco applyState failed. Result: {result}");
			}

			if (state.ApplyText)
			{
				await EnsureTextAppliedAsync(state);
				_shouldApplyTextToEditor = false;
			}
		}
		finally
		{
			_isPushingEditorText = false;
		}
	}

	private async Task<string> EvaluateRequiredAsync(string script)
	{
		try
		{
			return await EditorWebView.EvaluateJavaScriptAsync(script);
		}
		catch (Exception exception)
		{
			Debug.WriteLine($"[MonacoEditorSurface] JavaScript evaluation failed.{Environment.NewLine}{script}{Environment.NewLine}{exception}");
			throw;
		}
	}

	private async Task<string?> EvaluateOptionalAsync(string script)
	{
		try
		{
			return await EditorWebView.EvaluateJavaScriptAsync(script);
		}
		catch (Exception exception)
		{
			Debug.WriteLine($"[MonacoEditorSurface] Optional JavaScript evaluation failed.{Environment.NewLine}{script}{Environment.NewLine}{exception}");
			return null;
		}
	}

	private void StartSyncTimer()
	{
		if (_syncTimer is not null || Dispatcher is null || !_initialStateApplied)
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
		if (!_isEditorReady || !_initialStateApplied || _isPushingEditorText || _isPullingEditorText)
		{
			return;
		}

		string? result = await EvaluateOptionalAsync("window.forRestHost && window.forRestHost.editor ? window.forRestHost.getValueAsBase64() : null;");
		if (string.IsNullOrWhiteSpace(result) || string.Equals(result, "null", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		string editorText = ParseJavascriptBase64Result(result);
		if (!_contentHydrated && !string.IsNullOrWhiteSpace(editorText))
		{
			_contentHydrated = true;
		}

		if (!_contentHydrated &&
		    string.IsNullOrWhiteSpace(editorText) &&
		    !string.IsNullOrWhiteSpace(_pendingText))
		{
			RequestStateApply();
			await FlushPendingStateAsync();
			return;
		}

		if (string.Equals(Language, "settings-toml", StringComparison.Ordinal) &&
		    string.IsNullOrWhiteSpace(editorText))
		{
			string fallbackText = !string.IsNullOrWhiteSpace(Text) ? Text : _lastNonEmptySettingsText;
			if (!string.IsNullOrWhiteSpace(fallbackText))
			{
				Text = fallbackText;
				RequestStateApply();
				await FlushPendingStateAsync();
			}

			return;
		}

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

	private static string ParseJavascriptBase64Result(string result)
	{
		try
		{
			string normalized = result.Trim().Trim('"');
			if (string.IsNullOrEmpty(normalized))
			{
				return string.Empty;
			}

			byte[] bytes = Convert.FromBase64String(normalized);
			return System.Text.Encoding.UTF8.GetString(bytes);
		}
		catch
		{
			return string.Empty;
		}
	}

	private async Task EnsureTextAppliedAsync(EditorStatePayload state)
	{
		if (string.IsNullOrEmpty(state.Text))
		{
			return;
		}

		string expectedText = state.Text;
		string payloadJson = JsonSerializer.Serialize(
			state,
			new JsonSerializerOptions
			{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			});
		string payloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson));
		string applyScript = $"window.forRestHost ? window.forRestHost.applyStateFromBase64({JsonSerializer.Serialize(payloadBase64)}) : null;";

		for (int attempt = 0; attempt < 4; attempt++)
		{
			await Task.Delay(45);
			string? result = await EvaluateOptionalAsync("window.forRestHost && window.forRestHost.editor ? window.forRestHost.getValueAsBase64() : null;");
			if (string.IsNullOrWhiteSpace(result) || string.Equals(result, "null", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (ParseJavascriptBase64Result(result) == expectedText)
			{
				return;
			}

			await EvaluateRequiredAsync(applyScript);
		}

		throw new InvalidOperationException("Monaco editor did not hydrate the expected document text.");
	}

	private sealed record EditorStatePayload(
		string Text,
		string Language,
		string ThemeKey,
		bool IsReadOnly,
		string EditableRangesJson,
		string DiagnosticsJson,
		bool ApplyText);
}
