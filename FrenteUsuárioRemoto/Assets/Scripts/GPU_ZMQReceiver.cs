using System;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;
using NetMQ;
using NetMQ.Sockets;

public class GPU_ZMQReceiver : MonoBehaviour
{
    [Header("Rede (ZeroMQ)")]
    public string serverAddress = "tcp://localhost:5555";
    private bool isRunning = true;

    [Header("Renderização GPU")]
    public Material pointCloudMaterial;
    public int maxPoints = 5000000;
    public CameraController slamCamera;
    
    [Header("Compute Shader")]
    public ComputeShader voxelizerShader;
    public float voxelSize = 0.05f; 

    [StructLayout(LayoutKind.Sequential)]
    private struct PointData
    {
        public Vector3 position;
        public Vector3 color;
        public uint isActive;
    }

    private ComputeBuffer pointBuffer;
    private ComputeBuffer tempPointBuffer;
    private Mesh quadMesh;

    private Thread receiveThread;
    
    // Dados para transferir da Thread pra MainThread
    private List<Vector3> tempPoints = new List<Vector3>();
    private List<Vector3> tempColors = new List<Vector3>();
    private Matrix4x4 currentT_WC;
    private bool frameReadyToRender = false;

    void Start()
    {
        Application.runInBackground = true; // Impede a Unity de congelar quando perde o foco!
        
        AsyncIO.ForceDotNet.Force();

        pointBuffer = new ComputeBuffer(maxPoints, 28);
        tempPointBuffer = new ComputeBuffer(300000, 28);
        quadMesh = CreateQuadMesh();

        // Limpa a grid
        PointData[] emptyData = new PointData[maxPoints];
        pointBuffer.SetData(emptyData);

        receiveThread = new Thread(ReceiveData);
        receiveThread.IsBackground = true;
        receiveThread.Start();

        Debug.Log($"Servidor ZMQ escutando em {serverAddress}...");
    }

    private void ReceiveData()
    {
        using (var subSocket = new SubscriberSocket())
        {
            subSocket.Options.ReceiveHighWatermark = 1000;
            subSocket.Connect(serverAddress);
            subSocket.Subscribe(""); // Inscreve em todos os tópicos

            while (isRunning)
            {
                try
                {
                    byte[] data;
                    if (subSocket.TryReceiveFrameBytes(TimeSpan.FromMilliseconds(500), out data))
                    {
                        // Formato:
                        // 0-3: frameId (int)
                        // 4-7: numPoints (int)
                        // 8-71: Matriz T_WC (16 floats)
                        // 72 - ... : Pontos XYZ e Cores RGB

                        int frameId = BitConverter.ToInt32(data, 0);
                        int numPoints = BitConverter.ToInt32(data, 4);

                        Matrix4x4 t_wc = new Matrix4x4();
                        for (int i = 0; i < 16; i++)
                        {
                            t_wc[i] = BitConverter.ToSingle(data, 8 + (i * 4));
                        }
                        
                        List<Vector3> pts = new List<Vector3>(numPoints);
                        int offset = 72;
                        for (int i = 0; i < numPoints; i++)
                        {
                            float px = BitConverter.ToSingle(data, offset);
                            float py = BitConverter.ToSingle(data, offset + 4);
                            float pz = BitConverter.ToSingle(data, offset + 8);
                            pts.Add(new Vector3(px, -py, pz)); 
                            offset += 12;
                        }

                        List<Vector3> cols = new List<Vector3>(numPoints);
                        for (int i = 0; i < numPoints; i++)
                        {
                            float r = BitConverter.ToSingle(data, offset);
                            float g = BitConverter.ToSingle(data, offset + 4);
                            float b = BitConverter.ToSingle(data, offset + 8);
                            cols.Add(new Vector3(r, g, b));
                            offset += 12;
                        }

                        // Thread safe swap
                        lock (this)
                        {
                            currentT_WC = t_wc.transpose;
                            tempPoints = pts;
                            tempColors = cols;
                            frameReadyToRender = true;
                        }
                    }
                }
                catch (Exception e)
                {
                    if (isRunning) Debug.LogWarning("ZMQ Erro: " + e.Message);
                }
            }
        }
        NetMQConfig.Cleanup();
    }

    void Update()
    {
        bool shouldRender = false;
        List<Vector3> ptsToRender = null;
        List<Vector3> colsToRender = null;
        Matrix4x4 t_wcToRender = Matrix4x4.identity;

        lock (this)
        {
            if (frameReadyToRender)
            {
                shouldRender = true;
                ptsToRender = tempPoints;
                colsToRender = tempColors;
                t_wcToRender = currentT_WC;
                frameReadyToRender = false;
            }
        }

        if (shouldRender)
        {
            ProcessAndRenderFrame(ptsToRender, colsToRender, t_wcToRender);
        }

        // Renderiza Voxel Grid sempre
        if (pointBuffer != null)
        {
            pointCloudMaterial.SetBuffer("_PointBuffer", pointBuffer);
            Bounds bounds = new Bounds(Vector3.zero, new Vector3(10000f, 10000f, 10000f));
            Graphics.DrawMeshInstancedProcedural(quadMesh, 0, pointCloudMaterial, bounds, maxPoints);
        }
    }

    private void ProcessAndRenderFrame(List<Vector3> pts, List<Vector3> cols, Matrix4x4 t_wc)
    {
        Matrix4x4 conversion = Matrix4x4.Scale(new Vector3(1, -1, 1));
        Matrix4x4 finalT_WC = conversion * t_wc * conversion;

        if (slamCamera != null)
        {
            // Corrige o Follow Camera! Passando a pose real do SLAM.
            slamCamera.SetTrackingPose(finalT_WC.GetPosition(), finalT_WC.rotation);
        }

        int numNewPoints = pts.Count;
        if (numNewPoints == 0) return;

        PointData[] newPointsArray = new PointData[numNewPoints];
        for (int i = 0; i < numNewPoints; i++)
        {
            newPointsArray[i] = new PointData
            {
                position = finalT_WC.MultiplyPoint3x4(pts[i]),
                color = cols[i],
                isActive = 1
            };
        }

        tempPointBuffer.SetData(newPointsArray);

        // Dispara o Voxelizer
        int kernel = voxelizerShader.FindKernel("CSMain");
        voxelizerShader.SetBuffer(kernel, "_NewPoints", tempPointBuffer);
        voxelizerShader.SetBuffer(kernel, "_VoxelGrid", pointBuffer);
        voxelizerShader.SetInt("_NewPointsCount", numNewPoints);
        voxelizerShader.SetInt("_MaxVoxels", maxPoints);
        voxelizerShader.SetFloat("_VoxelSize", voxelSize);

        int threadGroups = Mathf.CeilToInt(numNewPoints / 256f);
        voxelizerShader.Dispatch(kernel, threadGroups, 1, 1);
    }

    private Mesh CreateQuadMesh()
    {
        Mesh mesh = new Mesh();
        mesh.vertices = new Vector3[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
        mesh.SetIndices(new int[] { 0, 2, 1, 2, 3, 1 }, MeshTopology.Triangles, 0);
        return mesh;
    }

    void OnDestroy()
    {
        isRunning = false;
        if (receiveThread != null) receiveThread.Join(1000); 
        if (pointBuffer != null) pointBuffer.Release();
        if (tempPointBuffer != null) tempPointBuffer.Release();
        NetMQConfig.Cleanup();
    }
}
