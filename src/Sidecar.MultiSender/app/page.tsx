'use client';

import { useState, useRef, useCallback, useEffect } from 'react';
import type { DesktopSource } from '../types/electron.d';

/**
 * Sidecar MultiSender メイン画面
 * ソース選択、プレビュー、接続制御を統合したシングルページUI
 */
export default function Home() {
    // 接続関連の状態
    const [host, setHost] = useState('192.168.1.1');
    const [port, setPort] = useState(9000);
    const [connected, setConnected] = useState(false);
    const [connecting, setConnecting] = useState(false);
    const [error, setError] = useState<string | null>(null);

    // キャプチャ関連の状態
    const [sources, setSources] = useState<DesktopSource[]>([]);
    const [selectedSourceId, setSelectedSourceId] = useState<string | null>(null);
    const [capturing, setCapturing] = useState(false);
    const [framesSent, setFramesSent] = useState(0);
    const [fps, setFps] = useState(0);

    // Refs
    const videoRef = useRef<HTMLVideoElement>(null);
    const canvasRef = useRef<HTMLCanvasElement>(null);
    const captureIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
    const streamRef = useRef<MediaStream | null>(null);
    const fpsCounterRef = useRef(0);

    /**
     * 画面/ウィンドウソース一覧をリフレッシュ
     */
    const refreshSources = useCallback(async () => {
        if (typeof window === 'undefined' || !window.electronAPI) return;
        try {
            const result = await window.electronAPI.listSources();
            setSources(result);
        } catch (err) {
            console.error('Failed to list sources:', err);
        }
    }, []);

    // 初回マウント時にソース一覧を取得
    useEffect(() => {
        refreshSources();
    }, [refreshSources]);

    // FPSカウンター
    useEffect(() => {
        const interval = setInterval(() => {
            setFps(fpsCounterRef.current);
            fpsCounterRef.current = 0;
        }, 1000);
        return () => clearInterval(interval);
    }, []);

    // ステータスポーリング
    useEffect(() => {
        if (!connected) return;
        const interval = setInterval(async () => {
            if (typeof window !== 'undefined' && window.electronAPI) {
                const status = await window.electronAPI.getStatus();
                setFramesSent(status.framesSent);
                if (!status.connected) {
                    setConnected(false);
                    stopCapture();
                }
            }
        }, 1000);
        return () => clearInterval(interval);
    }, [connected]);

    /**
     * 選択されたソースのキャプチャを開始
     */
    const startCapture = useCallback(async (sourceId: string) => {
        if (typeof navigator === 'undefined') return;

        try {
            // getUserMediaのconstraintsにsourceIdを指定してキャプチャ開始
            const stream = await (navigator.mediaDevices as any).getUserMedia({
                audio: false,
                video: {
                    mandatory: {
                        chromeMediaSource: 'desktop',
                        chromeMediaSourceId: sourceId,
                        maxFrameRate: 30,
                    },
                },
            });

            streamRef.current = stream;

            if (videoRef.current) {
                videoRef.current.srcObject = stream;
                await videoRef.current.play();
            }

            setCapturing(true);

            // キャプチャループ開始（30fps）
            captureIntervalRef.current = setInterval(() => {
                captureAndSendFrame();
            }, 33);
        } catch (err) {
            console.error('Capture error:', err);
            setError('画面キャプチャの開始に失敗しました');
        }
    }, []);

    /**
     * キャプチャを停止
     */
    const stopCapture = useCallback(() => {
        if (captureIntervalRef.current) {
            clearInterval(captureIntervalRef.current);
            captureIntervalRef.current = null;
        }
        if (streamRef.current) {
            streamRef.current.getTracks().forEach(track => track.stop());
            streamRef.current = null;
        }
        if (videoRef.current) {
            videoRef.current.srcObject = null;
        }
        setCapturing(false);
    }, []);

    /**
     * 1フレームをキャプチャしてJPEGに変換、メインプロセスへ送信
     */
    const captureAndSendFrame = useCallback(() => {
        const video = videoRef.current;
        const canvas = canvasRef.current;
        if (!video || !canvas || !window.electronAPI) return;

        const ctx = canvas.getContext('2d');
        if (!ctx) return;

        // Canvas サイズをビデオに合わせる
        canvas.width = video.videoWidth;
        canvas.height = video.videoHeight;

        // ビデオフレームをCanvasに描画
        ctx.drawImage(video, 0, 0);

        // CanvasからJPEGに変換
        canvas.toBlob(
            (blob) => {
                if (!blob) return;
                blob.arrayBuffer().then((buffer) => {
                    window.electronAPI.sendFrame(new Uint8Array(buffer));
                    fpsCounterRef.current++;
                });
            },
            'image/jpeg',
            0.75 // JPEG品質 75%
        );
    }, []);

    /**
     * Windows Relay Hostに接続
     */
    const handleConnect = useCallback(async () => {
        if (!window.electronAPI) return;
        setConnecting(true);
        setError(null);

        try {
            const result = await window.electronAPI.connect(host, port);
            if (result.success) {
                setConnected(true);
                // ソースが選択されていれば自動でキャプチャ開始
                if (selectedSourceId) {
                    await startCapture(selectedSourceId);
                }
            } else {
                setError(result.error || '接続に失敗しました');
            }
        } catch (err) {
            setError((err as Error).message);
        } finally {
            setConnecting(false);
        }
    }, [host, port, selectedSourceId, startCapture]);

    /**
     * 切断
     */
    const handleDisconnect = useCallback(async () => {
        if (!window.electronAPI) return;
        stopCapture();
        await window.electronAPI.disconnect();
        setConnected(false);
        setFramesSent(0);
        setFps(0);
    }, [stopCapture]);

    /**
     * ソース選択時の処理
     */
    const handleSourceSelect = useCallback(async (sourceId: string) => {
        setSelectedSourceId(sourceId);
        // 既にキャプチャ中なら切り替え
        if (capturing) {
            stopCapture();
            await startCapture(sourceId);
        } else if (connected) {
            await startCapture(sourceId);
        }
    }, [capturing, connected, stopCapture, startCapture]);

    // 接続状態に応じたバッジ
    const statusBadge = connected
        ? capturing
            ? { className: 'sending', label: '送信中', pulse: true }
            : { className: 'connected', label: '接続済み', pulse: false }
        : { className: 'disconnected', label: '未接続', pulse: false };

    return (
        <div className="app">
            {/* ヘッダー */}
            <header className="header">
                <h1>Sidecar MultiSender</h1>
                <span className={`status-badge ${statusBadge.className}`}>
                    <span className={`status-dot ${statusBadge.pulse ? 'pulse' : ''}`} />
                    {statusBadge.label}
                </span>
            </header>

            {/* メインコンテンツ */}
            <main className="content">
                {/* プレビュー */}
                <section className="preview-section">
                    <div className="preview-header">プレビュー</div>
                    <div className="preview-area">
                        {capturing ? (
                            <video ref={videoRef} muted playsInline />
                        ) : (
                            <div className="preview-placeholder">
                                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                                    <rect x="2" y="3" width="20" height="14" rx="2" />
                                    <path d="M8 21h8M12 17v4" />
                                </svg>
                                <span>ソースを選択してください</span>
                            </div>
                        )}
                        <video ref={videoRef} muted playsInline style={{ display: capturing ? 'block' : 'none' }} />
                    </div>
                    <canvas ref={canvasRef} className="hidden-canvas" />
                </section>

                {/* サイドパネル */}
                <aside className="side-panel">
                    {/* 接続設定 */}
                    <div className="panel-card">
                        <h3>接続先</h3>
                        <div className="form-group">
                            <label>Windows Host</label>
                            <div className="form-row">
                                <input
                                    type="text"
                                    value={host}
                                    onChange={(e) => setHost(e.target.value)}
                                    placeholder="IPアドレス"
                                    disabled={connected}
                                />
                                <input
                                    type="number"
                                    value={port}
                                    onChange={(e) => setPort(Number(e.target.value))}
                                    disabled={connected}
                                />
                            </div>
                        </div>
                        {error && (
                            <div style={{ color: 'var(--danger)', fontSize: '13px', marginBottom: '8px' }}>
                                {error}
                            </div>
                        )}
                        {connected ? (
                            <button className="btn btn-danger" onClick={handleDisconnect}>
                                切断
                            </button>
                        ) : (
                            <button
                                className="btn btn-primary"
                                onClick={handleConnect}
                                disabled={connecting || !host}
                            >
                                {connecting ? '接続中...' : '接続'}
                            </button>
                        )}
                    </div>

                    {/* ソース選択 */}
                    <div className="panel-card" style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0 }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px' }}>
                            <h3 style={{ margin: 0 }}>ソース</h3>
                            <button className="btn btn-outline" style={{ width: 'auto', padding: '4px 10px', fontSize: '12px' }} onClick={refreshSources}>
                                更新
                            </button>
                        </div>
                        <div className="source-list">
                            {sources.map((source) => (
                                <div
                                    key={source.id}
                                    className={`source-item ${selectedSourceId === source.id ? 'selected' : ''}`}
                                    onClick={() => handleSourceSelect(source.id)}
                                >
                                    <img src={source.thumbnailDataUrl} alt={source.name} />
                                    <span className="source-name">{source.name}</span>
                                </div>
                            ))}
                            {sources.length === 0 && (
                                <div style={{ color: 'var(--text-muted)', fontSize: '13px', textAlign: 'center', padding: '20px' }}>
                                    ソースが見つかりません
                                </div>
                            )}
                        </div>
                    </div>

                    {/* 統計情報 */}
                    {connected && (
                        <div className="panel-card">
                            <h3>統計</h3>
                            <div className="stats">
                                <div className="stat-item">
                                    <div className="stat-value">{fps}</div>
                                    <div className="stat-label">FPS</div>
                                </div>
                                <div className="stat-item">
                                    <div className="stat-value">{framesSent.toLocaleString()}</div>
                                    <div className="stat-label">送信済み</div>
                                </div>
                            </div>
                        </div>
                    )}
                </aside>
            </main>
        </div>
    );
}
