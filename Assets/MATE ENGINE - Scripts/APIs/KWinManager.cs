using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Tmds.DBus;
using Unity.VisualScripting.Antlr3.Runtime;
using UnityEngine;
using Debug = UnityEngine.Debug;

[DBusInterface("org.kde.kwin.Scripting")]
internal interface IScripting : IDBusObject
{
    Task<int> loadScriptAsync(string path, string name); // Note: DBus is case-sensitive. Don't modify the first letter even if Rider suggest you this is supposed to be "LoadScriptAsync"
    Task unloadScriptAsync(string name);
}

[DBusInterface("org.kde.kwin.Script")]
internal interface IScriptInstance : IDBusObject
{
    Task runAsync();
}


[DBusInterface("org.kdotool.callback")]
public interface IKWinCallback : IDBusObject //Phew! This field must be public to make methods accessible to KWinCallbackReceiverAdapter!
{
    Task ResultAsync(string id, string message);
    Task ErrorAsync(string id, string message);
    Task FinishAsync(string id);
}

public class KWinCallbackReceiver : IKWinCallback
{
    public ObjectPath ObjectPath => "/";

    // Maps ID -> The TaskCompletionSource that returns the final list
    private readonly ConcurrentDictionary<string, TaskCompletionSource<List<string>>> _pendingTasks = new();
    
    // Maps ID -> The actual list we are building
    private readonly ConcurrentDictionary<string, List<string>> _results = new();

    public void PrepareId(string id, TaskCompletionSource<List<string>> tcs)
    {
        _pendingTasks[id] = tcs;
        _results[id] = new List<string>();
    }

    public Task ResultAsync(string id, string message)
    {
        if (_results.TryGetValue(id, out var list))
            list.Add(message);
        return Task.CompletedTask;
    }

    public Task ErrorAsync(string id, string message)
    {
        if (_pendingTasks.TryRemove(id, out var tcs))
        {
            _results.TryRemove(id, out _);
            tcs.SetException(new Exception($"KWin Script Error: {message}"));
        }
        return Task.CompletedTask;
    }

    public Task FinishAsync(string id)
    {
        if (_pendingTasks.TryRemove(id, out var tcs))
        {
            if (_results.TryRemove(id, out var list))
                tcs.SetResult(list); // Success! Return the full list
        }
        return Task.CompletedTask;
    }
}

public class KWinManager : Singleton<KWinManager>
{
    private Connection _connection;
    private ConnectionInfo _connectionInfo;
    private KWinCallbackReceiver _callbackHandler;
    private string _kdeVersion;
    private string _windowUuid;

    private string _tempPath;

    public string UnityWindow => _windowUuid;

    private IScripting _scripting;

    private string _template;

    private bool _initialized;

