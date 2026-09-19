using UnityEngine;
using UnityEngine.SceneManagement;

public class LinkerGameManager
{
    public void Start2D()
    {
        SceneManager.LoadScene("Maze2D");
    }

    public void Start3D()
    {
        SceneManager.LoadScene("Maze3D");
    }

    public void QuitGame()
    {
        Debug.Log("Quit");
    }

    public void LoadVariableScene()
    {
        var sceneName = "DynamicScene";
        SceneManager.LoadScene(sceneName);
    }

    public void NoLoadScene()
    {
        Debug.Log("Hello");
    }
}