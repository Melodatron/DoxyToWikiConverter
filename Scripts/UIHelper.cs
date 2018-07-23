using UnityEngine;
using UnityEngine.UI;

using GracesGames.SimpleFileBrowser.Scripts;

public class UIHelper : MonoBehaviour
{
    public InputField xmlDirInputField;
    public InputField outDirInputField;

    private void Start()
    {
        #if UNITY_EDITOR
        RunOnDirectory();
        #endif
    }

    public void OpenFileBrowser()
    {
        Debug.LogWarning("Not Implemented");
    }

    public void RunOnFile()
    {
        Debug.Assert(System.IO.File.Exists(xmlDirInputField.text));
        Converter.RunOnFile(xmlDirInputField.text, outDirInputField.text);
    }

    public void RunOnDirectory()
    {
        Debug.Assert(System.IO.Directory.Exists(xmlDirInputField.text));
        Converter.RunOnDirectory(xmlDirInputField.text, outDirInputField.text);
    }
}
