using UnityEngine;
using System;
using System.Net.WebSockets;
using System.Text;
using System.IO;
using System.Threading;
using System.Security.Cryptography;
using Newtonsoft.Json;
using System.Collections;
using TMPro;
using System.Collections.Generic;

public class UnityWsTts : MonoBehaviour
{
    [Header("语音设置")]
    [SerializeField]
    private string APPID;
    //private const string APPID = "e40c7d8c";
    [SerializeField]
    private string APIKey;
    [SerializeField]
    private string APISecret;

    private ClientWebSocket webSocket;
    private bool isProcessing = false;
    private AudioSource audioSource;
    private string lastError = string.Empty;

    // 新增队列相关变量
    private Queue<string> textQueue = new Queue<string>();
   // private bool isQueueProcessing = false;

    [SerializeField]
    private bool debugLog = false;
    [SerializeField]
    private TMP_Text textComponent;

    private void Awake()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        // 如果没有在Inspector中指定textComponent，尝试自动获取
        if (textComponent == null)
        {
            textComponent = GetComponent<TMP_Text>();
        }
        // 启动队列处理协程
        StartCoroutine(ProcessQueue());
    }

    public void PlayTextFromTMP()
    {
        if (textComponent == null)
        {
            Debug.LogError("No TMP_Text component assigned!");
            return;
        }

        PlayText(textComponent.text);
    }

    private string CreateUrl()
    {
        string url = "wss://tts-api.xfyun.cn/v2/tts";
        string date = DateTime.UtcNow.ToString("r");
        string signatureOrigin = "host: ws-api.xfyun.cn\n";
        signatureOrigin += $"date: {date}\n";
        signatureOrigin += "GET /v2/tts HTTP/1.1";

        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(APISecret)))
        {
            byte[] signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureOrigin));
            string signature = Convert.ToBase64String(signatureBytes);
            string authorizationOrigin = $"api_key=\"{APIKey}\", algorithm=\"hmac-sha256\", headers=\"host date request-line\", signature=\"{signature}\"";
            string authorization = Convert.ToBase64String(Encoding.UTF8.GetBytes(authorizationOrigin));
            return $"{url}?authorization={Uri.EscapeDataString(authorization)}&date={Uri.EscapeDataString(date)}&host=ws-api.xfyun.cn";
        }
    }

    public void PlayText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            Debug.LogWarning("Text is empty, nothing to process");
            return;
        }

        // 清空当前队列
        textQueue.Clear();

        // 如果当前正在播放，停止播放
        StopCurrentAudio();

        // 将新文本添加到队列
        textQueue.Enqueue(text);
        if (debugLog)
        {
            Debug.Log($"Added text to queue. Queue length: {textQueue.Count}");
        }
    }

    // 队列处理协程
    private IEnumerator ProcessQueue()
    {
        while (true)
        {
            // 当队列不为空且当前没有正在处理的任务时，处理队列中的下一个文本
            if (textQueue.Count > 0 && !isProcessing)
            {
                string nextText = textQueue.Dequeue();
                if (debugLog)
                {
                    Debug.Log($"Processing next text from queue. Remaining items: {textQueue.Count}");
                }
                yield return ProcessTTS(nextText);
            }
            yield return new WaitForSeconds(0.1f); // 添加短暂延迟避免过度检查
        }
    }

    public void StopCurrentAudio()
    {
        // 停止当前的音频播放
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
        }

        // 停止当前的处理
        if (isProcessing)
        {
            StopCoroutine("ProcessTTS");
            CleanupAndExit();
        }

        // 重置状态
        isProcessing = false;
    }

    private IEnumerator ProcessTTS(string text)
    {
        // 在开始新的处理前确保之前的处理已经停止
        StopCurrentAudio();
        isProcessing = true;
        webSocket = new ClientWebSocket();
        lastError = string.Empty;

        yield return ConnectWebSocket();
        if (!string.IsNullOrEmpty(lastError))
        {
            CleanupAndExit();
            yield break;
        }

        if (PrepareAndSendRequest(text))
        {
            yield return SendWebSocketMessage(text);
            if (!string.IsNullOrEmpty(lastError))
            {
                CleanupAndExit();
                yield break;
            }
        }
        else
        {
            CleanupAndExit();
            yield break;
        }

        yield return ReceiveAudio();

        CleanupAndExit();
    }

    private bool PrepareAndSendRequest(string text)
    {
        try
        {
            var requestData = new
            {
                common = new { app_id = APPID },
                business = new
                {
                    aue = "raw",
                    auf = "audio/L16;rate=16000",
                    vcn = "xiaoyan",
                    tte = "utf8"
                },
                data = new
                {
                    status = 2,
                    text = Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
                }
            };

            return true;
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
            Debug.LogError($"Prepare request error: {ex.Message}");
            return false;
        }
    }

    private IEnumerator SendWebSocketMessage(string text)
    {
        var requestData = new
        {
            common = new { app_id = APPID },
            business = new
            {
                aue = "raw",
                auf = "audio/L16;rate=16000",
                vcn = "xiaoyan",
                tte = "utf8"
            },
            data = new
            {
                status = 2,
                text = Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
            }
        };

        string jsonRequest = JsonConvert.SerializeObject(requestData);
        byte[] requestBytes = Encoding.UTF8.GetBytes(jsonRequest);
        var sendBuffer = new ArraySegment<byte>(requestBytes);

        var sendTask = webSocket.SendAsync(sendBuffer,
                                         WebSocketMessageType.Text,
                                         true,
                                         CancellationToken.None);

        while (!sendTask.IsCompleted)
        {
            if (sendTask.IsFaulted)
            {
                lastError = "Failed to send request";
                Debug.LogError(lastError);
                yield break;
            }
            yield return null;
        }
    }

    private void CleanupAndExit()
    {
        if (webSocket != null && webSocket.State == WebSocketState.Open)
        {
            try
            {
                webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Completed",
                    CancellationToken.None
                ).Wait(1000);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error closing websocket: {ex.Message}");
            }
        }
        isProcessing = false;
    }

    private IEnumerator ConnectWebSocket()
    {
        var connectTask = webSocket.ConnectAsync(new Uri(CreateUrl()), CancellationToken.None);
        while (!connectTask.IsCompleted)
            yield return null;

        if (webSocket.State != WebSocketState.Open)
        {
            lastError = "Failed to connect to WebSocket server";
            Debug.LogError(lastError);
        }
    }

    private IEnumerator ReceiveAudio()
    {
        using (var audioStream = new MemoryStream())
        {
            byte[] buffer = new byte[8192]; // 增大缓冲区
            var messageBuffer = new StringBuilder();
            bool endOfMessage = false;

            while (!endOfMessage && string.IsNullOrEmpty(lastError))
            {
                var receiveTask = webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    CancellationToken.None
                );

                while (!receiveTask.IsCompleted)
                    yield return null;

                if (receiveTask.IsFaulted)
                {
                    lastError = "Failed to receive data";
                    Debug.LogError(lastError);
                    yield break;
                }

                var result = receiveTask.Result;
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string partialMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    messageBuffer.Append(partialMessage);

                    if (result.EndOfMessage)
                    {
                        try
                        {
                            string completeMessage = messageBuffer.ToString();
                            var response = JsonConvert.DeserializeObject<TtsResponse>(completeMessage);
                            messageBuffer.Clear();

                            if (response.Code != 0)
                            {
                                lastError = $"API Error: {response.Message}";
                                Debug.LogError(lastError);
                                yield break;
                            }

                            if (!string.IsNullOrEmpty(response.Data?.Audio))
                            {
                                byte[] audioData = Convert.FromBase64String(response.Data.Audio);
                                audioStream.Write(audioData, 0, audioData.Length);
                            }

                            if (response.Data?.Status == 2)
                                endOfMessage = true;
                        }
                        catch (JsonReaderException ex)
                        {
                            Debug.LogError($"JSON parsing error: {ex.Message}");
                            if (debugLog)
                            {
                                Debug.Log($"Received message: {messageBuffer}");
                            }
                            lastError = "Failed to parse response";
                            yield break;
                        }
                    }
                }
            }

            if (audioStream.Length > 0)
                yield return PlayAudioData(audioStream.ToArray());
        }
    }

    private IEnumerator PlayAudioData(byte[] audioData)
    {
        // 在播放新音频前停止当前音频
        if (audioSource.isPlaying)
        {
            audioSource.Stop();
        }

        int sampleCount = audioData.Length / 2;
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            short sample = BitConverter.ToInt16(audioData, i * 2);
            samples[i] = sample / 32768f;
        }

        AudioClip audioClip = AudioClip.Create("TTS_Audio", sampleCount, 1, 16000, false);
        audioClip.SetData(samples, 0);

        audioSource.clip = audioClip;
        audioSource.Play();

        while (audioSource.isPlaying)
            yield return null;
    }

    public void OnDestroy()
    {
        // 清理资源
        StopAllCoroutines();
        CleanupAndExit();
    }

    private class TtsResponse
    {
        public int Code { get; set; }
        public string Message { get; set; }
        public string Sid { get; set; }
        public TtsData Data { get; set; }
    }

    private class TtsData
    {
        public string Audio { get; set; }
        public int Status { get; set; }
    }
}