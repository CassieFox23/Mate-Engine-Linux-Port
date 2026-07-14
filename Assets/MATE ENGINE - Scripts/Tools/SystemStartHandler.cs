using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Debug = UnityEngine.Debug;

public class SystemStartHandler : MonoBehaviour
{
    [Header("UI (Optional)")]
    public Toggle autoStartToggle;
    public TMP_Text checkmarkText;

    [Header("Settings")]
    public string runKeyName = "MateEngine";
    public string commandLineArgs = "";

    private bool _isApplyingUI;

    private void Awake()
    {
        if (SaveLoadHandler.Instance) return;
        Debug.LogError("[SystemStartHandler] SaveLoadHandler.Instance is null. Place SaveLoadHandler in the scene first.");
        enabled = false;
    }

    private void Start()
    {
        // Fork change: the in-app "Start with X11" autostart is disabled. It wrote its own
        // ~/.config/autostart/MateEngine.desktop with Terminal=true (pops a terminal at login)
        // and bypassed the patched launch.sh. Autostart is handled externally on this fork by a
        // proper .desktop launcher through run-local.sh. Hide the control so it can't collide.
        if (autoStartToggle)
            autoStartToggle.gameObject.SetActive(false);
        if (checkmarkText)
            checkmarkText.gameObject.SetActive(false);

        LoadFromSaveWithoutNotify();
        //AddStartupEntry(SaveLoadHandler.Instance.data.startWithX11);
    }

    private void OnDestroy()
    {
        if (autoStartToggle)
            autoStartToggle.onValueChanged.RemoveListener(OnUIToggleChanged);
    }

    private void OnUIToggleChanged(bool isOn)
    {
        if (_isApplyingUI) return;

        SaveLoadHandler.Instance.data.startWithX11 = isOn;
        SaveLoadHandler.Instance.SaveToDisk();

        AddStartupEntry(isOn);
        UpdateCheckmarkText(isOn);
    }

    public void OnCheckmarkClicked()
    {
        bool newState = !GetSavedState();
        SetStateFromCode(newState);
    }

    private void SetStateFromCode(bool isOn)
    {
        SaveLoadHandler.Instance.data.startWithX11 = isOn;
        SaveLoadHandler.Instance.SaveToDisk();
        AddStartupEntry(isOn);
        ApplyToUIWithoutNotify(isOn);
    }

    private void LoadFromSaveWithoutNotify()
    {
        ApplyToUIWithoutNotify(GetSavedState());
    }

    private bool GetSavedState()
    {
        return SaveLoadHandler.Instance.data != null && SaveLoadHandler.Instance.data.startWithX11;
    }

    private void ApplyToUIWithoutNotify(bool isOn)
    {
        _isApplyingUI = true;
        try
        {
            if (autoStartToggle)
                autoStartToggle.SetIsOnWithoutNotify(isOn);
            UpdateCheckmarkText(isOn);
        }
        finally
        {
            _isApplyingUI = false;
        }
    }

    private void UpdateCheckmarkText(bool isOn)
    {
        if (checkmarkText)
            checkmarkText.text = isOn ? "☑ Start with X11" : "☐ Start with X11";
    }

    private void AddStartupEntry(bool enable)
    {
        // Fork change: in-app autostart is disabled (see Start()). This is a hard no-op so no
        // code path can write a competing ~/.config/autostart entry. Autostart is managed
        // externally via a .desktop launcher that routes through the patched launch.sh.
        Debug.Log("[SystemStartHandler] In-app autostart disabled on this fork; managed externally.");
        return;

#pragma warning disable CS0162 // unreachable code kept for reference
        if (Application.platform != RuntimePlatform.LinuxPlayer &&
            Application.platform != RuntimePlatform.LinuxEditor)
        {
            Debug.Log("[SystemStartHandler] Skipping autostart entry creating (not on Linux).");
            return;
        }

        try
        {
            string appName = "MateEngine";
            string execPath = Application.dataPath.Substring(0, Application.dataPath.LastIndexOf('/')) + "/launch.sh";
            if (!File.Exists(execPath))
                throw new FileNotFoundException("Required script for launching is missing.", execPath);
            string desktopFileName = $"{appName}.desktop";
            string autostartDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart");
            string desktopFilePath = Path.Combine(autostartDir, desktopFileName);

            if (!enable && File.Exists(desktopFilePath))
            {
                File.Delete(desktopFilePath);
                return;
            }

            // Ensure the autostart directory exists
            if (!Directory.Exists(autostartDir))
            {
                Debug.LogWarning("[SystemStartHandler] Wait... Do you even use X11?");
                Directory.CreateDirectory(autostartDir);
            }

            // Define the content of the .desktop file
            string desktopFileContent = 
$@"[Desktop Entry]
Type=Application
Name={appName}
Exec=bash {execPath}
Hidden=false
NoDisplay=false
Terminal=true
X-GNOME-Autostart-enabled=true
Comment=Autostart for {appName}
";

            // Write the .desktop file
            File.WriteAllText(desktopFilePath, desktopFileContent);
            
            try
            {
                // Set read/write permissions
                System.Diagnostics.Process.Start("chmod", $"u+x {desktopFilePath}")?.WaitForExit();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SystemStartHandler] Could not set permissions on {desktopFilePath}: {ex.Message}");
            }

            Debug.Log($"[SystemStartHandler] Successfully created {desktopFilePath}");
        }
        catch (Exception ex)
        {
            Debug.Log($"Error creating .desktop file: {ex.Message}");
        }
#pragma warning restore CS0162
    }
}
