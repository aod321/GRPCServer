using System.Threading;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.SceneManagement;

using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Rpc;
using Grpc.Core;
using DmEnvRpc.V1;

class GameServer : Environment.EnvironmentBase
{
    private const int _textureWidth = 84;
    private const int _textureHeight = 84;

    private const string _targetObjectName = "Sphere01";
    
    private const string _sampleSceneName = "SampleScene";
    private const string _actionPaddle = "paddle";
    private const string _actionJump = "jump";

    private enum Action : int
    {
        Paddle = 1,
        Jump = 2,
    }
    
    private enum ActionPaddle : int
    {
        Nothing = 0,
        Left = 1,
        Right = 2,
        Forward = 3,
        Backward = 4,
    }

    private enum Observation: int
    {
        Camera = 1,
        Reward = 2,
        Collided = 3,
    }
    private const string _observationCamera = "Camera";
    private const string _observationReward = "reward";
    private const string _observationCollided = "Collided";

    private const string _validWorldSettingsPrefix = "Following Key(s) is(are) not supported in current World:";
    private const string _validAgentSettingsPrefix = "Following Key(s) is(are) not supported for current Agent:";

    private readonly HashSet<string> VALID_WORLD_SETTINGS = new HashSet<string>{"seed"};
    private readonly HashSet<string> VALID_AGENT_SETTINGS = new HashSet<string>{"seed"};

    private static object _renderingLockObject = new object();
    private static readonly Queue<System.Action> _renderingActions = new Queue<System.Action>();

    // Because _worldSettings and _worldState are accessed across multiple threads, they need
    // locks. However even using locks, it is critical to consider carefully how to use locks.
    // For example, 'Step' requires _worldState is in 'Running' state, so could _worldState be
    // be changed during this 'step' is being processed? If not, the whole step should be
    // protected  by _worldStateLock. Again, the settings (Tensor) inside _worldSettings is not
    // protected by a lock. would it be copied inside across threads ? Or locked during being
    // accessed across threads?
    private SemaphoreSlim _worldSettingsLock;
    private const int _worldSettingsLockTimeOut = 3300; // milliseconds
    private MapField<string, Tensor> _worldSettings;

    private SemaphoreSlim _worldStateLock;
    private const int _worldStateLockTimeOut = 3300; // milliseconds
    private EnvironmentStateType _worldState = EnvironmentStateType.InvalidEnvironmentState;
    
    public GameServer()
    {
        _worldSettingsLock = new SemaphoreSlim(1, 1);
        _worldStateLock = new SemaphoreSlim(1, 1);
    }
    
