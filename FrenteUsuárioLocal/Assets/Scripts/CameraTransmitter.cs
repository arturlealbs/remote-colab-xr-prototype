using UnityEngine;
using System;
using System.Threading;
using System.Collections.Concurrent;
using NetMQ;
using NetMQ.Sockets;
using System.Text;

public class CameraTransmitter : MonoBehaviour
{
    [Header("Network")]
    public string serverIP = "192.168.0.100";
    public string serverPort = "5556";
    
    [Header("MRUK Passthrough")]
    public MonoBehaviour cameraAccess; // Arraste o componente PassthroughCameraAccess aqui
    public Material cameraMaterial; // Onde a câmera será renderizada
    
    private Thread sendThread;
    private bool isRunning = false;
    private ConcurrentQueue<byte[]> packetQueue = new ConcurrentQueue<byte[]>();

    private Texture2D captureTexture;
    private RenderTexture renderTexture;

    public void StartTransmission(string ip)
    {
        serverIP = ip;
        isRunning = true;
        
        AsyncIO.ForceDotNet.Force();
        sendThread = new Thread(SendLoop);
        sendThread.IsBackground = true;
        sendThread.Start();
        Debug.Log("Transmissão iniciada para: " + ip);
    }

    public void StopTransmission()
    {
        isRunning = false;
        if (sendThread != null) sendThread.Join(1000);
        NetMQConfig.Cleanup();
        Debug.Log("Transmissão parada.");
    }

    public void SendResetCommand()
    {
        string headerJson = "{\"cmd\": \"reset\"}";
        byte[] headerBytes = Encoding.UTF8.GetBytes(headerJson);
        byte[] headerSize = BitConverter.GetBytes(headerBytes.Length);
        
        byte[] packet = new byte[4 + headerBytes.Length];
        Buffer.BlockCopy(headerSize, 0, packet, 0, 4);
        Buffer.BlockCopy(headerBytes, 0, packet, 4, headerBytes.Length);
        
        packetQueue.Enqueue(packet);
        Debug.Log("Comando de Reset SLAM na fila!");
    }

    void Update()
    {
        if (!isRunning || cameraAccess == null || cameraMaterial == null || cameraMaterial.mainTexture == null) return;
        
        // Pega 1 frame a cada 6 (~15 FPS num Quest que roda a 90hz) para não engasgar o SLAM
        if (Time.frameCount % 6 != 0) return; 

        Texture sourceTexture = cameraMaterial.mainTexture;
        int width = sourceTexture.width;
        int height = sourceTexture.height;

        if (renderTexture == null || renderTexture.width != width || renderTexture.height != height)
        {
            renderTexture = new RenderTexture(width, height, 0);
            captureTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
        }

        // Puxa a textura da GPU para CPU (o que demora um pouquinho, mas é necessário)
        Graphics.Blit(sourceTexture, renderTexture);
        RenderTexture.active = renderTexture;
        captureTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        captureTexture.Apply();
        RenderTexture.active = null;

        // Comprime
        byte[] jpgBytes = captureTexture.EncodeToJPG(80);

        // Pega Gravity e Timestamp
        Vector3 gravity = Physics.gravity;
        double timestamp = Time.timeAsDouble;
        string headerJson = $"{{\"cmd\": \"frame\", \"timestamp\": {timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture)}, \"gravity\": [{gravity.x.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {gravity.y.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {gravity.z.ToString(System.Globalization.CultureInfo.InvariantCulture)}]";

        // Tenta usar Reflection para extrair as Intrinsics reais sem causar erro de compilação
        // caso o MRUK mude de nome os atributos
        try 
        {
            var intrinsics = cameraAccess.GetType().GetProperty("Intrinsics")?.GetValue(cameraAccess);
            if (intrinsics != null) 
            {
                var focalLength = (Vector2)intrinsics.GetType().GetField("FocalLength").GetValue(intrinsics);
                var principalPoint = (Vector2)intrinsics.GetType().GetField("PrincipalPoint").GetValue(intrinsics);
                headerJson += $", \"intrinsics\": [{focalLength.x.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {focalLength.y.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {principalPoint.x.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {principalPoint.y.ToString(System.Globalization.CultureInfo.InvariantCulture)}]";
            }
        } catch { }

        headerJson += "}";

        // Monta o Pacote ZMQ
        byte[] headerBytes = Encoding.UTF8.GetBytes(headerJson);
        byte[] headerSize = BitConverter.GetBytes(headerBytes.Length);

        byte[] packet = new byte[4 + headerBytes.Length + jpgBytes.Length];
        Buffer.BlockCopy(headerSize, 0, packet, 0, 4);
        Buffer.BlockCopy(headerBytes, 0, packet, 4, headerBytes.Length);
        Buffer.BlockCopy(jpgBytes, 0, packet, 4 + headerBytes.Length, jpgBytes.Length);

        // Envia apenas se a fila estiver livre (backpressure)
        if (packetQueue.Count < 2)
        {
            packetQueue.Enqueue(packet);
        }
    }

    private void SendLoop()
    {
        string address = $"tcp://{serverIP}:{serverPort}";
        using (var client = new RequestSocket())
        {
            client.Connect(address);
            
            while (isRunning)
            {
                if (packetQueue.TryDequeue(out byte[] packet))
                {
                    client.SendFrame(packet);
                    string resp = client.ReceiveFrameString();
                    Debug.Log("ZMQ TX OK: " + resp);
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
        }
    }

    void OnDestroy()
    {
        StopTransmission();
    }
}
