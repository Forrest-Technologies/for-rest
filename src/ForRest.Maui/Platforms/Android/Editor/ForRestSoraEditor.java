package com.forrest.maui.sora;

import android.content.Context;
import android.graphics.Color;
import android.graphics.Typeface;
import android.view.KeyEvent;
import android.view.inputmethod.InputMethodManager;
import android.widget.FrameLayout;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.HashMap;
import java.util.Map;

import io.github.rosemoe.sora.event.ContentChangeEvent;
import io.github.rosemoe.sora.event.EditorFocusChangeEvent;
import io.github.rosemoe.sora.event.EditorKeyEvent;
import io.github.rosemoe.sora.event.EventReceiver;
import io.github.rosemoe.sora.event.SelectionChangeEvent;
import io.github.rosemoe.sora.event.Unsubscribe;
import io.github.rosemoe.sora.lang.EmptyLanguage;
import io.github.rosemoe.sora.lang.diagnostic.DiagnosticRegion;
import io.github.rosemoe.sora.lang.diagnostic.DiagnosticsContainer;
import io.github.rosemoe.sora.langs.textmate.TextMateColorScheme;
import io.github.rosemoe.sora.langs.textmate.TextMateLanguage;
import io.github.rosemoe.sora.langs.textmate.registry.FileProviderRegistry;
import io.github.rosemoe.sora.langs.textmate.registry.GrammarRegistry;
import io.github.rosemoe.sora.langs.textmate.registry.ThemeRegistry;
import io.github.rosemoe.sora.langs.textmate.registry.model.ThemeModel;
import io.github.rosemoe.sora.langs.textmate.registry.provider.AssetsFileResolver;
import io.github.rosemoe.sora.text.Content;
import io.github.rosemoe.sora.text.Cursor;
import io.github.rosemoe.sora.widget.CodeEditor;
import io.github.rosemoe.sora.widget.schemes.EditorColorScheme;

import org.eclipse.tm4e.core.registry.IThemeSource;

/**
 * Java facade hosting the native Sora {@link CodeEditor}. The .NET MAUI Android build compiles and
 * binds this class (AndroidJavaSource), exposing a clean primitive API to C# while keeping all the
 * Kotlin/tm4e-heavy logic (TextMate grammars + themes, event wiring, diagnostics) on the Java side
 * so the .NET binding generator never has to bind the large Sora/tm4e surface directly.
 */
public final class ForRestSoraEditor extends FrameLayout {

    /** Callback surface relayed to the cross-platform editor view on the C# side. */
    public interface Listener {
        void onTextChanged(String text);

        void onCursorChanged(int line, int column);

        void onFocusChanged(boolean focused);

        void onSendRequested();

        void onUndoRequested();

        void onRedoRequested();
    }

    private static final Map<String, int[]> CHROME = buildChromeTable();
    private static final String[] THEME_KEYS = {
            "forrest-light", "forrest-azure", "forrest-dark", "forrest-black", "forrest-amber"
    };
    private static final String LANGUAGES_CONFIG = "sora/textmate/languages.json";
    private static final String THEME_DIR = "sora/textmate/themes";

    private static boolean textMateAttempted;
    private static boolean textMateReady;

    private final CodeEditor editor;
    private Listener listener;
    private String themeKey = "forrest-azure";
    private String languageId = "forrest";
    private boolean suppressEvents;

