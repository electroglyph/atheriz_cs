import Chafa from 'chafa-wasm';
import { ChafaConfig } from './chafaDefaults';

interface ChafaInstance {
    imageToAnsi(
        buffer: ArrayBuffer,
        opts: Record<string, unknown>,
        cb: (err: Error | null, result: { ansi: string } | null) => void,
    ): void;
}

type ChafaFactory = () => Promise<ChafaInstance>;

export async function convertImageToAnsi(
    buffer: ArrayBuffer, 
    width: number, 
    height: number, 
    options: ChafaConfig,
    pixelsWidth?: number,
    pixelsHeight?: number
): Promise<string> {
    // Note: Chafa-wasm's default export resolves to the emscripten module wrapper.
    // It MUST be called with no options. chafa-wasm@0.3.3 ignores a passed
    // locateFile's return value — its factory resolves the wasm URL as
    // `opts.locateFile ? <bundleDir> + "chafa.wasm" : new URL("chafa.wasm",
    // import.meta.url)` (node_modules/chafa-wasm/dist/chafa.js), so passing
    // locateFile makes it fetch the unhashed <bundleDir>/chafa.wasm, which
    // Vite never emits (only hashed assets/chafa-*.wasm), 404ing with an
    // empty Content-Type ("unsupported MIME type ''" -> Aborted). With no
    // options the Vite-rewritten hashed URL is used. Do NOT re-add
    // locateFile without re-verifying against the bundled factory.
    const chafa = await (Chafa as unknown as ChafaFactory)();

    return new Promise((resolve, reject) => {
        chafa.imageToAnsi(buffer, {
            ...options,
            pixelsWidth,
            pixelsHeight,
            width: width,
            height: height || Number(options.height),
        }, (err: Error | null, result: { ansi: string } | null) => {
            if (err) reject(err);
            else resolve(result ? result.ansi : '');
        });
    });
}
