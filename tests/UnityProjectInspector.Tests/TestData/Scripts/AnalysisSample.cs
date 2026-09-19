using UnityEngine;
using UnityEngine.SceneManagement;

namespace TestProject;

public class GameManager
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
        Application.Quit();
    }

    private void LogMessage()
    {
        Debug.Log("Hello");
    }

    // This is a comment: SceneManager.LoadScene("FakeScene");
    // Should NOT be identified as a call.

    public void FakeCall()
    {
        var text = "SceneManager.LoadScene(\"FakeScene\")";
        // Should NOT be identified as a call — it's just a string.
    }
}