// Delay-load official WinDivert from this module's directory only. Missing/blocked driver is an error.
#include <winsock2.h>
#include <windows.h>
#define WINDIVERTEXPORT extern
#include "windivert.h"
static HMODULE module;
static INIT_ONCE once=INIT_ONCE_STATIC_INIT;
static BOOL CALLBACK load(PINIT_ONCE init,PVOID param,PVOID *context)
{
    HMODULE self=NULL;wchar_t path[32768];
    if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,(LPCWSTR)&load,&self))return FALSE;
    DWORD n=GetModuleFileNameW(self,path,32768);if(!n||n>=32768)return FALSE;
    wchar_t *sep=wcsrchr(path,L'\\');if(!sep)return FALSE;
    wcscpy_s(sep+1,32768-(sep+1-path),L"WinDivert.dll");
    module=LoadLibraryExW(path,NULL,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);
    return module!=NULL;
}
static FARPROC symbol(const char *name)
{
    if(!InitOnceExecuteOnce(&once,load,NULL,NULL)){SetLastError(ERROR_MOD_NOT_FOUND);return NULL;}
    return GetProcAddress(module,name);
}
#define CALL(name,args,params,fail) typedef __typeof__(&name) FN; FN fn=(FN)symbol(#name); if(!fn)return fail; return fn params;
HANDLE WinDivertOpen(const char *f,WINDIVERT_LAYER l,INT16 p,UINT64 flags){CALL(WinDivertOpen,0,(f,l,p,flags),INVALID_HANDLE_VALUE)}
BOOL WinDivertRecv(HANDLE h,VOID *p,UINT n,UINT *r,WINDIVERT_ADDRESS *a){CALL(WinDivertRecv,0,(h,p,n,r,a),FALSE)}
BOOL WinDivertSend(HANDLE h,const VOID *p,UINT n,UINT *r,const WINDIVERT_ADDRESS *a){CALL(WinDivertSend,0,(h,p,n,r,a),FALSE)}
BOOL WinDivertShutdown(HANDLE h,WINDIVERT_SHUTDOWN s){CALL(WinDivertShutdown,0,(h,s),FALSE)}
BOOL WinDivertClose(HANDLE h){CALL(WinDivertClose,0,(h),FALSE)}
BOOL WinDivertSetParam(HANDLE h,WINDIVERT_PARAM p,UINT64 v){CALL(WinDivertSetParam,0,(h,p,v),FALSE)}
BOOL WinDivertHelperParsePacket(const VOID *p,UINT n,PWINDIVERT_IPHDR *ip,PWINDIVERT_IPV6HDR *ip6,UINT8 *proto,PWINDIVERT_ICMPHDR *icmp,PWINDIVERT_ICMPV6HDR *icmp6,PWINDIVERT_TCPHDR *tcp,PWINDIVERT_UDPHDR *udp,PVOID *data,UINT *len,PVOID *next,UINT *nextlen){CALL(WinDivertHelperParsePacket,0,(p,n,ip,ip6,proto,icmp,icmp6,tcp,udp,data,len,next,nextlen),FALSE)}
BOOL WinDivertHelperCalcChecksums(VOID *p,UINT n,WINDIVERT_ADDRESS *a,UINT64 f){CALL(WinDivertHelperCalcChecksums,0,(p,n,a,f),FALSE)}
