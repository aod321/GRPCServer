using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DmEnvRpc.V1;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.PlayerLoop;
using Debug = UnityEngine.Debug;

public class GridRender : MonoBehaviour
{
    public Grid grid;
    public GameObject[] cubePrefabs;
    public GameObject[] rampPrefabs;
    public GameObject[] cornerPrefabs;
    public GameObject wallPrefab;
    public GameObject emptyPrefab;
    public int height = 9;
    public int width = 9;
    // Material a pool of agents and foods
    public Dictionary<string,Dictionary<string, GameObject>> WorldAgentNameToGameObject;
    public Dictionary<string,Dictionary<string, GameObject>> WorldFoodNameToGameObject;
    // For ResetWorld and DestoryWorld
    public HashSet<string> worldNameSet;
    // Maintain a pool of objects for each world's grid
    public Dictionary<string, Dictionary<int, List<GameObject>>> WorldGridObjectsListDict;
    public Dictionary<string, GameObject> WorldNameToGameObject;
    // public Dictionary<string,Dictionary<int, int[]>> allcellDict = new Dictionary<string,Dictionary<int, int[]>>();
    // public Dictionary<int, int[]> cellDict = new Dictionary<int, int[]>();
    [Range(0.0f, 5.0f)]
    public float scaley = 0.5f;
    public Dictionary<string, int> ResersedObjectTileIndexDict;
    public Dictionary<string, GameObject> ExistPrefabDict;

    GameObject RotateAsMark(GameObject obj, int mark)
    {
        if (mark>3)
        {
            mark = 0;
            //Debug.LogWarning("Rotate data is out of range 0-3");
        }
        obj.transform.Rotate(0, mark*90, 0);
        return obj;
    }
    
    // Convert python cellIndex to Unity Grid cellIndex
    private Dictionary<int, int[]> CreateCellDict(Vector2Int bias)
    {
        var cellDict = new Dictionary<int, int[]>();
        var biasX = bias.x;
        var biasY = bias.y;
        var count = 0; 
        for(var y = width - 1; y >= 0; y--)
        {
            for(var x = 0; x < width; x++)
            {
                if (!cellDict.TryAdd(count, new int[] {x + biasX, y + biasY}))
                {
                    cellDict[count] = new int[] {x + biasX, y + biasY};
                }
                count += 1;
            }
        }
        return cellDict;
    }

    private void Awake()
    {
        grid = GetComponent<Grid>();
        WorldNameToGameObject = new Dictionary<string, GameObject>();
        ResersedObjectTileIndexDict = new Dictionary<string, int>();
        ExistPrefabDict = new Dictionary<string, GameObject>();
        worldNameSet = new HashSet<string>();
        WorldGridObjectsListDict = new Dictionary<string, Dictionary<int, List<GameObject>>>();
        WorldAgentNameToGameObject = new Dictionary<string, Dictionary<string, GameObject>>();
        WorldFoodNameToGameObject = new Dictionary<string, Dictionary<string, GameObject>>();
    }

    public void TagObjectInCell(int cellIndex, string worldName, string tag="Playable", Vector2Int bias=default(Vector2Int))
    {
        if (!WorldGridObjectsListDict.ContainsKey(worldName))
        {
            Debug.LogError("No rendered grid exists for the given world: " + worldName);
            throw new System.ArgumentException("No rendered grid exists for the given world: " + worldName);
        }
        var cellDict = CreateCellDict(bias);
        var cellX = cellDict[cellIndex][0];
        var cellY = cellDict[cellIndex][1];
        var pos = grid.GetCellCenterLocal(new Vector3Int(cellX, cellY, 0));
        
        // get the top layer object in that cell
        var objects = WorldGridObjectsListDict[worldName][cellIndex];
        foreach (var t_object in objects)
        {
            t_object.tag = tag;
            // var collider = t_object.GetComponentInChildren<Collider>();
            // collider.gameObject.tag = tag;
        }
        // var topchild = objects.Where(x => x.CompareTag("Finish"));
        // Assert.IsTrue(topchild.Count() == 1, "A cell should have exactly 1 top layer object");
        // var collider = topchild.First().GetComponentInChildren<Collider>();
    }

