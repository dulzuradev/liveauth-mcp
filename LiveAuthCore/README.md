# LiveAuthCore

LiveAuthCore is an ASP.NET Core 9.0 Web API that demonstrates pay‑per‑request authentication using the Bitcoin Lightning Network. It can:

- Create Lightning invoices for micro‑payments
- Check payment settlement status
- Issue short‑lived JWTs upon successful payment
- Provide admin endpoints for viewing login attempts
- Expose OpenAPI/Swagger UI for easy testing

This README walks you through setup, configuration, and running the service locally or in a hosted environment.

-------------------------------------------------------------------------------

Quick links
- Requirements
- Quick start
- Configuration (appsettings, environment variables, user-secrets)
- Running and testing (Swagger UI, curl)
- API overview and examples
- Database
- CORS and hosting
- Lightning setup notes
- Troubleshooting

-------------------------------------------------------------------------------

Requirements
- .NET SDK 9.0 or later
- SQLite (bundled with Microsoft.Data.Sqlite provider; no separate server needed)
- A Lightning node with REST API enabled (e.g., LND via Polar or Voltage) if you want real payments
- Stripe/OpenNode keys only if you plan to exercise those services (optional in this demo)

-------------------------------------------------------------------------------

Quick start
1) Clone the repo
   git clone <your-repo-url>
   cd LiveAuthCore

2) Configure minimal secrets
   At minimum, configure JWT settings. For local dev you can keep defaults in appsettings.json, but you should use user-secrets or environment variables to avoid committing secrets.

   Option A: dotnet user-secrets (recommended in dev)
   dotnet user-secrets init
   dotnet user-secrets set "Jwt:Key" "a-very-long-32+char-random-secret"
   dotnet user-secrets set "Jwt:Issuer" "LiveAuthService"
   dotnet user-secrets set "Jwt:Audience" "LiveAuthUsers"

   Option B: environment variables
   export Jwt__Key="a-very-long-32+char-random-secret"
   export Jwt__Issuer="LiveAuthService"
   export Jwt__Audience="LiveAuthUsers"

3) Run the API
   dotnet run

4) Open Swagger UI
   When running in Development, browse to:
   https://localhost:5001/  (HTTPS)
   or
   http://localhost:5000/   (HTTP)
   The Swagger UI is served at the root in Development.

-------------------------------------------------------------------------------

Configuration
The application reads its configuration from appsettings.json, appsettings.Development.json, environment variables, and user-secrets. Key sections:

- Jwt
  Key:     Symmetric signing key for JWTs (must be sufficiently long)
  Issuer:  Token issuer
  Audience: Token audience

- OpenNode (optional)
  ApiKey:  Your OpenNode API key
  BaseUrl: https://api.opennode.com/v1

- Stripe (optional)
  ApiKey: Your Stripe secret key

Set via user-secrets
dotnet user-secrets set "OpenNode:ApiKey" "your-opennode-api-key"
dotnet user-secrets set "Stripe:ApiKey" "your-stripe-secret-key"

Set via environment variables
export OpenNode__ApiKey="your-opennode-api-key"
export OpenNode__BaseUrl="https://api.opennode.com/v1"
export Stripe__ApiKey="your-stripe-secret-key"

Lightning configuration
The current LightningService uses constants intended for a local test setup:
- REST_HOST = localhost:8081
- MACAROON = <hex macaroon>

For a production or properly configurable setup, you should move these values into configuration. If you are using Polar, make sure the REST port and macaroon match your node. If using a hosted LND (e.g., Voltage), you will also need TLS/cert handling and to remove the SSL bypass used for local testing.

Important: The included HttpClient handler in LightningService bypasses SSL certificate validation for development/testnet only. Do not use this in production.

-------------------------------------------------------------------------------

Running and testing
- Development profile
  By default, Program.cs enables Swagger UI and OpenAPI only in Development. Ensure ASPNETCORE_ENVIRONMENT=Development for local testing.

  export ASPNETCORE_ENVIRONMENT=Development
  dotnet run

- Swagger
  - Root path shows Swagger UI in Development.
  - Try the Login endpoints directly in the browser.

- Curl examples
  1) Request login (generates an invoice if the user is not a mock-subscribed user)
     curl -X POST "https://localhost:5001/api/login" \
          -H "Content-Type: application/json" \
          -d '{"userId":"alice"}'

     Response (example)
     {
       "invoice": {
         "r_hash": "...",
         "payment_request": "lnbc...",
         "settled": false
       }
     }

  2) Check payment status
     curl "https://localhost:5001/api/login/payment-status/{paymentHashBase64}"

     If settled, you will receive a short-lived JWT token in the response.

-------------------------------------------------------------------------------

API overview
- POST /api/login
  Body: { "userId": "string" }
  Behavior:
  - If userId == "subscribed_user" (mock), returns an immediate JWT.
  - Otherwise creates a Lightning invoice (100 sats) and returns invoice details.

