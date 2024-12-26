using UnityEngine;
using TMPro;
using System;
using System.Threading.Tasks;
using Microsoft.MixedReality.SceneUnderstanding.Samples.Unity;
#if WINDOWS_UWP
using Windows.Media.Capture;
using Windows.Storage;
using Windows.Graphics.Imaging;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
#endif

public class HoloLensCamera : MonoBehaviour
{
    // UI和组件引用
    public TextMeshProUGUI statusText;         // 状态文本显示
    public VoiceInteraction voiceInteraction;  // 语音交互组件
    public UnityWsTts ttsComponent;           // 语音合成组件

    [SerializeField]
    private string imageAnalysisPrompt = "请详细描述该场景";  // AI分析提示语

#if WINDOWS_UWP
    private MediaCapture mediaCapture;              // 相机捕获组件
    private ImageEncodingProperties encodingProperties;  // 图像编码属性
    
    // 预设的图像转换参数
    private readonly BitmapTransform transform = new BitmapTransform
    {
        ScaledWidth = 1920,
        ScaledHeight = 1080,
        Rotation = BitmapRotation.None 
    };
#endif

    /// <summary>
    /// 启动时初始化相机和组件
    /// </summary>
    async void Start()
    {
        InitializeComponents();
        await InitializeCamera();
    }

    /// <summary>
    /// 初始化必要的组件和设置
    /// </summary>
    private void InitializeComponents()
    {
        // 确保主线程管理器存在
        if (MainThread.Instance == null)
        {
            new GameObject("MainThread").AddComponent<MainThread>();
        }

        // 查找或添加语音合成组件
        if (ttsComponent == null)
        {
            ttsComponent = FindObjectOfType<UnityWsTts>();
            if (ttsComponent == null)
            {
                Debug.LogError("UnityWsTts component not found!");
            }
        }

#if WINDOWS_UWP
        // 初始化JPEG编码设置
        encodingProperties = ImageEncodingProperties.CreateJpeg();
#endif
    }

    /// <summary>
    /// 初始化相机系统
    /// </summary>
    private async Task InitializeCamera()
    {
#if WINDOWS_UWP
        try
        {
            mediaCapture = new MediaCapture();
            await mediaCapture.InitializeAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Camera initialization failed: {ex.Message}");
            UpdateStatusText("photo failed");
            if (ttsComponent != null)
            {
                ttsComponent.PlayText("拍照失败，请重试");
            }
        }
#endif
    }

    /// <summary>
    /// 拍照并进行AI分析的主要方法
    /// </summary>
    public async void CapturePhoto()
    {
#if WINDOWS_UWP
        if (!ValidateCamera()) return;

        try
        {
            // 播放开始提示音
            PlayVoicePrompt("正在拍照");

            // 执行拍照操作
            var (pixels, width, height) = await CapturePhotoAsync();
            
            // 更新状态并通知用户
            UpdateStatusText("photo succeed");
            PlayVoicePrompt("拍照完成，正在分析");
            
            // 启动AI分析
            await StartImageAnalysis(pixels, width, height);
        }
        catch (Exception ex)
        {
            HandleError(ex, "Photo capture failed");
        }
#else
        try
        {
            PlayVoicePrompt("正在使用测试图片");
            Debug.Log("Loading test image from Resources...");

            // 加载原始图片
            Texture2D sourceTexture = Resources.Load<Texture2D>("TestImage");
            if (sourceTexture == null)
            {
                throw new Exception("Could not load TestImage from Resources");
            }

            Debug.Log($"Source image size: {sourceTexture.width}x{sourceTexture.height}");

            // 创建新的RGBA32格式纹理
            Texture2D processedTexture = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBA32, false);

            // 设置临时渲染纹理
            RenderTexture rt = RenderTexture.GetTemporary(sourceTexture.width, sourceTexture.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(sourceTexture, rt);

            // 保存当前RenderTexture
            RenderTexture previousRT = RenderTexture.active;
            RenderTexture.active = rt;

            // 读取像素数据到新纹理
            processedTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            processedTexture.Apply();

            // 恢复RenderTexture
            RenderTexture.active = previousRT;
            RenderTexture.ReleaseTemporary(rt);

            // 直接使用处理后的纹理进行分析
            Debug.Log("Directly using processed texture for analysis...");
            await StartImageAnalysis(processedTexture);

            // 清理资源
            Destroy(processedTexture);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Test image processing failed: {ex.Message}");
            Debug.LogError($"Stack trace: {ex.StackTrace}");
            UpdateStatusText("Test image failed");
            if (ttsComponent != null)
            {
                ttsComponent.PlayText("测试图片处理失败，请检查日志获取详细信息");
            }
        }

#endif
    }

#if WINDOWS_UWP
    /// <summary>
    /// 验证相机是否可用
    /// </summary>
    private bool ValidateCamera()
    {
        if (mediaCapture == null)
        {
            UpdateStatusText("photo failed");
            PlayVoicePrompt("拍照失败，请重试");
            return false;
        }
        return true;
    }

