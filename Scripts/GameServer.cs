using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using UnityEngine;
using UnityEngine.SceneManagement;

using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Rpc;
using Grpc.Core;
using DmEnvRpc.V1;
using Unity.VisualScripting;
using UnityEngine.Assertions;
using WFC;
using Environment = DmEnvRpc.V1.Environment;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

public class WorldObject
{
    public int index;
    private string name;
    public WorldObject(string baseName, int count)
    {
        name = baseName + "_" + count;
        index = count;
    }

    public bool SetName(string worldname)
    {
        name = worldname;
        // 取输入字符串中"_"后的字符作为index
        var sArray = worldname.Split("_");
        if (sArray.Length > 1)
        {
            index = int.Parse(sArray[1]);
        }
        else
        {
            Debug.LogError("Format Error: must be something like this 'World_0' ");
            return false;
        }
        return true;
    }

    public string GetName()
    {
        return name;
    }
}

class GameServer : Environment.EnvironmentBase
{
    private const int _textureWidth = 84;
    private const int _textureHeight = 84;
    private const string _actionPaddle = "paddle";
    private const string _actionJump = "jump";

    private const string _observationCamera = "RGBA_INTERLEAVED";
    private const string _observationReward = "reward";
    private const string _observationDone = "done";

    private enum Action : int
    {
        Paddle = 1,
        Jump = 2,
    }

    private enum ActionPaddle : int
    {
        Forward = 0,
        Backward = 1,
        Left = 2,
        Right = 3,
        LookLeft = 4,
        LookRight = 5,
        Slow = 6,
    }

    private enum Observation : int
    {
        RGBA_INTERLEAVED = 1,
        Reward = 2,
        Done = 3,
    }

    private const string _validWorldSettingsPrefix = "Following Key(s) is(are) not supported in current World:";
    private const string _validAgentSettingsPrefix = "Following Key(s) is(are) not supported for current Agent:";

    private readonly HashSet<string> VALID_WORLD_SETTINGS = new HashSet<string> {"seed"};
    private readonly HashSet<string> VALID_AGENT_SETTINGS = new HashSet<string> {"agent_pos_space", "object_pos_space", "max_steps"};
    private readonly HashSet<string> OPTIONAL_WORLD_SETTINGS = new HashSet<string> {};
    private readonly HashSet<string> OPTIONAL_AGENT_SETTINGS = new HashSet<string> {"max_steps"};

    //the whole step should be protected  by _worldStateLock. Ag
    private Dictionary<string, EnvironmentStateType> _worldStateDict = new Dictionary<string, EnvironmentStateType>();
    private Dictionary<string, SemaphoreSlim> _worldStateLockDict = new Dictionary<string, SemaphoreSlim>();
    private const int _worldStateLockTimeOut = 3300; // milliseconds
    
    /*
     * worldSettings and its Lock
     * duplicated because of the way the world is created
     */
    // private const int _worldSettingsLockTimeOut = 3300; // milliseconds
    // private Dictionary<string, SemaphoreSlim> _worldSettingsLockDict = new Dictionary<string, SemaphoreSlim>();
    // private Dictionary<string, MapField<string, Tensor>> _worldSettingsDict = 
        // new Dictionary<string, MapField<string, Tensor>>();

    
    private HashSet<string> _worldNameSet = new HashSet<string>();
    private int worldCount = 0;
    private int agentCount = 0;
    private int foodCount = 0;
    private Dictionary<string, HashSet<string>> agentNameSetDict = new Dictionary<string, HashSet<string>>();
    private Dictionary<string, HashSet<string>> foodNameSetDict = new Dictionary<string, HashSet<string>>();
    private Dictionary<string, HashSet<int>> occupiedSpaceSetDict = new Dictionary<string, HashSet<int>>();

    public GameServer()
    {
        // _worldSettingsLockList.Add(new SemaphoreSlim(1, 1));
        // _worldStateLockList.Add(new SemaphoreSlim(1, 1));
    }

