/* Folio 2.0 / PatchMe: intentionally patchable offline document converter.
 * Build with -O0. Keep can_export_pro and g_licensed symbols for the IDA lesson.
 * All premium exports share one gate. See ORGANIZER.md for the exercise. */
#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <commdlg.h>
#include <commctrl.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>
#include <wctype.h>
#include <stdarg.h>

#define APP_TITLE L"Folio - Document Converter"
#define EXPECTED_HASH 0x9BA8503CUL
#define MAX_TEXT 131072
#define MAX_FILE (MAX_TEXT * 4)
#define IDC_OPEN 101
#define IDC_LICENSE 102
#define IDC_EDITOR 104
#define IDC_TITLE 105
#define IDC_FORMAT 106
#define IDC_EXPORT 107
#define IDC_KEY 108
#define IDC_ACTIVATE 109
#define IDC_RESET 110
#define IDC_STATUS 111
#define IDC_STATS 112
#define IDC_PLAN 113
#define IDC_FORMAT_HELP 114

static int g_licensed = 0;
static HINSTANCE g_inst;
static HWND g_window, g_editor, g_title, g_format, g_key, g_status, g_stats, g_plan;
static HFONT g_font, g_small, g_heading, g_brand, g_mono;
static HBRUSH g_paper, g_white;
static int g_dirty = 0, g_setting = 0;
static int g_license_open = 0;
static float g_scale = 1.0f;
static const COLORREF INK = RGB(35,48,45), MUTED = RGB(103,117,109);
static const COLORREF GREEN = RGB(29,91,70), PAPER = RGB(246,247,242);
static const wchar_t SAMPLE[] =
    L"# Meeting notes\r\n\r\n"
    L"A quick recap of today's discussion.\r\n\r\n"
    L"## Next steps\r\n"
    L"- Finish the first draft\r\n"
    L"- Share it with the team\r\n\r\n"
    L"> Keep it simple.";

static int px(int n) { return (int)(n * g_scale + 0.5f); }
static void status(const wchar_t *s) { if (g_status) SetWindowTextW(g_status, s); }

static unsigned long hash_key(const wchar_t *s)
{
    unsigned long h = 5381UL;
    while (*s) h = (h * 33UL + (unsigned short)*s++) & 0xFFFFFFFFUL;
    return h;
}
static void normalize_key(wchar_t *out, const wchar_t *in)
{
    size_t n;
    while (*in && iswspace(*in)) in++;
    n = wcslen(in);
    while (n && iswspace(in[n-1])) n--;
    if (n > 127) n = 127;
    for (size_t i = 0; i < n; i++) out[i] = towupper(in[i]);
    out[n] = 0;
}
static int is_valid_key(const wchar_t *key)
{
    wchar_t norm[128];
    if (wcslen(key) > 127) return 0;
    normalize_key(norm, key);
    if (wcslen(norm) < 8) return 0;
    if (hash_key(norm) == EXPECTED_HASH) return 1;
    return 0;
}
static int license_path(wchar_t *out, int make_dir)
{
    wchar_t base[MAX_PATH];
    DWORD n = GetEnvironmentVariableW(L"LOCALAPPDATA", base, MAX_PATH);
    if (!n || n >= MAX_PATH - 24) return 0;
    swprintf(out, MAX_PATH, L"%ls\\PatchMe", base);
    if (make_dir) CreateDirectoryW(out, NULL);
    wcscat(out, L"\\license.dat");
    return 1;
}
static void load_license(void)
{
    wchar_t path[MAX_PATH], key[128]; char raw[128]; FILE *f;
    if (!license_path(path, 0) || !(f = _wfopen(path, L"rb"))) return;
    size_t n = fread(raw, 1, sizeof(raw)-1, f); fclose(f); raw[n] = 0;
    raw[strcspn(raw, "\r\n")] = 0;
    if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, raw, -1, key, 128))
        g_licensed = is_valid_key(key);
}

/* Canonical patch: jne -> je (one bit) or jne -> jmp (always allow).
 * Find the UTF-16 lock string in IDA; its xref leads to this function.
 * Both HTML and RTF call this gate; button labels do not enforce access. */
