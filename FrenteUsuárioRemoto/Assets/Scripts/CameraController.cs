using UnityEngine;

public class CameraController : MonoBehaviour
{
    [Header("Configurações de Movimento")]
    public float movementSpeed = 10f;
    public float fastMovementMultiplier = 3f;
    public float sensitivity = 3f;

    [Header("Configurações de Rotação")]
    public bool invertY = false;

    [Header("Modo Tracking (SLAM)")]
    public bool isTrackingMode = false;

    private float rotationX = 0f;
    private float rotationY = 0f;

    void Start()
    {
        // Trava o mouse no centro da tela e o esconde
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        SyncRotationToCamera();
    }

    void Update()
    {
        // Alternar entre modo Free Cam e Tracking
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            isTrackingMode = !isTrackingMode;
            Debug.Log("Modo da Câmera: " + (isTrackingMode ? "Tracking SLAM" : "Free Cam"));

            // Quando voltar para a Free Cam, sincroniza os ângulos para a câmera não dar um pulo brusco
            if (!isTrackingMode)
            {
                SyncRotationToCamera();
            }
        }

        // Atalho para liberar o mouse (ESC)
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Só permite input manual se não estiver no modo tracking
        if (!isTrackingMode)
        {
            HandleRotation();
            HandleMovement();
        }
    }

    void HandleRotation()
    {
        float mouseX = Input.GetAxis("Mouse X") * sensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * sensitivity;

        rotationY += mouseX;
        rotationX += invertY ? mouseY : -mouseY;

        // Limita a rotação vertical para evitar que a câmera "capote"
        rotationX = Mathf.Clamp(rotationX, -90f, 90f);

        transform.localRotation = Quaternion.Euler(rotationX, rotationY, 0f);
    }

    void HandleMovement()
    {
        float currentSpeed = movementSpeed;

        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            currentSpeed *= fastMovementMultiplier;
        }

        float moveForward = Input.GetAxis("Vertical");
        float moveSide = Input.GetAxis("Horizontal");

        float moveUp = 0f;
        if (Input.GetKey(KeyCode.E)) moveUp = 1f;
        if (Input.GetKey(KeyCode.Q)) moveUp = -1f;

        Vector3 direction = new Vector3(moveSide, moveUp, moveForward);

        transform.Translate(direction * currentSpeed * Time.deltaTime, Space.Self);
    }

    // Função auxiliar para evitar saltos bruscos na rotação ao sair do modo Tracking
    private void SyncRotationToCamera()
    {
        Vector3 currentRotation = transform.localRotation.eulerAngles;
        rotationY = currentRotation.y;
        rotationX = currentRotation.x;
        if (rotationX > 180f) rotationX -= 360f; // Normaliza o ângulo para o Clamp não bugar
    }

    // Função que será chamada pelo GPUFrameReader para injetar a pose do SLAM
    public void SetTrackingPose(Vector3 position, Quaternion rotation)
    {
        if (isTrackingMode)
        {
            transform.position = position;
            transform.rotation = rotation;
        }
    }
}