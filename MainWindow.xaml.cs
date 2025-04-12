using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls; // 引入 TextBlock
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using System.Diagnostics; // 引入 Debug 类

namespace ChatGPTApp
{
    public partial class MainWindow : Window
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly string geminiApiUrl = "https://gemini.abc15018045126.ip-ddns.com/v1/chat/completions"; // 正确的 API 端点，没有查询参数
        private static readonly string geminiApiKey = "AIzaSyDmGfx7r-MP8XglVrGkcG51JtTsqSH31uI"; // 替换为你的 Gemini API 密钥
        private static readonly string modelName = "gemini-2.0-flash"; // 替换为正确的模型名称

        private IMongoCollection<BsonDocument> chatLogsCollection;

        public MainWindow()
        {
            InitializeComponent();

            // 初始化 MongoDB 连接
            var mongoClient = new MongoClient("mongodb://localhost:27017"); // 替换为正确的 MongoDB 连接字符串
            var database = mongoClient.GetDatabase("ChatGPTDB");
            chatLogsCollection = database.GetCollection<BsonDocument>("ChatLogs");
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
                // 构建请求体
                var requestBody = new
                {
                    model = modelName,
                    messages = new[]
                    {
                        new { role = "user", content = input }
                    }
                };

                // 序列化请求体为 JSON
                string jsonContent = JsonConvert.SerializeObject(requestBody, Newtonsoft.Json.Formatting.Indented); // 格式化 JSON

                // 将请求体添加到 ChatTextBox
                AppendMessage("Request Body: " + Environment.NewLine + jsonContent);

                // 准备 HTTP 请求内容
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"); // 将 Content-Type 添加到 HttpContent 对象中

                // 设置 HTTP 请求头
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + geminiApiKey); // 添加 Authorization 头部

                // 发送 POST 请求
                HttpResponseMessage response = await httpClient.PostAsync(geminiApiUrl, content);

                // 获取返回的内容
                string responseContent = await response.Content.ReadAsStringAsync();

                Debug.WriteLine("Full Response Content: " + responseContent); // 打印完整的响应内容

                // 检查响应状态码
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"HTTP Error: {response.StatusCode}, Content: {responseContent}");
                }

                // 解析响应
                // 注意：你需要根据 Gemini API 的实际响应格式来解析响应
                // 假设响应格式与 OpenAI 类似
                dynamic responseObject = JsonConvert.DeserializeObject(responseContent);
                string geminiResponse = responseObject.choices[0].message.content;

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
            var chatLog = new BsonDocument
            {
                { "UserMessage", userMessage },
                { "BotResponse", botResponse },
                { "Timestamp", DateTime.UtcNow }
            };
            chatLogsCollection.InsertOne(chatLog);
        }
    }
}
