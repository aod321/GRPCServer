using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuHandler : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void CreateWorld()
    {
        Debug.Log("Create World by Button");

        StartCoroutine(LoadSampleScene());
    }

    IEnumerator LoadSampleScene()
    {
        var op = SceneManager.LoadSceneAsync("SampleScene");
        while(!op.isDone) {
            yield return null;
        }
    }
}
