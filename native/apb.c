#include "pb_internal.h"
#include <sddl.h>
static PSID owner_sid=NULL;
void apb_note_unresolved(void)
{
    static volatile LONG64 last=0;
    LONG64 now=(LONG64)GetTickCount64(),previous=InterlockedCompareExchange64(&last,0,0);
    if(now-previous>10000 && InterlockedCompareExchange64(&last,now,previous)==previous)
        log_message("Process ownership or executable path unavailable; retaining the system route for unidentified traffic.");
}
__declspec(dllexport) BOOL APB_SetOwnerSid(const char *sid)
{
    if(running||!sid)return FALSE;
    wchar_t wide[256];PSID candidate=NULL;
    if(!MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,sid,-1,wide,256)||!ConvertStringSidToSidW(wide,&candidate))return FALSE;
    if(owner_sid)LocalFree(owner_sid);owner_sid=candidate;return TRUE;
}
BOOL apb_is_owner(DWORD pid)
{
    if(!owner_sid)return FALSE;
    HANDLE process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,pid),token=NULL;
    if(!process){apb_note_unresolved();return FALSE;}
    BOOL result=FALSE;
    if(OpenProcessToken(process,TOKEN_QUERY,&token)){
        BYTE buffer[512];DWORD needed=0;
        if(GetTokenInformation(token,TokenUser,buffer,sizeof(buffer),&needed))result=EqualSid(owner_sid,((TOKEN_USER*)buffer)->User.Sid);
        CloseHandle(token);
    }
    CloseHandle(process);return result;
}

static void free_rules(PROCESS_RULE *rule)
{
    while(rule){PROCESS_RULE *next=rule->next;free(rule->target_hosts);free(rule->target_ports);free(rule->target_domains);free(rule);rule=next;}
}
// Paths are individual UTF-8 strings: commas and semicolons in a path remain literal.
__declspec(dllexport) BOOL APB_ReplaceRules(const char **paths,UINT32 count,UINT32 config,BOOL block_udp)
{
    if(count>256 || (count && (!paths||!find_proxy_config(config))))return FALSE;
    PROCESS_RULE *head=NULL,**tail=&head;
    for(UINT32 i=0;i<count;i++){
        if(!paths[i]||strlen(paths[i])>=MAX_RULE_PROCESS_LIST||!strchr(paths[i],'\\')||strpbrk(paths[i],"*?\r\n")){free_rules(head);return FALSE;}
        for(int kind=0;kind<(block_udp?2:1);kind++){
            PROCESS_RULE *r=calloc(1,sizeof(*r));if(!r){free_rules(head);return FALSE;}
            strcpy_s(r->process_name,sizeof(r->process_name),paths[i]);
            r->target_hosts=_strdup("*");r->target_ports=_strdup("*");r->target_domains=_strdup("*");
            if(!r->target_hosts||!r->target_ports||!r->target_domains){free_rules(r);free_rules(head);return FALSE;}
            r->rule_id=g_next_rule_id++;r->enabled=TRUE;r->proxy_config_id=config;
            r->protocol=block_udp?(kind?RULE_PROTOCOL_TCP:RULE_PROTOCOL_UDP):RULE_PROTOCOL_BOTH;
            r->action=block_udp&&!kind?RULE_ACTION_BLOCK:RULE_ACTION_PROXY;
            *tail=r;tail=&r->next;
        }
    }
    AcquireSRWLockExclusive(&g_rules_lock);
    PROCESS_RULE *old=rules_list;rules_list=head;g_has_active_rules=count>0;g_has_domain_rules=FALSE;
    ReleaseSRWLockExclusive(&g_rules_lock);
    free_rules(old);
    // Clear only direct-decision caches. Existing proxy flow records retain their config IDs.
    memset((void*)port_decided_bitmap,0,sizeof(port_decided_bitmap));
    memset((void*)port_direct_bitmap,0,sizeof(port_direct_bitmap));udp_direct_reset();
    return TRUE;
}
__declspec(dllexport) int APB_TestMatch(const char *path,const char *destination,BOOL udp,BOOL ipv6)
{
    UINT32 id=0;
    if(ipv6){UINT8 a[16];if(inet_pton(AF_INET6,destination,a)!=1)return -1;return match_rule_v6(path,a,443,udp,&id);}
    struct in_addr a;if(inet_pton(AF_INET,destination,&a)!=1)return -1;return match_rule(path,a.s_addr,443,udp,&id);
}
__declspec(dllexport) void APB_Reset(void)
{
    if(running)return;
    APB_ReplaceRules(NULL,0,0,FALSE);
    for(int i=0;i<g_proxy_config_count;i++){
        PROXY_CONFIG *c=&g_proxy_configs[i];
        if(c->udp_tcp_ctrl!=INVALID_SOCKET)closesocket(c->udp_tcp_ctrl);
        if(c->udp_send_sock!=INVALID_SOCKET)closesocket(c->udp_send_sock);
        SecureZeroMemory(c,sizeof(*c));
    }
    g_proxy_config_count=0;g_next_config_id=1;
}
