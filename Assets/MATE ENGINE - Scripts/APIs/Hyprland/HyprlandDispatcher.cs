using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace APIs.Hyprland
{
    /// <summary>
    /// Dispatches commands to the hyprland windowmanager directly via the socket
    /// </summary>
    public class HyprlandDispatcher : HyprlandSocketBase, IDisposable
    {
        class DispatchItem
        {
            internal SemaphoreSlim Semaphore { get; set; }
            internal string Response { get; set; }
            internal string Command { get; set; }
            public DispatchItem()
            {
                Semaphore = new SemaphoreSlim(0);
            }
        }

        const string _SocketName = ".socket.sock";
        CancellationTokenSource _CancellationTokenSource;
        ConcurrentQueue<DispatchItem> _DispatchItems;
        SemaphoreSlim _QueueSemaphore = new SemaphoreSlim(0);
        UnixDomainSocketEndPoint _EndPoint;

        public void Initialize()
        {
            _CancellationTokenSource = new CancellationTokenSource();
            _DispatchItems = new ConcurrentQueue<DispatchItem>();

            _EndPoint = new UnixDomainSocketEndPoint(GetSocketPath(_SocketName));

            Task.Run(async () =>
            {
                while (!_CancellationTokenSource.IsCancellationRequested)
                {
                    // wait here until the semaphore has been released/ an item has been queued
                    await _QueueSemaphore.WaitAsync(_CancellationTokenSource.Token);
                    if (_DispatchItems.TryDequeue(out DispatchItem item))
                    {
                        var sendData = Encoding.UTF8.GetBytes(item.Command);
                        using var ms = new MemoryStream();
                        // connect to socket dispacher an close as quickly as possible,
                        // hyprland sync dispatches and socket connections 
                        // hanging here will freeze the windowmanager
                        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                        {
                            // write the command 
                            await socket.ConnectAsync(_EndPoint);
                            var ok = await socket.SendAsync(sendData, SocketFlags.None);
                            if (ok < sendData.Length)
                                throw new Exception("send data too small");

                            // read the response
                            var buffer = new byte[4096];
                            var received = await socket.ReceiveAsync(buffer, SocketFlags.None);
                            await ms.WriteAsync(buffer, 0, received);
                            while (received > 0)
                            {
                                received = await socket.ReceiveAsync(buffer, SocketFlags.None);
                                await ms.WriteAsync(buffer, 0, received);
                            }
                        }
                        ms.Position = 0;
                        using var streamReader = new StreamReader(ms);
                        item.Response = await streamReader.ReadToEndAsync();
                        item.Semaphore.Release();
                    }
                }
            }, _CancellationTokenSource.Token);
        }

        public bool SocketExists() => SockerExistsImpl(_SocketName);

        public async Task SetPropAsync(string windowAddress, string propName, params object[] propValue)
        {
            var args = new string[4 + propValue.Length];
            args[0] = "dispatch";
            args[1] = "setprop";
            args[2] = $"address:{windowAddress}";
            args[3] = propName;
            for (int i = 4; i < propValue.Length + 4; i++)
                args[i] = propValue[i - 4]?.ToString();

            await DispatchCommandAsync(string.Join(" ", args));
        }

        public async Task MoveWindowToWorkspace(string windowAddress, int workspace, bool silent)
        {
            var method = "movetoworkspace";
            if(silent)
                method = "movetoworkspacesilent";
            await DispatchCommandAsync($"dispatch {method} {workspace},address:{windowAddress}");
        }

        public async Task<HyprlandClients> GetClientsAsync()
        {
            var response = await DispatchCommandAsync("-j/clients");
            response = $"{{ \"clients\" : {response} }}";
            var hashcode = response.GetHashCode();
            var clients = JsonUtility.FromJson<HyprlandClients>(response);
            clients.hashCode = hashcode;
            return clients;
        }

        public async Task<HyprlandMonitor[]> GetMonitorsAsync()
        {
            var response = await DispatchCommandAsync("-j/monitors");
            response = $"{{ \"monitors\" : {response} }}";
            var monitors = JsonUtility.FromJson<HyprlandMonitors>(response);
            return monitors.monitors;
        }

        public async Task<Dictionary<string, HyprlandLayerLevels>> GetLayersAsync()
        {
            var response = await DispatchCommandAsync("-j/layers");
            return JsonConvert.DeserializeObject<Dictionary<string, HyprlandLayerLevels>>(response);
        }

        public async Task<HyprlandWorkspace> GetActiveWorkspace()
        {
            var response = await DispatchCommandAsync("-j/activeworkspace");
            return JsonUtility.FromJson<HyprlandWorkspace>(response);
        }

        public async Task<Vector2Int> GetCursorPositionAsync()
        {
            var response = await DispatchCommandAsync("cursorpos");
            return HyprlandVectorToVector2Int(response);
        }

        private static Vector2Int HyprlandVectorToVector2Int(string hyprlandVector)
        {
            var parts = hyprlandVector.Trim().Split(',');
            if (parts.Length == 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
                return new Vector2Int(x, y);
            return Vector2Int.zero;
        }

        public async Task SetFloatingAsync(string windowAddress) => await DispatchCommandAsync($"dispatch setfloating address:{windowAddress}");

        public async Task TogglePinAsync(string windowAddress) => await DispatchCommandAsync($"dispatch pin address:{windowAddress}");

        public async Task ResizewindowpixelExactWindowSizeAsync(string windowAddress, Vector2Int size) => await DispatchCommandAsync($"dispatch resizewindowpixel exact {size.x} {size.y} , address:{windowAddress}");

        public async Task MoveWindowPixelExactAsync(string windowAddress, Vector2Int pos) => await DispatchCommandAsync($"dispatch movewindowpixel exact {pos.x} {pos.y} , address:{windowAddress}");

        async Task<string> DispatchCommandAsync(string command)
        {
            var item = new DispatchItem
            {
                Command = command
            };
            _DispatchItems.Enqueue(item);
            _QueueSemaphore.Release();
            // wait here until the dispatch item has been processed
            await item.Semaphore.WaitAsync();
            item.Semaphore.Dispose();
            return item.Response;
        }

        public void Dispose()
        {
            _CancellationTokenSource?.Cancel();
            _DispatchItems?.Clear();
            _QueueSemaphore?.Release();
        }
    }
}