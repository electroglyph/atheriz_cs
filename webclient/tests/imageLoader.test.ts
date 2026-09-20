import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('chafa-wasm', () => ({ default: vi.fn() }));

import Chafa from 'chafa-wasm';
import { convertImageToAnsi } from '../src/utils/imageLoader';
import { DEFAULT_CHAFA_OPTIONS } from '../src/utils/chafaDefaults';

const factory = Chafa as unknown as ReturnType<typeof vi.fn>;

type ImageToAnsiCb = (err: Error | null, result: { ansi: string } | null) => void;

function stubInstance(impl: (cb: ImageToAnsiCb) => void) {
  return {
    imageToAnsi: vi.fn((_buffer: ArrayBuffer, _opts: Record<string, unknown>, cb: ImageToAnsiCb) => {
      impl(cb);
    }),
  };
}

beforeEach(() => {
  factory.mockReset();
});

describe('convertImageToAnsi calls the chafa factory without options', () => {
  it('passes no arguments: chafa-wasm@0.3.3 ignores locateFile and would fetch unhashed <bundleDir>/chafa.wasm', async () => {
    factory.mockResolvedValue(stubInstance(cb => cb(null, { ansi: 'x' })));
    await convertImageToAnsi(new ArrayBuffer(8), 80, 25, DEFAULT_CHAFA_OPTIONS);
    expect(factory).toHaveBeenCalledTimes(1);
    expect(factory).toHaveBeenCalledWith();
  });
});

describe('convertImageToAnsi forwards conversion options', () => {
  it('merges config, dimensions, and pixel size into imageToAnsi', async () => {
    const instance = stubInstance(cb => cb(null, { ansi: 'x' }));
    factory.mockResolvedValue(instance);
    const buffer = new ArrayBuffer(8);
    await convertImageToAnsi(buffer, 80, 25, DEFAULT_CHAFA_OPTIONS, 640, 480);
    expect(instance.imageToAnsi).toHaveBeenCalledTimes(1);
    const [actualBuffer, opts] = instance.imageToAnsi.mock.calls[0] as unknown as [
      ArrayBuffer,
      Record<string, unknown>,
    ];
    expect(actualBuffer).toBe(buffer);
    expect(opts).toMatchObject({
      ...DEFAULT_CHAFA_OPTIONS,
      width: 80,
      height: 25,
      pixelsWidth: 640,
      pixelsHeight: 480,
    });
  });

  it('falls back to the config height when height is 0', async () => {
    const instance = stubInstance(cb => cb(null, { ansi: 'x' }));
    factory.mockResolvedValue(instance);
    await convertImageToAnsi(new ArrayBuffer(8), 80, 0, DEFAULT_CHAFA_OPTIONS);
    const [, opts] = instance.imageToAnsi.mock.calls[0] as unknown as [
      ArrayBuffer,
      Record<string, unknown>,
    ];
    expect(opts['height']).toBe(DEFAULT_CHAFA_OPTIONS.height);
  });
});

describe('convertImageToAnsi resolves the conversion result', () => {
  it('resolves the ansi string on success', async () => {
    factory.mockResolvedValue(stubInstance(cb => cb(null, { ansi: 'ansi-art' })));
    await expect(
      convertImageToAnsi(new ArrayBuffer(8), 80, 25, DEFAULT_CHAFA_OPTIONS),
    ).resolves.toBe('ansi-art');
  });

  it('resolves empty string when the result is null', async () => {
    factory.mockResolvedValue(stubInstance(cb => cb(null, null)));
    await expect(
      convertImageToAnsi(new ArrayBuffer(8), 80, 25, DEFAULT_CHAFA_OPTIONS),
    ).resolves.toBe('');
  });

  it('rejects when imageToAnsi reports an error', async () => {
    const failure = new Error('conversion failed');
    factory.mockResolvedValue(stubInstance(cb => cb(failure, null)));
    await expect(
      convertImageToAnsi(new ArrayBuffer(8), 80, 25, DEFAULT_CHAFA_OPTIONS),
    ).rejects.toBe(failure);
  });
});
