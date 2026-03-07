import { type LiveAuthConfig, type VerifyOptions, type LiveAuthResult } from './index.js';

/**
 * Agent authentication configuration
 */
export interface AgentAuthConfig extends LiveAuthConfig {
    /** The agent's unique identifier */
    agentId: string;
}

/**
 * Agent auth start response
 */
export interface AgentAuthStartResponse {
    sessionId: string;
    challenge: string;
    difficultyBits: number;
    expiresAtUnix: number;
}

/**
 * Agent auth verify response
 */
export interface AgentAuthVerifyResponse {
    verified: boolean;
    token?: string;
    expiresAtUnix?: number;
    error?: string;
}

/**
 * Agent auth validation response
 */
export interface AgentAuthValidateResponse {
    valid: boolean;
    agentId?: string;
    projectId?: string;
    projectName?: string;
    expiresAtUnix?: number;
}

/**
 * Agent authentication for AI agents
 * Uses PoW verification - agents compute the solution
 */
export class AgentAuth {
    private readonly config: AgentAuthConfig;
    private readonly baseUrl: string;

    constructor(config: AgentAuthConfig) {
        this.config = config;
        this.baseUrl = config.baseUrl || 'https://api.liveauth.app';
    }

    /**
     * Start agent authentication - gets PoW challenge
     */
    async start(): Promise<AgentAuthStartResponse> {
        const response = await fetch(`${this.baseUrl}/api/agent/auth/start`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-LW-Public': this.config.publicKey,
            },
            body: JSON.stringify({
                agentId: this.config.agentId,
                publicKey: this.config.apiKey || this.config.publicKey,
            }),
        });

        if (!response.ok) {
            const error = await response.json().catch(() => ({ error: 'Unknown error' }));
            throw new Error(error.error || `HTTP ${response.status}`);
        }

        return response.json();
    }

    /**
     * Verify PoW solution and get auth token
     * @param sessionId - Session ID from start()
     * @param solution - The PoW solution (challenge:nonce)
     */
    async verify(sessionId: string, solution: string): Promise<AgentAuthVerifyResponse> {
        const response = await fetch(`${this.baseUrl}/api/agent/auth/verify`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-LW-Public': this.config.publicKey,
            },
            body: JSON.stringify({
                sessionId,
                solution,
            }),
        });

        if (!response.ok) {
            const error = await response.json().catch(() => ({ error: 'Unknown error' }));
            throw new Error(error.error || `HTTP ${response.status}`);
        }

        return response.json();
    }

    /**
     * Validate an existing auth token
     */
    async validate(token: string): Promise<AgentAuthValidateResponse> {
        const response = await fetch(`${this.baseUrl}/api/agent/auth/validate`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-LW-Public': this.config.publicKey,
            },
            body: JSON.stringify({ token }),
        });

        if (!response.ok) {
            const error = await response.json().catch(() => ({ error: 'Unknown error' }));
            throw new Error(error.error || `HTTP ${response.status}`);
        }

        return response.json();
    }

    /**
     * Full auth flow - start, solve PoW, verify
     * @param powSolver - Function that takes challenge and returns solution
     */
    async authenticate(
        powSolver: (challenge: string, difficultyBits: number) => Promise<string>
    ): Promise<string> {
        // Start auth
        const { sessionId, challenge, difficultyBits } = await this.start();

        // Solve PoW
        const solution = await powSolver(challenge, difficultyBits);

        // Verify
        const result = await this.verify(sessionId, solution);

        if (!result.verified || !result.token) {
            throw new Error(result.error || 'Verification failed');
        }

        return result.token;
    }
}

/**
 * Helper to create an agent auth instance
 */
export function createAgentAuth(config: AgentAuthConfig): AgentAuth {
    return new AgentAuth(config);
}
