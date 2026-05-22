import test from 'node:test';
import assert from 'node:assert/strict';
import {
  extractMetaDescription,
  extractTitle,
  htmlToText,
  isBlockedIp,
  validateHttpUrl,
  WebFetchError
} from '../web-fetch.mjs';

test('blocks local and private URL targets', () => {
  for (const url of [
    'http://localhost:3000',
    'http://127.0.0.1',
    'http://10.0.0.5',
    'http://172.16.0.1',
    'http://192.168.1.10',
    'file:///etc/passwd'
  ]) {
    assert.throws(() => validateHttpUrl(url), WebFetchError);
  }
});

test('recognizes blocked IP ranges', () => {
  assert.equal(isBlockedIp('127.0.0.1'), true);
  assert.equal(isBlockedIp('10.1.2.3'), true);
  assert.equal(isBlockedIp('172.20.1.1'), true);
  assert.equal(isBlockedIp('192.168.1.2'), true);
  assert.equal(isBlockedIp('169.254.1.2'), true);
  assert.equal(isBlockedIp('::1'), true);
  assert.equal(isBlockedIp('fd00::1'), true);
  assert.equal(isBlockedIp('fe80::1'), true);
  assert.equal(isBlockedIp('93.184.216.34'), false);
});

test('extracts title, description, and readable text', () => {
  const html = `
    <html>
      <head>
        <title>Example &amp; Test</title>
        <meta name="description" content="A short &amp; useful description">
        <style>body { display: none; }</style>
        <script>alert("skip")</script>
      </head>
      <body>
        <h1>Hello</h1>
        <p>This is <strong>text</strong>.</p>
      </body>
    </html>
  `;

  assert.equal(extractTitle(html), 'Example & Test');
  assert.equal(extractMetaDescription(html), 'A short & useful description');
  assert.match(htmlToText(html), /Hello/);
  assert.match(htmlToText(html), /This is text/);
  assert.doesNotMatch(htmlToText(html), /alert/);
});

test('allows ordinary public http and https URL shapes', () => {
  assert.equal(validateHttpUrl('https://example.com/path#fragment').toString(), 'https://example.com/path');
  assert.equal(validateHttpUrl('http://example.com:8080/').hostname, 'example.com');
});