    private new async void Awake()
    {
        try
        {
            base.Awake();
            _tempPath = Application.temporaryCachePath;
            _kdeVersion = Environment.GetEnvironmentVariable("KDE_SESSION_VERSION");
            await SetupDBus();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    public void Dispose()
    {
        if (_cachedScriptPaths.Count > 0)
        {
            foreach (var path in _cachedScriptPaths)
            {
                File.Delete(path);
            }
        }
        _cachedScriptPaths.Clear();
        _connection.UnregisterObject(_callbackHandler);
    }

    private void OnApplicationQuit()
    {
        Dispose();
    }

    private void OnDestroy()
    {
        Dispose();
    }

    private async Task SetupDBus()
    {
        _connection = new Connection(Address.Session);
        _connectionInfo = await _connection.ConnectAsync();
        
        _template = $@"
            function send(msg) {{
                callDBus(
                    '{_connectionInfo.LocalName}', 
                    '/', 
                    'org.kdotool.callback', 
                    'Result',
                    'placeholder',
                    msg
                );
            }}
            function err(msg) {{
                callDBus(
                    '{_connectionInfo.LocalName}', 
                    '/', 
                    'org.kdotool.callback', 
                    'Error',
                    'placeholder',
                    msg
                );
            }}
            function done() {{
                callDBus(
                    '{_connectionInfo.LocalName}', 
                    '/', 
                    'org.kdotool.callback', 
                    'Finish',
                    'placeholder'
                );
            }}";

        // Register our local callback object so KWin can talk to us
        _callbackHandler = new KWinCallbackReceiver();
        await _connection.RegisterObjectAsync(_callbackHandler);
        _scripting = _connection.CreateProxy<IScripting>("org.kde.KWin", "/Scripting");

        _initialized = true;
        
        _windowUuid = await GetSelfWindowUuid();
    }
    
    private async Task<string> GetSelfWindowUuid()
    {
        if (!_initialized)
        {
            Debug.LogError("Please do not get uuid at the moment.");
            return string.Empty;
        }
        string scriptName = "KWin_GetWinUUID.js";
        string jsScript = _template.Replace("placeholder", "KWin_GetWinUUID") + $@"
            for (let win of workspace.{(_kdeVersion.StartsWith("5") ? "clientList" : "windowList")}()) {{
                if (win.pid == {Process.GetCurrentProcess().Id}) {{
                    send(win.internalId.toString());
                    break;
                }}
            }}
            done();";
        await File.WriteAllTextAsync(Path.Combine(_tempPath, scriptName), jsScript);
        return (await ExecuteKWinScript(scriptName, true))[0];
    }
    
    public async Task<int> GetWindowPid(string uuid = null)
    {
        if (!_initialized)
        {
            Debug.LogError("Please do not get window pid at the moment.");
            return -1;
        }
        if (string.IsNullOrEmpty(uuid)) uuid = _windowUuid;
        
        string scriptName = "KWin_GetWinPID.js";
        string jsScript = _template.Replace("placeholder", "KWin_GetWinPID") + $@"
            for (let win of workspace.{(_kdeVersion.StartsWith("5") ? "clientList" : "windowList")}()) {{
                if (win.internalId.toString() == ""{uuid}"") {{
                    send(win.pid.toString());
                    break;
                }}
            }}
            done();";
        await File.WriteAllTextAsync(Path.Combine(_tempPath, scriptName), jsScript);
        var results = await ExecuteKWinScript(scriptName, false);

        int.TryParse(results[0], out var result);
        return result;
    }
    
    public async Task<List<string>> GetAllWindows()
    {
        if (!_initialized)
        {
            Debug.LogError("Please do not get all windows at the moment.");
            return new List<string>();
        }
        string scriptName = "KWin_GetAllWin.js";
        string scriptPath = Path.Combine(_tempPath, scriptName);
        string jsScript = _template.Replace("placeholder", "KWin_GetAllWin") + $@"
            for (let win of workspace.{(_kdeVersion.StartsWith("5") ? "clientList" : "windowList")}()) {{
                send(win.internalId.toString());
            }}
            done();";
        
        if (!File.Exists(scriptPath)) await File.WriteAllTextAsync(scriptPath, jsScript);
        return await ExecuteKWinScript(scriptName, false);
    }

    public async Task<RectInt> GetWindowGeometry(string uuid = null)
    {
        if (!_initialized)
        {
            Debug.LogError("Please do not get window geometry at the moment.");
            return RectInt.zero;
        }
        if (string.IsNullOrEmpty(uuid)) uuid = _windowUuid;
        
        string scriptName = $"KWin_GetGeometryFor{uuid.Replace("-", "_")}.js";
        string scriptPath = Path.Combine(_tempPath, scriptName);
        string jsScript = _template.Replace("placeholder", $"KWin_GetGeometryFor{uuid.Replace("-", "_")}") + $@"

            for (let w of workspace.{(_kdeVersion.StartsWith("5") ? "clientList" : "windowList")}()) {{
                if (w.internalId.toString() == ""{uuid}"") {{
                    send(w.frameGeometry.x + ',' + w.frameGeometry.y);
                    send(w.frameGeometry.width + 'x' + w.frameGeometry.height);
                    break;
                }}
            }}
            done();";

        if (!File.Exists(scriptPath)) await File.WriteAllTextAsync(scriptPath, jsScript);

        var output = await ExecuteKWinScript(scriptName, true);

        var geo = new RectInt();
        var pos = output[0].Split(',');
        var size = output[1].Split('x');

        geo.x = int.Parse(pos[0]);
        geo.y = int.Parse(pos[1]);
        geo.width = int.Parse(size[0]);
        geo.height = int.Parse(size[1]);

        return geo;
    }
    
    public async Task<Vector2Int> GetCursorPos()
    {
        if (!_initialized)
        {
            Debug.LogError("Please do not get cursor position at the moment.");
            return new Vector2Int();
        }
        string scriptName = "KWin_GetCursorPos.js";
        string scriptPath = Path.Combine(_tempPath, scriptName);
        string jsScript = _template.Replace("placeholder", "KWin_GetCursorPos") + $@"
                send(workspace.cursorPos.x + ',' + workspace.cursorPos.y);
                done();";
        
        if (!File.Exists(scriptPath)) await File.WriteAllTextAsync(scriptPath, jsScript);
        var output = await ExecuteKWinScript(scriptName, false);

        var pos = output[0].Split(',');
        return new Vector2Int(int.Parse(pos[0]), int.Parse(pos[1]));
    }
    
    public async Task MoveWindow(Vector2 pos)
    {
        if (!_initialized)
        {
            Debug.LogError("Please do not move at the moment.");
            return;
        }
        string scriptName = $"KWin_MoveWin.js"; 
        string scriptPath = Path.Combine(_tempPath, scriptName);

        string jsScript = $@"
            for (let w of workspace.{(_kdeVersion.StartsWith("5") ? "clientList" : "windowList")}()) {{
                if (w.internalId.toString() == ""{_windowUuid}"") {{
                    w.clientStartUserMovedResized(w);
                    w.geometry.x = {(int)pos.x};
                    w.geometry.y = {(int)pos.y};
                    w.clientFinishUserMovedResized(w);
                    break;
                }}
            }}";
        
        await File.WriteAllTextAsync(scriptPath, jsScript);

        await ExecuteKWinScript(scriptName, false);
    }

    private readonly List<string> _cachedScriptPaths = new();
    
    private async Task<List<string>> ExecuteKWinScript(string scriptFileName, bool deleteOnFinishExecution)
    {
        if (!_initialized)
        {
            Debug.LogError($"Please do not execute {scriptFileName} at the moment.");
            return new List<string>();
        }
        
        var tcs = new TaskCompletionSource<List<string>>();
        _callbackHandler.PrepareId(Path.GetFileNameWithoutExtension(scriptFileName), tcs);
        
        string scriptPath = Path.Combine(_tempPath, scriptFileName);

        if (!File.Exists(scriptPath))
            throw new FileNotFoundException($"Attempting to execute script {scriptFileName} while it's not found under {Path.GetDirectoryName(scriptPath)}.");
        
        int scriptId = await _scripting.loadScriptAsync(scriptPath, Path.GetFileNameWithoutExtension(scriptFileName));

        if (scriptId == -1)
        {
            throw new Exception($"loadScript returned -1 for script {Path.GetFileNameWithoutExtension(scriptFileName)}. Possibly already loaded?");
        }

        var instance = _kdeVersion == "5" ? _connection.CreateProxy<IScriptInstance>("org.kde.KWin", $"/{scriptId}") : _connection.CreateProxy<IScriptInstance>("org.kde.KWin", $"/Scripting/Script{scriptId}");

        try
        {
            await instance.runAsync();

            var timeout = Task.Delay(1000);
            var completedTask = await Task.WhenAny(tcs.Task, timeout);
            
            if (completedTask == timeout)
                throw new TimeoutException($"Script {scriptFileName} timed out waiting for FinishAsync.");

            return await tcs.Task;
        }
        finally
        {
            await _scripting.unloadScriptAsync(Path.GetFileNameWithoutExtension(scriptFileName));
            if (!deleteOnFinishExecution)
            {
                _cachedScriptPaths.Add(scriptPath);
            }
            else if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
        }
    }
}