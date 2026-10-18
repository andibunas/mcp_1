# WebMCP: MCP flow and OAuth

How the WebMCP server works, how a client authenticates to it, and the pitfalls hit while building it.

## 1. What the server is

An ASP.NET Core (net10.0) MCP server using `ModelContextProtocol.AspNetCore` 2.2.0, exposing one tool (`echo`) over the **Streamable HTTP** transport at `/`. It is protected by a custom OAuth 2.1 flow where the "login" is the user pasting a **Google service account JSON key** into a web page.

| Piece | Where |
|---|---|
| Server wiring | `Program.cs` |
| OAuth code, one file per step | `OAuth/` |
| Tool | `EchoTool` at the bottom of `Program.cs` |

## 2. The MCP part

- `AddMcpServer().WithHttpTransport().WithToolsFromAssembly()` registers the SDK and discovers `[McpServerToolType]` classes.
- `app.MapMcp("/")` exposes the endpoint at the root.
- Client calls are JSON-RPC 2.0 over `POST /`.

Common request headers:

```
Authorization: Bearer <access token>
Content-Type: application/json
Accept: application/json, text/event-stream
```

Replies are either plain JSON or an SSE body (`event: message` / `data: {...}`). Postman may show the raw SSE text, which is expected.

### The four calls

| # | Method | Request | Response |
|---|---|---|---|
| 1 | `initialize` | `{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"x","version":"1"}}}` | `result` with `protocolVersion`, `capabilities`, `serverInfo` |
| 2 | `notifications/initialized` | `{"jsonrpc":"2.0","method":"notifications/initialized"}` (no `id`) | `202 Accepted`, empty body |
| 3 | `tools/list` | `{"jsonrpc":"2.0","id":2,"method":"tools/list"}` | `result.tools[]` with `name`, `description`, `inputSchema` (generated from the C# signature) |
| 4 | `tools/call` | `{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"echo","arguments":{"message":"hi"}}}` | `result.content[{"type":"text","text":"Echo: hi"}]` |

The tool name is the method name lower-cased (`Echo` becomes `echo`). Bad methods or params return a JSON-RPC `error` (for example `-32601`).

### Sessions: why there is no `Mcp-Session-Id`

SDK 2.2.0 defaults to **stateless** mode (`HttpServerTransportOptions.SessionMode = Stateless`, following the 2026-07-28 protocol revision). The server never issues `Mcp-Session-Id`, and `GET` and `DELETE` on the endpoint are disabled (405). Send no session header. To opt in to sessions for legacy clients:

```csharp
.WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateful) // namespace ModelContextProtocol.AspNetCore
```

`StatefulForInitializeClients` gives sessions only to clients that send `initialize`.

## 3. OAuth flow

`initialize` is blocked like every other call: **every** request to the MCP endpoint (`/`) needs a valid token, and the 401 on the first unauthenticated call is what starts the flow. Only discovery and the authorization-server endpoints stay open.

```
Client                                   WebMCP
  | POST / (no token)  ---------------> 401 + WWW-Authenticate: Bearer resource_metadata="<url>"
  | GET  /.well-known/oauth-protected-resource  -> { resource, authorization_servers }
  | GET  /.well-known/oauth-authorization-server -> { endpoints, PKCE S256 ... }
  | POST /register ----------> client_id
  | open browser: GET /authorize?... -> page: paste service account JSON
  |                                      POST /authorize -> 302 redirect_uri?code=...&state=...
  | POST /token (code + code_verifier) -> access_token, refresh_token
  | POST /  Authorization: Bearer <access_token>  -> works
```

### Endpoints (all in `OAuth/`)

| Step | Route | File | Notes |
|---|---|---|---|
| Protected resource metadata | `GET /.well-known/oauth-protected-resource` | `DiscoveryEndpoints.cs` | `resource` must equal the MCP URL (`http://localhost:5157`) |
| Auth server metadata | `GET /.well-known/oauth-authorization-server` | `DiscoveryEndpoints.cs` | `issuer` must equal the `authorization_servers` entry **exactly** (no trailing slash difference) |
| Dynamic Client Registration | `POST /register` | `RegisterEndpoint.cs` | validates redirect URIs; returns `client_id` (201) |
| Authorize | `GET/POST /authorize` | `AuthorizeEndpoint.cs` | GET shows the form; POST validates the key and redirects with a code |
| Token | `POST /token` | `TokenEndpoint.cs` | `authorization_code` (PKCE S256) and `refresh_token` grants |
| Guard | middleware on `/` | `BearerAuthMiddleware.cs` | decrypts the token, 401 with `WWW-Authenticate` on failure |

### Authorization server metadata fields

`issuer`, `authorization_endpoint`, `token_endpoint`, `registration_endpoint`, `response_types_supported: ["code"]`, `grant_types_supported: ["authorization_code","refresh_token"]`, `code_challenge_methods_supported: ["S256"]`, `token_endpoint_auth_methods_supported: ["none"]`, `scopes_supported`. `issuer`, the two endpoints, `response_types_supported` and S256 PKCE are mandatory in practice. The registration endpoint is needed for Dynamic Client Registration.

## 4. Stateless design: what "storage" means here

There is no database or session store. The server hands out values that are **sealed with ASP.NET Data Protection** (`TokenProtector.cs`): encrypted, tamper-proof and expiring. Each has its own purpose string, so a code can't be used as a token.

| Value | Contains | Lifetime |
|---|---|---|
| `client_id` | the registered redirect URIs | 365 days |
| authorization code | client id, redirect URI, PKCE challenge, the service account key | 60 s |
| access token | the service account key | 1 h |
| refresh token | client id, the service account key | 30 days |

The client stores the access token and sends it in the header. Each MCP request decrypts it. Tools read the key from `HttpContext.Items[OAuthSettings.CredentialItemKey]`.

Consequences:

- The only server-side state is the **Data Protection key ring** (local disk by default). If it is lost, all tokens become invalid and clients must re-authorize. Several instances must share a key store.
- A stateless server **cannot enforce single-use codes or revoke tokens**. The 60 s code lifetime and PKCE limit the exposure.
- The server can decrypt tokens, so they protect the key from the client, not from whoever holds the Data Protection keys.
- Codes (about 3.7 KB) and tokens (about 3.2 KB) are large because they embed the key. The code travels in the redirect URL, which is fine for loopback callbacks but can hit URL limits behind some proxies.

## 5. Service account key validation

`ServiceAccountKeyValidator.cs` checks the pasted JSON offline:

- valid JSON object, at most 10,000 characters
- `type == "service_account"`
- `client_email` matches `…@….iam.gserviceaccount.com`
- `private_key_id` is 32–64 hex characters
- `token_uri`, if present, is `https://oauth2.googleapis.com/token`
- `private_key` parses as an RSA PEM and is at least 2048 bits

Only `type, project_id, private_key_id, private_key, client_email, client_id, token_uri` are kept; other fields are dropped. **Planned:** a real Google Drive call (such as `files.list`) to confirm Google accepts the key. The `TODO(later)` marks the spot.

## 6. Security notes

- The authorize endpoint refuses to redirect until `client_id` and `redirect_uri` match (exact match). Unknown redirect URIs get a 400 page, never a redirect.
- Registration accepts `https`, loopback `http` and custom app schemes; it rejects `javascript:`, `data:`, `file:` and `vbscript:`.
- PKCE S256 only. The verifier is compared in constant time.
- The authorize page sets `Cache-Control: no-store` and `X-Frame-Options: DENY`, and HTML-encodes all echoed values. Token responses are `no-store`.
- Do not log tokens, codes or the key. The earlier debugging lines that printed the token were removed.
- The pasted key is a long-lived secret. Use a service account with the least access, and share only the needed Drive folders with it.
- The authorize form has no CSRF token; the hidden fields come from the client's request, so treat this as a local-development design.

## 7. Errors seen while building, and why

| Symptom | Cause |
|---|---|
| `Protected resource http://localhost:5157/resource/ does not match expected http://localhost:5157` | `resource` in the metadata must be the MCP endpoint URL (`http://localhost:5157`) |
| `Dynamic Client Registration rejected (HTTP 401)` | no `/register` endpoint, and the old middleware 401'd it |
| Three `404/401` lookups for `oauth-authorization-server`, `openid-configuration/authservers`, `authservers/.well-known/openid-configuration` | the client's standard metadata discovery order; the first one must exist |
| No `Mcp-Session-Id` header | SDK 2.2.0 is stateless by default (section 2) |
| Test server "worked" but showed old behaviour | an older instance still held port 5157 |

## 8. Logging payloads and headers for debugging

Put a middleware **above** the auth one:

- Request body: `Request.EnableBuffering()`, read it, then reset `Body.Position = 0`.
- Response body: swap `Response.Body` for a `MemoryStream`, run `next()`, log it, copy back in `finally`.
- Skip `GET` requests (long-lived SSE streams would be held back).
- Headers: loop over `Request.Headers` / `Response.Headers`, masking `Authorization`. Response headers are final only after `next()`.
- Quick alternative: `"Logging": {"LogLevel": {"ModelContextProtocol": "Debug"}}` in `appsettings.Development.json`.

## 9. Testing with curl or Postman

```bash
B=http://localhost:5157

# 1. unauthenticated call: expect 401 + WWW-Authenticate
curl -i -X POST $B/ -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' -d '{}'

# 2. register
curl -s -X POST $B/register -H 'Content-Type: application/json' \
  -d '{"redirect_uris":["http://localhost:9999/cb"],"client_name":"curl"}'

# 3. open in a browser (needs a PKCE challenge = base64url(SHA256(verifier))):
#    $B/authorize?response_type=code&client_id=<id>&redirect_uri=http://localhost:9999/cb
#         &code_challenge=<challenge>&code_challenge_method=S256&state=xyz
#    paste the service account JSON, then copy `code` from the redirect URL

# 4. exchange the code
curl -s -X POST $B/token -d grant_type=authorization_code \
  --data-urlencode code=<code> --data-urlencode client_id=<id> \
  --data-urlencode redirect_uri=http://localhost:9999/cb --data-urlencode code_verifier=<verifier>

# 5. call the MCP server (steps: initialize, then tools/list, then tools/call)
curl -s -X POST $B/ -H "Authorization: Bearer <access_token>" \
  -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}'
```

The full flow was verified end to end with curl against a copy on port 5158, using a generated fake key: no-token 401, registration, bad and good keys, wrong and right PKCE verifier, `initialize`, `tools/call`, a garbage token and the refresh grant. The Google Drive call has not been tested.

## 10. Known gaps and ideas

- The base URL is no longer hardcoded: `OAuthSettings.GetBaseUrl(ctx)` builds it from the request (scheme + host). Behind a proxy, `UseForwardedHeaders` in `Program.cs` takes scheme and host from `X-Forwarded-*`. The proxy must send those headers.
- The Google Drive verification of the key is not implemented yet.
- The hello page was removed; only the MCP endpoint at `/` is guarded.
- Scopes are advertised but not enforced.
- Option A (not chosen) was to delegate to an external identity provider (Entra, Auth0, Keycloak) and validate its JWTs with `AddJwtBearer`. That is the production route; this custom flow suits the "user types a Google service account key" requirement.





just some prompt that I didn't use
```
all of that is overkill for this flow.   There is no real code exchange needed for the service account since the user will just provide the actual string needed for the token.
I need the following changes:
- rename the oauth to fullOAuth and make a copy of the current program file in that folder as a reference.
- create a new simpleAuth folder and create very simple flow that just asks the user to enter the token value to verify it and the client can use it. and change program.cs to use this new flow.   I don't need the signing and checking flow besides a simple non empty pattern or maybe a basic pattern of the 

```