__attribute__((noinline)) static int can_export_pro(void)
{
    if (g_licensed == 0) {
        status(L"PRO license required for Word export. Open License to activate.");
        return 0;
    }
    return 1;
}

typedef struct { char *data; size_t len, cap; int failed; } Buffer;
static void append_n(Buffer *b, const char *s, size_t n)
{
    if (b->failed) return;
    if (b->len + n + 1 > b->cap) {
        size_t cap = (b->len + n + 1) * 2;
        char *p = (char *)realloc(b->data, cap);
        if (!p) { b->failed = 1; return; }
        b->data = p; b->cap = cap;
    }
    memcpy(b->data + b->len, s, n);
    b->len += n; b->data[b->len] = 0;
}
static void append(Buffer *b, const char *s) { append_n(b, s, strlen(s)); }
static void appendf(Buffer *b, const char *fmt, ...)
{
    char tmp[160]; va_list ap;
    va_start(ap, fmt); int n = vsnprintf(tmp, sizeof(tmp), fmt, ap); va_end(ap);
    if (n < 0 || n >= (int)sizeof(tmp)) { b->failed = 1; return; }
    append_n(b, tmp, (size_t)n);
}
static void utf8(Buffer *b, const wchar_t *s, size_t n)
{
    if (!n) return;
    int size = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, s, (int)n, NULL, 0, NULL, NULL);
    if (!size) { b->failed = 1; return; }
    char *tmp = (char *)malloc((size_t)size);
    if (!tmp) { b->failed = 1; return; }
    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, s, (int)n, tmp, size, NULL, NULL);
    append_n(b, tmp, (size_t)size); free(tmp);
}
/* Always escape user content. Raw HTML and inline links are not interpreted. */
static void html_text(Buffer *b, const wchar_t *s, size_t n)
{
    size_t start = 0;
    for (size_t i = 0; i < n; i++) {
        const char *escape = NULL;
        switch (s[i]) {
        case L'&': escape = "&amp;"; break;
        case L'<': escape = "&lt;"; break;
        case L'>': escape = "&gt;"; break;
        case L'\"': escape = "&quot;"; break;
        case L'\'': escape = "&#39;"; break;
        }
        if (escape) { utf8(b, s + start, i - start); append(b, escape); start = i+1; }
    }
    utf8(b, s + start, n - start);
}
static void rtf_text(Buffer *b, const wchar_t *s, size_t n)
{
    for (size_t i = 0; i < n; i++) {
        unsigned short c = (unsigned short)s[i];
        if (c == '\\' || c == '{' || c == '}') { append(b, "\\"); char v = (char)c; append_n(b, &v, 1); }
        else if (c == '\t') append(b, "\\tab ");
        else if (c >= 32 && c < 127) { char v = (char)c; append_n(b, &v, 1); }
        else appendf(b, "\\u%d?", (int)(short)c);
    }
}
/* Explicit Markdown subset: headings, bullet lists, quotes, paragraphs.
 * Unsupported syntax is preserved literally. */
