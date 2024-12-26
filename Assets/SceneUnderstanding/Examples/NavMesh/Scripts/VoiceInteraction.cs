using UnityEngine;
using System;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using UnityEngine.Networking;
using System.Threading;
using TMPro;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
#if WINDOWS_UWP
using Windows.Media.SpeechRecognition;
using Windows.Globalization;
#endif

namespace Microsoft.MixedReality.SceneUnderstanding.Samples.Unity
{
    public class VoiceInteraction : MonoBehaviour
    {
        [Header("OpenAI Settings")]
        [SerializeField]
        private string openAIModel = "gpt-4o";
        [SerializeField]
        private int maxTokens = 4096;
        [SerializeField]
        private float temperature = 0.7f;
        [SerializeField]
        private int maxConversationHistory = 10;

        [Header("API Settings")]
        [SerializeField]
        private string openAIApiKey;

        [Header("Components")]
        [SerializeField]
        public UnityWsTts ttsComponent;
        [SerializeField]
        private TextMeshProUGUI displayText;

        private List<ChatMessage> conversationHistory;
        private bool isInitialized = false;
        private bool isListening = false;
        private CancellationTokenSource timeoutCts;
        private bool isRecognizing = false;
        private bool needsReinitialization = false;

#if WINDOWS_UWP
        private SpeechRecognizer speechRecognizer;
#endif

        [Serializable]
        public class ChatMessage
        {
            public string role;
            public object content;

            public ChatMessage(string role, string content)
            {
                this.role = role;
                this.content = content;
            }

            public ChatMessage(string role, MessageContent[] content)
            {
                this.role = role;
                this.content = content;
            }

            // 添加新的构造函数
            public ChatMessage(string role, object[] content)
            {
                this.role = role;
                this.content = content;
            }
        }

        private void Awake()
        {
            InitializeConversationHistory();
        }

        private void InitializeConversationHistory()
        {
            conversationHistory = new List<ChatMessage>();
            conversationHistory.Add(new ChatMessage("system",
                "你是一个有帮助的AI助手。你的职责包括：" +
                "1. 记住并理解所有对话内容，包括之前的用户输入和你的回答。" +
                "2. 当用户询问之前的对话内容时，从历史记录中准确回忆并回答。" +
                "3. 分析图片时详细描述看到的内容。" +
                "4. 回答时要连贯并与上下文相关。" +
                "5. 永远不要说你无法查看历史记录，因为历史记录已经包含在消息数组中。"));

            Debug.Log("Conversation history initialized with new system prompt");
        }

