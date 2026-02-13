import { app, BrowserWindow, ipcMain, desktopCapturer } from 'electron';
import * as path from 'path';
import { TcpSender } from './tcp-sender';

/** メインウィンドウの参照 */
let mainWindow: BrowserWindow | null = null;

/** TCP送信クライアント */
const tcpSender = new TcpSender();

/** キャプチャタイマーID */
let captureInterval: ReturnType<typeof setInterval> | null = null;

/**
 * メインウィンドウを作成
 */
function createWindow(): void {
    mainWindow = new BrowserWindow({
        width: 900,
        height: 700,
        webPreferences: {
            preload: path.join(__dirname, 'preload.js'),
            contextIsolation: true,
            nodeIntegration: false,
        },
        title: 'Sidecar MultiSender',
    });

    // 開発時はNext.js dev serverに接続、本番時はビルド済みHTMLをロード
    if (process.env.NODE_ENV === 'development') {
        mainWindow.loadURL('http://localhost:3000');
    } else {
        mainWindow.loadFile(path.join(__dirname, '..', 'out', 'index.html'));
    }

    mainWindow.on('closed', () => {
        mainWindow = null;
    });
}

/**
 * 利用可能な画面/ウィンドウソースを列挙
 */
async function getDesktopSources(): Promise<Array<{ id: string; name: string; thumbnailDataUrl: string }>> {
    const sources = await desktopCapturer.getSources({
        types: ['screen', 'window'],
        thumbnailSize: { width: 320, height: 180 },
    });

    return sources.map(source => ({
        id: source.id,
        name: source.name,
        thumbnailDataUrl: source.thumbnail.toDataURL(),
    }));
}

// ==================== IPC ハンドラー ====================

/** ソース一覧を取得 */
ipcMain.handle('sources:list', async () => {
    return getDesktopSources();
});

/** TCP接続を開始 */
ipcMain.handle('connection:connect', async (_event, host: string, port: number) => {
    try {
        await tcpSender.connect(host, port);
        return { success: true };
    } catch (error) {
        return { success: false, error: (error as Error).message };
    }
});

/** TCP接続を切断 */
ipcMain.handle('connection:disconnect', async () => {
    tcpSender.disconnect();
    if (captureInterval) {
        clearInterval(captureInterval);
        captureInterval = null;
    }
    return { success: true };
});

/** 接続状態を取得 */
ipcMain.handle('connection:status', async () => {
    return {
        connected: tcpSender.isConnected,
        framesSent: tcpSender.framesSent,
    };
});

/** JPEGフレームを送信（Rendererから呼び出し） */
ipcMain.on('frame:send', (_event, jpegBuffer: Buffer) => {
    if (tcpSender.isConnected) {
        tcpSender.sendFrame(jpegBuffer);
    }
});

// ==================== アプリケーションライフサイクル ====================

app.whenReady().then(() => {
    createWindow();

    app.on('activate', () => {
        if (BrowserWindow.getAllWindows().length === 0) {
            createWindow();
        }
    });
});

app.on('window-all-closed', () => {
    tcpSender.disconnect();
    if (captureInterval) {
        clearInterval(captureInterval);
    }
    if (process.platform !== 'darwin') {
        app.quit();
    }
});
