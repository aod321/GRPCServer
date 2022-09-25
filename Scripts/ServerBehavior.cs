using System.Threading.Tasks;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.SceneManagement;
using Google.Rpc;
using Grpc.Core;
using DmEnvRpc.V1;

public class ServerBehavior : MonoBehaviour
{
    public static ServerBehavior _instance;
    private static Thread _mainThread;

    private static readonly ConcurrentQueue<System.Action> _updateActions = new ConcurrentQueue<System.Action>();
    private static readonly ConcurrentQueue<System.Action> _postRenderActions = new ConcurrentQueue<System.Action>();

    private Server server = null;

    public GameObject agentPrefab;
    public GameObject spherePrefab;
    
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

        Camera.onPostRender += OnPostRenderCallback;
        
        StartGrpcServer(Camera.main);
        // Time.timeScale = 2;
    }

    void Update()
    {
        System.Action action;
        // Debug.Log("updateActions count: " + _updateActions.Count);
        if(_updateActions.TryDequeue(out action)) {
            action();
        } else {
            // Debug.Log("Update: failed to dequeue action");
        }
    }

    void Awake()
    {
        if (_instance) {
            DestroyImmediate(this);
        }
        else {
            _instance = this;
            _mainThread = Thread.CurrentThread;
            DontDestroyOnLoad(this);
        }
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

    private void RunServer(Camera camera)
    {
        const int Port = 30051;
        const string Host = "localhost";
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
            Debug.Log("EnqueueUpdateAction: _instance is null");
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
        // Debug.Log("Semaphore Released");
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
        Debug.Log("Semaphore Released");
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
        while(_postRenderActions.TryDequeue(out action)) {
            action();
        }
    }
}
