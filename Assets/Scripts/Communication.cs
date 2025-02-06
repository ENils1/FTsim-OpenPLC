using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;


public class Communication : MonoBehaviour
{
    private string configFilePath;
    public AppConfig appConfig;
    public StatusBar panelStatusBar;

    // Mapper for input- og output-tags
    private Dictionary<string, string> outputTagToAddress = new();
    private Dictionary<string, string> inputTagToAddress = new();
    
    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    // Brukes for å lagre mottatte coil-verdier
    private Dictionary<string, bool> coilValues = new();
    private Dictionary<string, int> discreteValues = new();
    // Lås for trådsikker tilgang til coilValues
    private readonly object coilValuesLock = new();

    // Event for mottatte meldinger
    public event Action<string> MessageReceived;

    public bool IsConnected => client != null && client.Connected;

    void Awake()
    {
        Application.runInBackground = true;
        Debug.Log("Communication: Connecting to OpenPLC...");
        try
        {
            ConfigFileLoad();
            TCPConnect();
            
            UpdateDiscreteValues();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error during initialization: {ex.Message}");
        }
    }

    void Start()
    {
        InvokeRepeating(nameof(CheckConnection), 5f, 5f);
        MessageReceived += OnMessageReceived;
    }
    
    
    public void TCPConnect()
    {   
        try
        {
            client = new TcpClient();
            client.Connect(appConfig.ip, appConfig.port);
            stream = client.GetStream();
            
            receiveThread = new Thread(ReceiveLoop);
            receiveThread.IsBackground = true;
            receiveThread.Start();
            
            //Debug.Log("TCP CONNECTED...");
            panelStatusBar.SetStatusBarText("Connected to OpenPLC");

        }
        catch (Exception ex)
        {
            Debug.LogError("Error connecting: " + ex.Message);
        }
    }

    private void UpdateDiscreteValues()
    {
        
        WriteDiscreteInput("PhotocellEntry", 1);
        WriteDiscreteInput("PhotocellBelt1", 1);
        WriteDiscreteInput("PhotocellBelt2", 1);
        WriteDiscreteInput("PhotocellBelt3", 1);
        WriteDiscreteInput("PhotocellExit", 1);
        WriteDiscreteInput("SwitchPusher1Start", 1);
        WriteDiscreteInput("SwitchPusher2Start", 1);
        
    }
    
    private void ReceiveLoop()
    {
        try
        {
            byte[] buffer = new byte[1024];
            while (true)
            {
                // Blokkerende kall: leser data fra stream
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    // Forbindelsen ble avsluttet fra serverens side
                    break;
                }
                // Konverter mottatte bytes til tekst (for eksempel JSON)
                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                // Utløser eventen dersom noen har abonnert på den
                MessageReceived?.Invoke(message);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error receiving data: " + ex.Message);
            
        }
        finally
        {
            Disconnect();
        }
    }

    public void SendMessage(string message)
    {
        if (IsConnected)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            try
            {
                stream.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error sending message: " + ex.Message);
            }
        }
        else
        {
            Console.WriteLine("Cannot send message. Not connected.");
        }
    }
    
    public void Disconnect()
    {
        try
        {
            stream?.Close();
            client?.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error during disconnect: " + ex.Message);
        }
        Console.WriteLine("Disconnected from server.");
    }
    
    
    public void WriteDiscreteInput(string tag, int value)
    {   
        string address = inputTagToAddress[tag];
        discreteValues[tag] = value;
        var command = new
        {
            action = "set_IX",
            address,
            value
        };
        string jsonCommand = JsonConvert.SerializeObject(command);
        SendMessage(jsonCommand);
        //Debug.Log("Sent command: " + jsonCommand);
    }
    
    private void OnMessageReceived(string message)
    {
        //Debug.Log("Received: " + message);
        try
        {
            // Deserialiser JSON-strengen til en dictionary
            var data = JsonConvert.DeserializeObject<Dictionary<string, bool>>(message);
            if (data != null)
            {
                foreach (var entry in data)
                {
                    // Oppdaterer coilValues med hver key/value-par
                    coilValues[entry.Key] = entry.Value;
                    //Debug.Log($"Output update: {entry.Key} = {entry.Value}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("Error parsing JSON: " + ex.Message);
        }
    }
    
    public bool ReadCoil(string tag)
    {
        lock (coilValuesLock)
        {
            string address = outputTagToAddress[tag];
            if (coilValues.ContainsKey(address))
            {
                bool value = coilValues[address];
                //Debug.Log($"Coil {address} value: {value}");
                return value;
            } 
        }
        
        //Debug.LogWarning($"Coil {tag} not found (ingen oppdatering mottatt ennå).");
        return false;
    }

    

    private void CheckConnection()
    {
        if (client == null || !client.Connected)
        {
            Debug.LogWarning("Lost connection to OpenPLC. Reconnecting...");
            if (GameObject.FindWithTag("Dialog_error_PLC_connection") == null)
            {
                Dialog.MessageBox(
                    "Dialog_error_PLC_connection",
                    "Connection error",
                    $"The connection with OpenPLC cannot be established. Address in the config file is:\n{appConfig.ip}, {appConfig.port}",
                    "Retry", () => { TCPConnect(); }, widthMax: 300, heightMax: 120
                );
            }
        }
        else
        {
            GameObject errorDialog = GameObject.Find("Dialog_error_PLC_connection");
            if (errorDialog != null)
            {
                Destroy(errorDialog);
            }
        }
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
            panelStatusBar.SetStatusBarText($"Configuration saved to {configFile}.");
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
        if (client != null && client.Connected)
        {
            Disconnect();
            Debug.Log("Disconnected from OpenPLC");
        }
    }
}