    public ForRestSoraEditor(Context context) {
        super(context);
        editor = new CodeEditor(context);
        configure();
        addView(editor, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT));
        subscribeEvents();
    }

    private void configure() {
        editor.setTypefaceText(Typeface.MONOSPACE);
        editor.setTextSize(14f);
        editor.setEditorLanguage(new EmptyLanguage());
        editor.setColorScheme(buildChromeScheme(themeKey));
        editor.setWordwrap(false);
        editor.setEditable(true);
    }

    // region Public API

    public void setListener(Listener value) {
        listener = value;
    }

    public void setText(String text) {
        String next = text == null ? "" : text;
        if (next.contentEquals(editor.getText())) {
            return;
        }
        suppressEvents = true;
        try {
            editor.setText(next);
        } finally {
            suppressEvents = false;
        }
    }

    public String getEditorText() {
        Content content = editor.getText();
        return content == null ? "" : content.toString();
    }

    public void setLanguageId(String value) {
        languageId = value == null || value.isEmpty() ? "plaintext" : value;
        String scope = scopeFor(languageId);
        if (scope != null && ensureTextMate()) {
            try {
                editor.setEditorLanguage(TextMateLanguage.create(scope, true));
                return;
            } catch (Throwable ignored) {
                // Fall through to plain text.
            }
        }
        editor.setEditorLanguage(new EmptyLanguage());
    }

    public void setThemeKey(String value) {
        themeKey = value == null || value.isEmpty() ? "forrest-azure" : value;
        EditorColorScheme scheme = buildTextMateScheme(themeKey);
        if (scheme == null) {
            scheme = buildChromeScheme(themeKey);
        }
        editor.setColorScheme(scheme);
    }

    public void setFontSize(double sp) {
        float size = sp > 0 && !Double.isInfinite(sp) && !Double.isNaN(sp) ? (float) sp : 14f;
        editor.setTextSize(size);
    }

    public void setReadOnly(boolean readOnly) {
        editor.setEditable(!readOnly);
    }

    public void moveCursor(int line, int column) {
        try {
            int targetLine = Math.max(0, line - 1);
            int lineCount = editor.getLineCount();
            if (targetLine >= lineCount) {
                targetLine = Math.max(0, lineCount - 1);
            }
            int maxColumn = editor.getText().getColumnCount(targetLine);
            int targetColumn = Math.max(0, Math.min(column - 1, maxColumn));
            editor.setSelection(targetLine, targetColumn);
            focusEditor();
        } catch (Throwable ignored) {
        }
    }

    public void pasteText(String text) {
        if (text == null || text.isEmpty() || !editor.isEditable()) {
            return;
        }
        try {
            Cursor cursor = editor.getCursor();
            editor.getText().insert(cursor.getLeftLine(), cursor.getLeftColumn(), text);
            focusEditor();
        } catch (Throwable ignored) {
        }
    }

    public void focusEditor() {
        editor.requestFocus();
        Object service = getContext().getSystemService(Context.INPUT_METHOD_SERVICE);
        if (service instanceof InputMethodManager) {
            ((InputMethodManager) service).showSoftInput(editor, InputMethodManager.SHOW_IMPLICIT);
        }
    }

    public void setDiagnostics(String json) {
        try {
            DiagnosticsContainer container = new DiagnosticsContainer();
            JSONArray items = json == null || json.isEmpty() ? new JSONArray() : new JSONArray(json);
            Content content = editor.getText();
            for (int i = 0; i < items.length(); i++) {
                JSONObject item = items.optJSONObject(i);
                if (item == null) {
                    continue;
                }
                int startIndex = charIndex(content, item.optInt("startLineNumber", 0), item.optInt("startColumn", 0));
                int endIndex = charIndex(content, item.optInt("endLineNumber", 0), item.optInt("endColumn", 0));
                if (startIndex < 0 || endIndex < 0 || endIndex < startIndex) {
                    continue;
                }
                if (endIndex == startIndex) {
                    endIndex = startIndex + 1;
                }
                short severity = "warning".equalsIgnoreCase(item.optString("severity", "error"))
                        ? DiagnosticRegion.SEVERITY_WARNING
                        : DiagnosticRegion.SEVERITY_ERROR;
                container.addDiagnostic(new DiagnosticRegion(startIndex, endIndex, severity));
            }
            editor.setDiagnostics(container);
        } catch (Throwable ignored) {
        }
    }

    // endregion

    // region Events

    private void subscribeEvents() {
        editor.subscribeEvent(ContentChangeEvent.class, new EventReceiver<ContentChangeEvent>() {
            @Override
            public void onReceive(ContentChangeEvent event, Unsubscribe unsubscribe) {
                if (suppressEvents || listener == null) {
                    return;
                }
                listener.onTextChanged(getEditorText());
                reportCursor();
            }
        });
        editor.subscribeEvent(SelectionChangeEvent.class, new EventReceiver<SelectionChangeEvent>() {
            @Override
            public void onReceive(SelectionChangeEvent event, Unsubscribe unsubscribe) {
                reportCursor();
            }
        });
        editor.subscribeEvent(EditorFocusChangeEvent.class, new EventReceiver<EditorFocusChangeEvent>() {
            @Override
            public void onReceive(EditorFocusChangeEvent event, Unsubscribe unsubscribe) {
                if (listener != null) {
                    listener.onFocusChanged(event.isGainFocus());
                }
            }
        });
        editor.subscribeEvent(EditorKeyEvent.class, new EventReceiver<EditorKeyEvent>() {
            @Override
            public void onReceive(EditorKeyEvent event, Unsubscribe unsubscribe) {
                handleKey(event);
            }
        });
    }

    private void reportCursor() {
        if (listener == null) {
            return;
        }
        try {
            Cursor cursor = editor.getCursor();
            listener.onCursorChanged(cursor.getLeftLine() + 1, cursor.getLeftColumn() + 1);
        } catch (Throwable ignored) {
        }
    }

    private void handleKey(EditorKeyEvent event) {
        if (event.getEventType() != EditorKeyEvent.Type.DOWN || listener == null) {
            return;
        }
        boolean ctrl = event.isCtrlPressed();
        int code = event.getKeyCode();
        if ((ctrl && code == KeyEvent.KEYCODE_ENTER) || code == KeyEvent.KEYCODE_F5) {
            listener.onSendRequested();
            event.intercept();
        } else if (ctrl && code == KeyEvent.KEYCODE_Z && !event.isShiftPressed()) {
            listener.onUndoRequested();
            event.intercept();
        } else if (ctrl && (code == KeyEvent.KEYCODE_Y
                || (code == KeyEvent.KEYCODE_Z && event.isShiftPressed()))) {
            listener.onRedoRequested();
            event.intercept();
        }
    }

    // endregion

    // region TextMate

    private static String scopeFor(String languageId) {
        if ("forrest".equalsIgnoreCase(languageId)) {
            return "source.forrest";
        }
        if ("json".equalsIgnoreCase(languageId)) {
            return "source.json";
        }
        if ("settings-toml".equalsIgnoreCase(languageId)) {
            return "source.toml";
        }
        return null;
    }

    private boolean ensureTextMate() {
        if (textMateAttempted) {
            return textMateReady;
        }
        textMateAttempted = true;
        try {
            FileProviderRegistry.getInstance().addFileProvider(
                    new AssetsFileResolver(getContext().getApplicationContext().getAssets()));
            GrammarRegistry.getInstance().loadGrammars(LANGUAGES_CONFIG);
            for (String key : THEME_KEYS) {
                String path = THEME_DIR + "/" + key + ".json";
                try {
                    IThemeSource source = IThemeSource.fromInputStream(
                            FileProviderRegistry.getInstance().tryGetInputStream(path), path, null);
                    ThemeRegistry.getInstance().loadTheme(new ThemeModel(source, key));
                } catch (Throwable ignored) {
                }
            }
            textMateReady = true;
        } catch (Throwable ignored) {
            textMateReady = false;
        }
        return textMateReady;
    }

    private EditorColorScheme buildTextMateScheme(String key) {
        if (!ensureTextMate()) {
            return null;
        }
        try {
            ThemeRegistry.getInstance().setTheme(key);
            return TextMateColorScheme.create(ThemeRegistry.getInstance());
        } catch (Throwable ignored) {
            return null;
        }
    }

    // endregion

    // region Chrome scheme

    private static EditorColorScheme buildChromeScheme(String key) {
        int[] p = CHROME.containsKey(key) ? CHROME.get(key) : CHROME.get("forrest-azure");
        EditorColorScheme scheme = new EditorColorScheme();
        scheme.setColor(EditorColorScheme.WHOLE_BACKGROUND, p[0]);
        scheme.setColor(EditorColorScheme.TEXT_NORMAL, p[1]);
        scheme.setColor(EditorColorScheme.LINE_NUMBER_BACKGROUND, p[2]);
        scheme.setColor(EditorColorScheme.LINE_NUMBER, p[3]);
        scheme.setColor(EditorColorScheme.LINE_NUMBER_CURRENT, p[4]);
        scheme.setColor(EditorColorScheme.CURRENT_LINE, p[5]);
        scheme.setColor(EditorColorScheme.SELECTED_TEXT_BACKGROUND, p[6]);
        scheme.setColor(EditorColorScheme.SELECTION_INSERT, p[7]);
        scheme.setColor(EditorColorScheme.SELECTION_HANDLE, p[7]);
        scheme.setColor(EditorColorScheme.NON_PRINTABLE_CHAR, p[8]);
        scheme.setColor(EditorColorScheme.BLOCK_LINE, p[9]);
        scheme.setColor(EditorColorScheme.BLOCK_LINE_CURRENT, p[10]);
        return scheme;
    }

    private static Map<String, int[]> buildChromeTable() {
        Map<String, int[]> map = new HashMap<>();
        // bg, fg, gutter, lineNumber, lineNumberActive, currentLine, selection, cursor, whitespace, indent, indentActive
        map.put("forrest-light", parse("#FCFDFE", "#16202A", "#F2F4F7", "#9CA7B3", "#384552", "#F5F7FA", "#E1EAF2", "#16202A", "#D5DDE6", "#E6EBF0", "#C9D2DB"));
        map.put("forrest-azure", parse("#EDF5FD", "#0F2236", "#E2EDF8", "#5E7B97", "#1E3B57", "#E1F0FC", "#CFE4F8", "#0F2236", "#B3C9DE", "#C5D8EB", "#9FBAD5"));
        map.put("forrest-dark", parse("#141B24", "#EEF3F7", "#1A232D", "#6F8092", "#C5D2DE", "#19222D", "#29425F", "#EEF3F7", "#2E3A46", "#27313A", "#3E4B57"));
        map.put("forrest-black", parse("#101419", "#F3F6F8", "#151B21", "#66727F", "#E4EBF1", "#141A20", "#243341", "#F3F6F8", "#27313B", "#202932", "#34414D"));
        map.put("forrest-amber", parse("#FFFCF6", "#2B2218", "#F5ECDD", "#A08F79", "#6A5034", "#FBF3E6", "#F2DFC0", "#6D4A20", "#E1D3BE", "#E9DCC8", "#D5BE97"));
        return map;
    }

    private static int[] parse(String... hex) {
        int[] colors = new int[hex.length];
        for (int i = 0; i < hex.length; i++) {
            int value;
            try {
                value = Color.parseColor(hex[i]);
            } catch (Throwable t) {
                value = Color.parseColor("#0F2236");
            }
            colors[i] = value;
        }
        return colors;
    }

    private static int charIndex(Content content, int line, int column) {
        try {
            int target = Math.max(0, line - 1);
            int lineCount = content.getLineCount();
            if (target >= lineCount) {
                target = Math.max(0, lineCount - 1);
            }
            int maxColumn = content.getColumnCount(target);
            int col = Math.max(0, Math.min(column - 1, maxColumn));
            return content.getCharIndex(target, col);
        } catch (Throwable t) {
            return -1;
        }
    }

    // endregion
}