    public override async Task Process(Grpc.Core.IAsyncStreamReader<EnvironmentRequest> requestStream,
                                       Grpc.Core.IServerStreamWriter<EnvironmentResponse> responseStream,
                                       Grpc.Core.ServerCallContext context)
    {
        byte[] imageData = new byte[4 * _textureWidth * _textureHeight];

        string agentBaseName = context.Peer;
        int agentCount = 0;

        string agentName = "";
        var agentCameraName = "";
        
        int agentSpeed = 50;
        bool isAgentJoined = false;
        
        Debug.Log("Process: " + context.Peer);
        while (await requestStream.MoveNext())
        {
            var req = requestStream.Current;
            if(! await _PreCheckRequest(req, isAgentJoined, responseStream)) {
                continue;
            }
            switch (req.PayloadCase)
            {
                case EnvironmentRequest.PayloadOneofCase.CreateWorld:
                    {
                        Debug.Log("CreateWorld before enter state lock");
                        if(await _worldStateLock.WaitAsync(_worldStateLockTimeOut))
                        {
                            Debug.Log("CreateWorld enter state lock");
                            try
                            {
                                Debug.Log("CreateWorldRequest");
                                if(! await _ValidateSettings(req.CreateWorld.Settings, VALID_WORLD_SETTINGS, responseStream, _validWorldSettingsPrefix))
                                {
                                    continue;
                                }
                                var resp = new EnvironmentResponse
                                {
                                    CreateWorld = new CreateWorldResponse
                                    {
                                        WorldName = "world_name"
                                    }
                                };
                                Debug.Log("Loading Scene:" + _sampleSceneName);
                                await ServerBehavior.InvokeUpdateAsync(() =>
                                {
                                    SceneManager.LoadScene(_sampleSceneName);
                                });
                                await responseStream.WriteAsync(resp);
                                // According to dm_env_rpc.proto, this 'InvalidEnvironmentState
                                // should never be sent or received
                                _worldState = EnvironmentStateType.InvalidEnvironmentState;
                            } // try lock
                            finally
                            {
                                _worldStateLock.Release();
                                Debug.Log("CreateWorld exit state lock");
                            }
                        }
                        else {
                            EnvironmentResponse resp = new EnvironmentResponse
                            {
                                Error = new Google.Rpc.Status
                                {
                                    Code = (int)Google.Rpc.Code.Aborted,
                                    Message = "CreateWorld Failed: Aborted due to _worldStateLock timeout",
                                },
                            };
                            await responseStream.WriteAsync(resp);
                        }
                    }
                    break;
                case EnvironmentRequest.PayloadOneofCase.JoinWorld:
                    {
                        Debug.Log("JoinWorld before enter state lock");
                        if(await _worldStateLock.WaitAsync(_worldStateLockTimeOut))
                        {
                            Debug.Log("JoinWorld enter state lock");
                            try
                            {
                                Debug.Log("JoinWorldRequest");
                                if(! await _ValidateSettings(req.JoinWorld.Settings, VALID_AGENT_SETTINGS, responseStream, _validAgentSettingsPrefix))
                                {
                                    continue;
                                }
                                await ServerBehavior.InvokeUpdateAsync(() =>
                                {
                                    Debug.Log("JoinWorld: InvokeUpdateAsync");
                                    agentName = _GenAgentName(agentBaseName, agentCount);
                                    agentCameraName = _GenAgentCameraName(agentBaseName, agentCount);
                                    _CreateAgent(agentName, agentCameraName);
                                });

                                var specs = CurrentActionObservationSpecs();
                                EnvironmentResponse resp = new EnvironmentResponse
                                {
                                    JoinWorld = new JoinWorldResponse
                                    {
                                        Specs = specs,
                                    },
                                };
                                await responseStream.WriteAsync(resp);
                                isAgentJoined = true;

                                if(EnvironmentStateType.InvalidEnvironmentState == _worldState) {
                                    _worldState = EnvironmentStateType.Running;
                                }
                            } // try lock
                            finally
                            {
                                _worldStateLock.Release();
                                Debug.Log("JoinWorld exit state lock");
                            }
                        }
                        else {
                            EnvironmentResponse resp = new EnvironmentResponse
                            {
                                Error = new Google.Rpc.Status
                                {
                                    Code = (int)Google.Rpc.Code.Aborted,
                                    Message = "JoinWorld Failed: Aborted due to _worldStateLock timeout",
                                },
                            };
                            await responseStream.WriteAsync(resp);
                        }
                    }
                    break;
                case EnvironmentRequest.PayloadOneofCase.Step:
                    {
                        // Debug.Log("StepRequest");
                        if(await _worldStateLock.WaitAsync(_worldStateLockTimeOut))
                        {
                            try
                            {
                                var step = new StepResponse
                                {
                                    State = _worldState,
                                };
                                // According to dm_env_rpc.proto:
                                // If state is not RUNNING, the action on the next StepRequest will be
                                // ignored and the environment will transition to a RUNNING state.
                                //
                                // But I think when the world/environment state is in Non-Running state,
                                // the steps should be ignored until the world is reset in running state
                                // again, so in following code, unless the environment state is RUNNING,
                                // the StepRequest is igonred, and will not transit environment state to
                                // RUNNING state inside a StepRequest
                                // switch( _worldState)
                                // {
                                //     case EnvironmentStateType.InvalidEnvironmentState:
                                //         {
                                //             // According to dm_env_rpc.proto, this 'InvalidEnvironmentState
                                //             // should never be sent or received. So when the world state
                                //             // is InvalidEnvironmentState return error and quit
                                //             var error_resp = new EnvironmentResponse
                                //             {
                                //                 Error = new Google.Rpc.Status
                                //                 {
                                //                     Code = (int)Google.Rpc.Code.FailedPrecondition,
                                //                     Message = "World Environment is in Invalid EnvironmentState",
                                //                 },
                                //             };
                                //             await responseStream.WriteAsync(error_resp);
                                //             break;
                                //         }
                                //     case EnvironmentStateType.Terminated:
                                //         {
                                //             var error_resp = new EnvironmentResponse
                                //             {
                                //                 Error = new Google.Rpc.Status
                                //                 {
                                //                     Code = (int)Google.Rpc.Code.Aborted,
                                //                     Message = "World is terminated",
                                //                 },
                                //             };
                                //             await responseStream.WriteAsync(error_resp);
                                //             break;
                                //         }
                                //     case EnvironmentStateType.Interrupted:
                                //         {
                                //             var error_resp = new EnvironmentResponse
                                //             {
                                //                 Error = new Google.Rpc.Status
                                //                 {
                                //                     Code = (int)Google.Rpc.Code.Aborted,
                                //                     Message = "World is interrupted",
                                //                 },
                                //             };
                                //             await responseStream.WriteAsync(error_resp);
                                //             // According to dm_env_rpc.proto, Interrupted state is indicating
                                //             // The sequence was interrupted by a reset. So the step should be
                                //             // ignored and wait for world state is running again
                                //             continue;
                                //         }
                                // }
                                // if _worldState is not Running, skip this frame
                                if (EnvironmentStateType.Running == _worldState)
                                {
                                    await ServerBehavior.InvokeUpdateAsync(() =>
                                    {
                                        var agent = GameObject.Find(agentName);
                                        var colliderTrigger = agent.GetComponentInChildren(typeof(ColliderTrigger), true) as ColliderTrigger;
                                        // if agent is collided, tell client should teminated
                                        foreach(var kvp in req.Step.Actions) {
                                            // Debug.Log("actions: " + kvp.Key + ", value: " + kvp.Value);
                                            var value = kvp.Value;
                                            byte idx = 0;
                                            switch(value.PayloadCase) {
                                                case Tensor.PayloadOneofCase.Int8S:
                                                    {
                                                        var array = value.Int8S.Array;
                                                        // Debug.Log("Int8S: count: " + array.Length + " value: " + (sbyte)array[0]);
                                                        idx = array[0];
                                                    }
                                                    break;
                                                default:
                                                    continue;
                                            }

                                            switch (kvp.Key)
                                            {
                                                case (int)Action.Paddle:
                                                    {
                                                        switch(idx)
                                                        {
                                                            case (int)ActionPaddle.Left:
                                                                agent.transform.Translate(Vector3.left * agentSpeed * Time.deltaTime);
                                                                break;
                                                            case (int)ActionPaddle.Right:
                                                                agent.transform.Translate(Vector3.right * agentSpeed * Time.deltaTime);
                                                                break;
                                                            case (int)ActionPaddle.Forward:
                                                                agent.transform.Translate(Vector3.forward * agentSpeed * Time.deltaTime);
                                                                break;
                                                            case (int)ActionPaddle.Backward:
                                                                agent.transform.Translate(Vector3.back * agentSpeed * Time.deltaTime);
                                                                break;
                                                        }
                                                    }
                                                    break;
                                            }
                                        }
                                        var rewardTensor = new Tensor {
                                            Floats = new Tensor.Types.FloatArray(),
                                        };

                                        var collidedTensor = new Tensor {
                                            Int8S = new Tensor.Types.Int8Array(),
                                        };
                                        collidedTensor.Int8S.Array = ByteString.CopyFrom(new byte[1] {colliderTrigger.collided});
                                        step.Observations.Add((int)Observation.Collided, collidedTensor);
                                        rewardTensor.Floats.Array.Add((float)colliderTrigger.Reward);
                                        step.Observations.Add((int)Observation.Reward, rewardTensor);
                                        // Interrupted game if collided
                                        if (colliderTrigger.collided >= 1)
                                        {
                                            _worldState = EnvironmentStateType.Interrupted;
                                        }

                                    }); // InvokeUpdateAsync
                                } // if (EnvironmentStateType.Running == _worldState)
                                else
                                {
                                    await ServerBehavior.InvokeUpdateAsync(() =>
                                    {
                                        var agent = GameObject.Find(agentName);
                                        if(agent)
                                        {
                                            Object.Destroy(agent);
                                            agentCount ++;
                                        }
                                        agentName = _GenAgentName(agentBaseName, agentCount);
                                        agentCameraName = _GenAgentCameraName(agentBaseName, agentCount);
                                        _CreateAgent(agentName, agentCameraName);

                                    });
                                    // if the worldState is not Running, ignore current step,
                                    // and transit world state
                                    _worldState = EnvironmentStateType.Running;
                                    var rewardTensor = new Tensor {
                                        Floats = new Tensor.Types.FloatArray(),
                                    };
                                    rewardTensor.Floats.Array.Add(0.0F);
                                    step.Observations.Add((int)Observation.Reward, rewardTensor);
                                    var collidedTensor = new Tensor {
                                        Int8S = new Tensor.Types.Int8Array(),
                                    };
                                    collidedTensor.Int8S.Array = ByteString.CopyFrom(new byte[1] {0});
                                    step.Observations.Add((int)Observation.Collided, collidedTensor);
                                    step.State = _worldState;
                                     
                                }
                                 await ServerBehavior.InvokeOnPostRenderAsync(() =>
                                    {
                                        var agent = GameObject.Find(agentName);
                                        // var colliderTrigger = agent.GetComponentInChildren(typeof(ColliderTrigger), true) as ColliderTrigger;
                                        
                                        if(null == agent) {
                                            Debug.Log("No found agent:" + agentName);
                                        }
                                        var camera = agent.GetComponentInChildren(typeof(Camera), true) as Camera;
                                        Texture2D renderingTexture = new Texture2D(_textureWidth, _textureHeight, TextureFormat.RGBA32, false);

                                        var oldRT = RenderTexture.active;
                                        RenderTexture.active = camera.targetTexture;
                                        camera.Render();
                                        Rect rect = new Rect(0, 0, _textureWidth, _textureHeight);
                                        renderingTexture.ReadPixels(rect, 0, 0, false);
                                        var pixels = renderingTexture.GetPixelData<Color32>(0);
                                        // Debug.Log("pixels size: " + pixels.Length);
                                        for(var i = 0; i < pixels.Length; i ++) {
                                            var pixel = pixels[i];
                                            /// var p = ((uint)pixel.a << 24) | ((uint)pixel.r << 16) | ((uint)pixel.g << 8) | ((uint)pixel.b);
                                            imageData[i * 4 + 0] = pixel.r;
                                            imageData[i * 4 + 1] = pixel.g;
                                            imageData[i * 4 + 2] = pixel.b;
                                            imageData[i * 4 + 3] = pixel.a;
                                        }
                                        RenderTexture.active = oldRT;
                                    });

                                    var cameraTensor = new Tensor {
                                        Uint8S = new Tensor.Types.Uint8Array(),
                                    };
                                    cameraTensor.Uint8S.Array = ByteString.CopyFrom(imageData);
                                    var shape = new int[]{4, _textureWidth, _textureHeight};
                                    cameraTensor.Shape.Add(shape);
                                    step.Observations.Add((int)Observation.Camera, cameraTensor);

                                EnvironmentResponse resp = new EnvironmentResponse
                                {
                                    Step = step,
                                };
                                await responseStream.WriteAsync(resp);
                            } // try lock
                            finally
                            {
                                _worldStateLock.Release();
                            }
                        }
                        else {
                            EnvironmentResponse resp = new EnvironmentResponse
                            {
                                Error = new Google.Rpc.Status
                                {
                                    Code = (int)Google.Rpc.Code.Aborted,
                                    Message = "Step Failed: Aborted due to _worldStateLock timeout",
                                },
                            };
                            await responseStream.WriteAsync(resp);
                        }
                    }
                    break;
                case EnvironmentRequest.PayloadOneofCase.Reset:
                    {
                        Debug.Log("Reset Request");
                        if(! await _ValidateSettings(req.Reset.Settings, VALID_AGENT_SETTINGS, responseStream, _validAgentSettingsPrefix))
                        {
                            continue;
                        }
                        if(await _worldStateLock.WaitAsync(_worldStateLockTimeOut))
                        {
                            _worldState = EnvironmentStateType.Interrupted;
                            try
                            {
                                // await ServerBehavior.InvokeUpdateAsync(() =>
                                // {
                                //     var agent = GameObject.Find(agentName);
                                //     if(agent)
                                //     {
                                //         Object.Destroy(agent);
                                //         agentCount ++;
                                //     }
                                //     agentName = _GenAgentName(agentBaseName, agentCount);
                                //     agentCameraName = _GenAgentCameraName(agentBaseName, agentCount);
                                //     _CreateAgent(agentName, agentCameraName);

                                // });

                                var specs = CurrentActionObservationSpecs();
                                EnvironmentResponse resp = new EnvironmentResponse
                                {
                                    Reset = new ResetResponse
                                    {
                                        Specs = specs,
                                    }
                                };
                                await responseStream.WriteAsync(resp);
                            } // try lock
                            finally
                            {
                                _worldStateLock.Release();
                            }
                        }
                    }
                    break;
                case EnvironmentRequest.PayloadOneofCase.ResetWorld:
                    {
                        if(await _worldStateLock.WaitAsync(_worldStateLockTimeOut))
                        {
                            try
                            {
                                Debug.Log("Reset World Request");
                                if(! await _ValidateSettings(req.ResetWorld.Settings, VALID_WORLD_SETTINGS, responseStream, _validWorldSettingsPrefix))
                                {
                                    continue;
                                }
                                _worldState = EnvironmentStateType.Interrupted;
                                EnvironmentResponse resp = new EnvironmentResponse
                                {
                                    ResetWorld = new ResetWorldResponse(),
                                };
                                await ServerBehavior.InvokeUpdateAsync(() =>
                                {
                                    SceneManager.LoadScene(_sampleSceneName);
                                });
                                await responseStream.WriteAsync(resp);
                            } // try lock
                            finally
                            {
                                _worldStateLock.Release();
                            }
                        }
                        else {
                            EnvironmentResponse resp = new EnvironmentResponse
                            {
                                Error = new Google.Rpc.Status
                                {
                                    Code = (int)Google.Rpc.Code.Aborted,
                                    Message = "ResetWorld Failed: Aborted due to _worldStateLock timeout",
                                },
                            };
                            await responseStream.WriteAsync(resp);
                        }
                    }
                    break;
                case EnvironmentRequest.PayloadOneofCase.LeaveWorld:
                    {
                        Debug.Log("LeaveWorld Request");
                        EnvironmentResponse resp = new EnvironmentResponse
                        {
                            LeaveWorld = new LeaveWorldResponse(),
                        };
                        await responseStream.WriteAsync(resp);
                        isAgentJoined = false;
                    }
                    break;
                case EnvironmentRequest.PayloadOneofCase.DestroyWorld:
                    {
                        if(await _worldStateLock.WaitAsync(_worldStateLockTimeOut))
                        {
                            try
                            {
                                Debug.Log("DestroyWorldRequest");
                                EnvironmentResponse resp = new EnvironmentResponse
                                {
                                    DestroyWorld = new DestroyWorldResponse(),
                                };

                                _worldState = EnvironmentStateType.Terminated;
                                await ServerBehavior.InvokeUpdateAsync(() =>
                                {
                                    SceneManager.LoadScene(_sampleSceneName);
                                });
                                await responseStream.WriteAsync(resp);
                                _worldState = EnvironmentStateType.Terminated;
                            } // try lock
                            finally
                            {
                                _worldStateLock.Release();
                            }
                        }
                        else {
                            EnvironmentResponse resp = new EnvironmentResponse
                            {
                                Error = new Google.Rpc.Status
                                {
                                    Code = (int)Google.Rpc.Code.Aborted,
                                    Message = "DestroyWorld Failed: Aborted due to _worldStateLock timeout",
                                },
                            };
                            await responseStream.WriteAsync(resp);
                        }
                    }
                    break;
                // case EnvironmentRequest.PayloadOneofCase.Extension:
                //     {
                //         Debug.Log("Extension Request");
                //     }
                //     break;
                default:
                    {
                        EnvironmentResponse resp = new EnvironmentResponse
                        {
                            Error = new Google.Rpc.Status
                            {
                                Code = (int)Google.Rpc.Code.Unknown,
                                Message = "hello",
                            },
                        };
                        await responseStream.WriteAsync(resp);
                    }
                    break;
            }
        }
    }