    /// <summary>
    /// 执行实际的拍照操作
    /// </summary>
    /// <returns>返回拍摄的图像数据和尺寸</returns>
    private async Task<(byte[] pixels, int width, int height)> CapturePhotoAsync()
    {
        using (var stream = new InMemoryRandomAccessStream())
        {
            await mediaCapture.CapturePhotoToStreamAsync(encodingProperties, stream);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            
            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Rgba8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);
                
            return (pixelData.DetachPixelData(), 
                   (int)transform.ScaledWidth, 
                   (int)transform.ScaledHeight);
        }
    }
#endif

    /// <summary>
    /// 对拍摄的图片进行AI分析
    /// </summary>
    private async Task StartImageAnalysis(Texture2D texture)
    {
        try
        {
            if (voiceInteraction == null)
            {
                throw new Exception("VoiceInteraction component is missing!");
            }

            byte[] imageBytes = texture.EncodeToJPG(100);
            string base64Image = Convert.ToBase64String(imageBytes);
            Debug.Log($"Image converted to base64, length: {base64Image.Length}");

            UpdateStatusText("Analyzing image...");

            string analysis = await voiceInteraction.SendToOpenAI(imageAnalysisPrompt, base64Image);
            Debug.Log($"Received analysis result: {analysis != null}");

            if (string.IsNullOrEmpty(analysis))
            {
                throw new Exception("OpenAI returned empty analysis");
            }

            await MainThread.Instance.EnqueueAsync(() =>
            {
                UpdateStatusText("Analysis complete");
                voiceInteraction.ResponsePlay(analysis);
            });
        }
        catch (Exception ex)
        {
            Debug.LogError($"Image analysis failed: {ex.Message}\nStack trace: {ex.StackTrace}");
            await MainThread.Instance.EnqueueAsync(() =>
            {
                UpdateStatusText($"Analysis failed: {ex.Message}");
                PlayVoicePrompt("分析失败，请重试");
            });
        }
    }

    // 保持原有的 StartImageAnalysis 重载方法
    private async Task StartImageAnalysis(byte[] pixels, int width, int height)
    {
        Texture2D tempTexture = null;
        try
        {
            tempTexture = await MainThread.Instance.EnqueueAsync(() =>
            {
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.LoadRawTextureData(pixels);
                tex.Apply();
                return tex;
            });

            await StartImageAnalysis(tempTexture);
        }
        finally
        {
            if (tempTexture != null)
            {
                await MainThread.Instance.EnqueueAsync(() =>
                {
                    Destroy(tempTexture);
                });
            }
        }
    }

    /// <summary>
    /// 播放语音提示
    /// </summary>
    private void PlayVoicePrompt(string message)
    {
        if (ttsComponent != null)
        {
            ttsComponent.PlayText(message);
        }
    }

    /// <summary>
    /// 统一错误处理方法
    /// </summary>
    private void HandleError(Exception ex, string message)
    {
        Debug.LogError($"{message}: {ex.Message}");
        UpdateStatusText("photo failed");
        PlayVoicePrompt("拍照失败，请重试");
    }

    /// <summary>
    /// 更新UI状态文本
    /// </summary>
    private void UpdateStatusText(string message)
    {
        if (statusText != null)
        {
            if (Application.isPlaying && MainThread.Instance != null)
            {
                MainThread.Instance.Enqueue(() =>
                {
                    try
                    {
                        statusText.text = message;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error updating status text: {ex.Message}");
                    }
                });
            }
            else
            {
                // 直接更新，适用于编辑器模式或 MainThread 未初始化的情况
                statusText.text = message;
            }
        }
    }

    /// <summary>
    /// 组件销毁时清理资源
    /// </summary>
    void OnDestroy()
    {
#if WINDOWS_UWP
        mediaCapture?.Dispose();
        mediaCapture = null;
#endif
    }
}