    public GameObject SpawnObjectInCell(GameObject prefab, string prefabName, int cellIndex, string worldName, int prefabHeight = 1, Vector2Int bias=default(Vector2Int))
    {
        if (!WorldGridObjectsListDict.ContainsKey(worldName))
        {
            Debug.LogError("No rendered grid exists for the given world: " + worldName);
            throw new System.ArgumentException("No rendered grid exists for the given world: " + worldName);
        }
        var cellDict = CreateCellDict(bias);
        var cellX = cellDict[cellIndex][0];
        var cellY = cellDict[cellIndex][1];
        var pos = grid.GetCellCenterLocal(new Vector3Int(cellX, cellY, 0));
        // get all top layer objects in cell
        var objects = WorldGridObjectsListDict[worldName][cellIndex];
        // var topchild = objects.Where(x => x.CompareTag("Finish"));
        var topchild = objects.Last();
        // Assert.IsTrue(topchild.Count() == 1, "A cell should have exactly 1 top layer object");
        var topHolder = topchild.GetComponentsInChildren<Transform>().Where(x => x.CompareTag("TopObj"));
        pos.y = topHolder.First().position.y + prefabHeight;
        GameObject returnObj = null;
        // if prefab already exists, change its position instead of spawning a new one
        if (ExistPrefabDict.ContainsKey(prefabName))
        { 
            returnObj = ExistPrefabDict[prefabName];
            if(returnObj.CompareTag("Agent"))
            {
                var controllerRef = returnObj.GetComponent<CharacterController>();
                var colliderTrigger = returnObj.GetComponent<ColliderTrigger>();
                if (controllerRef)
                {
                    controllerRef.enabled = false;
                    returnObj.transform.position = pos;
                    controllerRef.enabled = true;
                }
                if (colliderTrigger)
                {
                    colliderTrigger.Reward = 0;
                    colliderTrigger.Done = 0;
                    colliderTrigger.AlreadyDrop = false;
                }
            }
            else
            {
                returnObj.transform.position = pos;
            }
            // allow origin place to be placed again
            if (ResersedObjectTileIndexDict.ContainsKey(prefabName))
            {
                ResersedObjectTileIndexDict.Remove(prefabName);
            }
        }
        else  // Only spawn prefab if it is not already exist in the world
        {
            returnObj = Instantiate(prefab, pos, Quaternion.identity);
            returnObj.name = prefabName;
            returnObj.transform.localScale = new Vector3(1.0f, 1.0f, 1.0f);
            returnObj.transform.parent = transform;
            ExistPrefabDict.Add(prefabName, returnObj);
            if (returnObj.CompareTag("Agent"))
            {
                WorldAgentNameToGameObject[worldName].Add(prefabName, returnObj);
            }
            if (returnObj.CompareTag("Food"))
            {
                WorldFoodNameToGameObject[worldName].Add(prefabName, returnObj);
            }
        }
        if (!ResersedObjectTileIndexDict.TryAdd(prefabName, cellIndex))
        {
            ResersedObjectTileIndexDict[prefabName] = cellIndex;
        }
        return returnObj;
    }

    
    public void DestoryWorld(string worldName)
    {
        if (WorldNameToGameObject.ContainsKey(worldName))
        {
            var obj = WorldNameToGameObject[worldName];
            Destroy(obj);
            worldNameSet.Remove(worldName);
            WorldNameToGameObject.Remove(worldName);
            WorldGridObjectsListDict.Remove(worldName);
            ExistPrefabDict.Remove(worldName);
            ResersedObjectTileIndexDict.Remove(worldName);
            WorldAgentNameToGameObject.Remove(worldName);
            WorldFoodNameToGameObject.Remove(worldName);
        }
        else
        {
            Debug.LogWarning("World name not found");
        }
    }
    