    // returns: true: passed check, false: failed to pass
    private async Task<bool>  _ValidateSettings(MapField<string, Tensor> settings,
                                                HashSet<string> validSettings,
                                                Grpc.Core.IServerStreamWriter<EnvironmentResponse> responseStream,
                                                string messagePrefix)
    {
        Debug.Log("_worldSettingsLock before enter");
        if(await _worldSettingsLock.WaitAsync(_worldSettingsLockTimeOut))
        {
            Debug.Log("_worldSettingsLock enter");
            try
            {
                var invalidKeys = new List<string>();
                foreach(var key in settings.Keys) {
                    if(!validSettings.Contains(key)) {
                        invalidKeys.Add(key);
                    }
                }
                if (invalidKeys.Count > 0) {
                    var message = messagePrefix + System.String.Join(", ", invalidKeys);
                    var error_resp = new EnvironmentResponse
                    {
                        Error = new Google.Rpc.Status
                        {
                            Code = (int)Google.Rpc.Code.InvalidArgument,
                            Message = message,
                        },
                    };
                    await responseStream.WriteAsync(error_resp);
                    return false;
                }
            } // try lock
            finally
            {
                _worldSettingsLock.Release();
                Debug.Log("_worldSettingsLock exit");
            }

            return true;
        }
        Debug.Log("_ValidateSettings lock timeout");
        return false;
    }