        private async void Start()
        {
            try
            {
                await InitializeComponents();
                await InitializeSpeechRecognition();

                if (isInitialized)
                {
                    UpdateDisplayText("System ready for interaction");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Initialization error: {ex.Message}");
                UpdateDisplayText("System initialization failed");
            }
        }

        private async Task InitializeComponents()
        {
            if (ttsComponent == null)
            {
                ttsComponent = GetComponent<UnityWsTts>();
                if (ttsComponent == null)
                {
                    ttsComponent = gameObject.AddComponent<UnityWsTts>();
                }
            }

            if (displayText == null)
            {
                Debug.LogError("Display text component is not assigned!");
            }

            await Task.CompletedTask;
        }

        private async Task InitializeSpeechRecognition()
        {
#if WINDOWS_UWP
            try
            {
                if (speechRecognizer != null)
                {
                    speechRecognizer.Dispose();
                    speechRecognizer = null;
                }

                // 创建中文语言对象
                var language = new Language("zh-CN");
                // 使用指定的语言创建语音识别器
                speechRecognizer = new SpeechRecognizer(language);
                
                // 设置更宽泛的约束
                var dictationConstraint = new SpeechRecognitionTopicConstraint(
                    SpeechRecognitionScenario.Dictation, "dictation");
                speechRecognizer.Constraints.Add(dictationConstraint);
                
                var compilationResult = await speechRecognizer.CompileConstraintsAsync();
                
                if (compilationResult.Status != SpeechRecognitionResultStatus.Success)
                {
                    Debug.LogError($"Failed to compile constraints: {compilationResult.Status}");
                    isInitialized = false;
                    throw new Exception($"Failed to compile constraints: {compilationResult.Status}");
                }
                
                // 添加错误处理事件
                speechRecognizer.RecognitionQualityDegrading += SpeechRecognizer_RecognitionQualityDegrading;
                
                needsReinitialization = false;
                isInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to initialize speech recognition: {ex.Message}");
                isInitialized = false;
                throw;
            }
#else
            await Task.CompletedTask;
            isInitialized = true;
#endif
        }

#if WINDOWS_UWP
private void SpeechRecognizer_RecognitionQualityDegrading(SpeechRecognizer sender, SpeechRecognitionQualityDegradingEventArgs args)
{
    Debug.LogWarning($"Speech recognition quality degrading: {args.Problem}");
}
#endif

        public async void StartListening()
        {
            if (!isInitialized || isListening)
            {
                string statusMessage = !isInitialized ? "System not initialized" : "Already listening";
                UpdateDisplayText(statusMessage);
                PlayVoicePrompt("请稍等，系统正在准备中");
                return;
            }

            try
            {
                if (needsReinitialization)
                {
                    await InitializeSpeechRecognition();
                    if (!isInitialized)
                    {
                        UpdateDisplayText("Failed to reinitialize speech recognition");
                        PlayVoicePrompt("语音识别初始化失败，请重试");
                        return;
                    }
                }

                isListening = true;
                UpdateDisplayText("Listening...");
                PlayVoicePrompt("请说");

                // 设置超时检测
                timeoutCts?.Cancel();
                timeoutCts = new CancellationTokenSource();

                var timeoutTask = Task.Delay(10000, timeoutCts.Token);
                var recognitionTask = RecognizeSpeechWithStatus();

                var completedTask = await Task.WhenAny(timeoutTask, recognitionTask);

                if (completedTask == timeoutTask && !timeoutTask.IsCanceled)
                {
                    UpdateDisplayText("Recognition timeout");
                    PlayVoicePrompt("识别超时，请重试");
                    return;
                }

                string recognizedText = await recognitionTask;

                if (!string.IsNullOrEmpty(recognizedText))
                {
                    UpdateDisplayText($"You: {recognizedText}", true);
                    PlayVoicePrompt("正在思考");

                    string aiResponse = await SendToOpenAI(recognizedText);
                    ResponsePlay(aiResponse);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Voice interaction error: {ex.Message}");
                UpdateDisplayText("Voice interaction failed");
                PlayVoicePrompt("语音交互失败，请重试");
            }
            finally
            {
                isListening = false;
                isRecognizing = false;
                timeoutCts?.Cancel();
                timeoutCts?.Dispose();
                timeoutCts = null;
                needsReinitialization = true;
            }
        }

        private async Task<string> RecognizeSpeechWithStatus()
        {
#if WINDOWS_UWP
    try
    {
        if (!isInitialized || speechRecognizer == null)
        {
            UpdateDisplayText("Speech recognition not initialized");
            return null;
        }

        isRecognizing = true;
        UpdateDisplayText("Recognizing...");

        // 添加重试机制
        int maxRetries = 2;
        int currentRetry = 0;
        SpeechRecognitionResultStatus lastStatus = SpeechRecognitionResultStatus.Success;

        while (currentRetry <= maxRetries)
        {
            try
            {
                var result = await speechRecognizer.RecognizeAsync();
                
                if (result.Status == SpeechRecognitionResultStatus.Success)
                {
                    timeoutCts?.Cancel();
                    return result.Text;
                }
                
                lastStatus = result.Status;
                
                if (currentRetry < maxRetries)
                {
                    if (result.Status == SpeechRecognitionResultStatus.TimeoutExceeded)
                    {
                        Debug.Log($"Recognition timeout, retrying... ({currentRetry + 1}/{maxRetries})");
                    }
                    else 
                    {
                        Debug.Log($"Recognition failed with status {result.Status}, retrying... ({currentRetry + 1}/{maxRetries})");
                    }
                    currentRetry++;
                    await Task.Delay(500); // 短暂延迟后重试
                    continue;
                }
            }
            catch (Exception ex)
            {
                if (currentRetry < maxRetries)
                {
                    currentRetry++;
                    await Task.Delay(500);
                    continue;
                }
                throw;
            }
        }
        
        // 所有重试都失败后才显示错误信息
        UpdateDisplayText($"Recognition failed after retries");
        PlayVoicePrompt("语音交互失败，请重试");
        needsReinitialization = true;
        return null;
    }
    catch (Exception ex)
    {
        UpdateDisplayText("Recognition error occurred");
        PlayVoicePrompt("语音交互失败，请重试");
        needsReinitialization = true;
        return null;
    }
    finally
    {
        isRecognizing = false;
    }
#else
            await Task.Delay(1000);
            isRecognizing = false;
            timeoutCts?.Cancel();
            return "我的上一句话是什么";
#endif
        }

        public void ResponsePlay(string response)
        {
            if (!string.IsNullOrEmpty(response))
            {
                UpdateDisplayText($"AI: {response}", true);
                if (ttsComponent != null)
                {
                    ttsComponent.PlayText(response);
                }
            }
            else
            {
                UpdateDisplayText("Failed to get AI response", true);
                PlayVoicePrompt("网络似乎不稳定，请重试");
            }
        }

        public async Task<string> SendToOpenAI(string message, string base64Image = null)
        {
            try
            {
                if (string.IsNullOrEmpty(message))
                {
                    Debug.LogError("Message cannot be empty");
                    return null;
                }

                var messages = new List<ChatMessage>(conversationHistory);

                // 添加用户消息到历史记录
                if (!string.IsNullOrEmpty(base64Image))
                {
                    var content = new List<object>();
                    content.Add(new { type = "text", text = message });
                    content.Add(new
                    {
                        type = "image_url",
                        image_url = new
                        {
                            url = base64Image.StartsWith("data:image/jpeg;base64,") ? base64Image : $"data:image/jpeg;base64,{base64Image}",
                            detail = "auto"
                        }
                    });
                    messages.Add(new ChatMessage("user", content.ToArray()));
                }
                else
                {
                    // 添加这个else分支来处理纯文本消息
                    messages.Add(new ChatMessage("user", message));
                }

                var request = new
                {
                    model = openAIModel,
                    messages = messages,
                    max_tokens = maxTokens,
                    temperature = temperature
                };

                var jsonContent = JsonConvert.SerializeObject(request);
                Debug.Log($"Request JSON: {jsonContent}");

                using (var www = new UnityWebRequest("https://api.openai.com/v1/chat/completions", "POST"))
                {
                    byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonContent);
                    www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    www.downloadHandler = new DownloadHandlerBuffer();
                    www.SetRequestHeader("Content-Type", "application/json");
                    www.SetRequestHeader("Authorization", $"Bearer {openAIApiKey}");
                    www.SetRequestHeader("OpenAI-Beta", "assistants=v1");

                    await www.SendWebRequest();

                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        var responseString = www.downloadHandler.text;
                        Debug.Log($"OpenAI Response: {responseString}");
                        var response = JsonConvert.DeserializeObject<OpenAIResponse>(responseString);
                        string responseContent = response?.choices?[0]?.message?.content;

                        if (!string.IsNullOrEmpty(responseContent))
                        {
                            // 添加助手回复到对话历史
                            ChatMessage assistantMessage = new ChatMessage("assistant", responseContent);
                            messages.Add(assistantMessage);

                            // 保持对话历史在限制范围内
                            while (messages.Count > maxConversationHistory + 1)
                            {
                                messages.RemoveAt(1);  // 保留system message
                            }

                            // 更新对话历史
                            conversationHistory = messages;

                            return responseContent;
                        }
                    }

                    Debug.LogError($"API Error: {www.error}\nResponse: {www.downloadHandler.text}");
                    return null;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"SendToOpenAI error: {e.Message}");
                return null;
            }
        }

