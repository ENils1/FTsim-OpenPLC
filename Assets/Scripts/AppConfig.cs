using System;
using System.Collections.Generic;

[Serializable]
public class AppConfig
{
    public string ip;
    public int port;
    public bool showFPS;
    public Dictionary<string, string> InputVariableMap;
    public Dictionary<string, string> OutputVariableMap;
    public Dictionary<string, string> TrainingModelSpecific;
    public List<int[]> SmartBarGrid;
}