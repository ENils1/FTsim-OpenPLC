using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using EasyModbus;

public class Communication : MonoBehaviour
{
    private ModbusClient modbusClient;
    public AppConfig appConfig;
    public StatusBar panelStatusBar;
    private bool isConnectedToPlc = false;
    private Dictionary<string, int> outputTagToAddress = new();
    private Dictionary<string, int> inputTagToAddress = new();

    void Awake()
    {
        Application.runInBackground = true;
        Debug.Log("ModbusCommunication: Connecting to Modbus PLC...");

        try
        {
            ConfigFileLoad();
            PlcConnect();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error during initialization: {ex.Message}");
        }
    }

    void Start()
    {
        InvokeRepeating(nameof(CheckConnection), 1f, 5f);
    }

    private void PlcConnect()
    {
        try
        {
            modbusClient = new ModbusClient("127.0.0.1", 502);
            modbusClient.Connect();
            
            if (modbusClient.Connected)
            {
                isConnectedToPlc = true;
                panelStatusBar.SetStatusBarText("Connected to Modbus PLC");
                Debug.Log("Connected to Modbus PLC");
            }
            else
            {
                throw new Exception("Failed to connect to Modbus PLC");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"PlcConnect: {ex.Message}");
            isConnectedToPlc = false;
        }
    }

    public bool ReadCoil(string tag)
    {
        try
        {
            if (!isConnectedToPlc) return false;

            int address = inputTagToAddress[tag];
            bool[] coilStatus = modbusClient.ReadCoils(address, 1);
            Debug.Log($"Read Coil {tag} ({address}): {coilStatus[0]}");
            return coilStatus[0];
        }
        catch (Exception ex)
        {
            Debug.LogError($"ReadCoil Error: {ex.Message}");
            return false;
        }
    }

    public int ReadRegister(string tag)
    {
        try
        {
            if (!isConnectedToPlc) return 0;

            int address = inputTagToAddress[tag];
            int[] registerValue = modbusClient.ReadHoldingRegisters(address, 1);
            Debug.Log($"Read Register {tag} ({address}): {registerValue[0]}");
            return registerValue[0];
        }
        catch (Exception ex)
        {
            Debug.LogError($"ReadRegister Error: {ex.Message}");
            return 0;
        }
    }

    public void WriteCoil(string tag, int value)
    {
        try
        {
            if (!isConnectedToPlc) return;

            int address = outputTagToAddress[tag];
            bool val = value > 0;
            
            modbusClient.WriteSingleCoil(address, val);
            Debug.Log($"Wrote Coil {tag} ({address}): {val}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"WriteCoil Error: {ex.Message}");
        }
    }

    public void WriteRegister(string tag, int value)
    {
        try
        {
            if (!isConnectedToPlc) return;

            int address = outputTagToAddress[tag];
            modbusClient.WriteSingleRegister(address, value);
            Debug.Log($"Wrote Register {tag} ({address}): {value}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"WriteRegister Error: {ex.Message}");
        }
    }

    private void CheckConnection()
    {
        if (!modbusClient.Connected)
        {
            Debug.LogWarning("Lost connection to Modbus PLC. Reconnecting...");
            PlcConnect();
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
        }
        catch (Exception ex)
        {
            Debug.LogError($"ConfigFileLoad Error: {ex.Message}");
        }
    }

    private void MapTags()
    {
        foreach (var entry in appConfig.InputVariableMap)
        {
            inputTagToAddress[entry.Key] = int.Parse(entry.Value);
        }

        foreach (var entry in appConfig.OutputVariableMap)
        {
            outputTagToAddress[entry.Key] = int.Parse(entry.Value);
        }
    }

    void OnApplicationQuit()
    {
        if (modbusClient != null && modbusClient.Connected)
        {
            modbusClient.Disconnect();
            Debug.Log("Disconnected from Modbus PLC");
        }
    }
}