    // returns: true: passed check, false: failed to pass
    private async Task<bool>  _PreCheckRequest(EnvironmentRequest req, bool isJoined, Grpc.Core.IServerStreamWriter<EnvironmentResponse> responseStream)
    {
        switch (req.PayloadCase)
        {
            case EnvironmentRequest.PayloadOneofCase.CreateWorld:
            case EnvironmentRequest.PayloadOneofCase.JoinWorld:
                {
                    // Test whether agent has joined this world
                    if(isJoined) {
                        var error_resp = new EnvironmentResponse
                        {
                            Error = new Google.Rpc.Status
                            {
                                Code = (int)Google.Rpc.Code.AlreadyExists,
                                Message = "This agent has joined a world",
                            },
                        };
                        await responseStream.WriteAsync(error_resp);
                        return false;
                    }
                    goto default;
                }
            case EnvironmentRequest.PayloadOneofCase.Step:
            case EnvironmentRequest.PayloadOneofCase.Reset:
            case EnvironmentRequest.PayloadOneofCase.LeaveWorld:
                {
                    // Test whether agent has joined this world
                    if (!isJoined)
                    {
                        var error_resp = new EnvironmentResponse
                        {
                            Error = new Google.Rpc.Status
                            {
                                Code = (int)Google.Rpc.Code.NotFound,
                                Message = "This agent does not join a world",
                            },
                        };
                        await responseStream.WriteAsync(error_resp);
                        return false;
                    }
                    goto default;
                }
            default:
                {
                    return true;
                }
        }
    }

