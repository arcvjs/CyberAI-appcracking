# Afterlight / Vapor: DRM architecture and exhibition analysis

## What matches Steam's public model

This implementation matches the *separation of responsibilities* visible in Steamworks documentation. Vapor is an independently written simulation. Valve's executable wrapper, client implementation, wire protocols, ticket formats and internal policies are proprietary; this project does not claim binary compatibility or exact implementation parity.

| Responsibility | Public Steam model | Implemented exhibition equivalent |
|---|---|---|
| Launch context | The game can request relaunch through the client before API initialization | `VaporAPI.RestartAppIfNecessary` opens the installed Vapor library on direct launch; a single client handles repeated requests. The guest then selects Play |
| Game integration | Game links an SDK and initializes a client connection | `Vaporworks.dll` obtains a session through Windows named pipes |
| Ownership | An application has an App ID; installation and ownership are separate | App 2937 maps to Afterlight; the backend account license determines permission independently of installed files |
| Issuing authority | Platform services establish identity and ownership | A separate reference backend owns the account/license database and ECDSA private signing key |
| Offline access | Prepared offline access depends on cached account/game state | DPAPI stores a signed ownership proof on this PC; the launcher can serve it within a seven-day demo lease |
| Client gate | The wrapper establishes client context and ownership | The game's bootstrap and periodic SDK check enforce a verified ticket before and during play |

The launch ordering is based on the [Steamworks API overview](https://partner.steamgames.com/doc/sdk/api). The distinction between installation and subscription follows [ISteamApps](https://partner.steamgames.com/doc/API/iSteamApps). The wrapper's limited protection is described in [Steam DRM](https://partner.steamgames.com/doc/features/drm).

## Authentication tickets are not interchangeable

Steam's session authentication tickets, web API authentication tickets, and encrypted application tickets serve different consumers and validation paths. In particular, encrypted application ticket keys belong on a trusted backend. Our game therefore checks a **publisher signature using only a public key**. It does not carry an application decryption secret. The reference backend retains an optional encrypted envelope for demonstration and protocol tests; that envelope is not the game's ownership authority. See [User Authentication and Ownership](https://partner.steamgames.com/doc/features/auth) and [ISteamUser](https://partner.steamgames.com/doc/api/ISteamUser).

## Concrete launch sequence

```mermaid
sequenceDiagram
    actor Guest
    participant Client as Vapor client
    participant Issuer as Local reference backend
    participant SDK as Vaporworks SDK
    participant Game as Afterlight
    Guest->>Client: Play Afterlight, App 2937
    Client->>Issuer: Query the demo account's ownership
    alt Trial expired / unowned
        Issuer-->>Client: Authoritative denial
        Client-->>Guest: Your trial has ended
    else License permits play
        Client->>Game: Start child with a one-use launch context
        Game->>SDK: Init
        SDK->>Client: App ID, device, PID, random nonce, launch secret
        Client->>Client: Verify Windows peer PID and registered launch
        Client->>Issuer: Request signed ownership proof
        Issuer-->>Client: ECDSA-signed account / app / device / dates
        Client-->>SDK: Nonce-bound reply with an HMAC
        SDK->>SDK: Verify server PID, MAC, nonce, account and signature
        SDK-->>Game: Verified permission and expiry
        loop Every 20 seconds
            Game->>SDK: Renew session; also enforce local deadline
        end
    end
```

Only the backend can issue a publisher signature. The launcher registers a child launch before the child can authenticate, uses a random 256-bit bootstrap secret, and checks the OS-reported client process ID. The SDK checks the OS-reported server process ID, fresh nonce and HMAC. These measures prevent accidental process mixing and simple reply replay. The game independently verifies signature, account, app, device, supported license type and validity interval. Merely changing the library UI or a JSON ownership label does not create a valid ticket.

IPC is restricted to the current Windows user, capped at 64 KiB, and timed out. Authoritative backend rejections never fall back to a cached ticket. Network failure may use the previously signed offline proof; the game still verifies all of its claims. The game rechecks its local deadline on every frame, including the pause menu. Time within a session advances from the time of acceptance using a monotonic stopwatch. GPU/audio resources are released before switching to a license failure screen.

## Exhibition threat model

The attacker in this exhibit controls a copy of their own local game files. They can inspect .NET assemblies, replace a DLL, patch a branch, record IPC, edit caches, or change the system clock. They do not begin with access to the conceptual platform signing authority. On first launch, the local service generates its own keys and account state so the example runs without internet. That is a teaching convenience, not a remote trust boundary.

| Experiment on this authored game | Expected result |
|---|---|
| Run a copied game without the launcher | Launcher-required gate |
| Run an expired trial from the library | Trial-ended dialog; game is not started |
| Change signed license text or its expiry | Signature failure |
| Sign with another key | Signature failure |
| Replay a response into another child or nonce | Session verification failure |
| Use a valid ticket for another account, app or device | Claim rejection |
| Disable the reference service with a cached owned ticket | Offline access until the cached lease expires |
| Disable the service with no valid cached proof | Access denied |
| Let a trial end while paused | License gate, rather than an indefinitely paused trial |
| Patch the executable's decision logic or replace the SDK | A sufficiently modified local game can bypass client enforcement |

The final row is the core exhibition lesson. Cryptography prevents forging an accepted signed claim; it cannot compel an attacker-controlled executable to execute its verification code. The separately distributed `Vaporworks.dll`, `VaporAPI.InitAsync`, `VaporSession.TicketStillValid`, and the bootstrap in `Afterlight/Program.cs` provide inspectable boundaries for discussion or authorized modification of this game. The walkthrough demonstrates manual DLL replacement.

## Deliberate limits

- This uses ordinary .NET assemblies. It has no obfuscation, kernel driver, anti-debugger, code-integrity service, hardware attestation or actual Steam wrapper.
- A process owner can inspect bootstrap secrets and modify both client and game. Named-pipe authentication is a process-context check, not protection from the machine owner.
- A fully offline computer provides no independently trusted calendar time. In-session rollback resistance and signed expiry limit casual manipulation; cross-restart clock/state rollback remains a limitation.
- Offline revocation is bounded by the cached lease. The seven-day lease and 20-second renewal are **our demo policies**, not claims about Steam's universal policy.
- Trial dates and licensing are publisher-defined in this demo. Its purchase API performs a simulated grant and does not process payments.
- The local mock account/password mechanism and locally generated issuer files are for the exhibition. A production platform would put its issuer, secrets, commerce, account controls and recovery processes on separately administered infrastructure.
- Diagnostic entry points that skip licensing are compiled only with `EnableDiagnostics=true`. Published builds set it to false. An arbitrary `--verify` or `--showcase` argument grants no release entitlement.

## Verification

The automated gameplay suite exercises collisions, cover occlusion, shooting, reload, projectile damage, navigation, all three waves and a complete combat run. The protocol suite checks valid and forged signatures, altered payloads, app/device mismatch, invalid dates, expired trials, encryption tampering, unsupported license flags, and the live-expiry regression. The native rendering workflow captures menu, gameplay, firing, reload, pause, settings and both result states. Integration checks and the final visual inspection are recorded alongside build artifacts.

