using UnityEngine;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Newtonsoft.Json;
using WebSocketSharp;
using UnityEngine.SceneManagement;

public class Communication : MonoBehaviour
{
    private string configFilePath;
    public AppConfig appConfig;
    public StatusBar panelStatusBar;

    private Dictionary<string, string> outputTagToAddress = new();
    private Dictionary<string, string> inputTagToAddress = new();
    private Dictionary<string, bool> coilValues = new();
    private Dictionary<string, int> discreteValues = new();
    private readonly object coilValuesLock = new();

    private WebSocket ws;
    private ConcurrentQueue<string> messageQueue = new ConcurrentQueue<string>();
    private const int MAX_MESSAGES_PER_FRAME = 2;
    private float reconnectDelay = 1f;
    private float lastReconnectAttempt = -1f;

    public bool IsConnected => ws != null && ws.ReadyState == WebSocketState.Open;

    void Awake()
    {
        Application.runInBackground = true;
        Application.targetFrameRate = appConfig.maxFPS;
        Debug.Log("Communication: Initializing...");
        try
        {
            ConfigFileLoad();
            InitializeWebSocket();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error during initialization: {ex.Message}");
        }
    }

    void Start()
    {
        // Initialize WebSocket with the correct URL
        string url = $"ws://{appConfig.ip}:{appConfig.port}";
        ws = new WebSocket(url);

        ws.OnOpen += (sender, e) =>
        {
            Debug.Log("WebSocket connected.");
            OnConnected();
        };

        ws.OnClose += (sender, e) =>
        {
            Debug.Log("WebSocket disconnected. Reason: " + e.Reason);
            OnDisconnected();
        };

        ws.OnError += (sender, e) =>
        {
            Debug.LogError("WebSocket error: " + e.Message);
            if (e.Exception != null)
            {
                Debug.LogError("Exception details: " + e.Exception);
            }
        };

        ws.OnMessage += (sender, e) =>
        {
            Debug.Log("Message received: " + e.Data);
            messageQueue.Enqueue(e.Data);
        };

        // Attempt to connect asynchronously
        ws.ConnectAsync();
    }

    void Update()
    {
        float startTime = Time.realtimeSinceStartup;

        int processedCount = 0;
        while (processedCount < MAX_MESSAGES_PER_FRAME && messageQueue.TryDequeue(out string message))
        {
            ProcessMessage(message);
            processedCount++;
        }

        if (processedCount > 0)
        {
            Debug.Log($"Processed {processedCount} messages this frame");
        }
        if (messageQueue.Count > 0)
        {
            Debug.LogWarning($"Queue backlog: {messageQueue.Count} messages remaining");
        }

        if (!IsConnected && Time.time - lastReconnectAttempt > reconnectDelay)
        {
            Reconnect();
            lastReconnectAttempt = Time.time;
        }

        float frameTime = (Time.realtimeSinceStartup - startTime) * 1000;
        if (frameTime > 50)
        {
            Debug.LogWarning($"Frame time exceeded 50ms: {frameTime:F2}ms");
        }
    }

    private void InitializeWebSocket()
    {
        ws = new WebSocket($"ws://{appConfig.ip}:{appConfig.port}");
        ws.ConnectAsync();
    }

    private void Reconnect()
    {
        if (ws.ReadyState != WebSocketState.Connecting && ws.ReadyState != WebSocketState.Open)
        {
            Debug.Log("Attempting to reconnect WebSocket...");
            ws.ConnectAsync();
        }
    }

    private void ProcessMessage(string message)
    {
        try
        {
            var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(message);
            if (data != null)
            {
                if (data.ContainsKey("action"))
                {
                    string action = data["action"].ToString();
                    if (action == "ping")
                    {
                        if (IsConnected)
                        {
                            ws.Send(JsonConvert.SerializeObject(new { action = "pong" }));
                            Debug.Log("Received ping, sent pong");
                        }
                        return;
                    }
                }

                var coilData = JsonConvert.DeserializeObject<Dictionary<string, bool>>(message);
                if (coilData != null)
                {
                    lock (coilValuesLock)
                    {
                        foreach (var entry in coilData)
                        {
                            coilValues[entry.Key] = entry.Value;
                        }
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            Debug.LogError($"Error parsing JSON: {ex.Message}");
        }
    }

    public void WriteDiscreteInput(string tag, int value)
    {
        string address = inputTagToAddress[tag];
        discreteValues[tag] = value;
        var command = new { action = "set_IX", address, value };
        if (IsConnected)
        {
            ws.Send(JsonConvert.SerializeObject(command));
        }
    }

    public bool ReadCoil(string tag)
    {
        lock (coilValuesLock)
        {
            string address = outputTagToAddress[tag];
            return coilValues.ContainsKey(address) ? coilValues[address] : false;
        }
    }

    private void OnConnected()
    {
        // Dispatch UI updates to the main thread.
        MainThreadDispatcher.Enqueue(() =>
        {
            panelStatusBar.SetStatusBarText("Connected to OpenPLC");
        });
        Debug.Log("WebSocket Connected");
        reconnectDelay = 1f; // Reset delay on successful connection
    }

    private void OnDisconnected()
    {
        MainThreadDispatcher.Enqueue(() =>
        {
            panelStatusBar.SetStatusBarText("Disconnected from OpenPLC");
        });
        Debug.Log("WebSocket Disconnected");
        reconnectDelay = Mathf.Min(reconnectDelay * 2, 30f); // Exponential backoff, max 30s
    }

    private void ConfigFileLoad()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        string configFile = "config-" + sceneName + ".json";
        try
        {
            configFilePath = System.IO.Path.Combine(Application.streamingAssetsPath, configFile);
            appConfig = ConfigFileManager.LoadConfig(configFilePath);
            MapTags();
            Debug.Log($"Config file loaded: {configFilePath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"ConfigFileLoad Error: {ex.Message}");
        }
    }

    public void ConfigFileSave()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        string configFile = "config-" + sceneName + ".json";
        try
        {
            configFilePath = System.IO.Path.Combine(Application.streamingAssetsPath, configFile);
            ConfigFileManager.SaveConfig(configFilePath, appConfig);
            MainThreadDispatcher.Enqueue(() =>
            {
                panelStatusBar.SetStatusBarText($"Configuration saved to {configFile}.");
            });
        }
        catch (Exception ex)
        {
            Debug.LogError("Error saving configuration: " + ex.Message);
            throw;
        }
    }

    private void MapTags()
    {
        foreach (var entry in appConfig.InputVariableMap)
        {
            inputTagToAddress[entry.Key] = entry.Value;
        }
        foreach (var entry in appConfig.OutputVariableMap)
        {
            outputTagToAddress[entry.Key] = entry.Value;
        }
    }

    void OnApplicationQuit()
    {
        if (ws != null && ws.ReadyState != WebSocketState.Closed)
        {
            ws.Close();
        }
        Debug.Log("Application quitting, WebSocket disconnected");
    }
}
