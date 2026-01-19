# Sidecar

自宅LAN内で、ゲーム機などの映像を「遅延ゼロ」で共有するためのサイドカー・アプリケーション。

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![GitHub Release](https://img.shields.io/github/v/release/zen-logi/Sidecar?include_prereleases)](https://github.com/zen-logi/Sidecar/releases)

## 概要

Sidecar は、LAN内でキャプチャデバイス（ゲームキャプチャボードなど）の映像を低遅延で配信・視聴するためのオープンソースアプリケーションです。

### 主な特徴

- 🚀 **超低遅延**: Nagleアルゴリズム無効化、最新フレームのみ保持する設計
- 🎨 **高度な色補正**: 補正プリセット（キャプチャカードの色被り救済など）とマニュアル調整（彩度・コントラスト・明るさ）機能
- 🌍 **クロスプラットフォーム**: Windows, macOS, iOS, Android 対応（クライアント側）
- 💡 **シンプル**: MJPEG over TCP による軽量なストリーミング
- 💾 **永続化**: ウィンドウ位置や接続先、色設定を自動保存
- 📖 **OSS**: MIT ライセンスで公開

## プロジェクト構成

```
Sidecar/
├── src/
│   ├── Sidecar.Client/     # MAUI クライアントアプリ（視聴用）
│   ├── Sidecar.Host/       # Windows ホストアプリ（配信用）
│   └── Sidecar.Shared/     # 共通ライブラリ
└── Sidecar.sln
```

## 必要環境

### ホスト（配信側）

- Windows 10/11
- .NET 10.0 Runtime
- キャプチャデバイス（USBカメラ、キャプチャボードなど）

### クライアント（視聴側）

- Windows 10/11, macOS 15+, iOS 15+, または Android 5.0+
- .NET 10.0 Runtime (デスクトップの場合)

## ダウンロード

[Releases ページ](https://github.com/zen-logi/Sidecar/releases) から最新のビルドをダウンロードできます。

### ビルド済みバイナリ

| プラットフォーム | ダウンロード |
|------------------|--------------|
| Windows (Host) | `Sidecar.Host-win-x64.zip` |
| Windows (Client) | `Sidecar.Client-win-x64.zip` |

## セットアップ手順

### ソースからビルドする場合

#### 1. リポジトリのクローン

```bash
git clone https://github.com/zen-logi/Sidecar.git
cd Sidecar
```

#### 2. ビルド

```bash
dotnet restore
dotnet build
```

#### 3. ホストの起動

```bash
cd src/Sidecar.Host
dotnet run
```

起動すると、利用可能なカメラデバイスが一覧表示されます。使用するカメラのインデックスを入力してください。

```text
=================================
 Sidecar Host - MJPEG Streamer
=================================

info: Program[0]
      利用可能なカメラデバイスを検索中...

3 個のカメラデバイスが見つかりました:
  [0] Camera 0 (USB Video)
  [1] Camera 1 (Capture Card)
  [2] Camera 2 (Integrated Webcam)

使用するカメラのインデックスを入力してください: 1

info: Sidecar.Host.Services.CameraService[0]
      Selected Format: JPEG, 1920x1080 @ 60
info: Sidecar.Host.Services.CameraService[0]
      Started FlashCap on Camera 1
info: Sidecar.Host.Services.StreamServer[0]
      ストリーミングサーバーがポート 8554 で開始しました

接続先: http://<このPCのIPアドレス>:8554
Ctrl+C で終了します。

接続クライアント数: 0
```

### コマンドライン引数

```bash
# カメラインデックスを引数で指定
dotnet run -- 1

# カメラインデックスとポート番号を指定
dotnet run -- 1 9000
```

#### 4. クライアントの起動

**Windows の場合:**

```bash
cd src/Sidecar.Client
dotnet run -f net10.0-windows10.0.19041.0
```

アプリを起動したら：

1. ホストのIPアドレスを入力
2. ポート番号（デフォルト: 8554）を確認
3. 「接続」ボタンをクリック

## 設定値

設定値は `Sidecar.Shared/StreamingConstants.cs` で管理されています。

| 設定項目 | デフォルト値 | 説明 |
|---------|-------------|------|
| `DefaultPort` | 8554 | ストリーミングポート |
| `JpegQuality` | 75 | JPEG圧縮品質（0-100） |
| `ReceiveBufferSize` | 1MB | 受信バッファサイズ |
| `ConnectionTimeoutMs` | 10000 | 接続タイムアウト（ミリ秒） |

## アーキテクチャ

```
┌─────────────────────────────────────────────────────────────┐
│                     Sidecar.Host                            │
│  ┌────────────────┐       ┌─────────────────────────────┐  │
│  │  CameraService │──────►│     StreamServer            │  │
│  │  (FlashCap)    │ JPEG  │  (TCP + MJPEG Boundary)     │  │
│  └────────────────┘       └─────────────────────────────┘  │
│            │                           │                    │
│     [最新フレームのみ保持]       [NoDelay=true]            │
│     [バッファサイズ=1]                                      │
└─────────────────────────────────────────────────────────────┘
                              │
                              │ TCP (port 8554)
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    Sidecar.Client                           │
│  ┌─────────────────────────────┐   ┌────────────────────┐  │
│  │      StreamClient           │──►│   MainPage         │  │
│  │  (TCP + MJPEG Parser)       │   │  (SKCanvasView)    │  │
│  └─────────────────────────────┘   └────────────────────┘  │
│            │                                │               │
│     [NoDelay=true]                   [60fps描画ループ]      │
│     [Fast-Forward]                   [アスペクト比維持]     │
│                                      [高度なカラーフィルタ]  │
└─────────────────────────────────────────────────────────────┘
```

## 技術スタック

| コンポーネント | 技術 |
|---------------|------|
| ホスト | .NET 10.0, FlashCap, OpenCvSharp4 |
| クライアント | .NET MAUI, SkiaSharp, CommunityToolkit.Mvvm |
| ストリーミング | MJPEG over TCP |
| CI/CD | GitHub Actions |

## ライセンス

MIT License - 詳細は [LICENSE](LICENSE) を参照してください。

## 貢献

プルリクエストを歓迎します！バグ報告や機能要望は [Issue](https://github.com/zen-logi/Sidecar/issues) でお願いします。
