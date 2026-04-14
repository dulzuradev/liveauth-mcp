/* ======================================================
 * BILLING CLIENT
 * L402 balance purchase via Lightning
 * ====================================================== */

export interface BillingClientConfig {
    /**
     * Developer JWT from Lightning login (POST /api/dev/auth/start → confirm).
     * Get this by logging in via Lightning and extracting the JWT from the response.
     */
    jwt: string;

    /** Optional API base URL (defaults to liveauth.app) */
    baseUrl?: string;
}

export interface PurchaseResult {
    /** ID to pass to getPurchaseStatus() */
    purchaseId: string;
    /** Bolt11 invoice — show as QR code */
    bolt11: string;
    /** Amount of sats being purchased */
    amountSats: number;
    /** Unix timestamp when invoice expires */
    expiresAtUnix: number;
    /** Always "pending" on creation */
    status: 'pending';
}

export interface PurchaseStatus {
    purchaseId: string;
    /** "pending" | "settling" | "settled" | "expired" */
    status: 'pending' | 'settling' | 'settled' | 'expired';
    amountSats: number;
    /** Available after settlement */
    newBalanceSats?: number;
    bolt11: string;
}

export class BillingClient {
    private readonly baseUrl: string;
    private readonly headers: Record<string, string>;

    constructor(config: BillingClientConfig) {
        if (!config.jwt) throw new Error('LiveAuth Billing: jwt is required');
        this.baseUrl = config.baseUrl ?? 'https://api.liveauth.app';
        this.headers = {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${config.jwt}`
        };
    }

    /**
     * Create a Lightning invoice to purchase L402 credits.
     *
     * @param amountSats  Amount of sats to add to balance (min 10, max 100,000)
     * @param projectId  Optional project ID. Defaults to developer's active project.
     * @returns Purchase result with Bolt11 invoice to show as QR
     *
     * @example
     * ```ts
     * const billing = new BillingClient({ jwt: developerJwt });
     * const purchase = await billing.purchaseCredits({ amountSats: 1000 });
     * // Show bolt11 as QR code
     * lnurlPayQR(purchase.bolt11);
     * ```
     */
    async purchaseCredits(opts: {
        amountSats: number;
        projectId?: string;
    }): Promise<PurchaseResult> {
        const { amountSats, projectId } = opts;

        if (amountSats < 10) throw new Error('Minimum purchase is 10 sats');
        if (amountSats > 100_000) throw new Error('Maximum purchase is 100,000 sats at a time');

        const body: Record<string, unknown> = { amountSats };
        if (projectId) body.projectId = projectId;

        const res = await fetch(`${this.baseUrl}/api/billing/purchase`, {
            method: 'POST',
            headers: this.headers,
            body: JSON.stringify(body)
        });

        if (!res.ok) {
            const err = await res.json().catch(() => ({ error: 'Unknown error' }));
            throw new Error(err.error ?? `HTTP ${res.status}`);
        }

        const json = await res.json();
        return {
            purchaseId: json.purchaseId,
            bolt11: json.bolt11,
            amountSats: json.amountSats,
            expiresAtUnix: json.expiresAtUnix,
            status: json.status
        };
    }

    /**
     * Poll for invoice payment status.
     * Call this after showing the QR code.
     *
     * @param purchaseId  From purchaseCredits() result
     * @param opts.pollIntervalMs  How often to check (default 2000ms)
     * @param opts.timeoutMs       Max time to wait (default 10 min)
     * @returns Final purchase status (settled or expired)
     *
     * @example
     * ```ts
     * const status = await billing.getPurchaseStatus(purchase.purchaseId, {
     *   pollIntervalMs: 2000,
     *   timeoutMs: 600_000, // 10 min
     * });
     * if (status.status === 'settled') {
     *   console.log('New balance:', status.newBalanceSats, 'sats');
     * }
     * ```
     */
    async getPurchaseStatus(
        purchaseId: string,
        opts: {
            pollIntervalMs?: number;
            timeoutMs?: number;
        } = {}
    ): Promise<PurchaseStatus> {
        const { pollIntervalMs = 2000, timeoutMs = 600_000 } = opts;
        const deadline = Date.now() + timeoutMs;

        while (Date.now() < deadline) {
            const res = await fetch(`${this.baseUrl}/api/billing/purchase/${purchaseId}`, {
                headers: this.headers
            });

            if (!res.ok) {
                const err = await res.json().catch(() => ({ error: 'Unknown error' }));
                throw new Error(err.error ?? `HTTP ${res.status}`);
            }

            const json = await res.json();

            // Terminal states
            if (json.status === 'settled') {
                return {
                    purchaseId: json.purchaseId,
                    status: 'settled',
                    amountSats: json.amountSats,
                    newBalanceSats: json.newBalanceSats ?? undefined,
                    bolt11: json.bolt11
                };
            }

            if (json.status === 'expired') {
                return {
                    purchaseId: json.purchaseId,
                    status: 'expired',
                    amountSats: json.amountSats,
                    bolt11: json.bolt11
                };
            }

            await sleep(pollIntervalMs);
        }

        throw new Error('Purchase poll timed out');
    }

    /**
     * Check L402 balance and today's usage for the developer's active project.
     *
     * @example
     * ```ts
     * const usage = await billing.getUsage();
     * console.log('L402 balance:', usage.l402BalanceSats, 'sats');
     * console.log('Calls today:', usage.callsUsedToday);
     * ```
     */
    async getUsage(): Promise<{
        l402BalanceSats: number;
        callsUsedToday: number;
        satsUsedToday: number;
        freeDailyLimitSats: number;
        freeDailyLimitCalls: number;
    }> {
        const res = await fetch(`${this.baseUrl}/api/billing/usage`, {
            headers: this.headers
        });

        if (!res.ok) {
            const err = await res.json().catch(() => ({ error: 'Unknown error' }));
            throw new Error(err.error ?? `HTTP ${res.status}`);
        }

        const json = await res.json();
        return {
            l402BalanceSats: json.l402BalanceSats,
            callsUsedToday: json.callsUsedToday,
            satsUsedToday: json.satsUsedToday,
            freeDailyLimitSats: json.freeDailyLimitSats,
            freeDailyLimitCalls: json.freeDailyLimitCalls
        };
    }
}

const sleep = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));
