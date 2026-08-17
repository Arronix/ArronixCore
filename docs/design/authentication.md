# Arronix — Authentication, Authorization and Identity

> **Status:** Design. Closes the two open questions `docs/design/pwa-and-push.md` §7 logged as blocking
> (#3 *multi-user*, #4 *authentication mode*), and resolves the `crossorigin="use-credentials"` decision
> in that document's §3.1 concretely.
>
> **Scope:** a new `Arronix.Host/Identity/` subsystem (media-agnostic domain, no ASP.NET types), a new
> `Arronix.Api/Authentication/` subsystem (schemes, handlers, endpoints), one new contract group in
> `Arronix.Abstractions.Wire` under the existing `ARX0017` diagnostic, and the client-side pieces in
> `Arronix.Client/Auth/`. Purely additive. Nothing under `src/NzbDrone.*`, `src/Sonarr.*` or `frontend/`
> is touched.
>
> **Empirical basis:** the authentication stacks of all five surveyed apps, which are
> character-for-character the same code — `src/Sonarr.Http/Authentication/` and
> `src/NzbDrone.Core/Authentication/` in-repo, and
> `_reference/{Radarr,Prowlarr,Lidarr,Readarr}/src/*.Http/Authentication/`. Every claim below cites the
> line that supports it.
>
> **Governing documents:** `ARCHITECTURE.md` §7/§11, `docs/design/unified-host-runtime.md` §1/§3.3/§6.6/§7.3,
> `docs/design/pwa-and-push.md` §1.5/§3.1/§4.7/§4.12, `docs/arronix-common-extraction-plan.md`
> (three-tier promotion rule, resolution #8), `docs/contracts/stability.md`.
>
> **Hard dependency, stated up front:** credentials, sessions and audit need a real relational store.
> `unified-host-runtime.md` §8 defers persistence to the Storage-Layer milestone. WP-A1…A3 below are
> store-agnostic and can start now; **WP-A4 onward cannot ship before `docs/design/storage-layer.md` lands.**

---

## Resolution table

Every contested call, with the reason on one line. Format matches
`docs/arronix-common-extraction-plan.md` §"How the three proposals were resolved".

| # | Question | Positions | Resolution | One-line reason |
|---|----------|-----------|------------|-----------------|
| 1 | Single-user, single-user-with-devices, or multi-user? | *arr parity (one shared password) · devices only · real accounts | **Genuinely multi-user, with exactly two roles** | The three things that cannot be retrofitted without a migration — an owner column, a principal in the authorization layer, an audit actor — are free today and only today; everything past two roles is additive. |
| 2 | Is the role split a confidentiality boundary? | yes · no | **No — it is an authorization boundary over *actions*** | A Member can see the whole library, every root-folder path and the whole queue; claiming privacy we do not enforce is worse than claiming none. |
| 3 | Per-media-kind access scoping ("the kids get movies, not TV") | ship it · defer it · reserve the column | **Do not ship it and do not reserve the column** | It is one route check on `/api/v1/kinds/{kind}/**` and *twelve* on the cross-kind surfaces (queue, history, calendar, health, notifications); a half-enforced scope teaches users a guarantee that does not exist. |
| 4 | Per-root-folder access | ship it · reject | **Reject, permanently** | File operations run in background jobs that have no user context by design (§8); enforcing it means threading identity into the job queue, which is the exact thing that makes the plugin model unenforceable. |
| 5 | Is there a "no authentication" mode? | *arr parity (`None`, and it is the fresh-install default) · mandatory auth | **Mandatory. No `None`, no `DisabledForLocalAddresses`** | The *arrs' local-address bypass (`UiAuthorizationHandler.cs:27-38`) reads an IP that `UseForwardedHeaders` has already rewritten from an untrusted header — it is a remote bypass wearing a LAN costume. |
| 6 | Session transport | bearer token in `localStorage` · bearer in memory · httpOnly cookie | **Opaque token in an httpOnly cookie** | A WASM app has a wide JS-interop surface, so any script-readable credential is one XSS from permanent theft; cookies move the problem to CSRF, which has a deterministic fix, and they are the only thing a WebSocket handshake carries. |
| 7 | Session representation | self-contained encrypted ticket (`AddCookie` + Data Protection, the *arr choice) · server-side record | **Server-side record, opaque 256-bit token, SHA-256 at rest** | Tickets cannot be revoked, cannot be listed, and drag in a Data Protection key ring whose corruption the *arrs already log an apology for (`AuthenticationController.cs:61-70`); "sign out my lost phone" is unimplementable without a record. |
| 8 | Data Protection at all? | keep it for antiforgery · eliminate it | **Eliminate it entirely** | With opaque sessions and a session-bound CSRF token there is nothing left to protect; that deletes the `asp/` XML directory, a backup hazard and a container-restart failure class in one move. |
| 9 | CSRF mechanism | in-box `IAntiforgery` · double-submit bound to the session | **Double-submit, token stored on the session row** | ~30 lines, no key ring (see #8), and it is revoked with the session for free; the cost — hand-rolling a well-understood primitive — is stated and accepted. |
| 10 | `SameSite` | `Strict` (as `pwa-and-push.md` §4.12 assumes) · `Lax` | **`Lax`** | `Strict` drops the cookie on a top-level GET from a Discord/e-mail notification deep link, so every notification we send lands the user on a login page. **Recommend amending `pwa-and-push.md` §4.12 item 6.** |
| 11 | API keys: one global key or many named keys? | *arr parity (one, in `config.xml`, forever) · named per-integration keys | **Named, owned, hashed, individually revocable; a fresh install has zero** | One key that cannot be scoped, cannot be rotated without breaking every integration at once, and is handed to the browser in `/initialize.json` (`InitializeJsonController.cs:49`) is a single point of total compromise — and the unified host multiplies its blast radius by four. |
| 12 | API keys in a query string (`?apikey=`, the *arr default lookup order) | keep for compatibility · reject | **Reject with `400`, never `401`** | Query strings land in every proxy access log, browser history and `Referer`; a `400` naming the reason is what stops the operator debugging a phantom auth failure. |
| 13 | Deprecate API keys in favor of OAuth? | yes · no | **No — API keys are the ecosystem's lingua franca and stay** | `X-Api-Key` is how Prowlarr talks to every *arr (`prowlarr-indexers.md:455`); what is deprecated is the *global* key and the *query-string* form, not the mechanism. |
| 14 | Reverse-proxy / header auth | *arr parity (`External` → trust everything) · trusted-peer + header · not supported | **Supported, and only armed when a non-empty trusted-proxy list is configured — empty list is a startup failure** | In all five apps `AddExternal` returns `NoAuthenticationHandler` (`AuthenticationBuilderExtensions.cs:26-29`, identical in Radarr/Prowlarr/Lidarr/Readarr), i.e. *every request authenticates as anonymous and no header is ever read*; the permissive default is the bug. |
| 15 | Trusted-proxy identification | IP allow-list only · shared secret only · both | **IP allow-list required, shared secret strongly recommended** | Inside a Docker bridge network every container shares a subnet, so an IP list cannot distinguish the proxy from a compromised sidecar — that is the case homelabs actually hit. |
| 16 | Ship an OIDC client? | yes · no | **No, and not by omission — by decision** | `Microsoft.AspNetCore.Authentication.OpenIdConnect` is a package and `Arronix.Api` has a mechanically-asserted zero-package invariant (`ApiProjectShapeTests`); the correct OIDC story today is proxy mode behind Authelia/Authentik, which homelab users already run and which does it better. |
| 17 | Auto-provision users from `Remote-User`? | yes · no · yes-as-Member | **Default off; when on, provisioned as `Member`, never `Administrator`** | Auto-provisioning an administrator from a request header turns one proxy misconfiguration into a takeover. |
| 18 | `crossorigin="use-credentials"` on the manifest link | add it · make the shell anonymous | **Make the shell anonymous and drop the attribute** | The *arrs must authenticate their static assets (`StaticResourceController.cs:13`) only because their bootstrap file leaks the API key; delete that design and the whole `crossorigin` trap disappears with it. |
| 19 | Password KDF | in-box PBKDF2 · Argon2id via a package | **PBKDF2-HMAC-SHA512, 210 000 iterations, `Rfc2898DeriveBytes.Pbkdf2`** | Argon2 is not in the BCL and every implementation is a third-party package; the *arrs' 10 000 iterations (`UserService.cs:26`, identical in Radarr and Prowlarr) is ~20× below current guidance and that is the gap worth closing. |
| 20 | First-run claim | trust-on-first-use (anyone who reaches a fresh instance) · one-time setup token | **One-time setup token, with a loopback exemption and an env-var escape** | A fresh instance accidentally exposed on first boot is claimed by whoever finds it first; the token costs one `docker logs` and removes the entire race. |
| 21 | Do background jobs run as the triggering user? | yes · no | **No — jobs run as the system principal; `TriggeredByUserId` is recorded for audit only** | Authorization is decided at the point of *request*; an RSS sync at 3 a.m. has no user, and behavior that depends on who happened to click is not behavior, it is a bug generator. |
| 22 | Does a plugin ever see a user identity? | never · pseudonymous id · real id | **Never. `IPluginContext` exposes no principal, no header, no `HttpContext`** | §11 least privilege: a plugin that can correlate users has an ambient capability nobody granted it, and the current `IPluginContext` (`unified-host-runtime.md` §3.3) already exposes none — this makes the absence a rule rather than an accident. |
| 23 | Where do the auth wire DTOs live? | a new `Arronix.Wire` project · `Arronix.Abstractions.Wire` (`ARX0017`) | **`Arronix.Abstractions.Wire`, guarded by a reachability test** | `Arronix.Client` may reference only Abstractions, so they must be there; a seventh project to hide five records from plugins is worse than one test asserting they are unreachable from `IPluginContext`. |
| 24 | Does `GET /health` stay anonymous? | yes (runtime spec §6.6) · split | **Split: anonymous `/health/live` + `/health/ready` (204/503, no body); authenticated `/api/v1/health`** | `HealthSnapshotView` names plugins, indexers and remediation hints — a reconnaissance gift; orchestrators only ever needed the status code. **Recommend amending `unified-host-runtime.md` §6.6.** |

---

## 1. Prior art: what the five *arrs actually do

All five apps ship the same nine files, byte-for-byte apart from the namespace. Read once, applies
everywhere. The `_reference/` copies confirm it:
`Radarr/src/Radarr.Http/Authentication/AuthenticationBuilderExtensions.cs:26`,
`Prowlarr/.../:26`, `Lidarr/.../:26`, `Readarr/.../:31` are the same `AddExternal`.

### 1.1 The model

| Concern | Implementation | Where |
|---|---|---|
| Modes | `None`, `Basic` (obsolete), `Forms`, `External` | `src/NzbDrone.Core/Authentication/AuthenticationType.cs:5-12` |
| Bypass | `Enabled` \| `DisabledForLocalAddresses` | `src/NzbDrone.Core/Authentication/AuthenticationRequiredType.cs:3-7` |
| User record | `Identifier`, `Username`, `Password`, `Salt`, `Iterations` | `src/NzbDrone.Core/Authentication/User.cs:6-13` |
| Login | form POST → cookie ticket → 302 | `src/Sonarr.Http/Authentication/AuthenticationController.cs:35-86` |
| Cookie | `{InstanceName}Auth`, 7-day sliding, Data-Protection-encrypted | `src/Sonarr.Http/Authentication/AuthenticationBuilderExtensions.cs:33-47` |
| API key | one global string in `config.xml` | `src/NzbDrone.Core/Configuration/ConfigFileProvider.cs:185-199` |
| Key lookup order | **query string**, then `X-Api-Key`, then `Authorization: Bearer` | `src/Sonarr.Http/Authentication/ApiKeyAuthenticationHandler.cs:40-51` |
| Hub auth | second API-key scheme reading `?access_token=` | `AuthenticationBuilderExtensions.cs:58-62`, `src/NzbDrone.Host/Startup.cs:323` |
| Authorization | one policy, `RequireAuthenticatedUser()`, no roles | `src/NzbDrone.Host/Startup.cs:220-231` |

### 1.2 The eight things they get wrong

1. **The UI is handed the global API key.** `/initialize.json` is cookie-authenticated
   (`InitializeJsonController.cs:11`) and its body contains `"apiKey": "<the global key>"`
   (`:49`). The browser then uses that key as a bearer for every call. So a session cookie is not a
   credential — it is a **bootstrap to a permanent, unscopeable, unrotatable, all-powerful shared secret**,
   and any XSS in the UI exfiltrates it forever. This one decision causes three others: static assets must
   be authenticated (`StaticResourceController.cs:13`), which causes the PWA `crossorigin` trap, which
   causes the "install button never appears" symptom.
2. **`External` authenticates everyone.** `AddExternal` returns `NoAuthenticationHandler`
   (`AuthenticationBuilderExtensions.cs:26-29`), whose `HandleAuthenticateAsync` unconditionally succeeds
   with an `Anonymous` principal (`NoAuthenticationHandler.cs:21-34`). **No *arr reads `Remote-User`,
   `X-Forwarded-User` or any identity header anywhere** — a repo-wide grep over all five trees returns
   nothing. "External auth" means "we have turned auth off and we hope you put something in front".
3. **The local-address bypass trusts a rewritten IP.** `UiAuthorizationHandler.cs:27-38` calls
   `GetRemoteIP()`, which reads `Connection.RemoteIpAddress`
   (`src/Sonarr.Http/Extensions/RequestExtensions.cs:83-97`) — *after* `app.UseForwardedHeaders()`
   (`Startup.cs:287`) has overwritten it from `X-Forwarded-For`. The forwarded-headers options clear both
   `KnownIPNetworks` and `KnownProxies` (`Startup.cs:62-68`), which disables the known-proxy check
   altogether. Net effect with `DisabledForLocalAddresses`: an attacker sends `X-Forwarded-For: 127.0.0.1`
   and is inside. *(Verify the middleware's `checkKnownIps` short-circuit against the pinned SDK before
   citing this externally; the configuration is unambiguous either way.)*
4. **10 000 PBKDF2 iterations.** `UserService.cs:26` (and `Radarr/.../UserService.cs:26`,
   `Prowlarr/.../UserService.cs:22`). Current guidance for PBKDF2-HMAC-SHA512 is ~210 000.
5. **Two non-constant-time secret comparisons.** `user.Password == hashedPassword`
   (`UserService.cs:152-158`) and `_apiKey == providedApiKey` (`ApiKeyAuthenticationHandler.cs:63`).
6. **No lockout, no rate limit, no audit trail.** `AuthenticationService.Login` (`:29-48`) does an
   unbounded credential check. There is a dedicated `Auth` NLog logger (`:18`) — the one thing this stack
   gets right — but nothing queryable and nothing that stops a thousand attempts a second.
7. **Logout is a `GET`** (`AuthenticationController.cs:88-94`), so any page can sign a user out with an
   `<img>` tag. Cosmetic, but it shows nobody looked.
8. **The schema is multi-user and the app is not.** `UserRepository` supports N rows, but `FindUser()`
   is `_repo.SingleOrDefault()` (`UserService.cs:41-44`) and `Upsert` overwrites whichever row exists
   (`:59-76`). There is no role, no enabled flag, no created-at, no lockout state on `User`. The result is
   a household sharing one password — which is precisely the gap Overseerr/Jellyseerr exist to fill with a
   *separate application*.

Two further notes without a verdict: the API CORS policy is `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()`
(`Startup.cs:74-81`) — harmless alone because credentials are not allowed, less harmless next to a key that
lives in the page; and sessions are unlistable and unrevocable by construction, which is the direct cause of
"I lost my phone, what do I do" having no answer.

---

## 2. The identity model

### 2.1 Recommendation

**Arronix is genuinely multi-user, with exactly two roles (`Administrator`, `Member`), a first-class
`Device` concept, and a household-of-one default that never mentions users in the setup flow.**

### 2.2 Why not single-user

The product is a self-hosted media manager on a homelab box, and the surveys make the household case
concrete: four *arrs collapse into one host sharing one database
(`arronix-unified-host-shared-database`), so the single shared password that used to reach *one* app now
reaches *all* of them. Single-user does not stay the same size under the unified host — it gets four times
worse.

Three things are cheap now and expensive forever after:

1. **An owner column** on sessions, devices, push subscriptions, API keys, audit rows and per-user
   preferences. `pwa-and-push.md` §4.7 already carries `UserId` on `PushSubscription` with the hedge
   *"a pseudo-user when authentication is disabled for local addresses"* — that hedge is a symptom of the
   unanswered question, and it disappears here (see §11's recommended amendments).
2. **A principal in the authorization layer.** Retrofitting "authenticated ⇒ allowed everything" into a
   role check touches every endpoint. Building it in touches none.
3. **An audit actor.** An audit log added later cannot describe the past.

Everything beyond those three — more roles, finer permissions, delegation, per-kind scopes — is purely
additive and is therefore correctly deferred (§11). This is the extraction plan's three-tier promotion rule
applied to a data model rather than to contracts.

### 2.3 Why not "single-user with devices"

Devices are necessary but not sufficient. `pwa-and-push.md` established that push subscriptions are
per-device, and the same is true of sessions ("sign out my phone"). But a device concept alone answers
*where* a credential is used, never *who* used it, and the actual household requirement is a *who*
question: **let my partner add things and see the queue without being able to reconfigure my download
client or delete my library.** No device model expresses that.

### 2.4 Why only two roles

Multi-user projects die in the ACL matrix. The realistic household has high mutual trust and exactly one
person who administers it. Two roles cover the whole observed requirement:

- **`Administrator`** — everything, including settings, plugins, users, file deletion and system diagnostics.
- **`Member`** — browse the library, add and monitor items, search, grab, see the queue and history, manage
  their own devices and their own API keys.

Third roles that were considered and rejected: `Viewer` (read-only — the person who cannot add anything has
no reason to open the app), `Requester` (an approval queue — a real product, and it is Overseerr's, not
ours; see §11).

### 2.5 The honest boundary

**The role split is an authorization boundary over actions, not a confidentiality boundary over data.** A
`Member` sees the entire library, every root-folder path, the whole queue and the whole history. What they
cannot do is change how the system behaves or destroy anything. This must be stated in the UI where roles
are assigned, because a user who believes otherwise will be wrong in a way that matters.

### 2.6 Entities

```csharp
// src/Arronix.Host/Identity/Models/  — host-side domain, Tier B, zero ASP.NET types.
// The whole namespace is testable without a web host, which is why a future CLI or TUI costs nothing.

public sealed record ArronixUser
{
    public required Guid Id { get; init; }                       // Guid.CreateVersion7()
    public required string Username { get; init; }               // lower-invariant, unique
    public string? DisplayName { get; init; }
    public string? Email { get; init; }                          // optional; used only by proxy mapping
    public required UserRole Role { get; init; }
    public required bool Enabled { get; init; }
    public PasswordHash? Password { get; init; }                 // null in Proxy mode
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastLoginAt { get; init; }
    public required int ConsecutiveFailedLogins { get; init; }
    public DateTimeOffset? LockedUntil { get; init; }
}

public enum UserRole { Member = 0, Administrator = 1 }

/// <summary>A browser or app installation. Long-lived, independent of any one session.</summary>
public sealed record AuthDevice
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public required string Label { get; init; }                  // user-editable; defaults to a UA summary
    public required string UserAgent { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset LastSeenAt { get; init; }
    public string? LastSeenIp { get; init; }
}

public sealed record AuthSession
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public required Guid DeviceId { get; init; }
    public required byte[] TokenHash { get; init; }              // SHA-256(raw), unique index
    public required byte[] CsrfTokenHash { get; init; }          // SHA-256(raw)
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset LastActiveAt { get; init; }
    public required DateTimeOffset AbsoluteExpiresAt { get; init; }
    public required DateTimeOffset IdleExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; init; }
    public string? RevokedReason { get; init; }
}

public sealed record AuthApiKey
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public required string Name { get; init; }                   // "Prowlarr", "Tautulli script"
    public required string Prefix { get; init; }                 // first 8 chars, clear, indexed, displayed
    public required byte[] TokenHash { get; init; }              // SHA-256(raw), unique index
    public required UserRole Role { get; init; }                 // never above the owner's
    public required PermissionSet Permissions { get; init; }     // defaults to the role's set; narrowable
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public DateTimeOffset? RevokedAt { get; init; }
}
```

`Guid.CreateVersion7()` throughout, matching the extraction plan's telemetry-token decision (resolution #6)
and `pwa-and-push.md` §4.7.

**Device identity survives logout.** A separate long-lived cookie `arx_device` (httpOnly, `Max-Age`
34 560 000 s = 400 days, the Chrome ceiling) carries the `DeviceId`. Re-logging in on the same browser
reuses the row rather than growing a new one per login, which is what keeps the device list — and therefore
the push-subscription list — meaningful.

---

## 3. Authentication mechanisms

Three, and only three: **session** (humans), **API key** (automation), **proxy header** (homelab SSO).
Selected by `Arronix:Auth:Mode`, except API keys, which are always accepted.

```csharp
public enum AuthMode { Password = 0, Proxy = 1 }
```

There is no `None`. Resolution #5.

### 3.1 Sessions

```text
POST /api/v1/auth/login   { username, password, rememberMe, deviceLabel? }
  → 200 SessionView + Set-Cookie: arx_session=<raw>; HttpOnly; SameSite=Lax; Path=<urlBase>/[; Secure]
                    + Set-Cookie: arx_csrf=<raw>;    SameSite=Lax; Path=<urlBase>/[; Secure]   (readable)
                    + Set-Cookie: arx_device=<id>;   HttpOnly; SameSite=Lax; Max-Age=34560000  [; Secure]
  → 401 AuthProblem { code: "credentials_invalid" | "account_locked_out" | "account_disabled" }
  → 429 + Retry-After
```

- Token: 32 bytes from `RandomNumberGenerator.GetBytes`, base64url, prefixed `arx_s_`. Stored as
  `SHA256(raw)` with a unique index. No slow KDF: the token is uniformly random, not a password, so a fast
  hash is exactly right and a slow one would only add a per-request cost.
- Lookup goes through `ICacheProvider` (Abstractions `ARX0001`, already implemented in `Arronix.Common`)
  keyed by the hash, with a 60-second entry TTL and explicit eviction on revoke. A homelab's request rate
  makes the cache a nicety, not a necessity; the eviction path is what makes revocation immediate.
- Expiry: `IdleExpiresAt` (default 7 days) refreshed at most once per `SlidingRefreshInterval` (default
  5 minutes) so a busy client is not one DB write per request; `AbsoluteExpiresAt` (default 30 days) never
  moves.
- `rememberMe: false` → the cookie is a browser-session cookie; the server session still carries both
  expiries. **The login form defaults "Keep me signed in" to checked**, because an installed PWA that
  demands a password every time iOS evicts it is the complaint that gets filed.
- `Secure` is set **iff the request arrived over HTTPS** (via `Request.IsHttps`, which
  `UseForwardedHeaders` has resolved from a *trusted* proxy — see §3.4). The cookie name does **not**
  change with the scheme: a `__Host-` prefix would be stronger, but it forces a rename when an operator
  adds TLS later, which silently signs out the whole household. Trade-off accepted and recorded; the
  mitigation for the lost `__Host-` guarantee is that Arronix is never a multi-tenant domain.
- **No ASP.NET Data Protection anywhere.** The session handler is a custom
  `AuthenticationHandler<SessionSchemeOptions>` over `Microsoft.AspNetCore.Authentication`, which is in the
  shared framework. Resolution #8.

### 3.2 Password storage

```csharp
// src/Arronix.Host/Identity/PasswordHasher.cs   (Tier B)
public sealed record PasswordHash(string Algorithm, int Iterations, byte[] Salt, byte[] Hash);

public interface IPasswordHasher
{
    PasswordHash Hash(string password);
    PasswordVerification Verify(PasswordHash stored, string password);
}

public enum PasswordVerification { Failed = 0, Success = 1, SuccessRehashNeeded = 2 }
```

- `Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, 32)` — BCL, no package,
  so `Arronix.Host`'s package set is unchanged.
- 16-byte salt, 210 000 iterations (`Arronix:Auth:Password:Pbkdf2Iterations`), algorithm and iteration
  count stored per row so the policy can move; `SuccessRehashNeeded` re-hashes on next successful login.
- `CryptographicOperations.FixedTimeEquals` for the comparison. Fixes finding 1.2/5.
- **Uniform failure**: when the username does not exist, run a dummy hash against a fixed dummy record
  before returning, so a missing account and a wrong password cost the same and say the same.
- No password complexity theater: one rule, `MinimumLength` (default 10). Composition rules push users to
  `Password1!`.

### 3.3 API keys

```text
POST   /api/v1/apikeys   { name, permissions? , expiresAt? }  → 201 { id, name, prefix, token }  ← once
GET    /api/v1/apikeys                                        → ApiKeyView[]  (no token, ever)
DELETE /api/v1/apikeys/{id}                                   → 204
```

Rules, each a direct inversion of an *arr behavior:

1. **A fresh install has zero keys.** The setup flow offers to mint one and names it after its consumer.
2. **`Authorization: Bearer <token>` or `X-Api-Key: <token>`. Never a query string.** A request carrying
   `?apikey=` / `?api_key=` / `?access_token=` on any route other than the hub handshake gets
   `400 AuthProblem { code: "api_key_in_query_string" }` with a one-line explanation. A `401` here would
   send the operator hunting for a wrong key; the whole point is to tell them the truth.
3. Token: 32 random bytes, base64url, prefixed `arx_`. `Prefix` = first 8 characters after the prefix,
   stored in clear and indexed (so lookup is an index seek, not a scan) and shown in the UI so a key is
   identifiable without being recoverable. Full token stored as `SHA256`, compared with `FixedTimeEquals`.
4. A key's `Role` is capped at its owner's and its `Permissions` may only be narrowed from its role's set.
   Disabling or deleting a user revokes their keys.
5. **The web client never receives an API key.** This is the single largest departure from the *arrs and it
   is what unlocks §6.
6. `LastUsedAt` is updated at most once per minute per key. A key unused for 180 days raises a
   `HealthCheck` (`HealthSeverity.Notice`) rather than being deleted — deleting a credential on a timer is
   how you break someone's backup script in the night.

### 3.4 Reverse-proxy / header authentication

This is how most homelab users actually front these apps, and it is the mechanism the *arrs handle worst
(finding 1.2/2). The design principle is one sentence:

> **A header is an identity claim only when it arrives over a path we have already authenticated.**

`Arronix:Auth:Mode = "Proxy"` arms `ProxyHeaderAuthenticationHandler`. Every request must pass **both**
gates before any header is read:

| Gate | Check | Failure |
|---|---|---|
| **Peer** | `HttpContext.Connection.RemoteIpAddress` — read *before* forwarded-header rewriting — is in `Arronix:Auth:Proxy:TrustedProxies` (individual addresses or CIDR) | `403 { code: "proxy_untrusted_peer" }`, audited |
| **Secret** | if `Arronix:Auth:Proxy:SharedSecret` is set, the configured header matches under `FixedTimeEquals` | `403 { code: "proxy_secret_mismatch" }`, audited |
| **Identity** | `UserHeader` (default `Remote-User`) present, non-empty, maps to an enabled user | `403 { code: "proxy_identity_missing" }` / `"proxy_user_unknown"`, audited |

And four supporting rules:

1. **An empty `TrustedProxies` list with `Mode = "Proxy"` is a startup failure**, surfaced through
   `AddValidatedOptions<AuthOptions>` as `CoreErrorCode.InvalidConfiguration`. The app does not start. This
   is the inversion of the *arr default and the most important line in this section: the insecure
   configuration must be unreachable, not merely discouraged.
2. **Identity headers are stripped on ingress** from any request whose peer is untrusted — `Remote-User`,
   `Remote-Name`, `Remote-Email`, `Remote-Groups`, `X-Forwarded-User`, `X-Forwarded-Preferred-Username`
   are removed from `HttpContext.Request.Headers` by the first middleware in the pipeline. Defense in
   depth: nothing downstream, including logging and support bundles, can then observe a spoofed claim.
3. **`ForwardedHeadersOptions` is configured from the same list.** `KnownProxies` / `KnownIPNetworks` are
   populated from `Arronix:Auth:Proxy:TrustedProxies`, never cleared. When the list is empty (i.e.
   `Mode = "Password"` with no proxy configured), `UseForwardedHeaders` is **not added to the pipeline at
   all** — an unconfigured forwarded-headers middleware is worse than none, which is finding 1.2/3.
4. **Direct access is refused.** In `Proxy` mode a request from an untrusted peer is `403` unless it
   carries a valid API key. This is what closes "the app is also on `:8989` and the proxy is optional".

```jsonc
"Proxy": {
  "TrustedProxies":      ["172.18.0.0/16"],        // REQUIRED, non-empty, or startup fails
  "UserHeader":          "Remote-User",
  "DisplayNameHeader":   "Remote-Name",
  "EmailHeader":         "Remote-Email",
  "GroupsHeader":        "Remote-Groups",
  "AdminGroup":          null,                      // null ⇒ roles are managed in Arronix
  "SharedSecretHeader":  "X-Arronix-Proxy-Secret",
  "SharedSecret":        null,                      // strongly recommended; see resolution #15
  "AutoProvision":       false,
  "LogoutUrl":           null                       // e.g. "https://auth.example.com/logout"
}
```

- **`AutoProvision: false` by default.** An unknown `Remote-User` is a clear `403` naming the username, not
  a silent account creation. When enabled, provisioned users are `Member`; a header can never mint an
  administrator (resolution #17).
- **`AdminGroup`** maps `Remote-Groups` to `Administrator`. Off by default: group membership is the
  proxy's opinion, and an operator who has not thought about it should not have their role model quietly
  outsourced.
- Proxy mode still issues an Arronix session on first contact (same cookie, same CSRF, same device row), so
  the rest of the system — including SignalR and push — has exactly one notion of a principal. The proxy
  headers are re-verified on every request; if they change or disappear, the session is revoked.
- **`LogoutUrl`**: in proxy mode, `POST /api/v1/auth/logout` clears the Arronix session and returns the
  configured URL for the client to navigate to. Without it, "log out" is a lie — the proxy signs you
  straight back in.

**Shipped documentation, not left as an exercise:** exact bypass/exemption snippets for Caddy, Traefik +
Authelia, nginx + Authentik, and oauth2-proxy, covering the anonymous asset set from §6. The single most
common support question this design will generate is "the install button vanished after I put Authelia in
front", and the answer is a config snippet.

### 3.5 OIDC — recommended against, explicitly

Do not ship an OIDC client in v1. Three reasons, in order of weight:

1. `Microsoft.AspNetCore.Authentication.OpenIdConnect` is a NuGet package, not part of the
   `Microsoft.AspNetCore.App` shared framework. `Arronix.Api` has **zero `PackageReference`s** as a
   mechanically-asserted invariant (`unified-host-runtime.md` §7.3, `ApiProjectShapeTests`). Taking the
   package to add a feature the deployment already has is a bad trade.
2. Every homelab that wants OIDC already runs Authelia, Authentik, Keycloak or oauth2-proxy in front. Proxy
   mode (§3.4) consumes that correctly and gets MFA, WebAuthn, session management and a consent screen for
   free. An in-app OIDC client would reimplement discovery, JWKS rotation, PKCE, nonce validation and
   refresh — a genuine security surface — to reach the same place.
3. Nobody has asked for OIDC *without* a proxy, and that is the only configuration an in-app client would
   serve.

**Trigger to un-defer, and the shape it takes:** a concrete deployment that must federate without a
reverse proxy. The implementation is then a *separate* optional assembly (`Arronix.Api.Oidc`) referenced by
the host but not by `Arronix.Api`, so the invariant survives. Recorded now so the decision does not have to
be re-litigated from scratch.

### 3.6 First run, and getting back in

**Setup mode** is entered when the user table is empty:

- `GET /api/v1/auth/session` → `200 { authenticated: false, challenge: "setup" }`.
- Only the anonymous asset set (§6.2) and `POST /api/v1/auth/setup` are served; everything else is `403`.
- `POST /api/v1/auth/setup` requires a **one-time setup token**, 16 random bytes base64url, which is:
  written to the log at `Information` on first start, written to `<AppData>/setup-token` with `0600`, and
  overridable by `Arronix:Auth:Setup:Token` (env `ARRONIX__AUTH__SETUP__TOKEN`) for declarative
  deployments.
- **Loopback exemption**: a request whose peer is a loopback address may complete setup without the token.
  Someone with a shell on the box has already won.
- The token is destroyed the moment the first administrator exists.

Trade-off, stated: a Docker user must run `docker logs` once. The alternative — trust-on-first-use — means
a fresh instance briefly exposed to the internet is claimed by a stranger, and the *arrs are worse still
(they default to no authentication at all, `ConfigFileProvider.cs:201-215`).

**Recovery**, which no *arr has (their answer is "hand-edit `config.xml`"):

```text
arronix --reset-admin-password        # prompts, requires local console, audited as PasswordReset
arronix --create-admin <username>     # only when no administrator exists
```

### 3.7 Configuration

`AuthOptions` lands in `Arronix.Host`, exactly where extraction-plan resolution #8 put it ("Common hosts no
HTTP server and no authentication").

```csharp
// src/Arronix.Host/Identity/AuthOptions.cs
public sealed class AuthOptions
{
    public const string SectionName = "Arronix:Auth";

    public AuthMode Mode { get; init; } = AuthMode.Password;
    public SessionOptions Session { get; init; } = new();
    public PasswordPolicyOptions Password { get; init; } = new();
    public ProxyAuthOptions Proxy { get; init; } = new();
    public SetupOptions Setup { get; init; } = new();
    public AuthRateLimitOptions RateLimit { get; init; } = new();
    public AuditOptions Audit { get; init; } = new();
}
```

```jsonc
{
  "Arronix": {
    "Auth": {
      "Mode": "Password",
      "Session":  { "IdleTimeout": "7.00:00:00", "AbsoluteTimeout": "30.00:00:00",
                    "SlidingRefreshInterval": "00:05:00", "MaxSessionsPerUser": 50 },
      "Password": { "MinimumLength": 10, "Pbkdf2Iterations": 210000,
                    "LockoutThreshold": 10, "LockoutBaseWindow": "00:00:30",
                    "LockoutMaxWindow": "00:15:00" },
      "Proxy":    { "TrustedProxies": [], "UserHeader": "Remote-User", "AutoProvision": false },
      "Setup":    { "Token": null },
      "RateLimit":{ "LoginAttemptsPerWindow": 10, "LoginWindow": "00:05:00",
                    "GlobalLoginAttemptsPerWindow": 60 },
      "Audit":    { "RetentionDays": 90 }
    }
  }
}
```

Validated through `AddValidatedOptions<AuthOptions>` under the `Arronix:` prefix, matching Common and
`unified-host-runtime.md` §4.3. Cross-field validation: `Mode == Proxy ⇒ Proxy.TrustedProxies.Count > 0`.

---

## 4. The WASM client

`Arronix.Client` references `Arronix.Abstractions` only. Everything below compiles against that.

### 4.1 Token storage — the decision

| Option | XSS exposure | Survives reload | Sent on WebSocket handshake | Verdict |
|---|---|---|---|---|
| Bearer in `localStorage` | **Total and permanent** — one `eval` exfiltrates a credential valid for weeks | yes | no (custom headers are impossible on browser WebSockets) | **Rejected** |
| Bearer in memory only | XSS can use it but not persist it | **no** — every reload is a re-login, which destroys the PWA cold-start story `pwa-and-push.md` §1.5.4 exists to protect | no | **Rejected** |
| Opaque token in an httpOnly cookie | Script cannot read it; XSS can still *act* as the user while the page lives, but cannot steal a portable credential | yes | **yes** | **Chosen** |

A Blazor WASM app has a wide JS-interop surface (service worker registration, push subscription,
`visibilitychange`, install prompts — all of `pwa-and-push.md` §4), so "we control all our JavaScript" is
not a claim this app gets to make. The cookie moves the risk from *credential theft* to *CSRF*, and CSRF
has a deterministic fix. **The client stores no credential of any kind.**

### 4.2 CSRF

Double-submit, bound to the session (resolution #9):

- At login the server mints a second 32-byte random token, stores `SHA256` on the session row, and returns
  it in a **readable** `arx_csrf` cookie.
- The client's single `DelegatingHandler` copies it into `X-Arronix-Csrf` on every unsafe request.
- The server requires the header on every `POST`/`PUT`/`PATCH`/`DELETE` and on the SignalR negotiate,
  compares with `FixedTimeEquals`, and additionally rejects when `Origin` is present and not same-origin,
  or `Sec-Fetch-Site` is `cross-site`. Two independent layers, both free.
- Rotated on privilege change (password change, role change), revoked with the session.
- **API-key requests are exempt** — they carry no ambient credential, so there is nothing to forge.

`SameSite=Lax` (resolution #10) already blocks cross-site `POST` cookie attachment in current browsers;
the double-submit covers same-site attackers and the `Lax` carve-outs. Note the direct consequence for
`pwa-and-push.md` §4.12 item 6, which assumes `SameSite=Strict`: the push-subscribe endpoint remains
CSRF-protected by exactly this mechanism, and the protection it provides — blocking *registration
injection*, an attacker adding their device to your account — is unchanged.

### 4.3 SignalR

The hub is `/hub/events` (`unified-host-runtime.md` §6.6).

- **Cookie clients need nothing.** The session cookie is sent on the WebSocket handshake because it is
  same-origin. Sonarr's second API-key scheme reading `?access_token=`
  (`AuthenticationBuilderExtensions.cs:58-62`) exists only because its client holds a bearer token, and it
  is the query-string credential leak we banned in §3.3. **Deleted, not ported.**
- The negotiate `POST` carries the antiforgery header like any other unsafe request.
- The hub checks `Origin` explicitly on connect. Cross-site WebSocket handshakes are subresource requests,
  so `SameSite=Lax` withholds the cookie already; the check is the belt.
- **API-key clients** that want live events use a short-lived ticket:

```text
POST /api/v1/auth/hub-ticket        (API key auth)  → { ticket, expiresAt }   // 30 s, single use
GET  /hub/events?access_token=<ticket>
```

  The ticket is bound to the calling principal, consumed on first use, and never valid for anything else.
  It does land in logs — that is why it lives for 30 seconds and once.
- `HubConnection` reconnect uses the explicit retry policy from `pwa-and-push.md` §1.5.3. A `401` on
  reconnect transitions the client to `Unauthenticated`; a transport error transitions it to
  `Unreachable`. Same discriminator as §4.5.

### 4.4 Renewal

There is none, and that is the point of choosing cookies over bearer tokens: no refresh-token rotation, no
silent-renew iframe, no token-expiry timer in the client, no race between a 401 and an in-flight refresh.
The cookie keeps working until idle or absolute expiry; the server slides `IdleExpiresAt` at most once per
five minutes. The client's only responsibility is to notice a `401` and route to login.

### 4.5 `Unauthenticated` vs `Unreachable` — the failure this section exists to prevent

To a naive client these are the same event: no data appeared. `pwa-and-push.md` §1.5.1 already separates
them in the state machine; what it could not specify, without this document, is *how the client tells them
apart*. Six rules:

1. **`GET /app/ping` is anonymous and returns `204`, always.** It answers exactly one question — is the
   host reachable — and it must never return `401`, or the state machine loses its ground truth.
2. **`GET /api/v1/auth/session` is anonymous and always returns `200`.** Never `401`. Its body *describes*
   the auth state rather than enforcing it. This single endpoint makes the boot decision deterministic:

   | Observation | State |
   |---|---|
   | transport error / timeout | `Unreachable` (or `Offline` if `navigator.onLine === false`) |
   | `502`/`503`/`504` | `ServerRestarting` |
   | `200`, `authenticated: false`, `challenge: "setup"` | route to first-run setup |
   | `200`, `authenticated: false` | `Unauthenticated` → login |
   | `200`, `authenticated: true` | `Online` |

3. **Every `401` and `403` Arronix itself emits carries `X-Arronix-Auth: 1`.** A `401` *without* that
   header did not come from Arronix — it came from a reverse proxy or an SSO portal whose own session
   expired. The client renders a different surface for it: *"Your sign-in provider is asking you to
   authenticate again. Open Arronix in a browser tab to continue."* Rendering the in-app login form there
   is the classic homelab dead end — the user types the right password into the right form and nothing
   happens, forever — and no *arr distinguishes the two.
4. **Never redirect an API request to a login page.** The session handler's `HandleChallengeAsync` returns
   `401` with a JSON `AuthProblem` for anything under `/api/**` or `/hub/**`, unconditionally. Sonarr sets
   `LoginPath` (`AuthenticationBuilderExtensions.cs:43`) and redirects (`AuthenticationController.cs:42`,
   `:77-85`), so an expired session on an XHR yields `200 text/html` and a JSON parse error — the symptom
   that sends people to the forums.
5. **A `401` never overrides `Unreachable`.** The transport must have succeeded for an auth transition to
   fire; the `DelegatingHandler` distinguishes "response received" from "no response".
6. **The service worker never touches auth paths.** `pwa-and-push.md` §2.2 already excludes them
   (*"a cached auth response is a security bug"*); the passthrough list in that document's §2.4 must gain
   `/^api\/v1\/auth\//` alongside the existing `/^login$/`, `/^logout$/` — see §11.

### 4.6 Wire contracts

`Arronix.Abstractions.Wire`, diagnostic `ARX0017` (resolution #23). No new contract area is opened.

```csharp
// src/Arronix.Abstractions/Wire/Auth/SessionView.cs
public sealed record SessionView
{
    public required bool Authenticated { get; init; }
    public required AuthChallenge Challenge { get; init; }
    public required AuthMode Mode { get; init; }
    public required bool SecureContext { get; init; }      // drives the "no TLS" notice, once
    public required string InstanceName { get; init; }
    public AuthUserView? User { get; init; }
    public AuthDeviceView? Device { get; init; }
    public string? ProxyLogoutUrl { get; init; }
}

public enum AuthChallenge { None = 0, Setup = 1, Password = 2, Proxy = 3 }

public sealed record AuthUserView
{
    public required Guid Id { get; init; }
    public required string Username { get; init; }
    public string? DisplayName { get; init; }
    public required UserRole Role { get; init; }
    public required IReadOnlyList<Permission> Permissions { get; init; }
}

public sealed record AuthProblem
{
    public required AuthFailureCode Code { get; init; }
    public required string Message { get; init; }          // safe to display verbatim
    public DateTimeOffset? RetryAfter { get; init; }
}

public enum AuthFailureCode
{
    CredentialsRequired = 0, CredentialsInvalid = 1, SessionExpired = 2, SessionRevoked = 3,
    AccountDisabled = 4, AccountLockedOut = 5, CsrfTokenMissing = 6, CsrfTokenInvalid = 7,
    ApiKeyInvalid = 8, ApiKeyExpired = 9, ApiKeyInQueryString = 10,
    ProxyUntrustedPeer = 11, ProxySecretMismatch = 12, ProxyIdentityMissing = 13, ProxyUserUnknown = 14,
    SetupRequired = 15, SetupTokenInvalid = 16, PermissionDenied = 17, RateLimited = 18,
}
```

Every discriminant is a closed enum, so the client's copy-map is a `switch` the compiler completes —
`TreatWarningsAsErrors` turns a missed case into a build failure, exactly as
`unified-host-runtime.md` §6.7 argues for the intent vocabulary.

---

## 5. Authorization

### 5.1 Permissions

A closed enum with a fixed role→set map. Not user-editable in v1.

```csharp
// src/Arronix.Abstractions/Wire/Auth/Permission.cs   (ARX0017)
public enum Permission
{
    ViewLibrary = 0, ViewActivity = 1, ViewHistory = 2,
    ManageLibrary = 3,          // add / edit / monitor items
    RequestAcquisition = 4,     // interactive search, grab, queue actions
    DeleteFiles = 5,            // remove media files from disk
    ManageSettings = 6,         // indexers, download clients, profiles, root folders, naming
    ManagePlugins = 7,          // install, enable, disable, configure
    ManageUsers = 8,
    ViewSystem = 9,             // logs, tasks, health detail, support bundle, audit
}
```

| Permission | `Member` | `Administrator` |
|---|:--:|:--:|
| `ViewLibrary`, `ViewActivity`, `ViewHistory` | ✅ | ✅ |
| `ManageLibrary`, `RequestAcquisition` | ✅ | ✅ |
| `DeleteFiles` | ❌ | ✅ |
| `ManageSettings`, `ManagePlugins`, `ManageUsers`, `ViewSystem` | ❌ | ✅ |

Self-scoped operations — one's own devices, one's own API keys, one's own password — need no permission and
are never grantable over another user.

Enforcement is an ASP.NET authorization policy per permission, applied at the endpoint. The client receives
`Permissions` in `SessionView` and uses it **only to decide what to render**. The server is the authority;
the client list is an affordance, never a gate.

### 5.2 What is deliberately *not* scoped

- **Per-media-kind access** (resolution #3). The route check on `/api/v1/kinds/{kind}/**` is one line; the
  problem is everything else. Queue, history, calendar, health, the SignalR `system` group, notification
  payloads and the support bundle are all cross-kind, so a per-kind scope that stops at the browse routes
  is a boundary in name only — and users will believe it. Ship none and say why.
  **Trigger to un-defer:** a store that can filter every cross-kind read by `MediaKindId`, plus a decided
  answer for what a push notification is allowed to say. Both belong to the Storage and Notification
  milestones, not this one.
- **Per-root-folder access** (resolution #4). Rejected permanently. Import, rename and delete happen inside
  background jobs that have no user context by design (§5.3); enforcing a folder scope means threading a
  principal through the job queue and into the plugin-facing import pipeline, which is precisely the
  identity leak §8 forbids.
- **Custom roles / editable permission sets.** Additive later; a maze now.

### 5.3 Where authorization is decided

**At the point of request, never at the point of execution.** An HTTP request is authorized against the
caller's permissions; if it enqueues work, the job runs as the **system principal** with full host
authority and records `TriggeredByUserId` for audit only.

The alternative — jobs inheriting the triggering user's authority — makes system behavior depend on who
happened to click, produces jobs that fail hours later because a user was disabled in the meantime, and has
no answer at all for the RSS sync that nobody triggered. Resolution #21.

### 5.4 Guards

- At least one **enabled** `Administrator` must exist: the last one cannot be deleted, disabled or demoted.
- Changing a user's role or password revokes all of that user's sessions **except the current one**, and
  revokes their API keys only on delete/disable.
- Deleting a user cascades to their sessions, devices, API keys and push subscriptions, and **anonymizes**
  rather than deletes their audit rows (an audit log you can erase by deleting an account is not one).

---

## 6. The manifest, installability, and the anonymous asset set

### 6.1 The resolution

**Drop `crossorigin="use-credentials"`. Serve the PWA shell anonymously.**

```html
<link rel="manifest" href="manifest.webmanifest" />
```

`pwa-and-push.md` §3.1 correctly identifies the trap — without the attribute the browser fetches the
manifest uncredentialed, gets a `401`, and silently declines to offer installation, with the maddening
symptom *"the install button never appears, but everything else works"* — and correctly notes the
alternative ("allow the manifest, icons and service worker anonymously"). This document takes the
alternative, and the reason is causal rather than preferential:

> The *arrs must authenticate their static assets (`StaticResourceController.cs:13`) **because their
> bootstrap file contains the API key** (`InitializeJsonController.cs:49`). Arronix's bootstrap contains no
> secret, because the client never receives a credential (§3.3 rule 5). Remove the leak and the
> authenticated-shell requirement — and with it the entire `crossorigin` failure mode — disappears.

`use-credentials` is also the fragile option in practice: it depends on the credential being a cookie the
browser will attach to a `no-cors`-adjacent fetch, it interacts badly with an SSO portal in front, and its
failure mode is silent. Anonymity has no failure mode.

### 6.2 The anonymous set, exactly

| Path | Why it is safe |
|---|---|
| `/`, `/index.html` | Static shell. Contains no data and no secret. |
| `/manifest.webmanifest` | Install metadata. Must carry nothing user-specific — no per-user `name`, no instance secrets. |
| `/service-worker.js`, `/service-worker-assets.js` | Served with `Cache-Control: no-cache` from the app root so its scope covers the origin (`unified-host-runtime.md` §6.1). |
| `/_framework/**`, `/css/**`, `/app/icons/**`, `/Logo/**` | The WASM runtime, assemblies and branding. |
| `/app/ping` | `204`, no auth, no logging, not intercepted (`pwa-and-push.md` §1.5.3). |
| `/app/asset-manifest.json` | Build/version metadata for the `Incompatible` gate. |
| `GET /api/v1/auth/session` | Describes state; never a secret (§4.5 rule 2). |
| `POST /api/v1/auth/login`, `POST /api/v1/auth/logout`, `POST /api/v1/auth/setup` | Rate-limited (§7.3). |
| `GET /api/v1/push/vapid` | Public by definition (`pwa-and-push.md` §4.6). |
| `GET /health/live`, `GET /health/ready` | `204` / `503`, **no body** (resolution #24). |

Everything else — including `GET /api/v1/health`, which returns a `HealthSnapshotView` naming plugins,
indexers and remediation hints — requires authentication.

**Media cover art (`/mediacover/**`) is authenticated.** It is library data. The cost is that the service
worker's cache-first artwork strategy (`pwa-and-push.md` §2.3) only works for an authenticated client,
which it always is when it is showing artwork.

### 6.3 The interaction operators will actually hit

If an operator fronts Arronix with an SSO portal that challenges every path, installability breaks again
and it is not our `401`. Two mitigations, both cheap:

1. The `X-Arronix-Auth` discriminator (§4.5 rule 3) lets the client say *"your sign-in provider blocked
   this"* instead of showing a broken login form.
2. Ship the exemption snippets (§3.4) covering exactly the table above, and add a **health check** that
   fetches `/manifest.webmanifest` anonymously through the configured external URL and reports
   `HealthSeverity.Warning` with the remediation hint *"installability is disabled because the manifest is
   not anonymously reachable"* when it does not get a `200`. That converts a silent, un-diagnosable failure
   into a line in the health panel — which is the whole reason this document exists.

---

## 7. API surface

### 7.1 Endpoints

```text
GET    /api/v1/auth/session          → SessionView              anonymous, always 200
POST   /api/v1/auth/login            → SessionView | AuthProblem anonymous, rate limited
POST   /api/v1/auth/logout           → 204 { proxyLogoutUrl? }  also deletes this device's push subscription
POST   /api/v1/auth/setup            → SessionView              setup mode only, one-time token
POST   /api/v1/auth/password         → 204                      requires current password; revokes other sessions
POST   /api/v1/auth/hub-ticket       → HubTicket                API-key auth only, 30 s, single use
GET    /api/v1/auth/devices          → AuthDeviceView[]         self; all with ManageUsers
PATCH  /api/v1/auth/devices/{id}     → AuthDeviceView           rename
DELETE /api/v1/auth/devices/{id}     → 204                      revokes its sessions and push subscriptions
GET    /api/v1/auth/sessions         → AuthSessionView[]        self
DELETE /api/v1/auth/sessions/{id}    → 204
GET    /api/v1/apikeys               → ApiKeyView[]             self
POST   /api/v1/apikeys               → ApiKeyCreated            raw token returned exactly once
DELETE /api/v1/apikeys/{id}          → 204
GET    /api/v1/users                 → AuthUserView[]           ManageUsers
POST   /api/v1/users                 → AuthUserView             ManageUsers
PATCH  /api/v1/users/{id}            → AuthUserView             ManageUsers
DELETE /api/v1/users/{id}            → 204                      ManageUsers, last-admin guard
GET    /api/v1/audit                 → AuditEntryView[]         ViewSystem, paged, filterable
GET    /health/live  /health/ready   → 204 | 503                anonymous, no body
```

### 7.2 Versioning, and the three names that are not versioned

Auth resources version with the API (`/api/v1/auth/**`). Three things are **transport conventions, not
versioned resources**:

1. the API-key header name `X-Api-Key` (and `Authorization: Bearer`);
2. the session cookie name `arx_session`;
3. the `X-Arronix-Auth: 1` discriminator header.

The property worth recording is that an unversioned transport convention becomes expensive to change *once
it has consumers*, because its consumers are exactly the things that never negotiate a version — a reverse
proxy config, a `curl` script in somebody's cron, a browser holding a cookie. So choose these three names
deliberately, once, and do not churn them for taste.

That is the whole rule. Two things an earlier draft attached to it are removed:

- **They are not "frozen across major versions".** Nothing consumes them yet: `arx_session` and
  `X-Arronix-Auth` are named here before the client that would send them exists (WP-A14), and no byte has
  shipped. Declaring a freeze now is a promise made on behalf of nobody, and it forecloses the one moment
  when changing a name costs a find-and-replace in one repository. The names become expensive when the
  first release goes out; until then they are just names in a document.
- **There is no sunset protocol.** An earlier draft designed in a `Deprecation:` / `Sunset:` response-header
  ladder (RFC 9745 / RFC 8594) plus a `HealthCheck` naming the credential still using a retired mechanism,
  justified as *"cost today: zero"*. Zero cost today is the tell rather than the justification: it is a
  deprecation window designed against zero implementers, for a product that has ruled deprecation windows
  out. A mechanism retired before it has consumers is deleted. If one is ever retired *after* it has
  consumers, the notice mechanism gets designed at that point, against the consumers that exist — which is
  the only way to get it right, and the reason not to invent it here.

### 7.3 Rate limiting

`Microsoft.AspNetCore.RateLimiting` is in the shared framework and `System.Threading.RateLimiting` is
already in `Arronix.Host`'s package set (`unified-host-runtime.md` §7.3) — **no new package**.

| Policy | Partition | Limit | Response |
|---|---|---|---|
| `auth-login` | `(client IP, SHA256(username))` — both, so one attacker cannot lock out a real user by IP alone and one password cannot be sprayed from many IPs | 10 / 5 min | `429` + `Retry-After` |
| `auth-login-global` | none | 60 / 5 min | `429` + `Retry-After` |
| `auth-setup` | none | 5 / 5 min | `429` |
| `auth-apikey-failure` | client IP | 100 / min | `429` |

**Honest caveat.** Behind a reverse proxy the client IP is the proxy's unless forwarded headers are
configured from a trusted list (§3.4). When they are not, the IP partition collapses to one partition and
`auth-login-global` is the only real limit — which is why the global policy exists and why its default is
set low enough to matter on its own.

### 7.4 Lockout

- `ConsecutiveFailedLogins` on the user row; at `LockoutThreshold` (10) the account locks for
  `min(LockoutBaseWindow × 2^(n-threshold), LockoutMaxWindow)` — 30 s doubling to a 15-minute ceiling.
- **Never permanent.** A permanent lockout is a denial-of-service any stranger can inflict on a household.
- Reset on successful login, on an administrator's explicit unlock, and by `--reset-admin-password`.
- A locked account returns the same shape and the same timing as a wrong password, with
  `AuthFailureCode.AccountLockedOut` and a `RetryAfter` — the account state is not a secret from someone
  who already knows the username, and hiding it produces a user who cannot understand why the right
  password fails.

### 7.5 Audit

One append-only table, host-owned, `Arronix:Auth:Audit:RetentionDays` (default 90).

```csharp
public enum AuthAuditKind
{
    LoginSucceeded = 0, LoginFailed = 1, LoggedOut = 2, SessionRevoked = 3, LockoutTriggered = 4,
    PasswordChanged = 5, PasswordReset = 6,
    UserCreated = 7, UserUpdated = 8, UserDeleted = 9, RoleChanged = 10,
    ApiKeyCreated = 11, ApiKeyRevoked = 12, ApiKeyFirstUsed = 13,
    DeviceRegistered = 14, DeviceRevoked = 15,
    ProxyHeaderRejected = 16, SetupCompleted = 17,
    SettingsChanged = 18, PluginInstalled = 19, PluginEnabled = 20, PluginDisabled = 21,
}
```

Columns: `Id`, `At`, `Kind`, `ActorUserId?`, `ActorApiKeyId?`, `DeviceId?`, `SourceIp`, `UserAgent`,
`TargetUserId?`, `Detail` (small JSON bag). Also mirrored to the dedicated `Auth` logger — the *arrs'
`AuthenticationService.cs:18` is the one part of this stack worth keeping.

**The detail bag goes through Common's redaction** (`Arronix.Abstractions/Diagnostics/IRedactionRuleProvider.cs`,
implemented in `Arronix.Common/Diagnostics/Redaction/`) so a `SettingsChanged` entry can never record a
download-client password. Auth-specific redaction rules — session tokens, API keys, the proxy shared secret,
the setup token, `Set-Cookie` — are registered by the host alongside the push endpoint rules
(`pwa-and-push.md` §4.11).

Audit rows are **never** put in the support bundle unredacted, and `SourceIp` is truncated to /24 (IPv4) or
/64 (IPv6) in exports.

---

## 8. Plugins and identity

### 8.1 The rule

> **No plugin ever observes a user identity.** `IPluginContext` exposes no principal, no user id, no
> session, no `HttpContext`, no request header, and no per-user value.

This is not new policy; it is the current `IPluginContext` (`unified-host-runtime.md` §3.3) written down as
a rule so that its absence stops being an accident. It follows directly from `ARCHITECTURE.md` §11: a plugin
that can correlate users holds an ambient capability nobody declared and nothing gated.

### 8.2 Consequences, stated

1. **Plugin and provider settings are global.** A `ProviderDefinition` (`unified-host-runtime.md` §5) is
   host-owned configuration administered by an `Administrator`. There is no "my indexer" and "your
   indexer".
2. **Per-user plugin settings do not exist in v1**, and adding them is not a small change. The shape would
   be: a plugin declares `FieldDescriptor`s at user scope, the host stores values keyed by
   `(userId, pluginId, fieldId)`, and the plugin receives the resolved values for the acting request —
   which requires an identity to flow into plugin code. **If that trigger ever fires, the identity passed
   must be an opaque per-plugin pseudonym, `HMAC(pluginKey, userId)`** — stable within one plugin,
   uncorrelatable across plugins, and never the real id. Recorded now so the wrong answer is not reached
   for under deadline.
3. **Notification providers receive no recipient identity.** A plugin implementing `INotificationProvider`
   gets a `NotificationEnvelope` and its own configured endpoint. Web-push is host-owned precisely because
   it is per-user and per-device (`pwa-and-push.md` §4.12 items 1–3) — this section confirms that choice
   rather than revisiting it.
4. **The audit actor is host-side.** A plugin-initiated action is attributed from the host's request
   context. A plugin cannot claim, forge or suppress an actor.
5. **Background jobs carry no principal** (§5.3), which is the same rule seen from the scheduler's side.

### 8.3 The governance test

Add to `Arronix.Abstractions.Tests`, alongside `MediaNeutralityTests` and `IntentVocabularyTests`:

| Test | Asserts |
|---|---|
| `IdentityNeutralityTests` | No type transitively reachable from `IPluginContext` or `IPluginRegistry` — properties, method parameters, return types, generic arguments — exposes a member whose name or type contains `User`, `Principal`, `Claim`, `Session`, `Password`, `Credential`, `Role`, `Permission`, `Identity`. Explicit allow-list: `HostIdentityOptions`, `LevelIdentity`, `IHostRuntimeInfo`, `SettingSensitivity` (whose `UserName`/`Credential` members describe a *provider's* secret, not a person's). |

Reachability rather than a namespace ban, because `Arronix.Client` also references only Abstractions and
therefore the auth wire DTOs (§4.6) must physically live there. The test asserts the property that actually
matters — *a plugin can never be handed one* — instead of the property that is merely tidy.

---

## 9. Storage

Five tables, all host-owned. DDL and migrations belong to `docs/design/storage-layer.md`; the shape is
fixed here so that document is not left guessing.

```text
users        (id PK, username UNIQUE, display_name, email, role, enabled,
              pwd_algorithm, pwd_iterations, pwd_salt, pwd_hash,
              created_at, last_login_at, consecutive_failed_logins, locked_until)

auth_devices (id PK, user_id FK→users ON DELETE CASCADE, label, user_agent,
              created_at, last_seen_at, last_seen_ip)

auth_sessions(id PK, user_id FK, device_id FK, token_hash BLOB(32) UNIQUE,
              csrf_hash BLOB(32), created_at, last_active_at,
              absolute_expires_at, idle_expires_at, revoked_at, revoked_reason)
              INDEX (user_id), INDEX (idle_expires_at)      -- sweep

auth_api_keys(id PK, user_id FK, name, prefix UNIQUE, token_hash BLOB(32) UNIQUE,
              role, permissions, created_at, expires_at, last_used_at, revoked_at)

auth_audit   (id PK, at, kind, actor_user_id, actor_api_key_id, device_id,
              source_ip, user_agent, target_user_id, detail JSON)
              INDEX (at DESC), INDEX (kind, at DESC)
```

**Effect on `push_subscriptions`** (`pwa-and-push.md` §4.7): `UserId` becomes a non-null FK to `users`
(the "pseudo-user when authentication is disabled" case cannot arise — there is no such mode), and a
`DeviceId` FK to `auth_devices` is added so that revoking a device revokes its push subscription in one
statement. This is the only structural change this document imposes on that design.

Dapper + SQLite/Postgres, no EF Core, per the contributor rule. A `SessionSweep` scheduled job
(`IScheduledJob`, `every 1h`) deletes expired sessions and audit rows past retention.

---

## 10. Work packages

Ordered. WP-A1…A3 are store-agnostic and can begin immediately; **WP-A4 onward blocks on the Storage-Layer
milestone.**

| WP | Deliverable | Files | Depends on |
|---|---|---|---|
| **WP-A1** | Wire contracts | `src/Arronix.Abstractions/Wire/Auth/{SessionView,AuthUserView,AuthDeviceView,AuthSessionView,ApiKeyView,ApiKeyCreated,HubTicket,AuthProblem,AuthFailureCode,AuthChallenge,AuthMode,UserRole,Permission}.cs`, all `[Experimental(ExperimentalContracts.Wire)]` (`ARX0017`); `docs/contracts/stability.md` row unchanged (no new area) | `unified-host-runtime.md` WP-1 |
| **WP-A2** | Identity domain, no ASP.NET types | `src/Arronix.Host/Identity/{AuthOptions,PasswordHasher,TokenGenerator,PermissionMap,LockoutPolicy,IIdentityStore,IdentityService,ProxyIdentityResolver}.cs` | WP-A1 |
| **WP-A3** | Governance test | `src/Arronix.Abstractions.Tests/Identity/IdentityNeutralityTests.cs` (§8.3) | WP-A1 |
| **WP-A4** | Persistence | Dapper `IIdentityStore` implementation + the five tables of §9 + the `push_subscriptions` change | **storage-layer milestone**, WP-A2 |
| **WP-A5** | Session scheme + CSRF | `src/Arronix.Api/Authentication/{SessionAuthenticationHandler,SessionSchemeOptions,CsrfMiddleware,AuthChallengeMiddleware}.cs`; `HandleChallengeAsync` returns JSON `401` with `X-Arronix-Auth` for `/api/**` and `/hub/**` | WP-A4 |
| **WP-A6** | API-key scheme | `ApiKeyAuthenticationHandler.cs`, query-string rejection with `400`, `FixedTimeEquals`, `LastUsedAt` throttling | WP-A4 |
| **WP-A7** | Proxy scheme + trust config | `ProxyHeaderAuthenticationHandler.cs`, `IdentityHeaderStrippingMiddleware.cs`, `ForwardedHeadersOptions` bound from `TrustedProxies`, startup validation that refuses to boot on an empty list | WP-A6 |
| **WP-A8** | Authorization policies | one policy per `Permission`, endpoint annotations across `Arronix.Api`, last-admin guards | WP-A5 |
| **WP-A9** | Endpoints | `src/Arronix.Api/Authentication/Endpoints/{Auth,Devices,Sessions,ApiKeys,Users,Audit}Endpoints.cs` per §7.1 | WP-A8 |
| **WP-A10** | Rate limiting + lockout | four policies of §7.3 via `Microsoft.AspNetCore.RateLimiting`; lockout in `LockoutPolicy` | WP-A9 |
| **WP-A11** | Audit + redaction | `AuthAuditWriter`, `AuthRedactionRuleProvider`, `/api/v1/audit`, `SessionSweep` job | WP-A9 |
| **WP-A12** | First run + CLI recovery | setup mode, one-time token, loopback exemption, `--reset-admin-password`, `--create-admin` | WP-A9 |
| **WP-A13** | Anonymous asset set + health split | endpoint allow-list of §6.2, `/health/live` + `/health/ready`, `/api/v1/health` authenticated, manifest reachability health check | WP-A9 |
| **WP-A14** | Client auth | `src/Arronix.Client/Auth/{AuthState,LoginPage,SetupPage,CsrfHandler,AuthDelegatingHandler}.razor(.cs)`; extend `IConnectivityMonitor` with the §4.5 discriminator; hub ticket path | WP-A9, `pwa-and-push.md` WP-P03 |
| **WP-A15** | Client settings surfaces | devices list, sessions list, API keys, users (admin), audit viewer | WP-A14 |
| **WP-A16** | Operator documentation | Caddy / Traefik+Authelia / nginx+Authentik / oauth2-proxy exemption snippets; the "no `None` mode" migration note; API-key rotation guide | WP-A13 |

---

## 11. Deferred, with the trigger that un-defers it

| Deferred | Trigger |
|---|---|
| **OIDC / OAuth2 client** | A deployment that must federate without a reverse proxy. Lands as a separate optional `Arronix.Api.Oidc` assembly so `Arronix.Api`'s zero-package invariant survives (§3.5). |
| **Passkeys / WebAuthn** | Would need `Fido2NetLib` or equivalent (third-party). Proxy mode gets it today from Authelia/Authentik. Revisit if a Microsoft-owned or in-box option appears. |
| **TOTP / MFA in-app** | Same reasoning. A homelab that wants MFA already has a portal that does it; adding a second, weaker implementation next to it is negative value. |
| **Per-media-kind scoping** | A store that filters every cross-kind read by `MediaKindId`, plus a decided answer for notification content (§5.2). |
| **Per-root-folder scoping** | Never — rejected, not deferred (§5.2). |
| **Custom roles / editable permission sets** | Two roles proving insufficient in a real household. Purely additive: the `Permission` enum and the policy-per-permission wiring already exist. |
| **A request/approval workflow** ("my kid asks, I approve") | This is Overseerr's product, not ours. Un-defers only if `RequestAcquisition` being a plain `Member` permission proves wrong in practice. |
| **Per-user preferences (theme, default view, notification mask)** | The device-level `EventMask` already exists (`pwa-and-push.md` §4.7). A user-level preference table is one table and no contract change; there is simply nothing that needs it yet. |
| **Per-user plugin settings** | See §8.2 item 2, including the pseudonymous-id shape it must take. |
| **Session binding to IP or user agent** | Roaming between Wi-Fi and mobile data breaks IP binding, and UA strings change on every browser update. The revocable device list is the better answer to the same worry. |
| **Encrypting credential columns at rest** | `pwa-and-push.md` resolution #16 applies verbatim: theater while indexer API keys sit beside them in the clear. Comes free with whole-store encryption. Note that passwords and tokens are already *hashed*, which is a different and sufficient property. |
| **Brute-force IP banning / fail2ban integration** | The `Auth` logger already emits parseable lines; an operator can point fail2ban at them today. Shipping our own ban list duplicates a solved problem. |
| **CAPTCHA on login** | Third-party service, network dependency, and a rate limit plus lockout already bounds the attack. |
| **A CI check for the anonymous asset set** | Consistent with the extraction plan's CI deferral. This is the first one worth automating when CI exists: an accidental `[Authorize]` on `/manifest.webmanifest` is silent and breaks installability. |

---

## 12. Recommended amendments to other documents

Owned by other agents; **not edited here.**

| Document | § | Amendment |
|---|---|---|
| `docs/design/pwa-and-push.md` | §7 q.3, q.4 | Both closed: multi-user with two roles; cookie sessions, not bearer tokens. |
| `docs/design/pwa-and-push.md` | §3.1 | Drop `crossorigin="use-credentials"`; adopt the anonymous asset set of §6.2 (the alternative that section already prefers). |
| `docs/design/pwa-and-push.md` | §4.7 table | `UserId` is a non-null FK; **delete the "pseudo-user when authentication is disabled for local addresses" clause** — no such mode exists. Add a `DeviceId` FK. |
| `docs/design/pwa-and-push.md` | §4.12 item 6 | `SameSite=Lax`, not `Strict` (resolution #10); the antiforgery mechanism is §4.2's session-bound double-submit. |
| `docs/design/pwa-and-push.md` | §2.4 `PASSTHROUGH` | Add `/^api\/v1\/auth\//`; the existing `/^login$/`, `/^logout$/` become client routes rather than server endpoints. |
| `docs/design/unified-host-runtime.md` | §6.6 | Split `GET /health` into anonymous `/health/live` + `/health/ready` (204/503, no body) and authenticated `/api/v1/health` (resolution #24). Add the `/api/v1/auth/**`, `/api/v1/apikeys`, `/api/v1/users`, `/api/v1/audit` groups of §7.1. |
| `docs/design/unified-host-runtime.md` | §7.5 | Add `IdentityNeutralityTests` (§8.3) to the governance-test table. |
| `ARCHITECTURE.md` | §11 | Add the §8.1 rule verbatim: a plugin never observes a user identity. |
| `ARCHITECTURE.md` | §7 | The API strategy says nothing about authentication; add a pointer here. |

---

## 13. Open questions

1. **URL base and cookie `Path`.** `UrlBase` (a reverse-proxy subpath) must scope the session cookie, or two
   Arronix instances behind one domain will overwrite each other's cookies. Confirm the host's `UrlBase`
   option name against `unified-host-runtime.md` before WP-A5.
2. **Does the setup token belong in the container's `HEALTHCHECK` output?** It would remove the
   `docker logs` step, and it would also print a credential somewhere new. Leaning no.
3. **Audit retention when the database is the log database.** `unified-host-runtime.md` does not yet say
   whether there is a separate log store; if there is, `auth_audit` should live in the main store anyway
   (it is evidence, not diagnostics). Confirm with `storage-layer.md`.
4. **Does `SessionView.SecureContext` duplicate what the client can determine itself?** `window.isSecureContext`
   is available via interop, but it does not know whether the *server* thinks the connection is secure
   (they differ behind a TLS-terminating proxy that forwards `http`). Keeping the server's opinion is
   probably right; verify against the `pwa-and-push.md` §3 insecure-context notice so the two do not
   disagree.
5. **Hub ticket vs. a first-message auth frame.** SignalR supports sending an access token via
   `AccessTokenProvider`, which for WebSockets ends up in the query string regardless. If a future
   transport makes a header possible, the ticket becomes unnecessary. Re-check at implementation time.
