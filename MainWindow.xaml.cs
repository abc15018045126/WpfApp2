using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using System.Diagnostics;

namespace ChatGPTApp
{
    public partial class MainWindow : Window
    {
        // 默认值
        private static readonly string defaultGeminiApiUrl = "https://gemini.abc15018045126.ip-ddns.com/v1/chat/completions";
        private static readonly string defaultGeminiApiKey = "AIzaSyDmGfx7r-MP8XglVrGkcG51JtTsqSH31uI";
        private static readonly string defaultModelName = "gemini-2.0-flash";
        private static readonly string defaultMongoConnectionString = "mongodb://localhost:27017";

        private IMongoClient mongoClient;
        private IMongoDatabase database;
        private IMongoCollection<BsonDocument> chatLogsCollection;
        private List<ChatMessage> chatHistory = new List<ChatMessage>();

        // 声明 HttpClient 对象
        private static readonly HttpClient httpClient = new HttpClient();

        public MainWindow()
        {
            InitializeComponent();

            // 初始化 MongoDB 连接
            InitializeMongoDB();

            // 初始化 HttpClient
            //httpClient = new HttpClient(); // 移除此行
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            string userInput = InputTextBox.Text.Trim();
            if (string.IsNullOrEmpty(userInput)) return;

            AppendMessage("You: " + userInput);

            // 发送输入到 Gemini
            string geminiResponse = await SendMessageToGeminiAsync(userInput);
            AppendMessage("Gemini: " + geminiResponse);

            // 保存聊天记录到 MongoDB
            SaveChatLog(userInput, geminiResponse);

            // 清空输入框
            InputTextBox.Text = "";
        }

        private void AppendMessage(string message)
        {
            ChatTextBox.AppendText(message + Environment.NewLine);
            ChatTextBox.ScrollToEnd();
        }

        private async Task<string> SendMessageToGeminiAsync(string input)
        {
            try
            {
                // 获取配置值
                string geminiApiUrl = string.IsNullOrEmpty(GeminiApiUrlTextBox.Text) ? defaultGeminiApiUrl : GeminiApiUrlTextBox.Text;
                string geminiApiKey = string.IsNullOrEmpty(GeminiApiKeyTextBox.Text) ? defaultGeminiApiKey : GeminiApiKeyTextBox.Text;
                string modelName = string.IsNullOrEmpty(ModelNameTextBox.Text) ? defaultModelName : ModelNameTextBox.Text;

                // 获取 HistoryCountTextBox 的值
                int maxHistoryLength = GetMaxHistoryLength();

                // 获取 EnableHistoryLimitTextBox 的值
                bool enableHistoryLimit = GetEnableHistoryLimit();

                // 更新对话历史记录
                chatHistory.Add(new ChatMessage { role = "user", content = input });

                // 限制对话历史记录的长度
                if (enableHistoryLimit)
                {
                    while (chatHistory.Count > maxHistoryLength)
                    {
                        chatHistory.RemoveAt(0); // 移除最旧的对话条目
                    }
                }

                // 构建请求体
                var requestBody = new Dictionary<string, object>
                {
                    { "model", modelName },
                    { "messages", chatHistory.ToArray() }
                };

                // 添加可选参数
                if (double.TryParse(TemperatureTextBox.Text, out double temperature))
                {
                    requestBody["temperature"] = temperature;
                }

                if (double.TryParse(TopPTextBox.Text, out double topP))
                {
                    requestBody["top_p"] = topP;
                }

                if (int.TryParse(TopKTextBox.Text, out int topK))
                {
                    requestBody["top_k"] = topK;
                }

                // 序列化请求体为 JSON
                string jsonContent = JsonConvert.SerializeObject(requestBody, Newtonsoft.Json.Formatting.Indented);

                // 将请求体添加到ChatTextBox
                AppendMessage("Request Body: " + Environment.NewLine + jsonContent);

                // 准备 HTTP 请求内容
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                // 设置 HTTP 请求头
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + geminiApiKey);

                // 发送 POST 请求
                HttpResponseMessage response = await httpClient.PostAsync(geminiApiUrl, content);

                // 获取返回的内容
                string responseContent = await response.Content.ReadAsStringAsync();

                Debug.WriteLine("Full Response Content: " + responseContent);

                // 检查响应状态码
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"HTTP Error: {response.StatusCode}, Content: {responseContent}");
                }

