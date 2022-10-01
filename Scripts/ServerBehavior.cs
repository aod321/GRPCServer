using System;
using System.Threading.Tasks;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Google.Rpc;
using Grpc.Core;
using Environment = DmEnvRpc.V1.Environment;

public class ServerBehavior : MonoBehaviour
{
    public static ServerBehavior _instance;
    private static Thread _mainThread;

    private static readonly ConcurrentQueue<System.Action> _updateActions = new ConcurrentQueue<System.Action>();
    private static readonly ConcurrentQueue<System.Action> _postRenderActions = new ConcurrentQueue<System.Action>();

    private Server server = null;
    
    public GameObject agentPrefab;
    public GameObject foodPrefab;
    public static GameObject agentPrefabStatic;
    public static GameObject foodPrefabStatic;
    public string Host = "localhost";
    public int Port = 30051;
    public int timeScale = 2;
    public GridRender gridRender;
    public static GridRender gridRenderStatic;

    
    public static bool isMainThread
    {
        get
        {
            return Thread.CurrentThread == _mainThread;
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        Debug.Log("Starting gRPC server ...");
        string[] args = System.Environment.GetCommandLineArgs ();
        // string[] args = new string[] {"-Host", "localhost", "-Port", "30052"};
        var port_1 = 0;
        var host_1 = "";
        for (int i = 0; i < args.Length; i++) {
            if (args [i] == "--Port")
            {
                int.TryParse(args[i + 1], out port_1);
                //Debug.Log(port_1);
            }
            if (port_1 != 0)
            {
                Port = port_1;
            }
            else
            {
                Port = 30051;
            }
            if (args [i] == "--Host") {
                host_1 = args [i + 1];
                //Debug.Log(host_1);
            }
            if (args [i] == "--TimeScale") {
                int.TryParse(args[i + 1], out timeScale);
                if (timeScale == 0) timeScale = 2;
                //Debug.Log(timeScale);
            }
        }
        Camera.onPostRender += OnPostRenderCallback;
        StartGrpcServer(Camera.main);
        Time.timeScale = timeScale;
    }

    void Update()
    {
        Time.timeScale = timeScale;
        System.Action action;
        // ////Debug.Log("updateActions count: " + _updateActions.Count);
        if(_updateActions.TryDequeue(out action)) {
            action();
        } else {
            // ////Debug.Log("Update: failed to dequeue action");
        }

    }

    void Awake()
    {
        #if UNITY_EDITOR
            Debug.unityLogger.logEnabled = true;
        #else
            Debug.unityLogger.logEnabled = false;
        #endif

        if (_instance) {
            DestroyImmediate(this);
        }
        else {
            _instance = this;
            _mainThread = Thread.CurrentThread;
            DontDestroyOnLoad(this);
        }
        agentPrefabStatic = agentPrefab;
        foodPrefabStatic = foodPrefab;
        gridRenderStatic = gridRender;
    }

    void OnDestroy()
    {
        Camera.onPostRender -= OnPostRenderCallback;
        if (_instance == this) {
            _instance = null;
        }
    }
    
    public void StartGrpcServer(Camera camera)
    {
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        try
        {
            RunServer(camera);
        }
        catch (System.Exception e)
        {
            Debug.Log("Failed to start GameServer: " + e.Message);
        }
    }

    // private void OnApplicationQuit()
    // {
    //     //Debug.Log("退出释放中....");
    //     server.ShutdownAsync();
    //     server = null;
    //     System.GC.Collect();
    // }

    private void RunServer(Camera camera)
    {
        if (null == server)
        {
            server = new Server
            {
                Services = { Environment.BindService(new GameServer()) },
                Ports = { new ServerPort(Host, Port, ServerCredentials.Insecure) }
            };

            Debug.Log("Starting Grpc Gaming Server: " + Host + ":" + Port);
            server.Start();
            Debug.Log("Started Grpc Gaming Server: " + Host + ":" + Port);
        }
    }

    public static void EnqueueUpdateAction(System.Action action)
    {
        if(_instance) {
            _updateActions.Enqueue(action);
        } else {
            ////Debug.Log("EnqueueUpdateAction: _instance is null");
        }
    }
                                                                                        
    public static void InvokeUpdate(System.Action action)
    {
        var sem = new SemaphoreSlim(0, 1);
        ServerBehavior.EnqueueUpdateAction(() => {
            try
            {
                action();
            }
            finally {
               sem.Release();
            }
        });

        sem.Wait();
        // ////Debug.Log("Semaphore Released");
    }

    public static async Task InvokeUpdateAsync(System.Action action)
    {
        var sem = new SemaphoreSlim(0, 1);
        ServerBehavior.EnqueueUpdateAction(() => {
            try {
                action();
            }
            finally {
                sem.Release();
            }
        });

        await sem.WaitAsync();
    }

    public static void EnqueueOnPostRender(System.Action action)
    {
        if(_instance) {
            _postRenderActions.Enqueue(action);
        }
    }
                                                                                        
    public static void InvokeOnPostRender(System.Action action)
    {
        var sem = new SemaphoreSlim(0, 1);
        ServerBehavior.EnqueueOnPostRender(() => {
            try {
                action();
            }
            finally {
                sem.Release();
            }
        });

        sem.Wait();
        ////Debug.Log("Semaphore Released");
    }

    public static async Task InvokeOnPostRenderAsync(System.Action action)
    {
        var sem = new SemaphoreSlim(0, 1);
        ServerBehavior.EnqueueOnPostRender(() => {
            try {
                action();
            }
            finally {
                sem.Release();
            }
        });
        
        await sem.WaitAsync();
    }

    void OnPostRenderCallback(Camera cam)
    {
        // Debug.Log("OnPostRenderCallback: " + cam.name);
            System.Action action;
            while (_postRenderActions.TryDequeue(out action))
            {
                action();
            }
    }
}
