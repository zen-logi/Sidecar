import * as net from 'net';

/**
 * Length-Prefixed TCPプロトコルでJPEGフレームを送信するクライアント
 *
 * パケット構造:
 *   [4バイト Big Endian ペイロードサイズ] + [JPEGペイロード]
 */
export class TcpSender {
    private _socket: net.Socket | null = null;
    private _connected = false;
    private _framesSent = 0;

    /** 接続中かどうか */
    get isConnected(): boolean {
        return this._connected;
    }

    /** 送信済みフレーム数 */
    get framesSent(): number {
        return this._framesSent;
    }

    /**
     * Windows Relay Hostに接続
     * @param host 接続先IPアドレス
     * @param port 接続先ポート番号
     */
    connect(host: string, port: number): Promise<void> {
        return new Promise((resolve, reject) => {
            if (this._connected) {
                this.disconnect();
            }

            this._socket = new net.Socket();
            this._socket.setNoDelay(true);

            this._socket.connect(port, host, () => {
                this._connected = true;
                this._framesSent = 0;
                console.log(`Connected to ${host}:${port}`);
                resolve();
            });

            this._socket.on('error', (err) => {
                console.error('TCP Error:', err.message);
                if (!this._connected) {
                    reject(err);
                }
                this.cleanup();
            });

            this._socket.on('close', () => {
                console.log('Connection closed');
                this.cleanup();
            });

            // 接続タイムアウト
            this._socket.setTimeout(10000, () => {
                if (!this._connected) {
                    this._socket?.destroy();
                    reject(new Error('Connection timeout'));
                }
            });
        });
    }

    /**
     * Length-Prefixedフォーマットでフレームを送信
     * ヘッダーとペイロードを結合して1回のwriteで送信（フラグメンテーション回避）
     * @param jpegBuffer JPEG画像データ
     */
    sendFrame(jpegBuffer: Buffer): void {
        if (!this._connected || !this._socket) {
            return;
        }

        try {
            // ヘッダー(4バイト) + ペイロードを1つのバッファに結合
            const packet = Buffer.alloc(4 + jpegBuffer.length);
            packet.writeUInt32BE(jpegBuffer.length, 0);
            jpegBuffer.copy(packet, 4);

            this._socket.write(packet);
            this._framesSent++;
        } catch (err) {
            console.error('Send error:', (err as Error).message);
            this.cleanup();
        }
    }

    /**
     * 接続を切断
     */
    disconnect(): void {
        if (this._socket) {
            this._socket.destroy();
        }
        this.cleanup();
    }

    /**
     * 内部状態をクリーンアップ
     */
    private cleanup(): void {
        this._connected = false;
        this._socket = null;
    }
}