                // 解析响应
                dynamic responseObject = JsonConvert.DeserializeObject(responseContent);
                string geminiResponse = responseObject.choices[0].message.content;

                // 更新对话历史记录
                chatHistory.Add(new ChatMessage { role = "assistant", content = geminiResponse });

                return geminiResponse;
            }
            catch (Exception ex)
            {
                // 捕获和显示错误信息
                MessageBox.Show($"Error occurred: {ex.Message}\nStack Trace: {ex.StackTrace}");
                return "Error occurred while communicating with Gemini.";
            }
        }

        private void SaveChatLog(string userMessage, string botResponse)
        {
            try
            {
                // 获取用户指定的数据库名称
                string databaseName = GetMongoDBDatabaseName();

                // 获取数据库对象
                database = mongoClient.GetDatabase(databaseName);

                // 获取 chatLogsCollection 对象
                chatLogsCollection = database.GetCollection<BsonDocument>("ChatLogs");

                var chatLog = new BsonDocument
                {
                    { "UserMessage", userMessage },
                    { "BotResponse", botResponse },
                    { "Timestamp", DateTime.UtcNow }
                };

                // 确保 chatLogsCollection 已初始化
                if (chatLogsCollection != null)
                {
                    chatLogsCollection.InsertOne(chatLog);
                }
                else
                {
                    MessageBox.Show("MongoDB collection is not initialized.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving chat log to MongoDB: {ex.Message}");
            }
        }

        // 获取 HistoryCountTextBox 的值
        private int GetMaxHistoryLength()
        {
            if (int.TryParse(HistoryCountTextBox.Text, out int maxHistoryLength))
            {
                return maxHistoryLength;
            }
            else
            {
                // 如果 HistoryCountTextBox 的值无效，则返回默认值
                MessageBox.Show("Invalid history count. Using default value of 10.");
                HistoryCountTextBox.Text = "10"; // 重置为默认值
                return 10;
            }
        }

        // 获取 EnableHistoryLimitTextBox 的值
        private bool GetEnableHistoryLimit()
        {
            if (int.TryParse(EnableHistoryLimitTextBox.Text, out int enableHistoryLimit))
            {
                return enableHistoryLimit == 0; // 0 表示启用限制，1 表示禁用限制
            }
            else
            {
                // 如果 EnableHistoryLimitTextBox 的值无效，则返回默认值
                MessageBox.Show("Invalid enable history limit value. Using default value of 0.");
                EnableHistoryLimitTextBox.Text = "0"; // 重置为默认值
                return true; // 默认启用限制
            }
        }

        // 获取 MongoDBDatabaseNameTextBox 的值
        private string GetMongoDBDatabaseName()
        {
            return MongoDBDatabaseNameTextBox.Text;
        }

        private void InitializeMongoDB()
        {
            try
            {
                string databaseName = GetMongoDBDatabaseName();
                mongoClient = new MongoClient(defaultMongoConnectionString); // 使用默认连接字符串

                // 检查数据库是否存在
                var databaseList = mongoClient.ListDatabaseNames().ToList();
                if (!databaseList.Contains(databaseName))
                {
                    // 创建数据库
                    database = mongoClient.GetDatabase(databaseName); // 创建数据库对象
                    Debug.WriteLine($"Database '{databaseName}' created successfully.");
                }

                database = mongoClient.GetDatabase(databaseName); // 获取数据库对象
                chatLogsCollection = database.GetCollection<BsonDocument>("ChatLogs");
                Debug.WriteLine($"Connected to database '{databaseName}'");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing MongoDB: {ex.Message}");
            }
        }
    }

    // 创建 ChatMessage 类
    public class ChatMessage
    {
        [JsonProperty("role")] // 添加 JsonProperty 属性
        public string role { get; set; }
        [JsonProperty("content")] // 添加 JsonProperty 属性
        public string content { get; set; }
    }
}
