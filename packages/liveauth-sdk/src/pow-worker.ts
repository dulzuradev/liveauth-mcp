import {
  hashCandidate,
  hexToBytes,
  isAtOrBelowTarget
} from './pow.js';

interface SolveMessage {
  type: 'solve';
  projectPublicKey: string;
  challengeId: string;
  targetHex: string;
}

interface WorkerScope {
  addEventListener(
    type: 'message',
    listener: (event: MessageEvent<unknown>) => void
  ): void;
  postMessage(message: unknown): void;
}

const scope = globalThis as unknown as WorkerScope;
const batchSize = 128;
const progressInterval = 4_096;

scope.addEventListener('message', async event => {
  try {
    const input = validateMessage(event.data);
    const target = hexToBytes(input.targetHex);
    const startedAt = performance.now();
    let nonce = 0;
    let nextProgressAt = progressInterval;

    while (Number.isSafeInteger(nonce)) {
      const hashes = await Promise.all(
        Array.from(
          { length: batchSize },
          (_, index) => hashCandidate(
            input.projectPublicKey,
            input.challengeId,
            nonce + index
          )
        )
      );

      for (let index = 0; index < hashes.length; index++) {
        const hash = hashes[index]!;
        if (isAtOrBelowTarget(hash.bytes, target)) {
          scope.postMessage({
            type: 'solved',
            nonce: nonce + index,
            hashHex: hash.hex,
            attempts: nonce + index + 1,
            elapsedMilliseconds: Math.round(
              performance.now() - startedAt
            )
          });
          return;
        }
      }

      nonce += batchSize;
      if (nonce >= nextProgressAt) {
        scope.postMessage({
          type: 'progress',
          attempts: nonce,
          nonce,
          elapsedMilliseconds: Math.round(
            performance.now() - startedAt
          )
        });
        nextProgressAt += progressInterval;
      }
    }

    throw new Error('The proof-of-work nonce exceeded the safe range.');
  } catch (error) {
    scope.postMessage({
      type: 'error',
      error: error instanceof Error
        ? error.message
        : 'Proof-of-work worker error.'
    });
  }
});

function validateMessage(value: unknown): SolveMessage {
  if (value == null || typeof value !== 'object')
    throw new Error('Invalid proof-of-work worker payload.');

  const message = value as Partial<SolveMessage>;
  if (
    message.type !== 'solve' ||
    typeof message.projectPublicKey !== 'string' ||
    message.projectPublicKey.length === 0 ||
    typeof message.challengeId !== 'string' ||
    !/^[a-f0-9]{32}$/.test(message.challengeId) ||
    typeof message.targetHex !== 'string' ||
    !/^[a-f0-9]{64}$/.test(message.targetHex)
  ) {
    throw new Error('Invalid proof-of-work worker payload.');
  }
  return message as SolveMessage;
}
