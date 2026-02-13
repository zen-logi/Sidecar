import { contextBridge, ipcRenderer } from 'electron';

/**
 * Renderer プロセスに公開する安全な API
 * contextIsolation: true 環境で window.electronAPI としてアクセス可能
 */
contextBridge.exposeInMainWorld('electronAPI', {
    /** 画面/ウィンドウソース一覧を取得 */
    listSources: (): Promise<Array<{ id: string; name: string; thumbnailDataUrl: string }>> =>
        ipcRenderer.invoke('sources:list'),

    /** Windows Relay Hostに接続 */
    connect: (host: string, port: number): Promise<{ success: boolean; error?: string }> =>
        ipcRenderer.invoke('connection:connect', host, port),

    /** 接続を切断 */
    disconnect: (): Promise<{ success: boolean }> =>
        ipcRenderer.invoke('connection:disconnect'),

    /** 接続状態を取得 */
    getStatus: (): Promise<{ connected: boolean; framesSent: number }> =>
        ipcRenderer.invoke('connection:status'),

    /** JPEGフレームをメインプロセスに送信 */
    sendFrame: (jpegBuffer: Uint8Array): void =>
        ipcRenderer.send('frame:send', Buffer.from(jpegBuffer)),
});
