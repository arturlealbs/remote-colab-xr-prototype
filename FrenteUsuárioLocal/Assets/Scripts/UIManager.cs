using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public CameraTransmitter transmitter;
    public TMP_InputField ipInputField;
    public Button startButton;
    public Button stopButton;
    public Button resetButton;

    void Start()
    {
        if (PlayerPrefs.HasKey("ServerIP"))
        {
            ipInputField.text = PlayerPrefs.GetString("ServerIP");
        }

        startButton.onClick.AddListener(OnStartClicked);
        stopButton.onClick.AddListener(OnStopClicked);
        resetButton.onClick.AddListener(OnResetClicked);
        
        stopButton.interactable = false;
        resetButton.interactable = false;
    }

    void OnStartClicked()
    {
        string ip = ipInputField.text;
        PlayerPrefs.SetString("ServerIP", ip);
        PlayerPrefs.Save();
        
        transmitter.StartTransmission(ip);
        
        startButton.interactable = false;
        ipInputField.interactable = false;
        stopButton.interactable = true;
        resetButton.interactable = true;
    }

    void OnStopClicked()
    {
        transmitter.StopTransmission();
        
        startButton.interactable = true;
        ipInputField.interactable = true;
        stopButton.interactable = false;
        resetButton.interactable = false;
    }

    void OnResetClicked()
    {
        transmitter.SendResetCommand();
    }
}
