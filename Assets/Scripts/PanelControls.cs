using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class PanelControls : MonoBehaviour
{
    public Communication communication;
    public GameObject workpiece;
    public Transform spawnPoint;
    public Transform lightRed;
    public Transform lightGreen;
    public Transform lightBlueTop;
    public Transform lightBlueBottom;
       

    void Start()
    {
        
        // Initialize all panel inputs to false (buttons, toggle switch)
        communication.WriteDiscreteInput("ToggleSwitch", 0);
        communication.WriteDiscreteInput("ButtonRed", 0);
        communication.WriteDiscreteInput("ButtonGreen", 0);
        communication.WriteDiscreteInput("ButtonBlackTopLeft", 0);
        communication.WriteDiscreteInput("ButtonBlackTopRight", 0);
        communication.WriteDiscreteInput("ButtonBlackBottomLeft", 0);
        communication.WriteDiscreteInput("ButtonBlackBottomRight", 0);

        workpiece.SetActive(false);
    }

    void Update()
    {
        // Control lights
        lightRed.GetComponent<Toggle>().isOn = communication.ReadCoil("LightRed");
        lightGreen.GetComponent<Toggle>().isOn = communication.ReadCoil("LightGreen");
        lightBlueTop.GetComponent<Toggle>().isOn = communication.ReadCoil("LightBlueTop");
        lightBlueBottom.GetComponent<Toggle>().isOn = communication.ReadCoil("LightBlueBottom");
    }

    public void CreateNewWorkpiece()
    {
        Vector3 pos = spawnPoint.position;
        GameObject clone = Instantiate(workpiece, new Vector3(pos.x, pos.y, pos.z), Quaternion.identity);
        clone.tag = "Workpiece";
        clone.SetActive(true);
    }
    public void RemoveWorkpiece()
    {
        GameObject[] workPiece = GameObject.FindGameObjectsWithTag("Workpiece");
        if (workPiece != null && workPiece.Length > 0)
        {
            Destroy(workPiece[0]);
        }
    }

    public void ResetScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(scene.name);
    }

    public void LoadStartMenuScene()
    {
        SceneManager.LoadScene("StartMenu");
    }

    public void ToggleSwitchOnChange(Toggle change)
    {
        communication.WriteDiscreteInput("ToggleSwitch", change.isOn ? 1:0);
    }
    public void ButtonRedOnChange(Toggle change)
    {
        communication.WriteDiscreteInput("ButtonRed", change.isOn ? 1:0);
    }
    public void ButtonGreenOnChange(Toggle change)
    {
        communication.WriteDiscreteInput("ButtonGreen", change.isOn ? 1:0);
    }
    public void ButtonBlackTopLeftOnChange(Toggle change)
    {
        communication.WriteDiscreteInput("ButtonBlackTopLeft", change.isOn ? 1:0);
    }
    public void ButtonBlackTopRightOnChange(Toggle change)
    {
        communication.WriteDiscreteInput("ButtonBlackTopRight", change.isOn ? 1:0);
    }
    public void ButtonBlackBottomLeftOnChange(Toggle change)
    {
        communication.WriteDiscreteInput("ButtonBlackBottomLeft", change.isOn ? 1:0);
    }
    public void ButtonBlackBottomRightOnChange(Toggle change)
    {
        communication.WriteDiscreteInput("ButtonBlackBottomRight", change.isOn ? 1:0);
    }
}
