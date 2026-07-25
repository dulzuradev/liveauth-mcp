export async function hashCandidate(
  publicKey: string,
  challengeId: string,
  nonce: number
): Promise<{
  bytes: Uint8Array<ArrayBuffer>;
  hex: string;
}> {
  const input = new TextEncoder().encode(
    `${publicKey}:${challengeId}:${nonce}`
  );
  const digest = await crypto.subtle.digest('SHA-256', input);
  const bytes = new Uint8Array(digest);
  return {
    bytes,
    hex: bytesToHex(bytes)
  };
}

export function hexToBytes(
  hex: string
): Uint8Array<ArrayBuffer> {
  const output = new Uint8Array(new ArrayBuffer(hex.length / 2));
  for (let index = 0; index < output.length; index++) {
    output[index] = Number.parseInt(
      hex.slice(index * 2, index * 2 + 2),
      16
    );
  }
  return output;
}

export function bytesToHex(bytes: Uint8Array): string {
  return Array.from(
    bytes,
    value => value.toString(16).padStart(2, '0')
  ).join('');
}

export function isAtOrBelowTarget(
  hash: Uint8Array,
  target: Uint8Array
): boolean {
  if (hash.length !== target.length)
    return false;
  for (let index = 0; index < hash.length; index++) {
    if (hash[index]! < target[index]!)
      return true;
    if (hash[index]! > target[index]!)
      return false;
  }
  return true;
}