    public override async Task Process(Grpc.Core.IAsyncStreamReader<EnvironmentRequest> requestStream,
        Grpc.Core.IServerStreamWriter<EnvironmentResponse> responseStream,
        Grpc.Core.ServerCallContext context)
    {
        Rect rect = new Rect(0, 0, _textureWidth, _textureHeight);
        RenderTexture oldRT = null;
        GameObject agent = null;
        byte[] imageData = new byte[4 * _textureWidth * _textureHeight];
        bool isAgentJoined = false;
        WorldObject currentWorld = new WorldObject("", 0);
        Tensor agent_pos_space = new Tensor();
        Tensor obj_pos_space = new Tensor();
        Tensor max_steps = new Tensor();
        bool is_max_steps_defined = false;
        var stepCount = 0;
        string worldName = currentWorld.GetName();
        string agentName = "";
        string objectName = worldName + "_Food_0";
        string agentCameraName = "";
        var currentBias = new Vector2Int(0, 0);

        void _ResetAgentAndFood()
        {
            // Get gridRender instance
            var gridRender = ServerBehavior.gridRenderStatic;
            // Tag Playable area
            foreach (var cell in agent_pos_space.Int32S.Array)
            {
                gridRender.TagObjectInCell(cellIndex: cell, worldName: worldName, tag: "Playable", bias: currentBias);
            }
            // reset all agent
            // 1. make a clone of pos_space
            var agent_cell_array = agent_pos_space.Int32S.Array.Clone();
            var obj_cell_array = obj_pos_space.Int32S.Array.Clone();
            // 2. reset occupied space
            occupiedSpaceSetDict[worldName].Clear();
            foreach (var tempName in agentNameSetDict[worldName])
            {
                // 3. remove all occupied space if any
                if (occupiedSpaceSetDict[worldName].Count > 0)
                {
                    // Extra step: If there is already a prefab, set the original position to free
                    if (gridRender.ResersedObjectTileIndexDict.ContainsKey(tempName))
                    {
                        var tempIndex = gridRender.ResersedObjectTileIndexDict[tempName];
                        if (occupiedSpaceSetDict[worldName].Contains(tempIndex))
                        {
                            occupiedSpaceSetDict[worldName].Remove(tempIndex);
                        }
                    }
                    foreach (var cell in occupiedSpaceSetDict[worldName])
                    {
                        if(agent_cell_array.Contains(cell))
                        {
                            agent_cell_array.Remove(cell);
                        }
                    }
                }
                // 4. random sample a postion from cell_array
                try
                {
                    var agent_rand_cell = agent_cell_array[Random.Range(0, agent_cell_array.Count - 1)];
                    if(obj_cell_array.Contains(agent_rand_cell))
                    {
                        obj_cell_array.Remove(agent_rand_cell);
                    }
                    // 5. occupy the space
                    occupiedSpaceSetDict[worldName].Add(agent_rand_cell);
                    // 6. spawn the agent
                    gridRender.SpawnObjectInCell(prefab: ServerBehavior.agentPrefabStatic, prefabName: tempName,
                        cellIndex: agent_rand_cell, worldName: worldName, prefabHeight: 1, bias: currentBias);
                }
                catch (System.ArgumentOutOfRangeException)
                {
                    Debug.LogError("No more space to spawn agent");
                    return;
                }
            }
            // reset all food's position
            foreach (var tempName2 in foodNameSetDict[worldName])
            { 
                // remove all occupied space if any
                if (occupiedSpaceSetDict[worldName].Count > 0)
                {
                    // Extra step: If there is already a prefab, set the original position to free
                    if (gridRender.ResersedObjectTileIndexDict.ContainsKey(tempName2))
                    {
                        var tempIndex = gridRender.ResersedObjectTileIndexDict[tempName2];
                        if (occupiedSpaceSetDict[worldName].Contains(tempIndex))
                        {
                            occupiedSpaceSetDict[worldName].Remove(tempIndex);
                        }
                    }
                    foreach (var cell in occupiedSpaceSetDict[worldName])
                    {
                        if (obj_cell_array.Contains(cell))
                        {
                            obj_cell_array.Remove(cell);
                        }
                    }
                }

                try
                {
                    // random sample a postion from cell_array
                    var object_rand_cell = obj_cell_array[Random.Range(0, obj_cell_array.Count - 1)];
                    // occupy the space
                    occupiedSpaceSetDict[worldName].Add(object_rand_cell);
                    // spawn the food
                    gridRender.SpawnObjectInCell(prefab: ServerBehavior.foodPrefabStatic, prefabName: tempName2,
                        cellIndex: object_rand_cell, worldName: worldName, prefabHeight: 1, bias: currentBias);
                }
                catch (System.ArgumentOutOfRangeException)
                {
                    Debug.LogError("No more space to spawn food");
                    return;
                }
            }
        }
        
        bool _CreateAgentAndFood()
            {
                // Get gridRender instance
                var gridRender = ServerBehavior.gridRenderStatic;
                // Tag Playable area
                foreach (var cell in agent_pos_space.Int32S.Array)
                {
                    gridRender.TagObjectInCell(cellIndex: cell, worldName: worldName, tag: "Playable", bias: currentBias);
                }

                /*
                 * Spawn agent and food
                 */
                
                // 1. Generate agent name
                agentName = _GenAgentName(ServerBehavior.agentPrefabStatic.name, agentCount);
                
                // 2. Generate Food name
                // Option1 : Eevery World only has 1 Food
                objectName = _GenAgentName(worldName + "_Food", 0);
                // Option2: Food number is equal to agent number
                // objectName = _GenAgentName(worldName + "_Food", foodCount);
                
                // 3. Generate Camera Name
                agentCameraName = _GenAgentCameraName(agentName, agentCount);
                
                /*
                  * 4.Random sample a postion from avaiable space
                */
                // 4.1 make a clone of pos_space
                var agent_cell_array = agent_pos_space.Int32S.Array.Clone();
                var obj_cell_array = obj_pos_space.Int32S.Array.Clone();
                
                // 4.2. remove all occupied space if any
                if (occupiedSpaceSetDict[worldName].Count > 0)
                {
                    // Extra step: If there is already a prefab, set the original position to free
                    if (gridRender.ResersedObjectTileIndexDict.ContainsKey(agentName))
                    {
                        var tempIndex = gridRender.ResersedObjectTileIndexDict[agentName];
                        if (occupiedSpaceSetDict[worldName].Contains(tempIndex))
                        {
                            occupiedSpaceSetDict[worldName].Remove(tempIndex);
                        }
                    }
                    if (gridRender.ResersedObjectTileIndexDict.ContainsKey(objectName))
                    {
                        var tempIndex = gridRender.ResersedObjectTileIndexDict[objectName];
                        if (occupiedSpaceSetDict[worldName].Contains(tempIndex))
                        {
                            occupiedSpaceSetDict[worldName].Remove(tempIndex);
                        }
                    }
                    // remove all occupied space in array
                    foreach (var cell in occupiedSpaceSetDict[worldName])
                    {
                        if (agent_cell_array.Contains(cell))
                        {
                            agent_cell_array.Remove(cell);
                        }
                        if (obj_cell_array.Contains(cell))
                        {
                            obj_cell_array.Remove(cell);
                        }
                    }
                }
                int agentRandCell;
                int objectRandCell;
                // 4.3 random sample a postion from cell_array
                if (agent_cell_array.Count > 0)
                {
                    agentRandCell = agent_cell_array[Random.Range(0, agent_cell_array.Count - 1)];
                    if(obj_cell_array.Contains(agentRandCell))
                    {
                        obj_cell_array.Remove(agentRandCell);
                    }
                }
                else
                {
                    return false;
                }
                if (obj_cell_array.Count > 0)
                {
                    objectRandCell = obj_cell_array[Random.Range(0, obj_cell_array.Count - 1)];
                }
                else
                {
                    return false;
                }
                // 4.4 occupy the space
                occupiedSpaceSetDict[worldName].Add(agentRandCell);
                occupiedSpaceSetDict[worldName].Add(objectRandCell);
                
                
                // 5. Spawn agent and food
                var agentObj = gridRender.SpawnObjectInCell(prefab: ServerBehavior.agentPrefabStatic, prefabName: agentName,
                    cellIndex: agentRandCell, worldName: worldName, prefabHeight: 1, bias: currentBias);
                var foodObj = gridRender.SpawnObjectInCell(prefab: ServerBehavior.foodPrefabStatic, prefabName: objectName,
                    cellIndex: objectRandCell, worldName: worldName, prefabHeight: 1, bias: currentBias);
                
                // 6. Check if agent and food are spawned successfully
                if ( agentObj == null)
                {
                    return false;
                }
                var controller = agentObj.GetComponent<PlayerController>();
                if (controller == null)
                {
                    return false;
                }
                agentNameSetDict[worldName].Add(agentName);
                foodNameSetDict[worldName].Add(objectName);
                agentCount +=1;
                foodCount +=1;
                controller.PlayerCamera.name = agentCameraName;
                controller.PlayerCamera.targetTexture =  new RenderTexture(_textureWidth, _textureHeight, 0, RenderTextureFormat.ARGB32);
                return true;
            }
        
        void _DeleteAgentAndFood()
        {
            var gridRender = ServerBehavior.gridRenderStatic;
            // delete all agent
            foreach (var tempName in agentNameSetDict[worldName])
            {
                var temp_agent = gridRender.WorldAgentNameToGameObject[worldName][tempName];
                if (gridRender.ExistPrefabDict.ContainsKey(tempName))
                {
                    gridRender.ExistPrefabDict.Remove(tempName);
                }
                // delete agent
                Object.Destroy(temp_agent);
            }
            // clear all agent's dict in this world
            gridRender.WorldAgentNameToGameObject[worldName].Clear();
            agentNameSetDict[worldName].Clear();
            // delete all food
            foreach (var tempName in foodNameSetDict[worldName])
            {
                var temp_food = gridRender.WorldFoodNameToGameObject[worldName][tempName];
                // delete food
                if (gridRender.ExistPrefabDict.ContainsKey(tempName))
                {
                    gridRender.ExistPrefabDict.Remove(tempName);
                }
                Object.Destroy(temp_food);
            }
            // clear all food's dict in this world
            gridRender.WorldFoodNameToGameObject[worldName].Clear();
            foodNameSetDict[worldName].Clear();
            // clear occupied space set
            occupiedSpaceSetDict[worldName].Clear();
        }
        
        void _OnEposideStartFunc()
        {
            _ResetAgentAndFood();
        }
            
        void _OnActionReceived(MapField<ulong, Tensor> Actions)
        {
                
                // Debug.Log("OnActionReceived");
                try
                { 
                    agent = ServerBehavior.gridRenderStatic.WorldAgentNameToGameObject[worldName][agentName];

                    var controller = agent.GetComponentInChildren<PlayerController>();
                    if (null == controller)
                    {
                        // Debug.LogError("PlayerController not found!");
                        throw new UnityException("PlayerController not found!");
                    }

                    foreach (var kvp in Actions)
                    {
                        Debug.Log("actions: " + kvp.Key + ", value: " + kvp.Value.Int8S.Array[0]);
                        var value = kvp.Value;
                        byte idx = 0;
                        switch (value.PayloadCase)
                        {
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
                            case (int) Action.Paddle:
                            {
                                switch (idx)
                                {
                                    case (int) ActionPaddle.LookLeft:
                                        controller.Look(new Vector2(-controller.agentLookSpeed, 0.0f));
                                        break;
                                    case (int) ActionPaddle.LookRight:
                                        controller.Look(new Vector2(controller.agentLookSpeed, 0.0f));
                                        break;
                                    case (int) ActionPaddle.Slow:
                                        controller.Move(new Vector3(0.0f, 0.0f,
                                            50.0f * Time.deltaTime));
                                        break;
                                    // case (int) ActionPaddle.LookDown:
                                        // controller.Look(new Vector2(0.0f, controller.agentLookSpeed));
                                        // break;
                                    case (int) ActionPaddle.Left:
                                        controller.Move(new Vector3(-controller.agentMoveSpeed * Time.deltaTime, 0.0f,
                                            0.0f));
                                        break;
                                    case (int) ActionPaddle.Right:
                                        controller.Move(new Vector3(controller.agentMoveSpeed * Time.deltaTime, 0.0f,
                                            0.0f));
                                        break;
                                    case (int) ActionPaddle.Forward:
                                        controller.Move(new Vector3(0.0f, 0.0f,
                                            controller.agentMoveSpeed * Time.deltaTime));
                                        break;
                                    case (int) ActionPaddle.Backward:
                                        controller.Move(new Vector3(0.0f, 0.0f,
                                            -controller.agentMoveSpeed * Time.deltaTime));
                                        break;
                                }
                            }
                                break;
                            case (int) Action.Jump:
                            {
                                switch (idx)
                                {
                                    case 0:
                                        // not jump
                                        break;
                                    case 1:
                                        controller.isJump = true;
                                        break;
                                }

                                break;
                            }
                        }
                    }
                }
                catch (UnityException e)
                {
                    Debug.LogError(e.Message);
                }
        }
            
        void _OnPostRenderFunc()
        {
            oldRT = RenderTexture.active;
            // camera.Render();
            agent = ServerBehavior.gridRenderStatic.WorldAgentNameToGameObject[worldName][agentName];
            if (agent == null)
            {
                Debug.LogError(agentName + " not found!");
                throw new Exception(agentName + " not found!");
            }
            else
            {
                var controller = agent.GetComponentInChildren<PlayerController>();
                if (null == controller)
                {
                    Debug.LogError("PlayerController not found!");
                }
                else
                {
                    RenderTexture.active = controller.PlayerCamera.targetTexture;
                    controller.renderingTexture.ReadPixels(rect, 0, 0, false);
                    var pixels = controller.renderingTexture.GetPixelData<Color32>(0);
                    // Debug.Log("pixels size: " + pixels.Length);
                    for (var i = 0; i < pixels.Length; i++)
                    {
                        var pixel = pixels[i];
                        /// var p = ((uint)pixel.a << 24) | ((uint)pixel.r << 16) | ((uint)pixel.g << 8) | ((uint)pixel.b);
                        imageData[i * 4 + 0] = pixel.r;
                        imageData[i * 4 + 1] = pixel.g;
                        imageData[i * 4 + 2] = pixel.b;
                        imageData[i * 4 + 3] = pixel.a;
                    }
                    RenderTexture.active = oldRT;
                }
            }
        }

        bool _Init_A_New_World()
        {
            // Create a new world name
            currentWorld = new WorldObject("World", worldCount);
            worldName = currentWorld.GetName();
            // If there is a duplicate name, it will return False
            if (_worldNameSet.Add(worldName))
            {
                worldCount += 1;
                currentBias = new Vector2Int((currentWorld.index % 4) * 30, (currentWorld.index / 4) * 30);
                // currentBias = new Vector2Int((currentWorld.index) * 20, 0);
                // Create WorldState and its Lock
                _worldStateDict.Add(worldName, EnvironmentStateType.InvalidEnvironmentState);
                _worldStateLockDict.Add(worldName, new SemaphoreSlim(1, 1));
                agentNameSetDict.Add(worldName, new HashSet<string>());
                foodNameSetDict.Add(worldName, new HashSet<string>());
                occupiedSpaceSetDict.Add(worldName, new HashSet<int>());
            }
            else
            {
                currentWorld = new WorldObject("", 0);
                worldName = "";
                return false;
            }
            return true;
        }

        void _Remove_World()
        {
            currentWorld = new WorldObject("", 0);
            _worldNameSet.Remove(worldName);
            if (worldCount > 0)
            {
                    worldCount -= 1;
            }
            _worldStateDict.Remove(worldName);
            _worldStateLockDict.Remove(worldName);
            agentNameSetDict.Remove(worldName);
            foodNameSetDict.Remove(worldName);
            occupiedSpaceSetDict.Remove(worldName);
            isAgentJoined = false;
        }

        // Debug.Log("Process: " + context.Peer);
        while (await requestStream.MoveNext())
        {
            var req = requestStream.Current;
            if (!await _PreCheckRequest(req, isAgentJoined, responseStream))
            {
                continue;
            }

            switch (req.PayloadCase)
            {
                case EnvironmentRequest.PayloadOneofCase.CreateWorld:
                {
                        // Debug.Log("CreateWorld enter state lock");
                        try
                        {
                            // Debug.Log("CreateWorldRequest");
                            if (!await _ValidateSettings(settings: req.CreateWorld.Settings, 
                                    validSettings: VALID_WORLD_SETTINGS, optionalSettings: OPTIONAL_WORLD_SETTINGS,
                                    responseStream: responseStream,
                                    messagePrefix: _validWorldSettingsPrefix))
                            {
                                Debug.Log("CreateWorld Failed: Invalid World Settings");
                                EnvironmentResponse errorResp = new EnvironmentResponse
                                {
                                    Error = new Google.Rpc.Status
                                    {
                                        Code = (int) Google.Rpc.Code.InvalidArgument,
                                        Message = "CreateWorld Failed: Invalid World Settings",
                                    },
                                };
                                await responseStream.WriteAsync(errorResp);
                                errorResp = null;
                                // Throw invalid argument exception
                                throw new System.ArgumentException("CreateWorld Failed: Invalid World Settings");
                            }
                            
                            // Init a new world
                            if (!_Init_A_New_World())
                            {
                                currentWorld = new WorldObject("", 0);
                                worldName = "";
                                Debug.LogWarning("CreateWorld Failed: Duplicate World Name");
                                EnvironmentResponse errorResp = new EnvironmentResponse
                                {
                                    Error = new Google.Rpc.Status
                                    {
                                        Code = (int) Google.Rpc.Code.AlreadyExists,
                                        Message = "CreateWorld Failed: Duplicate World Name",
                                    },
                                };
                                await responseStream.WriteAsync(errorResp);
                            }
                         
                            var resp = new EnvironmentResponse
                            {
                                CreateWorld = new CreateWorldResponse
                                {
                                    WorldName = worldName
                                }
                            };
                            await ServerBehavior.InvokeUpdateAsync(() =>
                            {
                                //Debug.Log("Creating World....");
                                // Read Tensor from settings
                                var wave_seed = req.CreateWorld.Settings["seed"];
                                var grid_render = ServerBehavior.gridRenderStatic;
                                var wave = new WaveMap(9, 9);
                                wave.decode(wave_seed.Int32S.Array);
                                grid_render.RenderOnGrid(wave.data, bias: currentBias, worldName);
                                wave = null;
                            });
                            await responseStream.WriteAsync(resp);
                            resp = null;
                            // According to dm_env_rpc.proto, this 'InvalidEnvironmentState
                            // should never be sent or received
                            _worldStateDict[worldName] = EnvironmentStateType.InvalidEnvironmentState;
                        }
                        catch (System.Exception e)
                        {
                            Debug.Log(e.Message);
                            EnvironmentResponse errorResp = new EnvironmentResponse
                            {
                                Error = new Google.Rpc.Status
                                {
                                    Code = (int) Google.Rpc.Code.Aborted,
                                    Message = "CreateWorld Failed: " + e.Message,
                                },
                            };
                            await responseStream.WriteAsync(errorResp);
                            errorResp = null;
                        }
                }
                    break;
                case EnvironmentRequest.PayloadOneofCase.JoinWorld:
                {
                        if (isAgentJoined)
                        {
                            //Debug.Log("JoinWorld Failed: Invalid Agent Settings");
                            EnvironmentResponse errorResp = new EnvironmentResponse
                            {
                                Error = new Google.Rpc.Status
                                {
                                    Code = (int) Google.Rpc.Code.AlreadyExists,
                                    Message = "JoinWorld Failed: Current Session Already has Agent Joined",
                                },
                            };
                            await responseStream.WriteAsync(errorResp);
                            errorResp = null;
                            // Throw invalid argument exception
                            throw new System.ArgumentException("JoinWorld Failed: Invalid Agent Settings");
                        }
                        
                        // Does not has required world name
                        if (!_worldNameSet.Contains(req.JoinWorld.WorldName))
                        {
                            Debug.Log("JoinWorld Failed: Required World Name Not Found");
                            EnvironmentResponse errorResp = new EnvironmentResponse
                            {
                                Error = new Google.Rpc.Status
                                {
                                    Code = (int) Google.Rpc.Code.NotFound,
                                    Message =
                                        "JoinWorld Failed: Required World Name Not Found",
                                },
                            };
                            await responseStream.WriteAsync(errorResp);
                            errorResp = null;
                            // Throw invalid argument exception
                            throw new System.ArgumentException(
                                "JoinWorld Failed: Required World Name Not Found");
                        }

                        if (!currentWorld.SetName(req.JoinWorld.WorldName))
                        {
                            Debug.Log("JoinWorld Failed: Format of WorldName is invalid, valid example:'World_0'");
                            EnvironmentResponse errorResp = new EnvironmentResponse
                            {
                                Error = new Google.Rpc.Status
                                {
                                    Code = (int) Google.Rpc.Code.InvalidArgument,
                                    Message =
                                        "JoinWorld Failed: Format of WorldName is invalid, valid example:'World_0'",
                                },
                            };
                            await responseStream.WriteAsync(errorResp);
                            errorResp = null;
                            // Throw invalid argument exception
                            throw new System.ArgumentException(
                                "JoinWorld Failed: Format of WorldName is invalid, valid example:'World_0'");
                        }
                        worldName = currentWorld.GetName();
                        // currentBias = new Vector2Int((currentWorld.index) * 20, 0);
                        currentBias = new Vector2Int((currentWorld.index % 4) * 30, (currentWorld.index / 4) * 30);

                        //Debug.Log("JoinWorld before enter state lock");
                        if (await _worldStateLockDict[worldName].WaitAsync(_worldStateLockTimeOut))
                        {
                            //Debug.Log("JoinWorld enter state lock");
                            try
                            {

                                //Debug.Log("JoinWorldRequest");
                                if (!await _ValidateSettings(settings: req.JoinWorld.Settings,validSettings: VALID_AGENT_SETTINGS,
                                        optionalSettings: OPTIONAL_AGENT_SETTINGS,
                                        responseStream: responseStream,
                                        messagePrefix: _validAgentSettingsPrefix))
                                {
                                    //Debug.Log("JoinWorld Failed: Invalid Agent Settings");
                                    EnvironmentResponse errorResp = new EnvironmentResponse
                                    {
                                        Error = new Google.Rpc.Status
                                        {
                                            Code = (int) Google.Rpc.Code.InvalidArgument,
                                            Message = "JoinWorld Failed: Invalid Agent Settings",
                                        },
                                    };
                                    await responseStream.WriteAsync(errorResp);
                                    errorResp = null;
                                    // Throw invalid argument exception
                                    throw new System.ArgumentException("JoinWorld Failed: Invalid Agent Settings");
                                }
                                var isCreated = false;
                                agent_pos_space = req.JoinWorld.Settings["agent_pos_space"];
                                obj_pos_space = req.JoinWorld.Settings["object_pos_space"];
                                if (req.JoinWorld.Settings.ContainsKey("max_steps"))
                                {
                                    max_steps = req.JoinWorld.Settings["max_steps"];
                                    is_max_steps_defined = true;
                                }
                                else
                                {
                                    is_max_steps_defined = false;
                                }
                                await ServerBehavior.InvokeUpdateAsync(() =>
                                {
                                    // //Debug.Log("JoinWorld: InvokeUpdateAsync");
                                    isCreated = _CreateAgentAndFood();
                                });
                                if (!isCreated)
                                {
                                    Debug.Log("当前总占用块：" + occupiedSpaceSetDict[worldName].Count);
                                    Debug.LogError("JoinWorld Failed: CreateAgentAndFood Failed, No free place to place");
                                    EnvironmentResponse errorResp = new EnvironmentResponse
                                    {
                                        Error = new Google.Rpc.Status
                                        {
                                            Code = (int) Google.Rpc.Code.Aborted,
                                            Message = "JoinWorld Failed: CreateAgentAndFood Failed, No free place to place",
                                        },
                                    };
                                    await responseStream.WriteAsync(errorResp);
                                    errorResp = null;
                                    // Throw invalid argument exception
                                    throw new System.ArgumentException("JoinWorld Failed: CreateAgentAndFood Failed, No free place to place");
                                }
                                var specs = CurrentActionObservationSpecs();
                                EnvironmentResponse resp = new EnvironmentResponse
                                {
                                    JoinWorld = new JoinWorldResponse
                                    {
                                        Specs = specs,
                                    },
                                };
                                await responseStream.WriteAsync(resp);
                                resp = null;
                                isAgentJoined = true;
                                if (EnvironmentStateType.InvalidEnvironmentState == _worldStateDict[worldName])
                                {
                                    _worldStateDict[worldName] = EnvironmentStateType.Running;
                                }
                            } // try lock
                            catch (System.Exception e)
                            {
                                Debug.Log(e.Message);
                                EnvironmentResponse errorResp = new EnvironmentResponse
                                {
                                    Error = new Google.Rpc.Status
                                    {
                                        Code = (int) Google.Rpc.Code.Aborted,
                                        Message = "JoinWorld Failed: " + e.Message,
                                    },
                                };
                                await responseStream.WriteAsync(errorResp);
                                errorResp = null;
                            }
                            finally
                            {
                                _worldStateLockDict[worldName].Release();
                                //Debug.Log("JoinWorld exit state lock");
                            }
                        }
                        else
                        {
                            EnvironmentResponse resp = new EnvironmentResponse
                            {
                                Error = new Google.Rpc.Status
                                {
                                    Code = (int) Google.Rpc.Code.Aborted,
                                    Message = "JoinWorld Failed: Aborted due to _worldStateLock timeout",
                                },
                            };
                            await responseStream.WriteAsync(resp);
                            resp = null;
                        }
                }
                    break;
                case EnvironmentRequest.PayloadOneofCase.Step:
                {
                    // Thanks to PreCheck, this should never happens
                    if (worldName == "")
                    {
                        Debug.Log("Step Failed: Joined World's Name not Found!");
                        EnvironmentResponse resp = new EnvironmentResponse
                        {
                            Error = new Google.Rpc.Status
                            {
                                Code = (int) Google.Rpc.Code.NotFound,
                                Message = "Step Failed: Joined World's Name not Found!",
                            },
                        };
                        await responseStream.WriteAsync(resp);
                        resp = null;
                        throw new System.ArgumentException("Step Failed: Joined World's Name not Found!");
                    }
                    // Debug.Log("StepRequest");
                    if (await _worldStateLockDict[worldName].WaitAsync(_worldStateLockTimeOut))
                    {
                        var step = new StepResponse
                        {
                            State = _worldStateDict[worldName],
                        };
                        var rewardTensor = new Tensor
                        {
                            Floats = new Tensor.Types.FloatArray(),
                        };

                        var doneTensor = new Tensor
                        {
                            Int8S = new Tensor.Types.Int8Array(),
                        };
                        try
                        {
                            // Reset the world state to Interrupted if there is no action
                            if (req.Step.Actions == null || req.Step.Actions.Count == 0)
                            { 
                                _worldStateDict[worldName] = EnvironmentStateType.Interrupted;
                            }
                            
                            // According to dm_env_rpc.proto:
                            // If state is not RUNNING, the action on the next StepRequest will be
                            // ignored and the environment will transition to a RUNNING state.
                            // if _worldState is not Running, skip this frame
                            var continute = true;
                            if (EnvironmentStateType.Running == _worldStateDict[worldName])
                            {
                                // Done if reach maxstep
                                if (is_max_steps_defined)
                                {
                                    if ((stepCount > max_steps.Int64S.Array[0]))
                                    {
                                        continute = false;
                                        stepCount = 0;
                                        _worldStateDict[worldName] = EnvironmentStateType.Interrupted;
                                        // done = 0
                                        doneTensor.Int8S.Array = ByteString.CopyFrom(new byte[1] {1});
                                        step.Observations.Add((int) Observation.Done, doneTensor);
                                        // reward = 0
                                        rewardTensor.Floats.Array.Add((float) 0.0f);
                                        step.Observations.Add((int) Observation.Reward, rewardTensor);
                                    }
                                }
                                if (continute)
                                {
                                    await ServerBehavior.InvokeUpdateAsync(() =>
                                    {
                                        _OnActionReceived(req.Step.Actions);
                                        agent = ServerBehavior.gridRenderStatic.WorldAgentNameToGameObject[worldName][
                                            agentName];
                                        var colliderTrigger = agent.GetComponentInChildren<ColliderTrigger>();
                                        doneTensor.Int8S.Array =
                                            ByteString.CopyFrom(new byte[1] {colliderTrigger.Done});
                                        step.Observations.Add((int) Observation.Done, doneTensor);
                                        rewardTensor.Floats.Array.Add((float) colliderTrigger.Reward);
                                        step.Observations.Add((int) Observation.Reward, rewardTensor);
                                        // Interrupted if done
                                        if (colliderTrigger.Done == 1)
                                        {
                                            _worldStateDict[worldName] = EnvironmentStateType.Interrupted;
                                            // step.State = EnvironmentStateType.Interrupted;
                                        }
                                    });
                                    if (is_max_steps_defined)
                                    {
                                        stepCount += 1;
                                    }
                                }
                            } // if (EnvironmentStateType.Running == _worldState)
                            else
                            {
                                // change worldState to Running
                                _worldStateDict[worldName] = EnvironmentStateType.Running;
                                step.State = EnvironmentStateType.Running;
                                // if the worldState is not Running, ignore current step and reset state,
                                // //Debug.Log("Ignore current step");
                                await ServerBehavior.InvokeUpdateAsync(_OnEposideStartFunc);
                                rewardTensor = new Tensor
                                {
                                    Floats = new Tensor.Types.FloatArray(),
                                };

                                doneTensor = new Tensor
                                {
                                    Int8S = new Tensor.Types.Int8Array(),
                                };
                                // done = 0
                                doneTensor.Int8S.Array = ByteString.CopyFrom(new byte[1] {0});
                                step.Observations.Add((int) Observation.Done, doneTensor);
                                // reward = 0
                                rewardTensor.Floats.Array.Add((float) 0.0f);
                                step.Observations.Add((int) Observation.Reward, rewardTensor);
                            } // if the worldState is not Running
                            // Camera Rendering
                            await ServerBehavior.InvokeOnPostRenderAsync(_OnPostRenderFunc);
                            var cameraTensor = new Tensor
                            {
                                Uint8S = new Tensor.Types.Uint8Array(),
                            };
                            cameraTensor.Uint8S.Array = ByteString.CopyFrom(imageData);
                            var shape = new int[] {4, _textureWidth, _textureHeight};
                            cameraTensor.Shape.Add(shape);
                            step.Observations.Add((int) Observation.RGBA_INTERLEAVED, cameraTensor);
                            EnvironmentResponse resp = new EnvironmentResponse
                            {
                                Step = step,
                            };
                            await responseStream.WriteAsync(resp);
                            resp = null;
                        } // try lock
                        catch (System.Exception e)
                        {
                            //Debug.LogError(e.Message);
                            EnvironmentResponse errr_resp = new EnvironmentResponse
                            {
                                Error = new Google.Rpc.Status
                                {
                                    Code = (int) Google.Rpc.Code.Aborted,
                                    Message = "Step Failed: " + e.Message,
                                },
                            };
                            await responseStream.WriteAsync(errr_resp);
                            errr_resp = null;
                        }
                        finally
                        {
                            _worldStateLockDict[worldName].Release();
                        }
                    }
                    else
                    {
                        EnvironmentResponse resp = new EnvironmentResponse
                        {
                            Error = new Google.Rpc.Status
                            {
                                Code = (int) Google.Rpc.Code.Aborted,
                                Message = "Step Failed: Aborted due to _worldStateLock timeout",
                            },
                        };
                        await responseStream.WriteAsync(resp);
                        resp = null;
                    }
                }
                    break;
                case EnvironmentRequest.PayloadOneofCase.LeaveWorld:
                {
                    //Debug.Log("LeaveWorld Request");
                    try
                    {
                        await ServerBehavior.InvokeUpdateAsync(() =>
                        {
                            ////Debug.Log("Leaving World....");
                            _DeleteAgentAndFood();
                        });
                    }
                    catch (System.Exception e)
                    {
                        //Debug.Log(e.Message);
                        EnvironmentResponse errr_resp = new EnvironmentResponse
                        {
                            Error = new Google.Rpc.Status
                            {
                                Code = (int) Google.Rpc.Code.Aborted,
                                Message = "LeaveWorld Failed: " + e.Message,
                            },
                        };
                        await responseStream.WriteAsync(errr_resp);
                        errr_resp = null;
                    }

                    EnvironmentResponse resp = new EnvironmentResponse
                    {
                        LeaveWorld = new LeaveWorldResponse(),
                    };
                    await responseStream.WriteAsync(resp);
                    isAgentJoined = false;
                    resp = null;
                }
                    break;
                case EnvironmentRequest.PayloadOneofCase.DestroyWorld:
                {
                    if (await _worldStateLockDict[worldName].WaitAsync(_worldStateLockTimeOut))
                    {
                        try
                        {
                            //Debug.Log("DestroyWorldRequest");
                            // World Name is null
                            if (req.DestroyWorld.WorldName == null)
                            {
                                //Debug.LogError("World Name is null");
                                EnvironmentResponse errr_resp = new EnvironmentResponse
                                {
                                    Error = new Google.Rpc.Status
                                    {
                                        Code = (int) Google.Rpc.Code.Aborted,
                                        Message = "DestroyWorld Failed: World Name is null",
                                    },
                                };
                                throw new System.ArgumentException("DestroyWorld Failed: World Name is null");
                            }

                            if (worldName == "")
                            {
                                //Debug.LogError("DestroyWorld Failed: No World exists now");
                                EnvironmentResponse errr_resp = new EnvironmentResponse
                                {
                                    Error = new Google.Rpc.Status
                                    {
                                        Code = (int) Google.Rpc.Code.Aborted,
                                        Message = "DestroyWorld Failed: No World exists now",
                                    },
                                };
                                throw new System.ArgumentException("DestroyWorld Failed: No World exists now");
                            }

                            // Requested World is not the current world
                            if (worldName != req.DestroyWorld.WorldName)
                            {
                                //Debug.LogError("DestroyWorld Failed: Requested World is not the current world");
                                EnvironmentResponse errr_resp = new EnvironmentResponse
                                {
                                    Error = new Google.Rpc.Status
                                    {
                                        Code = (int) Google.Rpc.Code.Aborted,
                                        Message = "DestroyWorld Failed: Requested World is not the current world",
                                    },
                                };
                                throw new System.ArgumentException(
                                    "DestroyWorld Failed: Requested World is not the current world");
                            }

                            EnvironmentResponse resp = new EnvironmentResponse
                            {
                                DestroyWorld = new DestroyWorldResponse(),
                            };
                            
                            // TODO : Destory Target World
                            await ServerBehavior.InvokeUpdateAsync(() =>
                            {
                                // LeaveWorld first
                                _DeleteAgentAndFood();
                                isAgentJoined = false;
                                // Destroy the World
                                var grid_render = ServerBehavior.gridRenderStatic;
                                grid_render.DestoryWorld(worldName);
                            });
                            _Remove_World();
                            await responseStream.WriteAsync(resp);
                            resp = null;
                        } // try lock
                        catch (System.Exception e)
                        {
                            //Debug.Log(e.Message);
                            EnvironmentResponse errr_resp = new EnvironmentResponse
                            {
                                Error = new Google.Rpc.Status
                                {
                                    Code = (int) Google.Rpc.Code.Aborted,
                                    Message = "DestoryWorld Failed: " + e.Message,
                                },
                            };
                            await responseStream.WriteAsync(errr_resp);
                            throw new System.ArgumentException("DestoryWorld Failed: " + e.Message);
                            errr_resp = null;
                        }
                        finally
                        {
                            if (_worldStateLockDict.ContainsKey(worldName))
                            {
                                _worldStateLockDict[worldName].Release();
                            }
                        }
                    }
                    else
                    {
                        EnvironmentResponse resp = new EnvironmentResponse
                        {
                            Error = new Google.Rpc.Status
                            {
                                Code = (int) Google.Rpc.Code.Aborted,
                                Message = "DestroyWorld Failed: Aborted due to _worldStateLock timeout",
                            },
                        };
                        await responseStream.WriteAsync(resp);
                        resp = null;
                    }
                }
                    break;
                // case EnvironmentRequest.PayloadOneofCase.Extension:
                case EnvironmentRequest.PayloadOneofCase.ResetWorld:
                {
                    if (await _worldStateLockDict[worldName].WaitAsync(_worldStateLockTimeOut))
                    {
                        try
                        {
                            //Debug.Log("Reset World Request");
                            if (!await _ValidateSettings(settings: req.ResetWorld.Settings, validSettings: VALID_WORLD_SETTINGS, 
                                    optionalSettings: OPTIONAL_WORLD_SETTINGS,
                                    responseStream: responseStream,
                                    messagePrefix: _validWorldSettingsPrefix))
                            {
                                //Debug.Log("ResetWorld Failed: Invalid World Settings");
                                EnvironmentResponse errorResp = new EnvironmentResponse
                                {
                                    Error = new Google.Rpc.Status
                                    {
                                        Code = (int) Google.Rpc.Code.InvalidArgument,
                                        Message = "ResetWorld Failed: Invalid World Settings",
                                    },
                                };
                                await responseStream.WriteAsync(errorResp);
                                // Throw invalid argument exception
                                throw new System.ArgumentException("ResetWorld Failed: Invalid World Settings");
                                errorResp = null;
                            }

                            // World Name is null
                            if (req.ResetWorld.WorldName == null)
                            {
                                //Debug.LogError("World Name is null");
                                EnvironmentResponse errorResp = new EnvironmentResponse
                                {
                                    Error = new Google.Rpc.Status
                                    {
                                        Code = (int) Google.Rpc.Code.Aborted,
                                        Message = "ResetWorld Failed: World Name is null",
                                    },
                                };
                                throw new System.ArgumentException("ResetWorld Failed: World Name is null");
                            }

                            // Requested World is not the current world
                            if (worldName != req.ResetWorld.WorldName)
                            {
                                //Debug.LogError("ResetWorld Failed: Requested World is not the current world");
                                EnvironmentResponse errr_resp = new EnvironmentResponse
                                {
                                    Error = new Google.Rpc.Status
                                    {
                                        Code = (int) Google.Rpc.Code.Aborted,
                                        Message = "ResetWorld Failed: Requested World is not the current world",
                                    },
                                };
                                throw new System.ArgumentException(
                                    "ResetWorld Failed: Requested World is not the current world");
                            }

                            _worldStateDict[worldName] = EnvironmentStateType.Interrupted;
                            EnvironmentResponse resp = new EnvironmentResponse
                            {
                                ResetWorld = new ResetWorldResponse(),
                            };
                            await ServerBehavior.InvokeUpdateAsync(() =>
                            {
                                _DeleteAgentAndFood();
                                // Leave World
                                isAgentJoined = false;
                                Debug.Log("Recreating World....");
                                // Read Tensor from settings
                                var wave_seed = req.ResetWorld.Settings["seed"];
                                var grid_render = ServerBehavior.gridRenderStatic;
                                var wave = new WaveMap(9, 9);
                                wave.decode(wave_seed.Int32S.Array);
                                grid_render.RenderOnGrid(wave.data, bias: currentBias, worldName);
                                wave = null;
                            });
                            await responseStream.WriteAsync(resp);
                            resp = null;
                        } // try lock
                        catch (System.Exception e)
                        {
                            //Debug.Log(e.Message);
                            EnvironmentResponse errr_resp = new EnvironmentResponse
                            {
                                Error = new Google.Rpc.Status
                                {
                                    Code = (int) Google.Rpc.Code.Aborted,
                                    Message = "ResetWorld Failed: " + e.Message,
                                },
                            };
                            await responseStream.WriteAsync(errr_resp);
                            errr_resp = null;
                        }
                        finally
                        {
                            _worldStateLockDict[worldName].Release();
                        }
                    }
                    else
                    {
                        EnvironmentResponse resp = new EnvironmentResponse
                        {
                            Error = new Google.Rpc.Status
                            {
                                Code = (int) Google.Rpc.Code.Aborted,
                                Message = "ResetWorld Failed: Aborted due to _worldStateLock timeout",
                            },
                        };
                        await responseStream.WriteAsync(resp);
                        resp = null;
                    }
                }
                    break;
                case EnvironmentRequest.PayloadOneofCase.Reset:
                {
                    //Debug.Log("Reset Request");
                    if (!await _ValidateSettings(settings: req.Reset.Settings, validSettings: VALID_AGENT_SETTINGS, 
                            optionalSettings: OPTIONAL_AGENT_SETTINGS,
                            responseStream: responseStream,
                            messagePrefix: _validAgentSettingsPrefix))
                    {
                        continue;
                    }

                    if (await _worldStateLockDict[worldName].WaitAsync(_worldStateLockTimeOut))
                    {
                        try
                        {
                            agent_pos_space = req.Reset.Settings["agent_pos_space"];
                            obj_pos_space = req.Reset.Settings["object_pos_space"];
                        }
                        catch (System.Exception e)
                        {
                            // //Debug.Log("Reset in same map");
                        }

                        _worldStateDict[worldName] = EnvironmentStateType.Interrupted;
                        try
                        {
                            var specs = CurrentActionObservationSpecs();
                            EnvironmentResponse resp = new EnvironmentResponse
                            {
                                Reset = new ResetResponse
                                {
                                    Specs = specs,
                                }
                            };
                            await responseStream.WriteAsync(resp);
                            resp = null;
                        } // try lock
                        catch (System.Exception e)
                        {
                            //Debug.Log(e.Message);
                            EnvironmentResponse errr_resp = new EnvironmentResponse
                            {
                                Error = new Google.Rpc.Status
                                {
                                    Code = (int) Google.Rpc.Code.Aborted,
                                    Message = "Reset Failed: " + e.Message,
                                },
                            };
                            await responseStream.WriteAsync(errr_resp);
                            errr_resp = null;
                        }
                        finally
                        {
                            _worldStateLockDict[worldName].Release();
                        }
                    }
                }
                    break;
                default:
                {
                    EnvironmentResponse resp = new EnvironmentResponse
                    {
                        Error = new Google.Rpc.Status
                        {
                            Code = (int) Google.Rpc.Code.Unknown,
                            Message = "hello",
                        },
                    };
                    await responseStream.WriteAsync(resp);
                    resp = null;
                }
                    break;
            }
        }
    }


