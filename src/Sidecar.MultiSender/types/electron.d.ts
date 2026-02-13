/**
 * Electron API の型定義
 * preload.ts で公開された API を Renderer で型安全に使用するための宣言
 */

interface DesktopSource {
    id: string;
    name: string;
    thumbnailDataUrl: string;
}

interface ConnectionResult {
    success: boolean;
    error?: string;
}

interface ConnectionStatus {
    connected: boolean;
    framesSent: number;
}

interface ElectronAPI {
    listSources: () => Promise<DesktopSource[]>;
    connect: (host: string, port: number) => Promise<ConnectionResult>;
    disconnect: () => Promise<ConnectionResult>;
    getStatus: () => Promise<ConnectionStatus>;
    sendFrame: (jpegBuffer: Uint8Array) => void;
}

declare global {
    interface Window {
        electronAPI: ElectronAPI;
    }
}

export type { DesktopSource, ConnectionResult, ConnectionStatus, ElectronAPI };
