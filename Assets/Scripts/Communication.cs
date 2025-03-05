using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using WebSocketSharp;
using UnityEngine.SceneManagement;

public class Communication : MonoBehaviour
{
    public AppConfig appConfig;
    public StatusBar panelStatusBar;

    private Dictionary<string, string> outputTagToAddress = new();
    private Dictionary<string, string> inputTagToAddress = new();
    private Dictionary<string, bool> coilValues = new();
    private Dictionary<string, int> discreteValues = new();
    private readonly object coilValuesLock = new();

    private WebSocket ws;
    private float lastMessageReceivedTime;
    private const float MESSAGE_TIMEOUT = 3f; // Timeout if no message is received within 3 seconds.

    public bool IsConnected => ws != null && ws.ReadyState == WebSocketState.Open;

    void Awake()
    {
        Application.runInBackground = true;
        Debug.Log("Communication: Initializing...");
        try
        {
            ConfigFileLoad();
            InitializeWebSocket();
            Application.targetFrameRate = appConfig.MaxFPS;
            QualitySettings.vSyncCount = appConfig.vSync ? 1 : 0;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error during initialization: {ex.Message}");
        }
    }

    void Start()
    {
        ws = new WebSocket($"ws://{appConfig.ip}:{appConfig.port}");

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
            MainThreadDispatcher.Enqueue(() =>
            {
                lastMessageReceivedTime = Time.time; // Update last received message timestamp.
            });

            ProcessMessage(e.Data);
        };

        ws.ConnectAsync();

        StartCoroutine(TimeoutChecker()); // Start the timeout monitoring coroutine.
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
        MainThreadDispatcher.Enqueue(() =>
        {
            panelStatusBar.SetStatusBarText("Connected to OpenPLC");
            lastMessageReceivedTime = Time.time; // Reset timeout timer on connect.
        });
        Debug.Log("WebSocket Connected");
        
    }

    private void OnDisconnected()
    {
        MainThreadDispatcher.Enqueue(() =>
        {
            panelStatusBar.SetStatusBarText("Disconnected from OpenPLC");
            ShowTimeoutDialog();
        });
        Debug.Log("WebSocket Disconnected");
    }

    private void ShowTimeoutDialog()
    {
        if (GameObject.FindWithTag("Dialog_error_PLC_connection") == null)
        {
            Dialog.MessageBox(
                "Dialog_error_PLC_connection",
                "Connection error",
                $"The connection with OpenPLC cannot be maintained.\nAddress: {appConfig.ip}, {appConfig.port}",
                "Retry",
                () => { Reconnect(); },
                widthMax: 300,
                heightMax: 120
            );
        }
    }

    IEnumerator TimeoutChecker()
    {
        while (true)
        {
            yield return new WaitForSeconds(1f); // Check every second.
            
            float timeSinceLastMessage = Time.time - lastMessageReceivedTime;

            if (timeSinceLastMessage > MESSAGE_TIMEOUT)
            {
                Debug.LogWarning("No message received for " + timeSinceLastMessage + " seconds. Showing timeout dialog.");
                OnDisconnected();
            }
        }
    }

    private void ConfigFileLoad()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        string configFile = "config-" + sceneName + ".json";
        try
        {
            string configFilePath = System.IO.Path.Combine(Application.streamingAssetsPath, configFile);
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
            string configFilePath = System.IO.Path.Combine(Application.streamingAssetsPath, configFile);
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