static int line_kind(const wchar_t **s, size_t *n)
{
    size_t h = 0;
    while (h < *n && h < 3 && (*s)[h] == '#') h++;
    if (h && h < *n && (*s)[h] == ' ') { *s += h+1; *n -= h+1; return (int)h; }
    if (*n >= 2 && (((*s)[0] == '-' || (*s)[0] == '*') && (*s)[1] == ' ')) {
        *s += 2; *n -= 2; return 4;
    }
    if (*n && (*s)[0] == '>') {
        (*s)++; (*n)--;
        if (*n && **s == ' ') { (*s)++; (*n)--; }
        return 5;
    }
    return 0;
}
static Buffer convert_document(const wchar_t *source, const wchar_t *title, int format)
{
    Buffer b = {0};
    if (format == 1) {
        append(&b, "<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">"
            "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>");
        html_text(&b, title, wcslen(title));
        append(&b, "</title><style>body{margin:0;background:#f6f7f2;color:#23302d;font:17px/1.75 system-ui,sans-serif}"
            "main{max-width:740px;margin:48px auto;padding:48px;background:white;border:1px solid #e1e6df;border-radius:12px}"
            "h1,h2,h3{line-height:1.2;letter-spacing:-.025em}h1{font-size:36px}h2{margin-top:36px}"
            "p,li,blockquote{white-space:pre-wrap;overflow-wrap:anywhere}blockquote{border-left:3px solid #1d5b46;"
            "margin:24px 0;padding:8px 24px;color:#52645b}li{margin:6px 0}"
            "header{color:#62776b;font-size:12px;letter-spacing:.12em;text-transform:uppercase;margin-bottom:32px}"
            "@media(max-width:600px){main{margin:12px;padding:24px}}"
            "@media print{body{background:white}main{border:0;margin:0;padding:0}}</style></head><body><main><header>");
        html_text(&b, title, wcslen(title)); append(&b, "</header>\n");
    } else {
        append(&b, "{\\rtf1\\ansi\\ansicpg1252\\uc1\\deff0{\\fonttbl{\\f0 Calibri;}}"
            "{\\colortbl;\\red35\\green48\\blue45;\\red29\\green91\\blue70;}"
            "\\paperw11906\\paperh16838\\margl1440\\margr1440\\margt1440\\margb1440\n"
            "{\\info{\\title ");
        rtf_text(&b, title, wcslen(title)); append(&b, "}}\n\\pard\\f0\\fs20\\cf2 ");
        rtf_text(&b, title, wcslen(title)); append(&b, "\\par\n");
    }
    const wchar_t *p = source; int in_list = 0;
    while (*p) {
        const wchar_t *line = p;
        while (*p && *p != '\r' && *p != '\n') p++;
        size_t n = (size_t)(p-line);
        if (*p == '\r') p++;
        if (*p == '\n') p++;
        int kind = line_kind(&line, &n);
        if (format == 1) {
            if (in_list && kind != 4) { append(&b, "</ul>\n"); in_list = 0; }
            if (!n) continue;
            if (kind == 4 && !in_list) { append(&b, "<ul>\n"); in_list = 1; }
            const char *tag = kind == 1 ? "h1" : kind == 2 ? "h2" : kind == 3 ? "h3" :
                              kind == 4 ? "li" : kind == 5 ? "blockquote" : "p";
            appendf(&b, "<%s>", tag); html_text(&b, line, n); appendf(&b, "</%s>\n", tag);
        } else {
            append(&b, "\\pard\\f0\\cf1\\b0\\i0\\fs24\\sa180 ");
            if (kind >= 1 && kind <= 3) appendf(&b, "\\b\\fs%d\\sb240 ", 44 - kind * 4);
            if (kind == 4) append(&b, "\\li360\\fi-240 \\u8226?\\tab ");
            if (kind == 5) append(&b, "\\li360\\i\\cf2 ");
            rtf_text(&b, line, n); append(&b, "\\par\n");
        }
    }
    if (format == 1) { if (in_list) append(&b, "</ul>\n"); append(&b, "</main></body></html>\n"); }
    else append(&b, "}");
    return b;
}
static wchar_t *editor_text(void)
{
    int n = GetWindowTextLengthW(g_editor);
    wchar_t *s = (wchar_t *)calloc((size_t)n + 1, sizeof(wchar_t));
    if (s) GetWindowTextW(g_editor, s, n+1);
    else status(L"Not enough memory to read this document.");
    return s;
}
static void update_stats(void)
{
    wchar_t *s = editor_text(), text[120];
    if (!s) return;
    size_t words = 0; int word = 0;
    for (wchar_t *p = s; *p; p++) {
        if (iswspace(*p)) word = 0;
        else if (!word) { words++; word = 1; }
    }
    swprintf(text, 120, L"%zu words%ls", words,
        g_dirty ? L"  /  Not converted yet" : L"");
    SetWindowTextW(g_stats, text); free(s);
}
static void refresh_plan(void)
{
    SetWindowTextW(g_plan, g_licensed ? L"PRO" : L"FREE");
    InvalidateRect(g_plan, NULL, TRUE);
}
static void format_help(void)
{
    status(L"");
}
static int confirm_replace(void)
{
    return !g_dirty || MessageBoxW(g_window, L"Your edits have not been converted. Discard them?",
        L"Unconverted changes", MB_OKCANCEL | MB_ICONWARNING) == IDOK;
}
/* UTF-8 (optional BOM) or UTF-16 LE with BOM; reject binary / oversized input. */
static wchar_t *decode_file(const unsigned char *raw, size_t n)
{
    wchar_t *text = NULL;
    if (n >= 2 && raw[0] == 0xff && raw[1] == 0xfe) {
        if ((n-2) % 2 || (n-2)/2 >= MAX_TEXT) return NULL;
        text = (wchar_t *)calloc(n/2, sizeof(wchar_t));
        if (!text) return NULL;
        memcpy(text, raw+2, n-2);
        if (wcslen(text) != (n-2)/2 || ((n > 2) && !WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text, (int)((n-2)/2), NULL, 0, NULL, NULL))) {
            free(text); return NULL;
        }
    } else {
        if (n >= 3 && !memcmp(raw, "\xef\xbb\xbf", 3)) { raw += 3; n -= 3; }
        if (memchr(raw, 0, n)) return NULL;
        int len = n ? MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, (const char *)raw, (int)n, NULL, 0) : 0;
        if ((n && !len) || len >= MAX_TEXT) return NULL;
        text = (wchar_t *)calloc((size_t)len+1, sizeof(wchar_t));
        if (!text) return NULL;
        if (n) MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, (const char *)raw, (int)n, text, len);
    }
    /* Native EDIT needs CRLF. Reject expansion beyond the editor limit. */
    size_t len = wcslen(text), j = 0;
    wchar_t *lines = (wchar_t *)calloc(len*2+1, sizeof(wchar_t));
    if (!lines) { free(text); return NULL; }
    for (size_t i = 0; i < len; i++) {
        if (text[i] == '\r' || text[i] == '\n') {
            if (text[i] == '\r' && text[i+1] == '\n') i++;
            lines[j++] = '\r'; lines[j++] = '\n';
        } else if (text[i] < 32 && text[i] != '\t') { free(lines); free(text); return NULL; }
        else lines[j++] = text[i];
    }
    free(text);
    if (j >= MAX_TEXT) { free(lines); return NULL; }
    return lines;
}
static void open_document(void)
{
    wchar_t path[MAX_PATH] = L""; OPENFILENAMEW of = {0};
    if (!confirm_replace()) return;
    of.lStructSize = sizeof(of); of.hwndOwner = g_window;
    of.lpstrFilter = L"Text & Markdown (*.txt;*.md)\0*.txt;*.md\0All files\0*.*\0";
    of.lpstrFile = path; of.nMaxFile = MAX_PATH;
    of.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;
    if (!GetOpenFileNameW(&of)) return;
    FILE *f = _wfopen(path, L"rb");
    if (!f) { status(L"This file could not be opened. Check its location and permissions."); return; }
    unsigned char *raw = (unsigned char *)malloc(MAX_FILE+1);
    if (!raw) { fclose(f); status(L"Not enough memory to open this document."); return; }
    size_t n = fread(raw, 1, MAX_FILE+1, f); int bad = ferror(f); fclose(f);
    wchar_t *text = !bad && n <= MAX_FILE ? decode_file(raw, n) : NULL; free(raw);
    if (!text) { status(L"Use UTF-8 or UTF-16 LE text, under 128K characters. This file cannot be imported."); return; }
    wchar_t *name = wcsrchr(path, '\\'); name = name ? name+1 : path;
    wchar_t title[180]; wcsncpy(title, name, 179); title[179] = 0;
    wchar_t *dot = wcsrchr(title, '.'); if (dot) *dot = 0;
    g_setting = 1; SetWindowTextW(g_title, title); SetWindowTextW(g_editor, text);
    g_setting = 0; g_dirty = 0; update_stats(); free(text);
    status(L"Ready to convert.");
}
/* Stage beside the destination; replace only after a full write. */
static int write_document(const wchar_t *path, const Buffer *b)
{
    wchar_t dir[MAX_PATH], temp[MAX_PATH];
    if (b->failed || wcslen(path) >= MAX_PATH) return 0;
    wcscpy(dir, path); wchar_t *slash = wcsrchr(dir, '\\');
    if (slash) slash[1] = 0; else wcscpy(dir, L".");
    if (!GetTempFileNameW(dir, L"flo", 0, temp)) return 0;
    FILE *f = _wfopen(temp, L"wb");
    if (!f) { DeleteFileW(temp); return 0; }
    int ok = fwrite(b->data ? b->data : "", 1, b->len, f) == b->len;
    if (fclose(f)) ok = 0;
    if (ok) ok = MoveFileExW(temp, path, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) != 0;
    if (!ok) DeleteFileW(temp);
    return ok;
}
static void export_document(void)
{
    int format = (int)SendMessageW(g_format, CB_GETCURSEL, 0, 0) + 1;
    if (format < 1 || format > 2) return;
    if (format == 2 && !can_export_pro()) return;
    wchar_t *source = editor_text(), title[180], path[MAX_PATH];
    if (!source) return;
    if (!*source) { free(source); status(L"Add some text before exporting a document."); return; }
    GetWindowTextW(g_title, title, 180);
    if (!*title) wcscpy(title, L"Untitled document");
    const wchar_t *ext = format == 1 ? L"html" : L"rtf";
    swprintf(path, MAX_PATH, L"%ls.%ls", title, ext);
    for (wchar_t *p = path; *p; p++) if (wcschr(L"<>:\"/\\|?*", *p) || *p < 32) *p = '_';
    OPENFILENAMEW of = {0}; of.lStructSize = sizeof(of); of.hwndOwner = g_window;
    of.lpstrFilter = format == 1 ? L"Web document (*.html)\0*.html\0" : L"Word-compatible document (*.rtf)\0*.rtf\0";
    of.lpstrFile = path; of.nMaxFile = MAX_PATH; of.lpstrDefExt = ext;
    of.Flags = OFN_OVERWRITEPROMPT | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;
    of.lpstrTitle = L"Save converted document";
    if (!GetSaveFileNameW(&of)) { free(source); return; }
    Buffer b = convert_document(source, title, format); free(source);
    if (b.failed || !write_document(path, &b)) status(L"Export failed. Check the destination and available disk space.");
    else {
        wchar_t msg[MAX_PATH+60];
        const wchar_t *name = wcsrchr(path, '\\'); name = name ? name+1 : path;
        swprintf(msg, MAX_PATH+60, L"Saved: %ls", name); status(msg);
        g_dirty = 0; update_stats();
    }
    free(b.data);
}
static void activate(void)
{
    wchar_t key[128], norm[128], path[MAX_PATH]; char raw[512];
    GetWindowTextW(g_key, key, 128);
    if (!is_valid_key(key)) { status(L"That license key is not valid. Check the key and try again."); return; }
    normalize_key(norm, key);
    int len = WideCharToMultiByte(CP_UTF8, 0, norm, -1, raw, sizeof(raw), NULL, NULL);
    Buffer b = {raw, len ? (size_t)len-1 : 0, sizeof(raw), !len};
    if (!license_path(path, 1) || !write_document(path, &b)) {
        status(L"The key is valid, but the license could not be saved. Check folder permissions."); return;
    }
    g_licensed = 1; refresh_plan(); SetWindowTextW(g_key, L"");
    status(L"Pro activated. Word export is unlocked.");
}
static void reset_license(void)
{
    wchar_t path[MAX_PATH];
    if (!license_path(path, 0)) { status(L"The license folder is unavailable."); return; }
    if (!DeleteFileW(path) && GetLastError() != ERROR_FILE_NOT_FOUND && GetLastError() != ERROR_PATH_NOT_FOUND) {
        status(L"The license could not be removed. Check folder permissions."); return;
    }
    g_licensed = 0; refresh_plan(); status(L"License removed. HTML export is free.");
}
static HFONT font(int size, int weight, const wchar_t *face)
{
    return CreateFontW(-px(size), 0, 0, 0, weight, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
        OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH, face);
}
static HWND ctl(const wchar_t *cls, const wchar_t *text, DWORD style, int x, int y, int w, int h, int id, HFONT f)
{
    HWND win = CreateWindowExW(0, cls, text, WS_CHILD | WS_VISIBLE | style,
        px(x), px(y), px(w), px(h), g_window, (HMENU)(INT_PTR)id, g_inst, NULL);
    SendMessageW(win, WM_SETFONT, (WPARAM)f, TRUE); return win;
}
static HWND button(const wchar_t *s, int x, int y, int w, int id)
{
    return ctl(L"BUTTON", s, WS_TABSTOP | BS_OWNERDRAW, x, y, w, 38, id, g_font);
}
static void paint_text(HDC dc, const wchar_t *s, int x, int y, int w, int h, HFONT f, COLORREF color)
{
    RECT r = {px(x), px(y), px(x+w), px(y+h)};
    SelectObject(dc, f); SetTextColor(dc, color); SetBkMode(dc, TRANSPARENT);
    DrawTextW(dc, s, -1, &r, DT_LEFT | DT_WORDBREAK | DT_NOPREFIX);
}
static void draw_button(DRAWITEMSTRUCT *d)
{
    int primary = d->CtlID == IDC_EXPORT || d->CtlID == IDC_ACTIVATE;
    COLORREF bg = primary ? GREEN : RGB(255,255,255);
    if (d->itemState & ODS_SELECTED) bg = primary ? RGB(20,65,48) : RGB(231,237,229);
    HBRUSH brush = CreateSolidBrush(bg); HPEN pen = CreatePen(PS_SOLID, 1, primary ? bg : RGB(213,221,211));
    HGDIOBJ oldb = SelectObject(d->hDC, brush), oldp = SelectObject(d->hDC, pen);
    RoundRect(d->hDC, d->rcItem.left, d->rcItem.top, d->rcItem.right, d->rcItem.bottom, px(8), px(8));
    SelectObject(d->hDC, oldb); SelectObject(d->hDC, oldp); DeleteObject(brush); DeleteObject(pen);
    wchar_t s[100]; GetWindowTextW(d->hwndItem, s, 100);
    RECT r = d->rcItem; SetBkMode(d->hDC, TRANSPARENT);
    SetTextColor(d->hDC, primary ? RGB(255,255,255) : INK); SelectObject(d->hDC, g_font);
    DrawTextW(d->hDC, s, -1, &r, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_HIDEPREFIX);
    if (d->itemState & ODS_FOCUS) { InflateRect(&r, -px(4), -px(4)); DrawFocusRect(d->hDC, &r); }
}
static void paint_window(void)
{
    PAINTSTRUCT ps; HDC dc = BeginPaint(g_window, &ps);
    RECT r; GetClientRect(g_window, &r); FillRect(dc, &r, g_paper);
    HPEN pen = CreatePen(PS_SOLID, 1, RGB(220,226,216)); HGDIOBJ old = SelectObject(dc, pen);
    MoveToEx(dc, px(24), px(92), NULL); LineTo(dc, px(736), px(92));
    if (g_license_open) { MoveToEx(dc, px(24), px(554), NULL); LineTo(dc, px(736), px(554)); }
    SelectObject(dc, old); DeleteObject(pen);
    paint_text(dc, L"folio", 24, 15, 100, 43, g_brand, GREEN);
    paint_text(dc, L"Markdown to HTML or Word", 26, 62, 390, 23, g_small, MUTED);
    if (g_license_open) paint_text(dc, L"Pro license key", 24, 568, 710, 20, g_small, MUTED);
    EndPaint(g_window, &ps);
}
static void toggle_license(void)
{
    g_license_open = !g_license_open;
    RECT r = {0,0,px(760),px(g_license_open ? 646 : 554)};
    AdjustWindowRect(&r, (DWORD)GetWindowLongPtrW(g_window, GWL_STYLE), FALSE);
    SetWindowPos(g_window, NULL, 0, 0, r.right-r.left, r.bottom-r.top, SWP_NOMOVE | SWP_NOZORDER);
    ShowWindow(g_key, g_license_open ? SW_SHOW : SW_HIDE);
    ShowWindow(GetDlgItem(g_window, IDC_ACTIVATE), g_license_open ? SW_SHOW : SW_HIDE);
    ShowWindow(GetDlgItem(g_window, IDC_RESET), g_license_open ? SW_SHOW : SW_HIDE);
    InvalidateRect(g_window, NULL, TRUE);
    if (g_license_open) SetFocus(g_key);
}
static void create_controls(void)
{
    g_plan = ctl(L"STATIC", L"FREE", 0, 123, 32, 70, 20, IDC_PLAN, g_small);
    button(L"Open .md / .txt", 468, 24, 146, IDC_OPEN);
    button(L"License", 626, 24, 110, IDC_LICENSE);
    g_key = ctl(L"EDIT", L"", WS_TABSTOP | WS_BORDER | ES_AUTOHSCROLL, 24, 598, 430, 30, IDC_KEY, g_font);
    SendMessageW(g_key, EM_SETLIMITTEXT, 127, 0);
    button(L"Activate", 466, 594, 125, IDC_ACTIVATE);
    button(L"Remove", 603, 594, 133, IDC_RESET);
    ShowWindow(g_key, SW_HIDE);
    ShowWindow(GetDlgItem(g_window, IDC_ACTIVATE), SW_HIDE);
    ShowWindow(GetDlgItem(g_window, IDC_RESET), SW_HIDE);
    g_title = ctl(L"EDIT", L"Meeting notes", WS_TABSTOP | WS_BORDER | ES_AUTOHSCROLL,
        24, 112, 712, 30, IDC_TITLE, g_font);
    SendMessageW(g_title, EM_SETLIMITTEXT, 170, 0);
    g_editor = ctl(L"EDIT", L"", WS_TABSTOP | WS_BORDER | WS_VSCROLL | ES_MULTILINE | ES_AUTOVSCROLL | ES_WANTRETURN,
        24, 156, 712, 268, IDC_EDITOR, g_mono);
    SendMessageW(g_editor, EM_SETLIMITTEXT, MAX_TEXT-1, 0);
    RECT inset = {px(14), px(12), px(682), px(255)}; SendMessageW(g_editor, EM_SETRECT, 0, (LPARAM)&inset);
    g_stats = ctl(L"STATIC", L"", 0, 24, 436, 712, 22, IDC_STATS, g_small);
    g_format = ctl(L"COMBOBOX", L"", WS_TABSTOP | CBS_DROPDOWNLIST | WS_VSCROLL,
        24, 476, 512, 130, IDC_FORMAT, g_font);
    SendMessageW(g_format, CB_ADDSTRING, 0, (LPARAM)L"HTML (.html)  -  Free");
    SendMessageW(g_format, CB_ADDSTRING, 0, (LPARAM)L"Word (.rtf)  -  PRO");
    SendMessageW(g_format, CB_SETCURSEL, 0, 0);
    button(L"Convert", 552, 468, 184, IDC_EXPORT);
    g_status = ctl(L"STATIC", L"", SS_LEFT, 24, 518, 712, 32, IDC_STATUS, g_small);
    g_setting = 1; SetWindowTextW(g_editor, SAMPLE); g_setting = 0; g_dirty = 0;
    update_stats(); refresh_plan(); format_help();
}
static LRESULT CALLBACK wnd_proc(HWND window, UINT msg, WPARAM wp, LPARAM lp)
{
    switch (msg) {
    case WM_PAINT: paint_window(); return 0;
    case WM_DRAWITEM: draw_button((DRAWITEMSTRUCT *)lp); return TRUE;
    case WM_CTLCOLORSTATIC: {
        HDC dc = (HDC)wp;
        SetTextColor(dc, (HWND)lp == g_plan || (HWND)lp == g_status ? GREEN : MUTED);
        SetBkColor(dc, PAPER);
        return (LRESULT)g_paper;
    }
    case WM_CTLCOLOREDIT: SetTextColor((HDC)wp, INK); SetBkColor((HDC)wp, RGB(255,255,255)); return (LRESULT)g_white;
    case WM_COMMAND:
        if ((LOWORD(wp) == IDC_EDITOR || LOWORD(wp) == IDC_TITLE) && HIWORD(wp) == EN_CHANGE) {
            if (!g_setting && g_stats) { g_dirty = 1; update_stats(); }
            return 0;
        }
        if (LOWORD(wp) == IDC_FORMAT && HIWORD(wp) == CBN_SELCHANGE) { format_help(); return 0; }
        if (HIWORD(wp) != BN_CLICKED) return 0;
        switch (LOWORD(wp)) {
        case IDC_OPEN: open_document(); break;
        case IDC_LICENSE: toggle_license(); break;
        case IDC_EXPORT: export_document(); break;
        case IDC_ACTIVATE: activate(); break;
        case IDC_RESET: reset_license(); break;
        }
        return 0;
    case WM_CLOSE: if (confirm_replace()) DestroyWindow(window); return 0;
    case WM_DESTROY: PostQuitMessage(0); return 0;
    }
    return DefWindowProcW(window, msg, wp, lp);
}
int WINAPI WinMain(HINSTANCE inst, HINSTANCE prev, LPSTR cmd, int show)
{
    (void)prev; (void)cmd; g_inst = inst;
    SetProcessDPIAware();
    HDC screen = GetDC(NULL); g_scale = GetDeviceCaps(screen, LOGPIXELSX) / 96.0f; ReleaseDC(NULL, screen);
    g_paper = CreateSolidBrush(PAPER); g_white = CreateSolidBrush(RGB(255,255,255));
    g_font = font(14, FW_NORMAL, L"Segoe UI"); g_small = font(12, FW_NORMAL, L"Segoe UI");
    g_heading = font(25, FW_SEMIBOLD, L"Segoe UI"); g_brand = font(34, FW_BOLD, L"Georgia");
    g_mono = font(14, FW_NORMAL, L"Consolas"); load_license();
    WNDCLASSW wc = {0}; wc.lpfnWndProc = wnd_proc; wc.hInstance = inst;
    wc.hCursor = LoadCursorW(NULL, IDC_ARROW); wc.hIcon = LoadIconW(NULL, IDI_APPLICATION);
    wc.hbrBackground = g_paper; wc.lpszClassName = L"FolioDocumentStudio";
    if (!RegisterClassW(&wc)) return 1;
    DWORD style = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX | WS_CLIPCHILDREN;
    RECT rc = {0,0,px(760),px(554)}, work;
    AdjustWindowRect(&rc, style, FALSE); SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0);
    int w = rc.right-rc.left, h = rc.bottom-rc.top;
    g_window = CreateWindowExW(WS_EX_CONTROLPARENT, wc.lpszClassName, APP_TITLE, style,
        work.left + (work.right-work.left-w)/2, work.top + (work.bottom-work.top-h)/2,
        w, h, NULL, NULL, inst, NULL);
    if (!g_window) return 1;
    create_controls(); ShowWindow(g_window, show); UpdateWindow(g_window);
    MSG m; int result;
    while ((result = GetMessageW(&m, NULL, 0, 0)) > 0) {
        if (m.message == WM_KEYDOWN && GetKeyState(VK_CONTROL) < 0 && (m.wParam == 'O' || m.wParam == 'S')) {
            if (m.wParam == 'O') open_document(); else export_document();
        } else if (!IsDialogMessageW(g_window, &m)) { TranslateMessage(&m); DispatchMessageW(&m); }
    }
    DeleteObject(g_font); DeleteObject(g_small); DeleteObject(g_heading); DeleteObject(g_brand); DeleteObject(g_mono);
    DeleteObject(g_paper); DeleteObject(g_white); return result == -1 ? 1 : 0;
}