        private void UpdateDisplayText(string message, bool append = false)
        {
            if (displayText != null)
            {
                if (Application.isPlaying && MainThread.Instance != null)
                {
                    MainThread.Instance.Enqueue(() =>
                    {
                        try
                        {
                            displayText.text = append ?
                                displayText.text + "\n" + message : message;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"Error updating display text: {ex.Message}");
                        }
                    });
                }
                else
                {
                    displayText.text = append ?
                        displayText.text + "\n" + message : message;
                }
            }
        }

        private void PlayVoicePrompt(string message)
        {
            if (ttsComponent != null)
            {
                ttsComponent.PlayText(message);
            }
        }

        public void ClearConversationHistory()
        {
            InitializeConversationHistory();
            UpdateDisplayText("Conversation history cleared");
        }

        void OnDestroy()
        {
            timeoutCts?.Cancel();
            timeoutCts?.Dispose();
#if WINDOWS_UWP
            if (speechRecognizer != null)
            {
                speechRecognizer.Dispose();
                speechRecognizer = null;
            }
#endif
        }

        [Serializable]
        public class MessageContent
        {
            public string type;
            public string text;
            public ImageUrl image_url;

            [Serializable]
            public class ImageUrl
            {
                public string url;
                public string detail = "high";
            }
        }
    }

    [Serializable]
    public class OpenAIResponse
    {
        public Choice[] choices;
    }

    [Serializable]
    public class Choice
    {
        public Message message;
    }

    [Serializable]
    public class Message
    {
        public string role;
        public string content;
    }
}