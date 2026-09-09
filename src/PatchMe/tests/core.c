/* Tests compile the real app in one translation unit; no test bypass in release. */
#include "../src/patchme.c"
#include <assert.h>

static void contains(const Buffer *b, const char *s)
{
    assert(!b->failed && b->data && strstr(b->data, s));
}

int main(int argc, char **argv)
{
    int inverted = argc > 1 && !strcmp(argv[1], "inverted");
    g_licensed = 0; assert(can_export_pro() == inverted);
    g_licensed = 1; assert(can_export_pro() == !inverted);
    g_licensed = 0;
    assert(is_valid_key(L"  vxk9-qm27-tr4d-8hbw  "));
    assert(!is_valid_key(L"DEMO-DEMO-DEMO-0000"));
    assert(!is_valid_key(L""));
    assert(!is_valid_key(L"       "));

    const wchar_t *source = L"# Hello <world>\r\n\r\n## Details\r\n- One\r\n- Two\r\n> A & B\r\n"
        L"<script>alert(1)</script>\r\nUnicode: caf\u00e9 \u4e16\u754c \U0001f642\r\nRTF: {x} \\path";
    Buffer html = convert_document(source, L"Test <title> & notes", 1);
    contains(&html, "<h1>Hello &lt;world&gt;</h1>");
    contains(&html, "<h2>Details</h2>");
    contains(&html, "<ul>\n<li>One</li>\n<li>Two</li>\n</ul>");
    contains(&html, "<blockquote>A &amp; B</blockquote>");
    contains(&html, "<title>Test &lt;title&gt; &amp; notes</title>");
    contains(&html, "&lt;script&gt;alert(1)&lt;/script&gt;");
    assert(!strstr(html.data, "<script>"));
    contains(&html, "caf\xc3\xa9 \xe4\xb8\x96\xe7\x95\x8c \xf0\x9f\x99\x82");
    Buffer rtf = convert_document(source, L"Test {title}", 2);
    contains(&rtf, "{\\rtf1"); contains(&rtf, "\\b\\fs40");
    contains(&rtf, "\\u233?"); contains(&rtf, "\\u19990?");
    contains(&rtf, "\\u-10179?\\u-8638?");
    contains(&rtf, "\\{x\\} \\\\path");
    contains(&rtf, "\\u8226?\\tab One");
    Buffer txt = {0}; utf8(&txt, source, wcslen(source));
    assert(!txt.failed);
    wchar_t *decoded = decode_file((const unsigned char *)txt.data, txt.len);
    assert(decoded && !wcscmp(decoded, source)); free(decoded);

    const unsigned char utf16[] = {0xff,0xfe,'A',0,'\n',0,0x16,0x4e};
    decoded = decode_file(utf16, sizeof(utf16));
    assert(decoded && !wcscmp(decoded, L"A\r\n\u4e16")); free(decoded);
    decoded = decode_file((const unsigned char *)"\xef\xbb\xbf" "A\rB\nC\r\nD", 11);
    assert(decoded && !wcscmp(decoded, L"A\r\nB\r\nC\r\nD")); free(decoded);
    decoded = decode_file((const unsigned char *)"", 0); assert(decoded && !*decoded); free(decoded);
    assert(!decode_file((const unsigned char *)"a\0b", 3));
    assert(!decode_file((const unsigned char *)"\xff\xff", 2));
    const unsigned char broken16[] = {0xff,0xfe,0,0xd8};
    assert(!decode_file(broken16, sizeof(broken16)));
    unsigned char *big = (unsigned char *)malloc(MAX_TEXT);
    assert(big); memset(big, 'a', MAX_TEXT);
    assert(!decode_file(big, MAX_TEXT)); free(big);

    /* Test actual filesystem writes in a disposable directory provided by runner. */
    if (argc > 2) {
        wchar_t dir[MAX_PATH], path[MAX_PATH];
        assert(MultiByteToWideChar(CP_UTF8, 0, argv[2], -1, dir, MAX_PATH));
        swprintf(path, MAX_PATH, L"%ls\\test.html", dir); assert(write_document(path, &html));
        swprintf(path, MAX_PATH, L"%ls\\test.rtf", dir); assert(write_document(path, &rtf));
        swprintf(path, MAX_PATH, L"%ls\\test.txt", dir); assert(write_document(path, &txt));
        /* Replace an existing file and verify exact UTF-8 bytes. */
        assert(write_document(path, &txt));
        FILE *f = _wfopen(path, L"rb"); assert(f);
        char bytes[1024]; size_t n = fread(bytes, 1, sizeof(bytes), f); fclose(f);
        assert(n == txt.len && !memcmp(bytes, txt.data, n));
        swprintf(path, MAX_PATH, L"%ls\\missing-folder\\test.txt", dir);
        assert(!write_document(path, &txt));
    }
    /* Exercise maximum editor input, escaping and allocation growth. */
    wchar_t *large = (wchar_t *)malloc(MAX_TEXT * sizeof(wchar_t)); assert(large);
    for (size_t i = 0; i < MAX_TEXT-1; i++) large[i] = '<';
    large[MAX_TEXT-1] = 0;
    Buffer stress = convert_document(large, L"Large", 1);
    assert(!stress.failed && stress.len > (MAX_TEXT-1)*4);
    free(large); free(stress.data); free(txt.data); free(html.data); free(rtf.data);
    printf("PASS: %s gate, converters, Unicode, escaping, input limits, file I/O\n",
        inverted ? "one-byte inverted" : "original");
    return 0;
}
