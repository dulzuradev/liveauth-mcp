#!/bin/bash
# Local development startup for LiveAuth API

set -e

cd /users/sydney/.openclaw/workspace/LiveAuth/LiveAuthCore

export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://localhost:5000
export DB_PROVIDER=sqlite
export ConnectionStrings__Default="Data Source=$(pwd)/data/liveauth.db"
export Admin__SkipPayment=true
export Jwt__SigningKey="dev-only-jwt-signing-key-at-least-32-bytes"
export Jwt__Issuer=LiveAuth
export Jwt__Audience=LiveAuthUsers
export LiveAuth__PowHmacSecret="dev-only-pow-hmac-secret-at-least-32-bytes"
export LiveAuth__DemoProjectId=00000000-0000-0000-0000-000000000002
export GitHub__ClientId=dummy
export GitHub__ClientSecret=dummy
export Lnd__BaseUrl=https://localhost:9739

dotnet run --no-build