    public void RenderOnGrid(int [,,] inData, Vector2Int bias, string worldName)
    {
        // 1. Add Parent Object for every world to keep track of all objects in the world
        GameObject worldParent = null;
        // if worldName already exists, then reset the world
        if (!worldNameSet.Add(worldName))
        {
            worldParent = WorldNameToGameObject[worldName];
            // clean up the world
            foreach (Transform child in worldParent.transform)
            {
                Destroy(child.gameObject);
            }
            WorldGridObjectsListDict[worldName].Clear();
        }
        else // add a new empty GameObject as the world Parent
        {
            worldParent = Instantiate(emptyPrefab, Vector3.zero, Quaternion.identity);
            worldParent.name = worldName;
            worldParent.transform.parent = transform;
            WorldNameToGameObject.Add(worldName, worldParent);
            WorldGridObjectsListDict.Add(worldName, new Dictionary<int, List<GameObject>>());
            WorldAgentNameToGameObject.Add(worldName, new Dictionary<string, GameObject>());
            WorldFoodNameToGameObject.Add(worldName, new Dictionary<string, GameObject>());
        }
        // 2. Add objects to the world
        // 2.1 Adding walls to every world border
        var wallPos = grid.GetCellCenterLocal(new Vector3Int(bias.x, bias.y, 0));
        // Instantiate a left wall
        var leftWall = Instantiate(wallPrefab, wallPos, Quaternion.identity);
        leftWall.name = "WallLeft";
        leftWall.transform.rotation = Quaternion.Euler(-90, 0, -90);
        leftWall.transform.localScale = new Vector3(2.0f, 1.0f, 2.0f);
        leftWall.transform.position = new Vector3(wallPos.x - 1, 0, (wallPos.z - 1) + height);
        leftWall.transform.parent = worldParent.transform;
        // Instantiate a right wall
        var rightWall = Instantiate(wallPrefab, wallPos, Quaternion.identity);
        rightWall.name = "WallRight";
        rightWall.transform.rotation = Quaternion.Euler(-90, 0, 90);
        rightWall.transform.localScale = new Vector3(2.0f, 1.0f, 2.0f);
        rightWall.transform.position = new Vector3((wallPos.x - 1) + (width * 2), 0, (wallPos.z - 1) + height);
        rightWall.transform.parent = worldParent.transform;
        // Instantiate a forward wall
        var topWall = Instantiate(wallPrefab, wallPos, Quaternion.identity);
        topWall.name = "WallForwad";
        topWall.transform.rotation = Quaternion.Euler(-90, -90, 90);
        topWall.transform.localScale = new Vector3(2.0f, 1.0f, 2.0f);
        topWall.transform.position = new Vector3((wallPos.x - 1) + width, 0, (wallPos.z - 1) + (height * 2));
        topWall.transform.parent = worldParent.transform;
        // Instantiate a back wall
        var bottomWall = Instantiate(wallPrefab, wallPos, Quaternion.identity);
        bottomWall.name = "WallBack";
        bottomWall.transform.rotation = Quaternion.Euler(-90, -90, -90);
        bottomWall.transform.localScale = new Vector3(2.0f, 1.0f, 2.0f);
        bottomWall.transform.position = new Vector3((wallPos.x - 1) + width, 0, (wallPos.z - 1));
        bottomWall.transform.parent = worldParent.transform;
        
        // cellIndex: from 0 ~ (width * height - 1)
        var cellIndex = 0;
        // 2.2 Adding all the tiles in the world
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y <height; y++)
            {
                cellIndex = y * 9 + x;
                if (!WorldGridObjectsListDict[worldName].TryAdd(cellIndex, new List<GameObject>()))
                {
                    WorldGridObjectsListDict[worldName][cellIndex].Clear();
                }
                // Get Current Tile position in the grid
                var pos = grid.GetCellCenterLocal(new Vector3Int(x + bias.x, (8 - y) + bias.y, 0));
                GameObject obj = null;
                // Get Current Tile Type
                // 1 - 5 stacked cubes
                if (inData[x,y,0]>0 && inData[x, y, 0] <= 5)
                {
                    // Instantiate a cube at the position
                    for (var i = 0; i < inData[x, y, 0]; i++)
                    {
                        pos.y = i * 2 + 1;
                        // pos.x *= scalex;
                        pos.y *= scaley;
                        // pos.z *= scalez;
                        obj = Instantiate(cubePrefabs[i], pos, Quaternion.identity);
                        obj = RotateAsMark(obj, inData[x, y, 1]);
                        obj.transform.SetParent(worldParent.transform);
                        obj.transform.localScale = new Vector3(1.0f, scaley, 1.0f);
                        obj.transform.position = pos;
                        // Add all the objects to the set of objects in the cell
                        WorldGridObjectsListDict[worldName][cellIndex].Add(obj);
                        // Set Tag for the object on top Layer
                        if (i == inData[x, y, 0] - 1)
                        {
                            obj.tag = "Finish";
                        }
                    }
                }
                // 6 -9 corners
                else if (inData[x, y, 0] >= 6 && inData[x, y, 0] <= 9)
                {
                    // Instantiate base cube at the position for high layer corners
                    // The bottom layer is always a cube
                    // So the corners tile will never be the bottom layer
                    for (var i = 0; i < inData[x, y, 0] - 5; i++)
                    {
                        pos.y = i * 2 + 1;
                        // pos.x *= scalex;
                        pos.y *= scaley;
                        // pos.z *= scalez;
                        obj = Instantiate(cubePrefabs[i], pos, Quaternion.identity);
                        obj = RotateAsMark(obj, inData[x, y, 1]);
                        obj.transform.SetParent(worldParent.transform);
                        obj.transform.localScale = new Vector3(1.0f, scaley, 1.0f);
                        obj.transform.position = pos;
                        // Add all the objects to the set of objects in the cell
                        WorldGridObjectsListDict[worldName][cellIndex].Add(obj);
                        obj.tag = "Playable";
                    }
                    // Instantiate the corners at the position
                    pos.y = (inData[x, y, 0] - 5) * 2 + 1;
                    // pos.x *= scalex;
                    pos.y *= scaley;
                    // pos.z *= scalez;
                    obj = Instantiate(cornerPrefabs[inData[x, y, 0] - 6], pos, Quaternion.identity);
                    obj = RotateAsMark(obj, inData[x, y, 1]);
                    obj.transform.SetParent(worldParent.transform);
                    obj.transform.localScale = new Vector3(1.0f, scaley, 1.0f);
                    obj.transform.position = pos;
                    // Set Tag for the object on top Layer
                    obj.tag = "Playable";
                    // Add all the objects to the set of objects in the cell
                    WorldGridObjectsListDict[worldName][cellIndex].Add(obj);
                }
                // 10 - 13 ramps
                else if(inData[x,y,0]>=10 && inData[x,y,0]<=13)
                {
                    // Instantiate base cube at the position for high layer corners
                    // The bottom layer is always a cube
                    // So the ramps tile will never be the bottom laye
                    for (var i = 0; i < inData[x, y, 0] - 9; i++)
                    {
                        pos.y = i * 2 + 1;
                        // pos.x *= scalex;
                        pos.y *= scaley;
                        // pos.z *= scalez;
                        obj = Instantiate(cubePrefabs[i], pos,  Quaternion.identity);
                        obj = RotateAsMark(obj, inData[x, y, 1]);
                        obj.transform.SetParent(worldParent.transform);
                        obj.transform.localScale = new Vector3(1.0f, scaley, 1.0f);
                        obj.transform.position = pos;
                        // Add all the objects to the set of objects in the cell
                        WorldGridObjectsListDict[worldName][cellIndex].Add(obj);
                        obj.tag = "Playable";
                    } 
                    // Instantiate the ramps at the position
                    pos.y = (inData[x, y, 0] - 9) * 2 + 1;
                    // pos.x *= scalex;
                    pos.y *= scaley;
                    // pos.z *= scalez;
                    obj = Instantiate(rampPrefabs[inData[x, y, 0] - 10], pos, Quaternion.identity);
                    obj = RotateAsMark(obj, inData[x, y, 1]);
                    obj.transform.SetParent(worldParent.transform);
                    obj.transform.localScale = new Vector3(1.0f, scaley, 1.0f);
                    obj.transform.position = pos;
                    // Set Tag for the object on top Layer
                    obj.tag = "Playable";
                    // Add all the objects to the set of objects in the cell
                    WorldGridObjectsListDict[worldName][cellIndex].Add(obj);
                }
                else
                {
                    Debug.LogError("Tile Data is out of range 0-13");
                }
                // Iterate the cell index
            }
        }
    }
}