- GET /api/login/payment-status/{paymentHash}
  - paymentHash is the base64-encoded r_hash returned from CreateInvoice.
  - If settled: returns JWT token. Otherwise: { "Status": "Pending" }.

- GET /api/admin/login-attempts (requires Admin role)
  - Requires Authorization: Bearer <token> with role Admin.

Authorization
- JWT authentication is configured with Microsoft.AspNetCore.Authentication.JwtBearer.
- Tokens are short-lived (currently 5 minutes in LightningService.GenerateJwtToken).
- The AdminController uses [Authorize(Roles = "Admin")].

-------------------------------------------------------------------------------

Database
- SQLite file: lightningcaptcha.db (created automatically at startup if not present).
- EF Core context: AppDbContext.
- Migrations: If you modify entities, add migrations and update DB:
  dotnet ef migrations add <Name>
  dotnet ef database update

Note: The app currently calls Database.EnsureCreated() at startup. If you switch to migrations-only flow, remove EnsureCreated() and rely on ef database update instead.

-------------------------------------------------------------------------------

CORS and hosting
- Two CORS policies are defined in Program.cs:
  - AllowSpecificOrigins: restricts origins to https://example.com and http://localhost:4200.
  - AllowAll: allows all origins (not enabled by default middleware pipeline).
- By default the app applies AllowSpecificOrigins; adjust origins in Program.cs for your client apps.

Reverse proxy / HTTPS
- For local dev, Kestrel serves HTTP (5000) and HTTPS (5001). For production, place behind a reverse proxy (Nginx, Apache, IIS, Azure Front Door, etc.). Configure certificates appropriately.

-------------------------------------------------------------------------------

Lightning setup notes
- Local testing with Polar:
  - Start an LND node with REST enabled (default port often 8081 in Polar topologies).
  - Retrieve the admin macaroon in hex (Polar provides it) and replace the MACAROON constant in LightningService for testing.
  - If using self-signed certs, the current handler bypasses SSL validation. Do not keep this for production.

- Hosted LND (Voltage, custom):
  - Configure REST host, TLS cert, and macaroon via configuration (recommended future change).
  - Remove SSL bypass and validate certificates.

-------------------------------------------------------------------------------

Troubleshooting
- 401/403 errors calling admin endpoints:
  - Ensure your JWT includes role Admin. The demo code issues Admin role only if userId == "admin" in GenerateJwtToken.

- Cannot connect to LND REST:
  - Verify REST_HOST and MACAROON match your node.
  - Confirm ports are reachable and node is running.
  - On macOS/Linux, check firewall rules.

- Swagger UI not loading:
  - Ensure ASPNETCORE_ENVIRONMENT=Development.
  - Check console for HTTPS certificate trust issues.

- JWT validation failed:
  - Ensure Jwt:Key, Jwt:Issuer, and Jwt:Audience used to generate tokens match those configured in the API.

-------------------------------------------------------------------------------
sequenceDiagram
participant Dev as Developer (Browser)
participant FE as Angular Dev Portal
participant API as DevAuthController
participant DB as LiveAuthDbContext
participant LND as LightningService → LND

    Dev->>FE: Enter email + click "Login (LN)"
    FE->>API: POST /api/dev/auth/start { developerEmail }
    API->>LND: CreateLoginInvoiceAsync(email, 21 sats, 10m)
    LND-->>API: { invoiceId (r_hash b64), bolt11, expiresAtUnix }
    API->>DB: INSERT DevLoginSession (email, invoiceId, bolt11, expiresAt, isPaid=false)
    DB-->>API: OK
    API-->>FE: { sessionId, invoice, amountSats, expiresAtUnix }

    FE->>Dev: Show QR + countdown
    Dev->>LND: Pay BOLT11 invoice from wallet

    loop every 2s until verified/expired
        FE->>API: POST /api/dev/auth/confirm { sessionId }
        API->>DB: SELECT DevLoginSession by sessionId
        DB-->>API: session
        API->>LND: GetInvoiceStatusAsync(session.InvoiceId)
        LND-->>API: { isPaid, payerLightningAuthKey? }

        alt not paid OR expired
            API-->>FE: { verified: false, token: null }
        else paid
            API->>DB: UPDATE DevLoginSession set IsPaid, PaidAt, PayerLightningAuthKey
            API->>DB: Find/Create Developer (email + LightningAuthKey)
            DB-->>API: developer
            API->>API: GenerateJwtForDeveloper(developerId, "Developer")
            API-->>FE: { verified: true, token }
        end
    end

    FE->>FE: saveToken(token) → localStorage
    FE->>API: (later) GET /api/dev/projects with Bearer token
    API->>DB: Check Developer by userId claim
    DB-->>API: projects
    API-->>FE: project list

License
This project is provided as-is, for demonstration purposes. Add your license details as appropriate.
