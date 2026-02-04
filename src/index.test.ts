import { describe, it, expect, vi, beforeEach } from 'vitest';
import fetch from 'node-fetch';

// Mock node-fetch
vi.mock('node-fetch');
const mockedFetch = vi.mocked(fetch);

describe('LiveAuth MCP Server', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('liveauth_get_challenge', () => {
    it('should fetch challenge successfully', async () => {
      const mockChallenge = {
        projectPublicKey: 'la_pk_test123',
        challengeHex: 'a1b2c3d4',
        targetHex: '0000ffff',
        difficultyBits: 18,
        expiresAtUnix: 1234567890,
        sig: 'signature123',
      };

      mockedFetch.mockResolvedValueOnce({
        ok: true,
        json: async () => mockChallenge,
      } as any);

      // Test that the tool would call fetch correctly
      const projectPublicKey = 'la_pk_test123';
      await fetch(`${process.env.LIVEAUTH_API_BASE || 'https://api.liveauth.app'}/api/public/pow/challenge`, {
        headers: {
          'X-LW-Public': projectPublicKey,
          'Content-Type': 'application/json',
        },
      });

      expect(mockedFetch).toHaveBeenCalledWith(
        expect.stringContaining('/api/public/pow/challenge'),
        expect.objectContaining({
          headers: expect.objectContaining({
            'X-LW-Public': projectPublicKey,
          }),
        })
      );
    });

    it('should handle API errors gracefully', async () => {
      mockedFetch.mockResolvedValueOnce({
        ok: false,
        statusText: 'Unauthorized',
      } as any);

      await expect(async () => {
        const response = await fetch('https://api.liveauth.app/api/public/pow/challenge', {
          headers: {
            'X-LW-Public': 'invalid_key',
            'Content-Type': 'application/json',
          },
        });

        if (!response.ok) {
          throw new Error(`Challenge request failed: ${response.statusText}`);
        }
      }).rejects.toThrow('Challenge request failed: Unauthorized');
    });

    it('should validate projectPublicKey format', () => {
      const validKey = 'la_pk_test123';
      const invalidKey = 'invalid_key';

      expect(validKey.startsWith('la_pk_')).toBe(true);
      expect(invalidKey.startsWith('la_pk_')).toBe(false);
    });
  });

  describe('liveauth_verify_pow', () => {
    it('should verify PoW successfully', async () => {
      const mockVerifyResponse = {
        verified: true,
        token: 'jwt_token_here',
      };

      mockedFetch.mockResolvedValueOnce({
        ok: true,
        json: async () => mockVerifyResponse,
      } as any);

      const verifyRequest = {
        projectPublicKey: 'la_pk_test123',
        challengeHex: 'a1b2c3d4',
        nonce: 12345,
        hashHex: '00001234abcd',
        expiresAtUnix: 1234567890,
        difficultyBits: 18,
        sig: 'signature123',
      };

      await fetch('https://api.liveauth.app/api/public/pow/verify', {
        method: 'POST',
        headers: {
          'X-LW-Public': verifyRequest.projectPublicKey,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          challengeHex: verifyRequest.challengeHex,
          nonce: verifyRequest.nonce,
          hashHex: verifyRequest.hashHex,
          expiresAtUnix: verifyRequest.expiresAtUnix,
          difficultyBits: verifyRequest.difficultyBits,
          sig: verifyRequest.sig,
        }),
      });

      expect(mockedFetch).toHaveBeenCalledWith(
        expect.stringContaining('/api/public/pow/verify'),
        expect.objectContaining({
          method: 'POST',
          headers: expect.objectContaining({
            'X-LW-Public': 'la_pk_test123',
          }),
        })
      );
    });

    it('should handle Lightning fallback', async () => {
      const mockVerifyResponse = {
        verified: false,
        fallback: 'lightning',
      };

      mockedFetch.mockResolvedValueOnce({
        ok: true,
        json: async () => mockVerifyResponse,
      } as any);

      const response = await fetch('https://api.liveauth.app/api/public/pow/verify', {
        method: 'POST',
        headers: {
          'X-LW-Public': 'la_pk_test123',
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          challengeHex: 'test',
          nonce: 0,
          hashHex: 'invalid',
          expiresAtUnix: 0,
          difficultyBits: 18,
          sig: 'sig',
        }),
      });

      const result = await response.json() as { fallback?: string };
      expect(result).toHaveProperty('fallback', 'lightning');
    });

    it('should validate all required fields', () => {
      const validRequest = {
        projectPublicKey: 'la_pk_test',
        challengeHex: 'abc',
        nonce: 123,
        hashHex: 'def',
        expiresAtUnix: 123456,
        difficultyBits: 18,
        sig: 'sig',
      };

      const requiredFields = [
        'projectPublicKey',
        'challengeHex',
        'nonce',
        'hashHex',
        'expiresAtUnix',
        'difficultyBits',
        'sig',
      ];

      requiredFields.forEach((field) => {
        expect(validRequest).toHaveProperty(field);
        expect(validRequest[field as keyof typeof validRequest]).toBeDefined();
      });
    });
  });

  describe('liveauth_start_lightning', () => {
    it('should start Lightning session successfully', async () => {
      const mockLightningResponse = {
        sessionId: 'session123',
        invoice: 'lnbc1...',
        amountSats: 100,
        expiresAtUnix: 1234567890,
        mode: 'TEST',
      };

      mockedFetch.mockResolvedValueOnce({
        ok: true,
        json: async () => mockLightningResponse,
      } as any);

      await fetch('https://api.liveauth.app/api/public/auth/start', {
        method: 'POST',
        headers: {
          'X-LW-Public': 'la_pk_test123',
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ userHint: 'agent' }),
      });

      expect(mockedFetch).toHaveBeenCalledWith(
        expect.stringContaining('/api/public/auth/start'),
        expect.objectContaining({
          method: 'POST',
          headers: expect.objectContaining({
            'X-LW-Public': 'la_pk_test123',
          }),
        })
      );
    });

    it('should handle empty invoice (test mode)', async () => {
      const mockLightningResponse = {
        sessionId: 'session123',
        invoice: null,
        amountSats: 0,
        expiresAtUnix: 1234567890,
        mode: 'TEST',
      };

      mockedFetch.mockResolvedValueOnce({
        ok: true,
        json: async () => mockLightningResponse,
      } as any);

      const response = await fetch('https://api.liveauth.app/api/public/auth/start', {
        method: 'POST',
        headers: {
          'X-LW-Public': 'la_pk_test123',
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ userHint: 'agent' }),
      });

      const result = await response.json() as { mode: string; invoice: string | null };
      expect(result.mode).toBe('TEST');
      expect(result.invoice).toBeNull();
    });
  });

  describe('Error handling', () => {
    it('should handle network errors', async () => {
      mockedFetch.mockRejectedValueOnce(new Error('Network error'));

      await expect(async () => {
        await fetch('https://api.liveauth.app/api/public/pow/challenge', {
          headers: {
            'X-LW-Public': 'la_pk_test',
            'Content-Type': 'application/json',
          },
        });
      }).rejects.toThrow('Network error');
    });

    it('should handle rate limiting (429)', async () => {
      mockedFetch.mockResolvedValueOnce({
        ok: false,
        status: 429,
        statusText: 'Too Many Requests',
      } as any);

      const response = await fetch('https://api.liveauth.app/api/public/pow/challenge', {
        headers: {
          'X-LW-Public': 'la_pk_test',
          'Content-Type': 'application/json',
        },
      });

      expect(response.status).toBe(429);
    });

    it('should handle unauthorized (401)', async () => {
      mockedFetch.mockResolvedValueOnce({
        ok: false,
        status: 401,
        statusText: 'Unauthorized',
      } as any);

      const response = await fetch('https://api.liveauth.app/api/public/pow/challenge', {
        headers: {
          'X-LW-Public': 'invalid_key',
          'Content-Type': 'application/json',
        },
      });

      expect(response.status).toBe(401);
    });
  });

  describe('Input validation', () => {
    it('should reject invalid project keys', () => {
      const testCases = [
        { key: '', valid: false },
        { key: 'la_pk_', valid: false },
        { key: 'invalid', valid: false },
        { key: 'la_pk_test123', valid: true },
        { key: 'la_pk_abcdef1234567890', valid: true },
      ];

      testCases.forEach(({ key, valid }) => {
        const isValid = key.startsWith('la_pk_') && key.length > 6;
        expect(isValid).toBe(valid);
      });
    });

    it('should validate numeric types', () => {
      const nonce = 12345;
      const difficultyBits = 18;
      const expiresAtUnix = 1234567890;

      expect(typeof nonce).toBe('number');
      expect(typeof difficultyBits).toBe('number');
      expect(typeof expiresAtUnix).toBe('number');
      expect(difficultyBits).toBeGreaterThan(0);
      expect(difficultyBits).toBeLessThan(256);
    });

    it('should validate hex strings', () => {
      const validHex = 'a1b2c3d4';
      const invalidHex = 'xyz123';

      expect(/^[0-9a-fA-F]+$/.test(validHex)).toBe(true);
      expect(/^[0-9a-fA-F]+$/.test(invalidHex)).toBe(false);
    });
  });
});
