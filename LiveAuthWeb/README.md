# LiveAuth

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 20.3.6.

## Development server

To start a local development server, run:

```bash
ng serve
```

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
ng build
```

This will compile your project and store the build artifacts in the `dist/` directory. By default, the production build optimizes your application for performance and speed.

## Running unit tests

To execute unit tests with the [Karma](https://karma-runner.github.io) test runner, use the following command:

```bash
ng test
```

## Running end-to-end tests

For end-to-end (e2e) testing, run:

```bash
ng e2e
```

Angular CLI does not come with an end-to-end testing framework by default. You can choose one that suits your needs.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.

# ⚡ Bitcoin + Lightning + Nostr Dev Stack Cheat Sheet

A quick reference for running your local regtest environment with Docker.

---

## 🚀 Container Management

| Task | Command |
|------|----------|
| **Start all containers** | `docker compose up -d` |
| **Stop all containers** | `docker compose down` |
| **View running containers** | `docker ps` |
| **View logs for one container** | `docker logs -f bitcoin` (or `lnd`, `nostr-relay`) |
| **Restart a single service** | `docker restart nostr-relay` |

---

## 🪙 Bitcoin Core (Regtest)

### Common Commands

```powershell
# open a shell inside the Bitcoin container
docker exec -it bitcoin bash
bash
Copy code
# create a wallet (first time only)
bitcoin-cli -regtest -rpcuser=bitcoin -rpcpassword=bitcoin createwallet miner

# load your wallet if needed
bitcoin-cli -regtest -rpcuser=bitcoin -rpcpassword=bitcoin loadwallet miner

# get a new mining address
bitcoin-cli -regtest -rpcuser=bitcoin -rpcpassword=bitcoin -rpcwallet=miner getnewaddress

# mine 101 blocks (first batch)
bitcoin-cli -regtest -rpcuser=bitcoin -rpcpassword=bitcoin generatetoaddress 101 <bcrt1-address>

# check balance
bitcoin-cli -regtest -rpcuser=bitcoin -rpcpassword=bitcoin -rpcwallet=miner getbalance

# send BTC (fund LND)
bitcoin-cli -regtest -rpcuser=bitcoin -rpcpassword=bitcoin -rpcwallet=miner sendtoaddress <lnd-address> 1
⚡ Lightning Node (LND)
powershell
Copy code
# open a shell in LND
docker exec -it lnd bash
Wallet & Info
bash
Copy code
lncli --network=regtest create        # first run
lncli --network=regtest unlock        # unlock existing wallet
lncli --network=regtest getinfo       # node info
lncli --network=regtest walletbalance # view BTC balance
lncli --network=regtest newaddress p2wkh   # get deposit address
Invoices & Payments
bash
Copy code
lncli --network=regtest addinvoice --amt 1000       # create invoice
lncli --network=regtest listinvoices                # list all invoices
lncli --network=regtest payinvoice <bolt11>         # pay invoice
🛰️ Nostr Relay
Task	Command
Check relay is running	curl http://localhost:8080
Restart relay	docker restart nostr-relay

Expected output: small JSON or “I’m a Nostr relay” message.

🧩 Quick Balance Check
powershell
Copy code
# Bitcoin wallet balance
docker exec -it bitcoin bitcoin-cli -regtest -rpcuser=bitcoin -rpcpassword=bitcoin -rpcwallet=miner getbalance

# LND wallet balance
docker exec -it lnd lncli --network=regtest walletbalance
🔁 Rebuild / Reset Everything
⚠️ This removes all wallets, mined blocks, and channel data.

powershell
Copy code
docker compose down -v
rmdir /s /q .\bitcoin\data
rmdir /s /q .\lnd\data
rmdir /s /q .\nostr\data
docker compose up -d
🧠 Typical Workflow
Step	Action	Command
1	Start stack	docker compose up -d
2	Create/load miner wallet	createwallet miner
3	Mine 101 blocks	generatetoaddress 101 <bcrt1>
4	Get LND address	lncli newaddress p2wkh
5	Send funds to LND	sendtoaddress <addr> 1
6	Mine 6 confirm blocks	generatetoaddress 6 <bcrt1>
7	Check LND balance	lncli walletbalance
8	Create & pay invoice	lncli addinvoice --amt 1000 / lncli payinvoice <bolt11>
9	Test Nostr relay	curl http://localhost:8080

💡 Pro Tip
Keep this stack running in the background while developing your apps.
It gives you a fully self-contained Bitcoin + Lightning + Nostr playground.

📂 Project Layout

kotlin
Copy code
nostr-lightning-stack/
├─ docker-compose.yml
├─ bitcoin/
│  └─ data/
├─ lnd/
│  └─ data/
├─ nostr/
│  └─ data/
└─ node-helper/
   └─ src/index.ts
yaml
Copy code

---
