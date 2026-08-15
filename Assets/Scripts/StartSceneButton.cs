using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public sealed class StartSceneButton : MonoBehaviour
{
    private const string TargetScenePath =
        "Assets/Scenes/bomb-main level/iron level _remake.unity";

    public void StartGame()
    {
        GetComponent<Button>().interactable = false;
        SceneManager.LoadScene(TargetScenePath, LoadSceneMode.Single);
    }
}