    private ActionObservationSpecs CurrentActionObservationSpecs()
    {
        var specs = new ActionObservationSpecs();
        var paddleSpec = new TensorSpec
        {
            Name = _actionPaddle,
            Dtype = DataType.Int8,
        };
        paddleSpec.Shape.Add(new int[] {1});
        specs.Actions.Add((int) Action.Paddle, paddleSpec);

        var jumpSpec = new TensorSpec
        {
            Name = _actionJump,
            Dtype = DataType.Int8,
        };
        jumpSpec.Shape.Add(new int[] {1});
        specs.Actions.Add((int) Action.Jump, jumpSpec);
        var cameraSpec = new TensorSpec
        {
            Name = _observationCamera,
            Dtype = DataType.Uint8,
        };
        var shape = new int[] {4, _textureWidth, _textureHeight};
        cameraSpec.Shape.Add(shape);
        specs.Observations.Add((int) Observation.RGBA_INTERLEAVED, cameraSpec);

        var rewardSpec = new TensorSpec
        {
            Name = _observationReward,
            Dtype = DataType.Float,
        };
        specs.Observations.Add((int) Observation.Reward, rewardSpec);
        var doneSpec = new TensorSpec
        {
            Name = _observationDone,
            Dtype = DataType.Int8,
        };
        specs.Observations.Add((int) Observation.Done, doneSpec);
        return specs;
    }

