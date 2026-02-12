using FlashCap;
using Microsoft.Extensions.Logging;
using Sidecar.Host.Interfaces;
using Sidecar.Shared;
using Sidecar.Shared.Models;

namespace Sidecar.Host.Services;

/// <summary>
/// FlashCapを使用したカメラキャプチャサービス
/// </summary>
/// <remarks>
/// <see cref="CameraService"/> クラスの新しいインスタンスを初期化
/// </remarks>
/// <param name="logger">ロガー</param>
/// <param name="formatInterceptor">フォーマット決定サービス</param>
/// <param name="gpuPipeline">GPU処理パイプライン</param>
public sealed class CameraService(
    ILogger<CameraService> logger,
    IFormatInterceptor formatInterceptor,
    IGpuPipelineService gpuPipeline) : ICameraService, IDisposable {
    private CaptureDevice? _captureDevice;
    private VideoCharacteristics? _characteristics;
    private byte[]? _latestFrame;
    private long _frameNumber;
    private bool _disposed;


    /// <inheritdoc/>
    public event EventHandler<FrameEventArgs>? FrameAvailable;

    /// <inheritdoc/>
    public bool IsCapturing => _captureDevice is not null;

    /// <inheritdoc/>
    public IReadOnlyList<CameraDevice> GetAvailableDevices() {

        var devices = new List<CameraDevice>();
        var descriptors = new CaptureDevices();
        var index = 0;
        foreach (var descriptor in descriptors.EnumerateDescriptors()) {
            // FlashCap uses unique IDs, but we'll map to index for compatibility with existing UI
            devices.Add(new CameraDevice(index++, descriptor.Name));
        }
        return devices.AsReadOnly();
    }

    /// <inheritdoc/>
    public async Task StartCaptureAsync(int deviceIndex, CancellationToken cancellationToken = default) {
        if (_captureDevice is not null)
            throw new InvalidOperationException("Capturing already started.");

        var descriptors = new CaptureDevices();
        var descriptorList = descriptors.EnumerateDescriptors().ToList();

        if (deviceIndex < 0 || deviceIndex >= descriptorList.Count) {
            throw new ArgumentOutOfRangeException(nameof(deviceIndex));
        }

        var targetDescriptor = descriptorList[deviceIndex];

        // 優先度順に候補を列挙: JPEG > YUYV > NV12 > その他、同形式内は高解像度優先
        var candidates = targetDescriptor.Characteristics
            .OrderByDescending(c => c.PixelFormat == PixelFormats.JPEG)
            .ThenByDescending(c => c.PixelFormat == PixelFormats.YUYV)
            .ThenByDescending(c => c.Width * c.Height)
            .ToList();

        if (candidates.Count == 0)
            throw new InvalidOperationException("デバイスの有効な特性が見つからない");

        // GPU処理パイプラインを初期化
        gpuPipeline.Initialize();

        // フォールバック付きでデバイスをオープン (設定不可能な候補はスキップ)
        foreach (var candidate in candidates) {
            try {
                logger.LogInformation(
                    "フォーマット試行: {Format}, {Width}x{Height} @ {Fps}",
                    candidate.PixelFormat, candidate.Width, candidate.Height, candidate.FramesPerSecond);

                _captureDevice = await targetDescriptor.OpenAsync(candidate, pixelBufferArrived: OnPixelBufferArrived, transcodeFormat: TranscodeFormats.DoNotTranscode, ct: cancellationToken);
                await _captureDevice.StartAsync(cancellationToken);

                // 成功した場合のみ保持
                _characteristics = candidate;
                formatInterceptor.DetermineFormat(candidate.PixelFormat.ToString());

                logger.LogInformation("Started FlashCap on {DeviceName} ({Format} {Width}x{Height})",
                    targetDescriptor.Name, candidate.PixelFormat, candidate.Width, candidate.Height);
                return;
            } catch (ArgumentException ex) {
                logger.LogWarning("フォーマット設定失敗 ({Format} {Width}x{Height}): {Message}",
                    candidate.PixelFormat, candidate.Width, candidate.Height, ex.Message);
                // 失敗した場合、次の候補へフォールバック
                if (_captureDevice is not null) {
                    _captureDevice.Dispose();
                    _captureDevice = null;
                }
            }
        }

        throw new InvalidOperationException("すべてのフォーマット候補が失敗した");
    }

    /// <summary>
    /// カメラからフレームデータが到着した際のコールバック処理
    /// </summary>
    /// <param name="scope">ピクセルバッファのスコープ</param>
    private void OnPixelBufferArrived(PixelBufferScope scope) {
        try {
            byte[] jpegData;

            // 1. MJPG/JPEGの場合、高速パス (そのままコピー)
            if (_characteristics?.PixelFormat == PixelFormats.JPEG) {
                jpegData = scope.Buffer.CopyImage();
            } else {
                // 2. YUY2/NV12/RGBの場合、GPU処理パイプラインで変換
                var imageData = scope.Buffer.CopyImage();
                var width = _characteristics?.Width ?? 0;
                var height = _characteristics?.Height ?? 0;

                // BMPヘッダーの検出と除去
                imageData = StripBmpHeader(imageData, width, height);

                // RAWバイトダンプ (診断用)
                if (formatInterceptor.DumpRequested) {
                    formatInterceptor.DumpRequested = false;
                    DumpRawBytes(imageData, width, height);
                }

                // CPU検証フレーム保存 (OpenCvSharp CvtColorでYUY2→BGR変換)
                if (formatInterceptor.VerifyRequested) {
                    formatInterceptor.VerifyRequested = false;
                    SaveVerifyFrames(imageData, width, height);
                }

                // Interceptorから現在のフォーマット設定を取得
                var inputFormat = formatInterceptor.InputFormat;
                var enableToneMap = formatInterceptor.EnableToneMap;

                // GPU処理実行
                jpegData = gpuPipeline.ProcessFrame(imageData, width, height, inputFormat, enableToneMap);
            }

            var frameNumber = Interlocked.Increment(ref _frameNumber);
            var frameData = new FrameData(jpegData, DateTime.UtcNow, frameNumber);

            Volatile.Write(ref _latestFrame, jpegData);
            FrameAvailable?.Invoke(this, new FrameEventArgs(frameData));
        } catch (Exception ex) {
            logger.LogError(ex, "Error processing frame");
        }
    }

    /// <inheritdoc/>
    public async Task StopCaptureAsync(CancellationToken cancellationToken = default) {
        if (_captureDevice != null) {
            await _captureDevice.StopAsync(cancellationToken);
            _captureDevice.Dispose();
            _captureDevice = null;
        }
        logger.LogInformation("キャプチャを停止");
    }

    /// <inheritdoc/>
    public byte[]? GetLatestFrame() => Volatile.Read(ref _latestFrame);

    /// <summary>
    /// OpenCvSharp CPU変換で全YUV422バリエーションを試して保存
    /// </summary>
    private void SaveVerifyFrames(byte[] yuvData, int width, int height) {
        try {
            var outputDir = AppDomain.CurrentDomain.BaseDirectory;

            // RAW YUVデータをそのまま保存
            var rawPath = Path.Combine(outputDir, "verify_raw.bin");
            File.WriteAllBytes(rawPath, yuvData);
            Console.WriteLine($"RAWデータ保存: {rawPath} ({yuvData.Length} bytes)");

            using var yuvMat = new OpenCvSharp.Mat(height, width, OpenCvSharp.MatType.CV_8UC2);
            System.Runtime.InteropServices.Marshal.Copy(yuvData, 0, yuvMat.Data, Math.Min(yuvData.Length, width * height * 2));

            // 全YUV422変換コードを試す
            var conversions = new (string name, OpenCvSharp.ColorConversionCodes code)[] {
                ("yuy2_bgr", OpenCvSharp.ColorConversionCodes.YUV2BGR_YUY2),
                ("uyvy_bgr", OpenCvSharp.ColorConversionCodes.YUV2BGR_UYVY),
                ("yvyu_bgr", OpenCvSharp.ColorConversionCodes.YUV2BGR_YVYU),
                ("yuy2_rgb", OpenCvSharp.ColorConversionCodes.YUV2RGB_YUY2),
                ("uyvy_rgb", OpenCvSharp.ColorConversionCodes.YUV2RGB_UYVY),
                ("yvyu_rgb", OpenCvSharp.ColorConversionCodes.YUV2RGB_YVYU),
            };

            foreach (var (name, code) in conversions) {
                try {
                    using var result = new OpenCvSharp.Mat();
                    OpenCvSharp.Cv2.CvtColor(yuvMat, result, code);
                    var path = Path.Combine(outputDir, $"verify_{name}.jpg");
                    OpenCvSharp.Cv2.ImWrite(path, result);
                    Console.WriteLine($"保存: {path}");
                } catch (Exception ex) {
                    Console.WriteLine($"変換失敗 ({name}): {ex.Message}");
                }
            }

            // U/V スワップテスト: データのU,Vバイトを入れ替えてからYUY2変換
            var swapped = new byte[yuvData.Length];
            Array.Copy(yuvData, swapped, yuvData.Length);
            for (var i = 0; i < swapped.Length - 3; i += 4) {
                // YUY2: [Y0, U, Y1, V] → [Y0, V, Y1, U] (U↔V swap)
                (swapped[i + 1], swapped[i + 3]) = (swapped[i + 3], swapped[i + 1]);
            }
            using var swapMat = new OpenCvSharp.Mat(height, width, OpenCvSharp.MatType.CV_8UC2);
            System.Runtime.InteropServices.Marshal.Copy(swapped, 0, swapMat.Data, swapped.Length);
            using var swapResult = new OpenCvSharp.Mat();
            OpenCvSharp.Cv2.CvtColor(swapMat, swapResult, OpenCvSharp.ColorConversionCodes.YUV2BGR_YUY2);
            var swapPath = Path.Combine(outputDir, "verify_uv_swap.jpg");
            OpenCvSharp.Cv2.ImWrite(swapPath, swapResult);
            Console.WriteLine($"保存 (U/Vスワップ): {swapPath}");

            // 中央ピクセル値を出力
            var offset = (height / 2 * width + width / 2) * 2;
            if (offset + 3 < yuvData.Length) {
                Console.WriteLine($"\n中央バイト: [{yuvData[offset]:X2} {yuvData[offset+1]:X2} {yuvData[offset+2]:X2} {yuvData[offset+3]:X2}]");
                Console.WriteLine($"  YUY2解釈: Y0={yuvData[offset]} U={yuvData[offset+1]} Y1={yuvData[offset+2]} V={yuvData[offset+3]}");
                Console.WriteLine($"  UYVY解釈: U={yuvData[offset]} Y0={yuvData[offset+1]} V={yuvData[offset+2]} Y1={yuvData[offset+3]}");
            }

            Console.WriteLine("\n全検証ファイル保存完了！正しい色のファイル名を教えてください。");
        } catch (Exception ex) {
            Console.WriteLine($"検証フレーム保存エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// BMPヘッダーを検出して除去し、ピクセルデータのみを返す
    /// </summary>
    /// <param name="data">CopyImage()の生バッファ</param>
    /// <param name="width">フレーム幅</param>
    /// <param name="height">フレーム高さ</param>
    /// <returns>ヘッダー除去済みのピクセルデータ</returns>
    private byte[] StripBmpHeader(byte[] data, int width, int height) {
        // BMPマジック "BM" (0x42 0x4D) を検出
        if (data.Length < 54 || data[0] != 0x42 || data[1] != 0x4D)
            return data; // BMPヘッダーなし → そのまま返す

        // ピクセルデータオフセットを読み取り (バイト10-13, リトルエンディアン)
        var pixelOffset = BitConverter.ToInt32(data, 10);

        // ピクセルデータのみ抽出 (DirectShowは通常トップダウン順で配信)
        var pixelDataLength = data.Length - pixelOffset;
        var pixelData = new byte[pixelDataLength];
        Buffer.BlockCopy(data, pixelOffset, pixelData, 0, pixelDataLength);

        logger.LogDebug("BMPヘッダー除去 (offset={Offset}, pixelSize={Size})", pixelOffset, pixelDataLength);
        return pixelData;
    }

    /// <summary>
    /// RAWバッファの診断情報をコンソールに出力
    /// </summary>
    /// <param name="data">RAWバイトデータ</param>
    /// <param name="width">フレーム幅</param>
    /// <param name="height">フレーム高さ</param>
    private void DumpRawBytes(byte[] data, int width, int height) {
        Console.WriteLine("\n========== RAW FRAME DUMP ==========");
        Console.WriteLine($"バッファサイズ: {data.Length} bytes");
        Console.WriteLine($"解像度: {width}x{height}");
        Console.WriteLine($"期待サイズ (YUYV 2bpp): {width * height * 2}");
        Console.WriteLine($"期待サイズ (NV12 1.5bpp): {width * height * 3 / 2}");
        Console.WriteLine($"期待サイズ (RGB24 3bpp): {width * height * 3}");
        Console.WriteLine($"期待サイズ (BGRA 4bpp): {width * height * 4}");
        Console.WriteLine($"実際のbpp: {(double)data.Length / (width * height):F2}");

        // 先頭64バイト
        var hexLen = Math.Min(64, data.Length);
        Console.Write("先頭64バイト: ");
        for (var i = 0; i < hexLen; i++) {
            Console.Write($"{data[i]:X2} ");
            if ((i + 1) % 16 == 0) Console.Write("\n               ");
        }
        Console.WriteLine();

        // 中央付近の8バイト (YUY2想定: 4ピクセル分)
        var centerByte = (height / 2 * width + width / 2) * 2; // YUYV stride
        if (centerByte + 8 <= data.Length) {
            Console.Write($"中央ピクセル付近 (offset {centerByte}): ");
            for (var i = 0; i < 8; i++) {
                Console.Write($"{data[centerByte + i]:X2} ");
            }
            Console.WriteLine();
            Console.WriteLine($"  YUY2解釈: Y0={data[centerByte]}, U={data[centerByte + 1]}, Y1={data[centerByte + 2]}, V={data[centerByte + 3]}");
            Console.WriteLine($"  UYVY解釈: U={data[centerByte]}, Y0={data[centerByte + 1]}, V={data[centerByte + 2]}, Y1={data[centerByte + 3]}");
        }

        Console.WriteLine("====================================\n");
    }

    /// <inheritdoc/>
    public void Dispose() {
        if (_disposed)
            return;
        _captureDevice?.Dispose();
        _disposed = true;
    }
}
