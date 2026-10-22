# Third-party notices

## InterceptSuite/ProxyBridge

Source: https://github.com/InterceptSuite/ProxyBridge
Pinned commit: 8cb9dceeed582fcd7c2baed8d20eec34934732fc
License: MIT; complete notice in vendor/ProxyBridge/LICENSE.

The Windows native engine is vendored and adapted. Changes: exact Unicode full-path application rules; installation-owner process isolation; literal path delimiters; atomic rule replacement; missing-config blocking; immutable per-revision local gateways; isolated relay ports 34910/34911; exclusive listener binds; startup readiness and cleanup; pre-existing TCP connection preservation; local traffic bypass for UDP block rules; removal of runtime firewall modification; portable C compiler compatibility. The original generic name/wildcard GUI and its tests are excluded; the application has its own regression suite.

## WinDivert

Source: https://github.com/basil00/WinDivert/tree/v2.2.2
Runtime SDK: WinDivert-2.2.2-A, official release asset 78510020.
License: LGPLv3 or GPLv2 at the recipient's choice; complete terms in vendor/WinDivert/LICENSE.

Only the public header and license are checked into source control. The optional runtime remains a separately replaceable DLL and official signed driver. The native delay loader searches only its own installed directory and Windows system dependencies. It does not substitute a simulated driver when a runtime is missing. No WinDivert runtime binaries are included in the current incomplete developer preview if blocked by local security software.

## GPT Proxy Bridge

Local project: ../GPTProxyBridge
License: MIT (project LICENSE).

The upstream TCP handshake/storage implementation and initial WPF visual resources are reused and adapted. No user configuration, credentials, binaries, or Git history is copied from the old project. The current logo is newly generated for App Proxy Bridge and represents multiple connections converging at a shared hub and dispersing again.

## Build tools

Zig 0.14.1 is an optional local build dependency, not part of the distributed application. SHA256 of its official Windows x64 ZIP:
554f5378228923ffd558eac35e21af020c73789d87afeabf4bfd16f2e6feed2c

.NET runtime files in self-contained distributions remain subject to their bundled Microsoft notices.
