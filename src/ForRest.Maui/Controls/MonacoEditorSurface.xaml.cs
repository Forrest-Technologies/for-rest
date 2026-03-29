using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ForRest.Maui.Theming;
using Microsoft.Maui.Dispatching;
using ForRest.Maui.Services;

#if ANDROID
using Android.Content;
using Android.Views;
using Android.Views.InputMethods;
using Android.Webkit;
#endif

namespace ForRest.Maui.Controls;

public partial class MonacoEditorSurface : ContentView
{
	public event EventHandler? SendRequested;
	public event EventHandler<MonacoResponseVarRequestEventArgs>? ResponseVarCopyRequested;
	public event EventHandler<MonacoCursorPositionChangedEventArgs>? CursorPositionChanged;
	private static readonly double DefaultEditorFontSize = OperatingSystem.IsAndroid() ? 14d : 13.5d;

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

    body.android-host {
      -webkit-font-smoothing: auto;
      text-rendering: auto;
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
      const baseUrl = new URL("monaco/", document.baseURI).toString().replace(/\/$/, "");

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
              { token: "comment.ai.prompt", foreground: "446F98", fontStyle: "bold" },
              { token: "comment.ai.response", foreground: "2C7C78", fontStyle: "italic" },
              { token: "comment.ai.stale", foreground: "9BA5B1", fontStyle: "italic" },
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
              { token: "comment.ai.prompt", foreground: "0B63A7", fontStyle: "bold" },
              { token: "comment.ai.response", foreground: "1E7A5F", fontStyle: "italic" },
              { token: "comment.ai.stale", foreground: "8FA0B3", fontStyle: "italic" },
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
              { token: "comment.ai.prompt", foreground: "7BC1FF", fontStyle: "bold" },
              { token: "comment.ai.response", foreground: "91D9BF", fontStyle: "italic" },
              { token: "comment.ai.stale", foreground: "6E7F90", fontStyle: "italic" },
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
              { token: "comment.ai.prompt", foreground: "9EC5F2", fontStyle: "bold" },
              { token: "comment.ai.response", foreground: "A3E2C7", fontStyle: "italic" },
              { token: "comment.ai.stale", foreground: "738191", fontStyle: "italic" },
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
              { token: "comment.ai.prompt", foreground: "9B6B2F", fontStyle: "bold" },
              { token: "comment.ai.response", foreground: "6F8A54", fontStyle: "italic" },
              { token: "comment.ai.stale", foreground: "A79E8B", fontStyle: "italic" },
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

      function getLanguageHelpEntries() {
        if (window.forRestHost && Array.isArray(window.forRestHost.pendingLanguageHelp)) {
          return window.forRestHost.pendingLanguageHelp;
        }

        return [];
      }

      function mapCompletionKind(monaco, kind) {
        const lookup = {
          keyword: monaco.languages.CompletionItemKind.Keyword,
          snippet: monaco.languages.CompletionItemKind.Snippet,
          method: monaco.languages.CompletionItemKind.Method,
          property: monaco.languages.CompletionItemKind.Property,
          function: monaco.languages.CompletionItemKind.Function
        };

        return lookup[String(kind || "keyword").toLowerCase()] || monaco.languages.CompletionItemKind.Keyword;
      }

      function getHoverLookup() {
        const lookup = {};
        getLanguageHelpEntries().forEach((entry) => {
          const contents = [
            `**${entry.label}**`,
            entry.summary || "",
            entry.documentation || "",
            entry.example ? `Example:\n\`\`\`frs\n${entry.example}\n\`\`\`` : ""
          ].filter(Boolean);

          const aliases = []
            .concat(Array.isArray(entry.hoverTerms) ? entry.hoverTerms : [])
            .concat([entry.label]);

          aliases
            .filter(Boolean)
            .forEach((term) => {
              lookup[String(term).toLowerCase()] = contents;
            });
        });

        return lookup;
      }

      function resolveHoverToken(model, position) {
        const lineText = model.getLineContent(position.lineNumber) || "";
        const index = Math.max(0, position.column - 2);
        if (!lineText.length || index >= lineText.length) {
          return null;
        }

        const isTokenChar = (character) => /[A-Za-z0-9_\.\[\]]/.test(character);
        if (!isTokenChar(lineText[index])) {
          return null;
        }

        let start = index;
        let end = index;
        while (start > 0 && isTokenChar(lineText[start - 1])) {
          start--;
        }

        while (end + 1 < lineText.length && isTokenChar(lineText[end + 1])) {
          end++;
        }

        return {
          token: lineText.substring(start, end + 1),
          startColumn: start + 1,
          endColumn: end + 2
        };
      }

      function registerLanguage(monaco) {
        monaco.languages.register({ id: "forrest" });
        monaco.languages.setMonarchTokensProvider("forrest", {
          tokenizer: {
            root: [
              [/^##[^\n]*/, "comment.ai.prompt"],
              [/^#>[^\n]*/, "comment.ai.response"],
              [/^#~[^\n]*/, "comment.ai.stale"],
              [/#[^\n]*/, "comment"],
              [/\b(name|method|url|timeout|max_send_iterations|redirects|ssl|history|content_type|header|query|body|form|multipart|extract|expect|repeat|retry|auth)\b/, "keyword.directive"],
              [/\b(GET|POST|PUT|PATCH|DELETE|OPTIONS|HEAD)\b/, "keyword.method"],
              [/\b(request\.send)\b/, "keyword.flow"],
              [/\b(await|runtime|request|response|workspace|variables|json|console|encoding|crypto|regex|log|warn|error|let|if|else|while|for|foreach|in|and|or|not)\b/, "keyword.flow"],
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
            const entries = getLanguageHelpEntries();

            return {
              suggestions: entries
                .filter((entry) => typeof entry.insertText === "string" && entry.insertText.length > 0)
                .map((entry) => ({
                  label: entry.label,
                  kind: mapCompletionKind(monaco, entry.kind),
                  insertText: entry.insertText,
                  insertTextRules: entry.insertAsSnippet ? insertAsSnippet : undefined,
                  detail: entry.category,
                  documentation: `${entry.summary || ""}${entry.documentation ? `\n\n${entry.documentation}` : ""}`.trim(),
                  range
                }))
            };
          }
        });

        monaco.languages.registerHoverProvider("forrest", {
          provideHover: function (model, position) {
            const target = resolveHoverToken(model, position);
            if (!target) {
              return null;
            }

            const lookup = getHoverLookup();
            const lowerToken = String(target.token || "").toLowerCase();
            const tokenSegments = lowerToken.split(".");
            const candidates = [
              lowerToken,
              tokenSegments.slice(-2).join("."),
              tokenSegments[tokenSegments.length - 1],
              tokenSegments[0]
            ].filter(Boolean);
            const content = candidates
              .map((candidate) => lookup[candidate])
              .find((candidate) => Array.isArray(candidate) && candidate.length > 0);
            if (!content) {
              return null;
            }

            return {
              range: new monaco.Range(position.lineNumber, target.startColumn, position.lineNumber, target.endColumn),
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

      function requestHostCommand(commandName, payload) {
        try {
          const query = payload && typeof payload === "object"
            ? Object.entries(payload)
                .filter((entry) => entry[1] !== undefined && entry[1] !== null)
                .map((entry) => `${encodeURIComponent(entry[0])}=${encodeURIComponent(String(entry[1]))}`)
                .join("&")
            : "";
          const suffix = query.length > 0 ? `?${query}` : "";
          window.location.href = `forrest://command/${commandName}${suffix}`;
        } catch {
        }
      }

      function getCursorPayload(editor) {
        if (!editor) {
          return {};
        }

        const position = editor.getPosition();
        if (!position) {
          return {};
        }

        return {
          line: position.lineNumber,
          column: position.column
        };
      }

      function normalizeEditorFontSize(value, fallback) {
        const candidate = Number(value);
        if (!Number.isFinite(candidate)) {
          return fallback;
        }

        return Math.min(28, Math.max(10, candidate));
      }

      function calculateLineHeight(fontSize, isAndroid) {
        const ratio = isAndroid ? 1.5 : 1.46;
        return Math.max(Math.round(fontSize * ratio), Math.round(fontSize) + 5);
      }

      const supportedSettingsKeys = ["light", "azure", "dark", "black", "amber"];

      window.forRestHost = {
        editor: null,
        model: null,
        ready: false,
        pendingValue: "",
        pendingLanguage: "forrest",
        pendingTheme: "forrest-azure",
        pendingFontSize: 13.5,
        pendingReadOnly: false,
        isAndroid: /Android/i.test(navigator.userAgent || ""),
        pendingEditableRanges: [],
        pendingDiagnostics: [],
        pendingLanguageHelp: [],
        pendingEnableResponseActions: false,
        pendingCursorLineNumber: 0,
        pendingCursorColumn: 0,
        pendingCursorRequestVersion: 0,
        lastAppliedCursorRequestVersion: 0,
        pendingShouldApplyText: true,
        editableDecorations: [],
        currentEditableRanges: [],
        lastKnownValue: "",
        responseActionsRegistered: false,
        lastContextPosition: null,
        isApplyingProtectedEdit: false,
        layoutRefreshHandle: null,
        androidFontRemeasureHandle: null,
        textSyncHandle: null,
        topPadding: 8,
        baseBottomPadding: 24,
        scheduleLayoutRefresh: function () {
          if (this.layoutRefreshHandle) {
            return;
          }

          this.layoutRefreshHandle = window.requestAnimationFrame(() => {
            this.layoutRefreshHandle = null;
            this.refreshViewportLayout();
          });
        },
        scheduleTextSyncNotification: function () {
          if (this.pendingReadOnly) {
            return;
          }

          if (this.textSyncHandle) {
            window.clearTimeout(this.textSyncHandle);
          }

          const delay = this.isAndroid ? 180 : 120;
          this.textSyncHandle = window.setTimeout(() => {
            this.textSyncHandle = null;
            requestHostCommand("text-sync");
          }, delay);
        },
        isInlineAiPromptLine: function (lineText) {
          return /^\s*##(?!#)/.test(lineText || "");
        },
        shouldSubmitInlineAiPromptOnEnter: function (event, monaco) {
          if (this.pendingReadOnly || !this.editor || !this.model || !event || !monaco) {
            return false;
          }

          if (event.keyCode !== monaco.KeyCode.Enter) {
            return false;
          }

          if (event.shiftKey || event.ctrlKey || event.metaKey || event.altKey) {
            return false;
          }

          const browserEvent = event.browserEvent;
          if (browserEvent && browserEvent.isComposing) {
            return false;
          }

          const position = this.editor.getPosition();
          if (!position) {
            return false;
          }

          const lineText = this.model.getLineContent(position.lineNumber) || "";
          return this.isInlineAiPromptLine(lineText);
        },
        scheduleAndroidFontRemeasure: function () {
          if (!this.isAndroid || !this.editor || !window.monaco || this.androidFontRemeasureHandle) {
            return;
          }

          this.androidFontRemeasureHandle = window.requestAnimationFrame(() => {
            this.androidFontRemeasureHandle = null;
            if (!this.editor || !window.monaco) {
              return;
            }

            if (window.monaco.editor && typeof window.monaco.editor.remeasureFonts === "function") {
              window.monaco.editor.remeasureFonts();
            }

            this.editor.layout();
          });
        },
        refreshViewportLayout: function () {
          const container = document.getElementById("container");
          if (!container) {
            return;
          }

          const viewport = window.visualViewport;
          const viewportHeight = Math.max(0, Math.floor(viewport ? viewport.height : window.innerHeight || document.documentElement.clientHeight || 0));
          const nextHeight = viewportHeight > 0 ? `${viewportHeight}px` : "100%";
          document.documentElement.style.height = nextHeight;
          document.body.style.height = nextHeight;
          container.style.height = nextHeight;

          const keyboardInset = viewport
            ? Math.max(0, Math.round((window.innerHeight || viewport.height) - viewport.height - viewport.offsetTop))
            : 0;

          if (this.editor) {
            this.editor.updateOptions({
              padding: {
                top: this.topPadding,
                bottom: this.baseBottomPadding + keyboardInset
              }
            });
            this.editor.layout();
            this.scheduleAndroidFontRemeasure();

            const position = this.editor.getPosition();
            if (position) {
              this.editor.revealPositionInCenterIfOutsideViewport(position);
            }
          }
        },
        attachViewportListeners: function () {
          if (this.viewportListenersAttached) {
            return;
          }

          this.viewportListenersAttached = true;
          window.addEventListener("resize", () => this.scheduleLayoutRefresh());
          if (window.visualViewport) {
            window.visualViewport.addEventListener("resize", () => this.scheduleLayoutRefresh());
            window.visualViewport.addEventListener("scroll", () => this.scheduleLayoutRefresh());
          }
        },
        create: function (monaco) {
          registerLanguage(monaco);
          if (this.isAndroid) {
            document.body.classList.add("android-host");
          }

          this.model = monaco.editor.createModel(this.pendingValue || "", this.pendingLanguage || "forrest");
          const normalizedFontSize = normalizeEditorFontSize(this.pendingFontSize, this.isAndroid ? 14 : 13.5);
          const editorOptions = {
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
            fontSize: normalizedFontSize,
            lineHeight: calculateLineHeight(normalizedFontSize, this.isAndroid),
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
          };

          if (this.isAndroid) {
            delete editorOptions.fontFamily;
            editorOptions.letterSpacing = 0;
          }

          this.editor = monaco.editor.create(document.getElementById("container"), editorOptions);

          const domNode = this.editor.getDomNode();
          if (domNode) {
            domNode.style.touchAction = "auto";
          }

          this.editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter, function () {
            requestHostCommand("send", getCursorPayload(window.forRestHost && window.forRestHost.editor));
          });
          this.editor.addCommand(monaco.KeyCode.F5, function () {
            requestHostCommand("send", getCursorPayload(window.forRestHost && window.forRestHost.editor));
          });
          this.editor.onKeyDown((event) => {
            if (this.shouldSubmitInlineAiPromptOnEnter(event, monaco)) {
              event.preventDefault();
              event.stopPropagation();
              requestHostCommand("send", getCursorPayload(this.editor));
            }
          });
          this.editor.onDidChangeCursorPosition((event) => {
            if (!event || !event.position) {
              return;
            }

            requestHostCommand("cursor-position", {
              line: event.position.lineNumber,
              column: event.position.column
            });
          });
          this.editor.onContextMenu((event) => {
            this.lastContextPosition = event.target && event.target.position
              ? event.target.position
              : this.editor.getPosition();
          });
          this.editor.onDidBlurEditorText(() => {
            if (this.pendingReadOnly) {
              return;
            }

            this.scheduleTextSyncNotification();
            const position = this.editor.getPosition();
            if (!position) {
              return;
            }

            requestHostCommand("cursor-position", {
              line: position.lineNumber,
              column: position.column
            });
          });

          this.lastKnownValue = this.editor.getValue();
          this.editor.onDidChangeModelContent((event) => {
            this.handleModelContentChanged(event);
          });
          if (this.isAndroid) {
            this.scheduleAndroidFontRemeasure();
            if (document.fonts && document.fonts.ready && typeof document.fonts.ready.then === "function") {
              document.fonts.ready
                .then(() => this.scheduleAndroidFontRemeasure())
                .catch(() => {});
            } else {
              window.setTimeout(() => this.scheduleAndroidFontRemeasure(), 120);
            }
          }
          this.ready = true;
          this.applyState({
            text: this.pendingValue,
            language: this.pendingLanguage,
            themeKey: this.pendingTheme,
            editorFontSize: this.pendingFontSize,
            isReadOnly: this.pendingReadOnly,
            editableRanges: this.pendingEditableRanges,
            diagnostics: this.pendingDiagnostics,
            languageHelp: this.pendingLanguageHelp,
            enableResponseActions: this.pendingEnableResponseActions,
            applyText: this.pendingShouldApplyText
          });
          this.editor.focus();
          this.attachViewportListeners();
          this.scheduleLayoutRefresh();
        },
        ensureResponseActions: function (monaco) {
          if (this.responseActionsRegistered || !this.pendingEnableResponseActions || !this.editor) {
            return;
          }

          this.responseActionsRegistered = true;
          this.editor.addAction({
            id: "forrest.copyResponseVar",
            label: "Copy response var",
            contextMenuGroupId: "navigation",
            contextMenuOrder: 1.2,
            run: () => {
              const position = this.lastContextPosition || this.editor.getPosition();
              if (!position) {
                return null;
              }

              requestHostCommand("copy-response-var", {
                line: position.lineNumber,
                column: position.column
              });
              return null;
            }
          });
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
        parseLanguageHelp: function (state) {
          if (Array.isArray(state?.languageHelp)) {
            return state.languageHelp;
          }

          if (typeof state?.languageHelpJson === "string" && state.languageHelpJson.length > 0) {
            try {
              const parsed = JSON.parse(state.languageHelpJson);
              return Array.isArray(parsed) ? parsed : [];
            } catch {
              return [];
            }
          }

          return [];
        },
        applyPendingCursorMove: function () {
          if (!this.editor || !this.model) {
            return;
          }

          if (!Number.isInteger(this.pendingCursorRequestVersion) ||
              this.pendingCursorRequestVersion <= this.lastAppliedCursorRequestVersion ||
              !Number.isInteger(this.pendingCursorLineNumber) ||
              this.pendingCursorLineNumber < 1) {
            return;
          }

          const lineCount = Math.max(1, this.model.getLineCount());
          const lineNumber = Math.min(Math.max(1, this.pendingCursorLineNumber), lineCount);
          const maxColumn = Math.max(1, this.model.getLineMaxColumn(lineNumber));
          const column = Math.min(Math.max(1, this.pendingCursorColumn || 1), maxColumn);
          const position = { lineNumber, column };
          this.editor.setPosition(position);
          this.editor.setSelection({
            startLineNumber: lineNumber,
            startColumn: column,
            endLineNumber: lineNumber,
            endColumn: column
          });
          if (typeof this.editor.revealPositionInCenterIfOutsideViewport === "function") {
            this.editor.revealPositionInCenterIfOutsideViewport(position);
          } else if (typeof this.editor.revealPositionInCenter === "function") {
            this.editor.revealPositionInCenter(position);
          }

          this.editor.focus();
          this.lastAppliedCursorRequestVersion = this.pendingCursorRequestVersion;
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
          const previousLanguage = this.pendingLanguage;
          const previousTheme = this.pendingTheme;
          const previousFontSize = this.pendingFontSize;
          const previousReadOnly = this.pendingReadOnly;
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
          this.pendingFontSize = normalizeEditorFontSize(nextState.editorFontSize, this.isAndroid ? 14 : 13.5);
          this.pendingReadOnly = !!nextState.isReadOnly;
          this.pendingEditableRanges = this.parseEditableRanges(nextState);
          this.pendingDiagnostics = this.parseDiagnostics(nextState);
          this.pendingLanguageHelp = this.parseLanguageHelp(nextState);
          this.pendingEnableResponseActions = !!nextState.enableResponseActions;
          this.pendingCursorLineNumber = Number.isInteger(nextState.requestedCursorLineNumber) ? nextState.requestedCursorLineNumber : 0;
          this.pendingCursorColumn = Number.isInteger(nextState.requestedCursorColumn) ? nextState.requestedCursorColumn : 0;
          this.pendingCursorRequestVersion = Number.isInteger(nextState.requestedCursorVersion) ? nextState.requestedCursorVersion : 0;
          applyHostThemeChrome(this.pendingTheme);
          const shouldRefreshLayout = shouldApplyText ||
            previousLanguage !== this.pendingLanguage ||
            previousTheme !== this.pendingTheme ||
            previousFontSize !== this.pendingFontSize ||
            previousReadOnly !== this.pendingReadOnly;

          if (this.model && window.monaco && this.model.getLanguageId() !== this.pendingLanguage) {
            window.monaco.editor.setModelLanguage(this.model, this.pendingLanguage);
          }

          if (this.editor) {
            this.editor.updateOptions({
              readOnly: this.pendingReadOnly,
              fontSize: this.pendingFontSize,
              lineHeight: calculateLineHeight(this.pendingFontSize, this.isAndroid)
            });

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
              if (shouldRefreshLayout) {
                this.editor.layout();
              }
            }
          }

          if (this.editor && window.monaco) {
            this.ensureResponseActions(window.monaco);
            window.monaco.editor.setTheme(this.pendingTheme);
          }

          this.applyDiagnostics();
          this.applyPendingCursorMove();

          this.lastKnownValue = this.editor ? this.editor.getValue() : this.pendingValue;
          if (shouldRefreshLayout) {
            this.scheduleLayoutRefresh();
          }

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
            this.scheduleTextSyncNotification();
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
          this.scheduleTextSyncNotification();
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

	public static readonly BindableProperty EditorFontSizeProperty = BindableProperty.Create(
		nameof(EditorFontSize),
		typeof(double),
		typeof(MonacoEditorSurface),
		DefaultEditorFontSize,
		propertyChanged: OnEditorFontSizeChanged);

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

	public static readonly BindableProperty LanguageHelpJsonProperty = BindableProperty.Create(
		nameof(LanguageHelpJson),
		typeof(string),
		typeof(MonacoEditorSurface),
		"[]",
		propertyChanged: OnLanguageHelpJsonChanged);

	public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
		nameof(IsReadOnly),
		typeof(bool),
		typeof(MonacoEditorSurface),
		false,
		propertyChanged: OnIsReadOnlyChanged);

	public static readonly BindableProperty EnableResponseActionsProperty = BindableProperty.Create(
		nameof(EnableResponseActions),
		typeof(bool),
		typeof(MonacoEditorSurface),
		false,
		propertyChanged: OnEnableResponseActionsChanged);

	public static readonly BindableProperty RequestedCursorLineNumberProperty = BindableProperty.Create(
		nameof(RequestedCursorLineNumber),
		typeof(int),
		typeof(MonacoEditorSurface),
		0,
		propertyChanged: OnRequestedCursorLineNumberChanged);

	public static readonly BindableProperty RequestedCursorColumnProperty = BindableProperty.Create(
		nameof(RequestedCursorColumn),
		typeof(int),
		typeof(MonacoEditorSurface),
		0,
		propertyChanged: OnRequestedCursorColumnChanged);

	public static readonly BindableProperty RequestedCursorVersionProperty = BindableProperty.Create(
		nameof(RequestedCursorVersion),
		typeof(int),
		typeof(MonacoEditorSurface),
		0,
		propertyChanged: OnRequestedCursorVersionChanged);

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
	private double _pendingEditorFontSize = DefaultEditorFontSize;
	private string _pendingEditableRangesJson = "[]";
	private string _pendingDiagnosticsJson = "[]";
	private string _pendingLanguageHelpJson = "[]";
	private bool _pendingIsReadOnly;
	private bool _pendingEnableResponseActions;
	private int _pendingRequestedCursorLineNumber;
	private int _pendingRequestedCursorColumn;
	private int _pendingRequestedCursorVersion;
	private bool _contentHydrated;
	private bool _shouldApplyTextToEditor = true;
#if ANDROID
	private Android.Webkit.WebView? _androidPlatformWebView;
#endif

	public MonacoEditorSurface()
	{
		InitializeComponent();
		EditorWebView.Source = new HtmlWebViewSource
		{
			Html = MonacoHostHtml,
			BaseUrl = GetEditorWebViewBaseUrl()
		};
		EditorWebView.HandlerChanged += OnEditorWebViewHandlerChanged;
		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
	}

	private static string GetEditorWebViewBaseUrl()
	{
#if WINDOWS
		return "https://appdir/";
#elif ANDROID
		return "file:///android_asset/";
#else
		return "/";
#endif
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

	public double EditorFontSize
	{
		get => (double)GetValue(EditorFontSizeProperty);
		set => SetValue(EditorFontSizeProperty, value);
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

	public string LanguageHelpJson
	{
		get => (string)GetValue(LanguageHelpJsonProperty);
		set => SetValue(LanguageHelpJsonProperty, value);
	}

	public bool IsReadOnly
	{
		get => (bool)GetValue(IsReadOnlyProperty);
		set => SetValue(IsReadOnlyProperty, value);
	}

	public bool EnableResponseActions
	{
		get => (bool)GetValue(EnableResponseActionsProperty);
		set => SetValue(EnableResponseActionsProperty, value);
	}

	public int RequestedCursorLineNumber
	{
		get => (int)GetValue(RequestedCursorLineNumberProperty);
		set => SetValue(RequestedCursorLineNumberProperty, value);
	}

	public int RequestedCursorColumn
	{
		get => (int)GetValue(RequestedCursorColumnProperty);
		set => SetValue(RequestedCursorColumnProperty, value);
	}

	public int RequestedCursorVersion
	{
		get => (int)GetValue(RequestedCursorVersionProperty);
		set => SetValue(RequestedCursorVersionProperty, value);
	}

	private void OnLoaded(object? sender, EventArgs e)
	{
#if ANDROID
		AttachAndroidWebView();
#endif
		if (!_isEditorReady && !_isWaitingForReady)
		{
			_ = EnsureEditorReadyAsync();
			return;
		}

		if (_isEditorReady && !_pendingIsReadOnly)
		{
			StartSyncTimer();
		}
	}

	private void OnUnloaded(object? sender, EventArgs e)
	{
		StopSyncTimer();
		_isEditorReady = false;
		_isWaitingForReady = false;
#if ANDROID
		DetachAndroidWebView();
#endif
	}

	private void OnEditorWebViewHandlerChanged(object? sender, EventArgs e)
	{
#if ANDROID
		AttachAndroidWebView();
#endif
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

	private static void OnEditorFontSizeChanged(BindableObject bindable, object? oldValue, object? newValue)
	{
		MonacoEditorSurface editor = (MonacoEditorSurface)bindable;
		editor._pendingEditorFontSize = newValue is double fontSize && double.IsFinite(fontSize) ? fontSize : DefaultEditorFontSize;
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

	private static void OnLanguageHelpJsonChanged(BindableObject bindable, object? oldValue, object? newValue)
	{
		MonacoEditorSurface editor = (MonacoEditorSurface)bindable;
		editor._pendingLanguageHelpJson = newValue as string ?? "[]";
		editor.RequestStateApply();
	}

	private static void OnIsReadOnlyChanged(BindableObject bindable, object? oldValue, object? newValue)
	{
		MonacoEditorSurface editor = (MonacoEditorSurface)bindable;
		editor._pendingIsReadOnly = (bool)(newValue ?? false);
		editor.RequestStateApply();
	}

	private static void OnEnableResponseActionsChanged(BindableObject bindable, object? oldValue, object? newValue)
	{
		MonacoEditorSurface editor = (MonacoEditorSurface)bindable;
		editor._pendingEnableResponseActions = (bool)(newValue ?? false);
		editor.RequestStateApply();
	}

	private static void OnRequestedCursorLineNumberChanged(BindableObject bindable, object? oldValue, object? newValue)
	{
		MonacoEditorSurface editor = (MonacoEditorSurface)bindable;
		editor._pendingRequestedCursorLineNumber = newValue is int lineNumber ? lineNumber : 0;
		editor.RequestStateApply();
	}

	private static void OnRequestedCursorColumnChanged(BindableObject bindable, object? oldValue, object? newValue)
	{
		MonacoEditorSurface editor = (MonacoEditorSurface)bindable;
		editor._pendingRequestedCursorColumn = newValue is int column ? column : 0;
		editor.RequestStateApply();
	}

	private static void OnRequestedCursorVersionChanged(BindableObject bindable, object? oldValue, object? newValue)
	{
		MonacoEditorSurface editor = (MonacoEditorSurface)bindable;
		editor._pendingRequestedCursorVersion = newValue is int version ? version : 0;
		editor.RequestStateApply();
	}

	private async void OnEditorWebViewNavigated(object? sender, WebNavigatedEventArgs e)
	{
		await EnsureEditorReadyAsync();
	}

	private async void OnEditorWebViewNavigating(object? sender, WebNavigatingEventArgs e)
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
			if (TryGetQueryValue(uri, "line", out int sendLine) &&
			    TryGetQueryValue(uri, "column", out int sendColumn))
			{
				CursorPositionChanged?.Invoke(this, new MonacoCursorPositionChangedEventArgs(sendLine, sendColumn));
			}

			await SyncEditorTextAsync();
			SendRequested?.Invoke(this, EventArgs.Empty);
			return;
		}

		if (string.Equals(uri.Host, "command", StringComparison.OrdinalIgnoreCase) &&
		    string.Equals(uri.AbsolutePath.Trim('/'), "text-sync", StringComparison.OrdinalIgnoreCase))
		{
			_ = SyncEditorTextAsync();
			return;
		}

		if (string.Equals(uri.Host, "command", StringComparison.OrdinalIgnoreCase) &&
		    string.Equals(uri.AbsolutePath.Trim('/'), "copy-response-var", StringComparison.OrdinalIgnoreCase) &&
		    TryGetQueryValue(uri, "line", out int lineNumber) &&
		    TryGetQueryValue(uri, "column", out int column))
		{
			ResponseVarCopyRequested?.Invoke(this, new MonacoResponseVarRequestEventArgs(lineNumber, column));
			return;
		}

		if (string.Equals(uri.Host, "command", StringComparison.OrdinalIgnoreCase) &&
		    string.Equals(uri.AbsolutePath.Trim('/'), "cursor-position", StringComparison.OrdinalIgnoreCase) &&
		    TryGetQueryValue(uri, "line", out int cursorLineNumber) &&
		    TryGetQueryValue(uri, "column", out int cursorColumn))
		{
			CursorPositionChanged?.Invoke(this, new MonacoCursorPositionChangedEventArgs(cursorLineNumber, cursorColumn));
			return;
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
				_pendingEditorFontSize = EditorFontSize;
				_pendingEditableRangesJson = EditableRangesJson;
				_pendingDiagnosticsJson = DiagnosticsJson;
				_pendingLanguageHelpJson = LanguageHelpJson;
				_pendingIsReadOnly = IsReadOnly;
				_pendingEnableResponseActions = EnableResponseActions;
				_pendingRequestedCursorLineNumber = RequestedCursorLineNumber;
				_pendingRequestedCursorColumn = RequestedCursorColumn;
				_pendingRequestedCursorVersion = RequestedCursorVersion;
				_pendingText = Text;
				_shouldApplyTextToEditor = true;
				_contentHydrated = string.IsNullOrWhiteSpace(_pendingText);

				RequestStateApply();
				await FlushPendingStateAsync();
				if (!_pendingIsReadOnly)
				{
#if ANDROID
					await FocusAndroidEditorAsync(requestKeyboard: false, focusMonaco: true);
#else
					await EvaluateOptionalAsync("window.forRestHost && window.forRestHost.focus();");
#endif
					if (_initialStateApplied)
					{
						StartSyncTimer();
					}
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
					AppLaunchGuard.RecordException($"Monaco editor state apply failed at version {requestedVersion}.", exception);
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
			EditorFontSize: _pendingEditorFontSize,
			IsReadOnly: _pendingIsReadOnly,
			EditableRangesJson: string.IsNullOrWhiteSpace(_pendingEditableRangesJson) ? "[]" : _pendingEditableRangesJson,
			DiagnosticsJson: string.IsNullOrWhiteSpace(_pendingDiagnosticsJson) ? "[]" : _pendingDiagnosticsJson,
			LanguageHelpJson: string.IsNullOrWhiteSpace(_pendingLanguageHelpJson) ? "[]" : _pendingLanguageHelpJson,
			EnableResponseActions: _pendingEnableResponseActions,
			ApplyText: _shouldApplyTextToEditor,
			RequestedCursorLineNumber: _pendingRequestedCursorLineNumber,
			RequestedCursorColumn: _pendingRequestedCursorColumn,
			RequestedCursorVersion: _pendingRequestedCursorVersion);
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
		if (EditorWebView.Handler is null)
		{
			throw new InvalidOperationException("The Monaco web view is not attached to a native handler.");
		}

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
		if (EditorWebView.Handler is null || Handler is null)
		{
			return null;
		}

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
		if (_syncTimer is not null || Dispatcher is null || !_initialStateApplied || _pendingIsReadOnly)
		{
			return;
		}

		if (OperatingSystem.IsAndroid())
		{
			return;
		}

		_syncTimer = Dispatcher.CreateTimer();
		_syncTimer.Interval = TimeSpan.FromMilliseconds(450);
		_syncTimer.Tick += async (_, _) => await SyncEditorTextAsync();
		_syncTimer.Start();
	}

	private void StopSyncTimer()
	{
		if (_syncTimer is null)
		{
			return;
		}

		_syncTimer.Stop();
		_syncTimer = null;
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

	public Task FlushTextSyncAsync()
	{
		return SyncEditorTextAsync();
	}

	public async Task MoveCursorToAsync(int lineNumber, int column)
	{
		await EnsureEditorReadyAsync();
		if (!_isEditorReady)
		{
			return;
		}

		int targetLine = Math.Max(1, lineNumber);
		int targetColumn = Math.Max(1, column);
		string script =
			$"window.forRestHost && window.forRestHost.editor && window.forRestHost.model ? (function() {{ " +
			$"const maxLine = Math.max(1, window.forRestHost.model.getLineCount()); " +
			$"const nextLine = Math.min({targetLine}, maxLine); " +
			$"const maxColumn = Math.max(1, window.forRestHost.model.getLineMaxColumn(nextLine)); " +
			$"const nextColumn = Math.min({targetColumn}, maxColumn); " +
			$"const position = {{ lineNumber: nextLine, column: nextColumn }}; " +
			$"window.forRestHost.editor.setPosition(position); " +
			$"window.forRestHost.editor.revealPositionInCenterIfOutsideViewport(position); " +
			$"window.forRestHost.editor.focus(); " +
			$"return JSON.stringify(position); " +
			$"}})() : null;";
		await EvaluateOptionalAsync(script);
#if ANDROID
		await FocusAndroidEditorAsync(requestKeyboard: false, focusMonaco: false);
#endif
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

#if ANDROID
	private void AttachAndroidWebView()
	{
		Android.Webkit.WebView? platformView = EditorWebView.Handler?.PlatformView as Android.Webkit.WebView;
		if (ReferenceEquals(_androidPlatformWebView, platformView))
		{
			return;
		}

		DetachAndroidWebView();
		_androidPlatformWebView = platformView;
		if (_androidPlatformWebView is null)
		{
			return;
		}

		_androidPlatformWebView.Focusable = true;
		_androidPlatformWebView.FocusableInTouchMode = true;
		_androidPlatformWebView.Clickable = true;
		_androidPlatformWebView.LongClickable = true;
		WebSettings? settings = _androidPlatformWebView.Settings;
		if (settings is not null)
		{
			settings.TextZoom = 100;
			settings.UseWideViewPort = false;
			settings.LoadWithOverviewMode = false;
			settings.BuiltInZoomControls = false;
			settings.DisplayZoomControls = false;
			settings.SetSupportZoom(false);
		}

		_androidPlatformWebView.Touch += OnAndroidWebViewTouch;
	}

	private void DetachAndroidWebView()
	{
		if (_androidPlatformWebView is null)
		{
			return;
		}

		_androidPlatformWebView.Touch -= OnAndroidWebViewTouch;
		_androidPlatformWebView = null;
	}

	private void OnAndroidWebViewTouch(object? sender, Android.Views.View.TouchEventArgs e)
	{
		e.Handled = false;

		if (_pendingIsReadOnly || e.Event is null || _androidPlatformWebView is null)
		{
			return;
		}

		if (e.Event.ActionMasked is MotionEventActions.Down &&
		    !_androidPlatformWebView.HasFocus)
		{
			_ = BootstrapAndroidEditorTouchAsync();
		}
	}

	private async Task BootstrapAndroidEditorTouchAsync()
	{
		await Task.Delay(32);
		await FocusAndroidEditorAsync(requestKeyboard: true, focusMonaco: false, keyboardDelayMs: 75);
	}

	private async Task FocusAndroidEditorAsync(bool requestKeyboard, bool focusMonaco, int keyboardDelayMs = 40)
	{
		AttachAndroidWebView();
		if (_androidPlatformWebView is null)
		{
			if (focusMonaco)
			{
				await EvaluateOptionalAsync("window.forRestHost && window.forRestHost.focus();");
			}

			return;
		}

		await Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(() =>
		{
			EditorWebView.Focus();
			_androidPlatformWebView.RequestFocusFromTouch();
			_androidPlatformWebView.RequestFocus();
		});

		if (focusMonaco)
		{
			await EvaluateOptionalAsync("window.forRestHost && window.forRestHost.focus();");
		}

		if (!requestKeyboard)
		{
			return;
		}

		await Task.Delay(keyboardDelayMs);
		await Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(() =>
		{
			if (_androidPlatformWebView is null)
			{
				return;
			}

			InputMethodManager? inputMethodManager = _androidPlatformWebView.Context?.GetSystemService(Context.InputMethodService) as InputMethodManager;
			inputMethodManager?.RestartInput(_androidPlatformWebView);
			inputMethodManager?.ShowSoftInput(_androidPlatformWebView, ShowFlags.Implicit);
		});
	}
#endif

	private static bool TryGetQueryValue(Uri uri, string key, out int value)
	{
		value = 0;
		string query = uri.Query;
		if (string.IsNullOrWhiteSpace(query))
		{
			return false;
		}

		foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
		{
			string[] parts = pair.Split('=', 2, StringSplitOptions.None);
			if (parts.Length != 2 || !string.Equals(Uri.UnescapeDataString(parts[0]), key, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			return int.TryParse(Uri.UnescapeDataString(parts[1]), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
		}

		return false;
	}

	private sealed record EditorStatePayload(
		string Text,
		string Language,
		string ThemeKey,
		double EditorFontSize,
		bool IsReadOnly,
		string EditableRangesJson,
		string DiagnosticsJson,
		string LanguageHelpJson,
		bool EnableResponseActions,
		bool ApplyText,
		int RequestedCursorLineNumber,
		int RequestedCursorColumn,
		int RequestedCursorVersion);
}

public sealed class MonacoResponseVarRequestEventArgs(int lineNumber, int column) : EventArgs
{
	public int LineNumber { get; } = lineNumber;

	public int Column { get; } = column;
}

public sealed class MonacoCursorPositionChangedEventArgs(int lineNumber, int column) : EventArgs
{
	public int LineNumber { get; } = lineNumber;

	public int Column { get; } = column;
}