    private ActionObservationSpecs CurrentActionObservationSpecs()
    {
        var specs = new ActionObservationSpecs();
        var paddleSpec = new TensorSpec {
            Name = _actionPaddle,
            Dtype = DataType.Int8,
        };
        paddleSpec.Shape.Add(new int[] { 1 });
        specs.Actions.Add((int)Action.Paddle, paddleSpec);

        var jumpSpec = new TensorSpec {
            Name = _actionJump,
            Dtype = DataType.Int8,
        };
        jumpSpec.Shape.Add(new int[] { 1 });
        specs.Actions.Add((int)Action.Jump, jumpSpec);
        var cameraSpec = new TensorSpec {
            Name = _observationCamera,
            Dtype = DataType.Uint8,
        };
        var shape = new int[]{4, _textureWidth, _textureHeight};
        cameraSpec.Shape.Add(shape);
        specs.Observations.Add((int)Observation.Camera, cameraSpec);

        var rewardSpec = new TensorSpec {
            Name = _observationReward,
            Dtype = DataType.Float,
        };
        specs.Observations.Add((int)Observation.Reward, rewardSpec);
        var collidedSpec = new TensorSpec {
            Name = _observationCollided,
            Dtype = DataType.Int8,
        };
        specs.Observations.Add((int)Observation.Collided, collidedSpec);
        return specs;
    }

    // need to be called in main thread
    private void _CreateAgent(string agentName, string agentCameraName)
    {
        var serverBehaviorObject = GameObject.FindWithTag("GameController");
        var serverBehavior = serverBehaviorObject.GetComponent(typeof(ServerBehavior)) as ServerBehavior;
        var agent = GameObject.Find(agentName);
        if(null == agent) {
            agent = GameObject.Instantiate(serverBehavior.agentPrefab) as GameObject;
            agent.transform.position = new Vector3(3.0f, 1.0f, 4.0f);
            agent.name = agentName;
            var camera = agent.GetComponentInChildren(typeof(Camera), true) as Camera;
            camera.name = agentCameraName;
            Debug.Log("agent: " + agentName + ": camera: " + camera.name + ", tag:" + camera.tag);

            var cameraTexture = new RenderTexture(_textureWidth, _textureHeight, 0, RenderTextureFormat.ARGB32);
            camera.targetTexture = cameraTexture;
        }

    }

    private string _GenAgentName(string baseName, int count)
    {
        return baseName + "_" + count;
    }

    private string _GenAgentCameraName(string baseName, int count)
    {
        return baseName + "_" + count + "_camera";
    }
}