    // returns: true: passed check, false: failed to pass
    private async Task<bool> _ValidateSettings(MapField<string, Tensor> settings,
        HashSet<string> validSettings, HashSet<string> optionalSettings,
        Grpc.Core.IServerStreamWriter<EnvironmentResponse> responseStream,
        string messagePrefix)
    {
        try
        {
            var invalidKeys = new List<string>();
            foreach (var key in settings.Keys)
            {
                if (!validSettings.Contains(key) && !optionalSettings.Contains(key))
                {
                    invalidKeys.Add(key);
                }
            }

            if (invalidKeys.Count > 0)
            {
                var message = messagePrefix + System.String.Join(", ", invalidKeys);
                var errorResp = new EnvironmentResponse
                {
                    Error = new Google.Rpc.Status
                    {
                        Code = (int) Google.Rpc.Code.InvalidArgument,
                        Message = message,
                    },
                };
                await responseStream.WriteAsync(errorResp);
                return false;
            }
        }
        catch (System.Exception e)
        {
            Debug.Log(e.Message);
        }
        return true;
    }

    // returns: true: passed check, false: failed to pass
    private async Task<bool> _PreCheckRequest(EnvironmentRequest req, bool isJoined,
        Grpc.Core.IServerStreamWriter<EnvironmentResponse> responseStream)
    {
        switch (req.PayloadCase)
        {
            case EnvironmentRequest.PayloadOneofCase.CreateWorld:
            case EnvironmentRequest.PayloadOneofCase.JoinWorld:
            {
                // Test whether agent has joined this world
                if (isJoined)
                {
                    var errorResp = new EnvironmentResponse
                    {
                        Error = new Google.Rpc.Status
                        {
                            Code = (int) Google.Rpc.Code.AlreadyExists,
                            Message = "This agent has already joined a world",
                        },
                    };
                    await responseStream.WriteAsync(errorResp);
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
                    var errorResp = new EnvironmentResponse
                    {
                        Error = new Google.Rpc.Status
                        {
                            Code = (int) Google.Rpc.Code.NotFound,
                            Message = "This agent does not join a world",
                        },
                    };
                    await responseStream.WriteAsync(errorResp);
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
    

    
    private string _GenAgentName(string baseName, int count)
    {
        return baseName + "_" + count;
    }

    private string _GenAgentCameraName(string baseName, int count)
    {
        return baseName + "_" + count + "_camera";
    }
}
    
     