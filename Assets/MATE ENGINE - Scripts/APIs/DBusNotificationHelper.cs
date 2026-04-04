using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tmds.DBus;

[DBusInterface("org.freedesktop.Notifications")]
public interface INotifications : IDBusObject
{
    Task<uint> NotifyAsync(string appName, uint replacesId, string appIcon, string summary, string body, string[] actions, IDictionary<string, object> hints, int expireTimeout);
}

public class DBusNotificationHelper : Singleton<DBusNotificationHelper>
{
    public Connection Connection;

    private INotifications _proxy;

    public async Task<uint> Send(string title, string message, string icon = "dialog-information", string[] actions = null, int timeout = 5000)
    {
        string appName = "Mate Engine";
        uint replacesId = 0;
        actions ??= Array.Empty<string>();
        var hints = new Dictionary<string, object>
        {
            { "desktop-entry", "mateengine" } 
        };
        
        _proxy = Connection.CreateProxy<INotifications>(
            "org.freedesktop.Notifications",
            "/org/freedesktop/Notifications"
        );

        return await _proxy.NotifyAsync(appName, replacesId, icon, title, message, actions, hints, timeout);
    }
}