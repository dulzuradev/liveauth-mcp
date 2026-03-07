#!/bin/bash
# Local development startup for LiveAuth API

cd /users/sydney/.openclaw/workspace/LiveAuth/LiveAuthCore

export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://localhost:5000
export DB_PROVIDER=sqlite
export ConnectionStrings__Default="Data Source=$(pwd)/data/liveauth.db"
export Admin__SkipPayment=true
export Jwt__SigningKey=faffda742870fc04b9ad90474a05c6d31aad8d1eaa58d66e73602f7182d4de80
export Jwt__Issuer=LiveAuth
export Jwt__Audience=LiveAuthUsers
export LiveAuth__PowHmacSecret=17b6b192e379f5689f87d99bf06510dc83e71de2d7429666bdb1c1437b6bd9e0
export LiveAuth__DemoProjectId=00000000-0000-0000-0000-000000000002
export GitHub__ClientId=dummy
export GitHub__ClientSecret=dummy
export Lnd__BaseUrl=https://localhost:9739

# Run the API
dotnet run --no-build
